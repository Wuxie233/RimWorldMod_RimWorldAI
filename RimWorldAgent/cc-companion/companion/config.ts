/** 配置 — CLI 参数 + 环境变量 */
import type { AgentProviderKind, ProviderCapabilities, ProviderConfig } from '../providers/types.js';
import { redactSensitive } from '../agent-runtime/logging.js';

export type ThinkingEffort = 'low' | 'medium' | 'high' | 'xhigh' | 'max';

export interface CompanionConfig {
  host: string;
  port: number;
  token: string;
  projectPath: string;
  provider: AgentProviderKind;
  apiBaseUrl: string;
  apiKey: string;
  agentMcpUrl: string;
  modelName: string;
  settingSources: string[];
  skills: string[];
  logSdk: boolean;
  debug: boolean;
  requestTimeoutMs: number;
  maxRetries: number;
  maxToolTurns: number;
  historyMaxMessages: number;
  historyMaxChars: number;
  mcpToolCacheTtlMs: number;
}

export const Thinking = {
  mode: 'adaptive' as string,
  effort: 'high' as ThinkingEffort,
};

function parseSkillsJson(raw: string | undefined): string[] {
  if (!raw) return [];
  try { return JSON.parse(raw); }
  catch (err: unknown) { console.error(`[config] 解析 CCB_SKILLS 失败: ${errorMessage(err)} raw=${raw.substring(0, 200)}`); return []; }
}

export function errorMessage(err: unknown): string {
  return redactSensitive(err instanceof Error ? err.message : String(err));
}

export const CONFIG: CompanionConfig = {
  host: process.env.CCB_HOST || '0.0.0.0',
  port: parseInt(process.env.CCB_PORT || '19998'),
  token: process.env.CCB_AUTH_TOKEN || '',
  projectPath: process.env.RIMWORLD_PROJECT_PATH || process.cwd(),
  provider: parseProvider(process.env.CCB_AI_PROVIDER),
  apiBaseUrl: process.env.CCB_API_BASE_URL || '',
  apiKey: process.env.CCB_API_KEY || '',
  agentMcpUrl: process.env.CCB_AGENT_MCP_URL || 'http://localhost:9878/mcp',
  modelName: process.env.CCB_MODEL_NAME || '',
  settingSources: process.env.CCB_SETTING_SOURCES
    ? process.env.CCB_SETTING_SOURCES.split(',').map(s => s.trim())
    : ['user', 'project', 'local'],
  skills: parseSkillsJson(process.env.CCB_SKILLS),
  logSdk: process.env.CCB_LOG_SDK === '1' || process.env.CCB_LOG_SDK === 'true',
  debug: process.env.CCB_DEBUG === '1' || process.env.CCB_DEBUG === 'true',
  requestTimeoutMs: parseInt(process.env.CCB_REQUEST_TIMEOUT_MS || '120000'),
  maxRetries: parseInt(process.env.CCB_MAX_RETRIES || '1'),
  maxToolTurns: parseInt(process.env.CCB_MAX_TOOL_TURNS || '20'),
  historyMaxMessages: parseInt(process.env.CCB_HISTORY_MAX_MESSAGES || '40'),
  historyMaxChars: parseInt(process.env.CCB_HISTORY_MAX_CHARS || '120000'),
  mcpToolCacheTtlMs: parseInt(process.env.CCB_MCP_TOOL_CACHE_TTL_MS || '60000'),
};

export function parseProvider(raw: string | undefined): AgentProviderKind {
  switch ((raw || 'claude-sdk').trim().toLowerCase()) {
    case 'claude-sdk':
    case 'claude':
      return 'claude-sdk';
    case 'anthropic':
      return 'anthropic';
    case 'openai':
      return 'openai';
    case 'openai-compatible':
    case 'openai_compatible':
    case 'compatible':
      return 'openai-compatible';
    default:
      throw new Error(`不支持的 AI provider: ${raw}`);
  }
}

