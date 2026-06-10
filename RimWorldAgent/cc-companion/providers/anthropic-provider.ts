import { createAnthropic } from '@ai-sdk/anthropic';
import type { JSONValue, LanguageModel, ModelMessage } from 'ai';
import { ConversationState } from '../agent-runtime/conversation-state.js';
import { McpToolRuntime } from '../agent-runtime/mcp-tools.js';
import { createProviderSession } from '../agent-runtime/provider-session.js';
import { runProviderRound } from '../agent-runtime/provider-runner.js';
import { runAiSdkTurn } from '../agent-runtime/ai-sdk-turn.js';
import { CONFIG, Thinking, buildProviderConfig, providerCapabilities } from '../companion/config.js';
import type { AgentProvider } from './types.js';

const DEFAULT_ANTHROPIC_MODEL = process.env.CCB_ANTHROPIC_DEFAULT_MODEL || 'claude-3-5-sonnet-latest';

export function createAnthropicProvider(): AgentProvider {
  const config = buildProviderConfig('anthropic', DEFAULT_ANTHROPIC_MODEL);
  const capabilities = providerCapabilities('anthropic');
  const model: LanguageModel = createAnthropic({
    apiKey: config.apiKey || undefined,
    baseURL: config.apiBaseUrl || undefined,
  })(config.model);

  const tools = new McpToolRuntime(config.mcpToolCacheTtlMs);
  const history = new ConversationState<ModelMessage>(config.historyMaxMessages, config.historyMaxChars);

  return {
    kind: 'anthropic',
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
        providerOptions: buildAnthropicProviderOptions(),
        signal: turnSignal,
      }));
    }, abortController),
  };
}

/** 思考开启时启用 Anthropic extended thinking（需 claude 3.7+ 模型；budgetTokens 按 effort 映射）。 */
function buildAnthropicProviderOptions(): Record<string, Record<string, JSONValue>> | undefined {
  if (Thinking.mode === 'disabled') return undefined;
  return { anthropic: { thinking: { type: 'enabled', budgetTokens: thinkingBudget(Thinking.effort) } } };
}

function thinkingBudget(effort: string): number {
  switch (effort) {
    case 'low': return 2048;
    case 'medium': return 6144;
    case 'high': return 12000;
    case 'xhigh': return 24000;
    case 'max': return 32000;
    default: return 12000;
  }
}
