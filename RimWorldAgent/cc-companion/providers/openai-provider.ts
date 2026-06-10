import { createOpenAI } from '@ai-sdk/openai';
import { createOpenAICompatible } from '@ai-sdk/openai-compatible';
import type { JSONValue, LanguageModel, ModelMessage } from 'ai';
import { ConversationState } from '../agent-runtime/conversation-state.js';
import { McpToolRuntime } from '../agent-runtime/mcp-tools.js';
import { createProviderSession } from '../agent-runtime/provider-session.js';
import { runProviderRound } from '../agent-runtime/provider-runner.js';
import { runAiSdkTurn } from '../agent-runtime/ai-sdk-turn.js';
import { CONFIG, Thinking, buildProviderConfig, providerCapabilities } from '../companion/config.js';
import type { AgentProvider } from './types.js';

const DEFAULT_OPENAI_MODEL = 'gpt-4o-mini';

export function createOpenAiProvider(): AgentProvider {
  const kind = CONFIG.provider === 'openai-compatible' ? 'openai-compatible' : 'openai';
  const config = buildProviderConfig(kind, DEFAULT_OPENAI_MODEL);
  const capabilities = providerCapabilities(kind);

  const model: LanguageModel = kind === 'openai-compatible'
    ? createOpenAICompatible({ name: 'openai-compatible', baseURL: config.apiBaseUrl ?? '', apiKey: config.apiKey })(config.model)
    : createOpenAI({ apiKey: config.apiKey || undefined, baseURL: config.apiBaseUrl || undefined })(config.model);

  const tools = new McpToolRuntime(config.mcpToolCacheTtlMs);
  const history = new ConversationState<ModelMessage>(config.historyMaxMessages, config.historyMaxChars);

  return {
    kind,
    config,
    capabilities,
    createSession: ({ abortController }) => createProviderSession((text, signal) => {
      const startedAt = Date.now();
      return runProviderRound({
        model: config.model,
        startedAt,
        requestTimeoutMs: config.requestTimeoutMs,
        maxRetries: config.maxRetries,
        signal,
      }, (turnSignal) => runAiSdkTurn({
        model,
        toolRuntime: tools,
        config,
        capabilities,
        history,
        text,
        startedAt,
        providerOptions: buildOpenAiProviderOptions(kind),
        signal: turnSignal,
      }));
    }, abortController),
  };
}

/** 思考开启时为 OpenAI 官方启用 reasoning summary 流式（openai-compatible 网关多不支持，故跳过）。 */
function buildOpenAiProviderOptions(kind: string): Record<string, Record<string, JSONValue>> | undefined {
  if (Thinking.mode === 'disabled') return undefined;
  if (kind === 'openai') return { openai: { reasoningSummary: 'detailed' } };
  return undefined;
}