export function parseArgs(argv: string[]): void {
  for (let i = 2; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--port' && argv[i + 1]) CONFIG.port = parseInt(argv[++i]);
    else if (a === '--host' && argv[i + 1]) CONFIG.host = argv[++i];
    else if (a === '--token' && argv[i + 1]) CONFIG.token = argv[++i];
    else if (a === '--provider' && argv[i + 1]) CONFIG.provider = parseProvider(argv[++i]);
    else if (a === '--api-base-url' && argv[i + 1]) CONFIG.apiBaseUrl = argv[++i];
    else if (a === '--api-key' && argv[i + 1]) CONFIG.apiKey = argv[++i];
    else if (a === '--agent-mcp-url' && argv[i + 1]) CONFIG.agentMcpUrl = argv[++i];
    else if (a === '--model-name' && argv[i + 1]) CONFIG.modelName = argv[++i];
    else if (a === '--request-timeout-ms' && argv[i + 1]) CONFIG.requestTimeoutMs = parseInt(argv[++i]);
    else if (a === '--max-retries' && argv[i + 1]) CONFIG.maxRetries = parseInt(argv[++i]);
    else if (a === '--max-tool-turns' && argv[i + 1]) CONFIG.maxToolTurns = parseInt(argv[++i]);
    else if (a === '--history-max-messages' && argv[i + 1]) CONFIG.historyMaxMessages = parseInt(argv[++i]);
    else if (a === '--history-max-chars' && argv[i + 1]) CONFIG.historyMaxChars = parseInt(argv[++i]);
    else if (a === '--mcp-tool-cache-ttl-ms' && argv[i + 1]) CONFIG.mcpToolCacheTtlMs = parseInt(argv[++i]);
    else if (a === '--project-path' && argv[i + 1]) CONFIG.projectPath = argv[++i];
    else if (a === '--setting-sources' && argv[i + 1]) CONFIG.settingSources = argv[++i].split(',').map(s => s.trim());
    else if (a === '--skills' && argv[i + 1]) {
      const raw = argv[++i];
      try { CONFIG.skills = JSON.parse(raw); }
      catch (err: unknown) { console.error(`[config] 解析 --skills 参数失败: ${errorMessage(err)} raw=${raw.substring(0, 200)}`); }
    }
    else if (a === '--log-sdk') CONFIG.logSdk = true;
    else if (a === '--debug') CONFIG.debug = true;
  }
}

export function providerCapabilities(kind: AgentProviderKind): ProviderCapabilities {
  switch (kind) {
    case 'claude-sdk':
      return { supportsThinking: true, supportsStreaming: true, requiresApiKey: false };
    case 'anthropic':
      return { supportsThinking: false, supportsStreaming: false, requiresApiKey: true };
    case 'openai':
    case 'openai-compatible':
      return { supportsThinking: false, supportsStreaming: false, requiresApiKey: true };
  }
}

export function buildProviderConfig(kind: AgentProviderKind, defaultModel: string): ProviderConfig {
  return {
    kind,
    model: CONFIG.modelName || defaultModel,
    apiBaseUrl: CONFIG.apiBaseUrl || undefined,
    apiKey: CONFIG.apiKey || undefined,
    requestTimeoutMs: CONFIG.requestTimeoutMs,
    maxRetries: CONFIG.maxRetries,
    maxToolTurns: CONFIG.maxToolTurns,
    historyMaxMessages: CONFIG.historyMaxMessages,
    historyMaxChars: CONFIG.historyMaxChars,
    mcpToolCacheTtlMs: CONFIG.mcpToolCacheTtlMs,
  };
}

export function validateConfig(): void {
  if (!Number.isFinite(CONFIG.port) || CONFIG.port <= 0 || CONFIG.port > 65535) throw new Error(`CCB_PORT 无效: ${CONFIG.port}`);
  if (!Number.isFinite(CONFIG.requestTimeoutMs) || CONFIG.requestTimeoutMs <= 0) throw new Error(`CCB_REQUEST_TIMEOUT_MS 无效: ${CONFIG.requestTimeoutMs}`);
  if (!Number.isFinite(CONFIG.maxRetries) || CONFIG.maxRetries < 0) throw new Error(`CCB_MAX_RETRIES 无效: ${CONFIG.maxRetries}`);
  if (!Number.isFinite(CONFIG.maxToolTurns) || CONFIG.maxToolTurns <= 0) throw new Error(`CCB_MAX_TOOL_TURNS 无效: ${CONFIG.maxToolTurns}`);
  if (!Number.isFinite(CONFIG.historyMaxMessages) || CONFIG.historyMaxMessages <= 0) throw new Error(`CCB_HISTORY_MAX_MESSAGES 无效: ${CONFIG.historyMaxMessages}`);
  if (!Number.isFinite(CONFIG.historyMaxChars) || CONFIG.historyMaxChars <= 0) throw new Error(`CCB_HISTORY_MAX_CHARS 无效: ${CONFIG.historyMaxChars}`);
  if (!Number.isFinite(CONFIG.mcpToolCacheTtlMs) || CONFIG.mcpToolCacheTtlMs <= 0) throw new Error(`CCB_MCP_TOOL_CACHE_TTL_MS 无效: ${CONFIG.mcpToolCacheTtlMs}`);
  if (CONFIG.apiBaseUrl) new URL(CONFIG.apiBaseUrl);
  if (providerCapabilities(CONFIG.provider).requiresApiKey && !CONFIG.apiKey) {
    const envName = CONFIG.provider === 'anthropic' ? 'ANTHROPIC_API_KEY' : 'OPENAI_API_KEY';
    if (!process.env[envName]) throw new Error(`${CONFIG.provider} 缺少 API key，请设置 CCB_API_KEY 或 ${envName}`);
  }
  if (CONFIG.provider === 'openai-compatible' && !CONFIG.apiBaseUrl) throw new Error('openai-compatible 需要 API 地址');
}
