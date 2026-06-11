#!/usr/bin/env tsx
/**
 * Claude Code SDK 桥接 — 纯 WS，只认 4 种消息
 *
 * WebSocket 协议：
 *   C# → companion:  {"type":"chat", "text":"...", "session":"bus|system", "thinking":{mode,effort,tokens?}}
 *                    {"type":"abort"}
 *   companion → C#:  {"type":"hello-ok"}
 *                    SDK 消息 (type: assistant / stream_event / result / system / user / aborted)
 */

import { writeFileSync, unlinkSync, appendFileSync } from 'fs';
import { join } from 'path';
import { createServer } from 'http';
import { WebSocketServer, WebSocket } from 'ws';
import { CONFIG, Thinking, errorMessage, parseArgs, parseProvider, validateConfig } from './config.js';
import { SessionManager } from '../agent-runtime/session-manager.js';
import { redactSensitive } from '../agent-runtime/logging.js';
import { createAgentProvider } from '../providers/provider-factory.js';
import type { AgentInboundMessage } from '../providers/types.js';
import type { ThinkingConfig } from './protocol.js';

parseArgs(process.argv);
validateConfig();

function log(text: string) {
  console.log(`[bridge] ${text}`);
}

function debugLog(text: string) {
  if (CONFIG.debug) log(text);
}

function sendJson(ws: WebSocket, obj: Record<string, unknown>) {
  if (ws.readyState === 1) ws.send(JSON.stringify(obj));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function readAuthToken(value: unknown): string | undefined {
  if (!isRecord(value)) return undefined;
  return typeof value.token === 'string' ? value.token : undefined;
}

function readThinking(value: unknown): ThinkingConfig | undefined {
  if (!isRecord(value)) return undefined;
  if (value.mode !== 'adaptive' && value.mode !== 'disabled') return undefined;
  const cfg: ThinkingConfig = { mode: value.mode };
  if (value.effort === 'low' || value.effort === 'medium' || value.effort === 'high' || value.effort === 'xhigh' || value.effort === 'max') {
    cfg.effort = value.effort;
  }
  return cfg;
}

let sdkLogPath: string | null = null;
if (CONFIG.logSdk) {
  sdkLogPath = join(CONFIG.projectPath, 'sdk-log.txt');
  log(`SDK 日志已启用: ${sdkLogPath}`);
}

function sdkLog(dir: '→' | '←', data: string) {
  if (!sdkLogPath) return;
  try {
    const now = new Date().toISOString();
    appendFileSync(sdkLogPath, `[${now}] ${dir} ${redactSensitive(data)}\n`, 'utf8');
  } catch (err: unknown) {
    debugLog(`SDK 日志写入失败: ${errorMessage(err)}`);
  }
}

async function main() {
  log(`启动 PID=${process.pid} port=${CONFIG.port} provider=${CONFIG.provider} model=${CONFIG.modelName || 'default'}`);

  const provider = await createAgentProvider();
  let busBroadcast: (data: string) => void;
  let sessionManager: SessionManager;

  function applyThinking(cfg?: ThinkingConfig) {
    if (!cfg?.mode) return;
    if (cfg.mode === Thinking.mode && cfg.effort === Thinking.effort) return;
    if (!provider.capabilities.supportsThinking) {
      log(`provider=${provider.kind} 不支持 thinking，已忽略请求 mode=${cfg.mode}`);
      return;
    }
    Thinking.mode = cfg.mode;
    if (cfg.effort) Thinking.effort = cfg.effort;
    log(`思考模式: ${Thinking.mode}${cfg.effort ? ' effort=' + cfg.effort : ''}`);
    sessionManager.rebuild('thinking changed');
  }

  async function handleConfigUpdate(ws: WebSocket, msg: Record<string, unknown>) {
    const requestId = typeof msg.requestId === 'string' ? msg.requestId : '';
    const gateway = isRecord(msg.gateway) ? msg.gateway : {};
    const prev = { provider: CONFIG.provider, apiBaseUrl: CONFIG.apiBaseUrl, apiKey: CONFIG.apiKey, modelName: CONFIG.modelName };
    try {
      if (typeof gateway.provider === 'string') CONFIG.provider = parseProvider(gateway.provider);
      CONFIG.apiBaseUrl = typeof gateway.apiBaseUrl === 'string' ? gateway.apiBaseUrl : '';
      CONFIG.apiKey = typeof gateway.apiKey === 'string' ? gateway.apiKey : '';
      CONFIG.modelName = typeof gateway.modelName === 'string' ? gateway.modelName : '';
      validateConfig();
      const built = await createAgentProvider();
      sessionManager.setProvider(built);
      log(`网关已热切换: provider=${CONFIG.provider} model=${CONFIG.modelName || 'default'}`);
      sendJson(ws, {
        type: 'config-update-ack',
        requestId,
        ok: true,
        active: { provider: CONFIG.provider, apiBaseUrl: CONFIG.apiBaseUrl, modelName: CONFIG.modelName, hasApiKey: !!CONFIG.apiKey },
        history: 'reset',
        inFlight: 'aborted',
      });
    } catch (err: unknown) {
      CONFIG.provider = prev.provider;
      CONFIG.apiBaseUrl = prev.apiBaseUrl;
      CONFIG.apiKey = prev.apiKey;
      CONFIG.modelName = prev.modelName;
      log(`网关切换失败: ${errorMessage(err)}`);
      sendJson(ws, { type: 'config-update-ack', requestId, ok: false, error: { code: 'invalid', message: errorMessage(err) } });
    }
  }

  // ===== WS Server（先于 SDK 启动，避免竞态）=====
  const httpServer = createServer();
  const wss = new WebSocketServer({ server: httpServer });
  await new Promise<void>((resolve, reject) => {
    const onError = (err: Error) => reject(err);
    httpServer.once('error', onError);
    httpServer.listen(CONFIG.port, CONFIG.host, () => {
      httpServer.off('error', onError);
      resolve();
    });
  });

  busBroadcast = (data: string) => {
    sdkLog('←', data);
    for (const c of wss.clients) {
      if (c.readyState === 1) c.send(data);
    }
  };

  sessionManager = new SessionManager(provider, (msg) => setImmediate(() => busBroadcast(JSON.stringify(msg))), log);

  wss.on('connection', (ws: WebSocket) => {
    debugLog(`新 WS 连接, token=${CONFIG.token ? 'required' : 'none'}`);
    let authenticated = !CONFIG.token;

    ws.on('message', (data: Buffer) => {
      let raw: unknown;
      try { raw = JSON.parse(data.toString().trim()); }
      catch {
        debugLog(`无效 JSON: ${data.toString().substring(0, 200)}`);
        return;
      }
      if (!isRecord(raw) || typeof raw.type !== 'string') {
        debugLog(`无效消息: ${data.toString().substring(0, 200)}`);
        return;
      }
      const msg = raw;
      debugLog(`收到消息 type=${msg.type} auth=${readAuthToken(msg.auth) ? 'present' : 'none'}`);

      // auth
      if (msg.type === 'hello') {
        if (!authenticated) {
          if (readAuthToken(msg.auth) === CONFIG.token) {
            authenticated = true;
          } else {
            sendJson(ws, { type: 'error', error: 'auth failed' });
            log('auth 失败');
            ws.close();
            return;
          }
        }
        sendJson(ws, { type: 'hello-ok' });
        debugLog(`hello-ok 已发送${!CONFIG.token ? ' (无认证)' : ''}`);
        return;
      }
      if (!authenticated) return;

      // dispatch
      switch (msg.type) {
        case 'chat': {
          if (typeof msg.text !== 'string') return;
          applyThinking(readThinking(msg.thinking));
          const sessionName = typeof msg.session === 'string' ? msg.session : '';
          log(`chat session=${sessionName} len=${msg.text.length}`);
          const userMsg: AgentInboundMessage = { type: 'user', message: { role: 'user', content: msg.text } };
          sdkLog('→', JSON.stringify(userMsg));
          sessionManager.enqueue(userMsg);
          break;
        }
        case 'abort':
          log('收到 abort 指令');
          sessionManager.abort('ws abort');
          break;
        case 'configure_session': {
          CONFIG.stableMemory = typeof msg.stableMemory === 'string' ? msg.stableMemory : '';
          log(`configure_session stableMemory.len=${CONFIG.stableMemory.length}`);
          sessionManager.configure();
          sendJson(ws, { type: 'session_configured', ok: true });
          break;
        }
        case 'config-update':
          void handleConfigUpdate(ws, msg);
          break;
      }
    });
  });

  // ===== PID 文件 + 清理 =====
  const pidFile = join(process.cwd(), '.pid');
  writeFileSync(pidFile, String(process.pid));

  function shutdown() {
    try { unlinkSync(pidFile); }
    catch (err: unknown) { debugLog(`PID 文件清理失败: ${errorMessage(err)}`); }
    process.exit(0);
  }
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);

  log(`就绪 ws://${CONFIG.host}:${CONFIG.port}`);
}

main().catch((err: unknown) => {
  console.error(`[bridge] 致命错误: ${errorMessage(err)}`);
  process.exit(1);
});
