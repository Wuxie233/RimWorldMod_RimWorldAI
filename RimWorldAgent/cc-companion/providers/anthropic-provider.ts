import Anthropic from '@anthropic-ai/sdk';
import type { ContentBlock, MessageParam, Tool, ToolResultBlockParam, ToolUseBlock } from '@anthropic-ai/sdk/resources/messages.js';
import { ConversationState } from '../agent-runtime/conversation-state.js';
import { McpToolRuntime } from '../agent-runtime/mcp-tools.js';
import { createProviderSession } from '../agent-runtime/provider-session.js';
import { runProviderRound } from '../agent-runtime/provider-runner.js';
import {
  assistantEvent,
  resultEvent,
  streamBlockStop,
  streamTextDelta,
  streamTextStart,
  streamUsage,
  systemInitEvent,
  userToolResultEvent,
} from '../agent-runtime/sdk-like-events.js';
import { buildAgentSystemPrompt } from '../agent-runtime/system-prompt.js';
import { buildProviderConfig, providerCapabilities } from '../companion/config.js';
import type { AgentEvent, AgentProvider, AgentToolCall, AgentUsage, ProviderCapabilities, ProviderConfig } from './types.js';

const DEFAULT_ANTHROPIC_MODEL = process.env.CCB_ANTHROPIC_DEFAULT_MODEL || 'claude-3-5-sonnet-latest';

export function createAnthropicProvider(): AgentProvider {
  const config = buildProviderConfig('anthropic', DEFAULT_ANTHROPIC_MODEL);
  const capabilities = providerCapabilities('anthropic');
  const client = new Anthropic({
    apiKey: config.apiKey || process.env.ANTHROPIC_API_KEY || '',
    baseURL: config.apiBaseUrl,
  });
  const tools = new McpToolRuntime(config.mcpToolCacheTtlMs);
  const history = new ConversationState<MessageParam>(config.historyMaxMessages, config.historyMaxChars);

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
      }, (turnSignal) => runAnthropicTurn(client, tools, config, capabilities, history, text, startedAt, turnSignal));
    }, abortController),
  };
}

async function* runAnthropicTurn(
  client: Anthropic,
  toolRuntime: McpToolRuntime,
  config: ProviderConfig,
  capabilities: ProviderCapabilities,
  history: ConversationState<MessageParam>,
  text: string,
  startedAt: number,
  signal?: AbortSignal,
): AsyncIterable<AgentEvent> {
  const availableTools = await toolRuntime.listTools();
  yield systemInitEvent('anthropic', config.model, availableTools, capabilities);

  const userMessage: MessageParam = { role: 'user', content: text };
  const messages: MessageParam[] = [...history.getHistory(), userMessage];
  const pendingHistory: MessageParam[] = [userMessage];
  const anthropicTools: Tool[] = availableTools.map(tool => ({
    name: tool.name,
    description: tool.description,
    input_schema: ensureAnthropicSchema(tool.inputSchema),
  }));

  let usage: AgentUsage | undefined;
  let lastText = '';
  let turn = 0;

  while (turn < config.maxToolTurns) {
    if (signal?.aborted) return;
    turn++;
    const response = await client.messages.create({
      model: config.model,
      max_tokens: 4096,
      system: buildAgentSystemPrompt(),
      messages,
      tools: anthropicTools.length > 0 ? anthropicTools : undefined,
    }, { signal });

    usage = {
      inputTokens: response.usage.input_tokens,
      outputTokens: response.usage.output_tokens,
      cacheReadInputTokens: response.usage.cache_read_input_tokens ?? undefined,
      cacheCreationInputTokens: response.usage.cache_creation_input_tokens ?? undefined,
    };
    const textBlocks = response.content.filter(isTextBlock);
    const content = textBlocks.map(block => block.text).join('');
    if (content.length > 0) {
      lastText += content;
      yield streamTextStart(0, Date.now() - startedAt);
      yield streamTextDelta(content, 0);
      yield streamBlockStop(0);
    }

    const assistantMessage: MessageParam = { role: 'assistant', content: response.content };
    const toolCalls = response.content.filter(isToolUseBlock).map(toAgentToolCall);
    pendingHistory.push(assistantMessage);
    yield assistantEvent(config.model, content, toolCalls, usage, response.stop_reason ?? undefined);
    const usageEvent = streamUsage(usage);
    if (usageEvent) yield usageEvent;

    if (toolCalls.length === 0) {
      history.append(...pendingHistory);
      yield resultEvent('success', startedAt, config.model, usage, lastText, 'end_turn', turn);
      return;
    }

    messages.push(assistantMessage);
    const toolResults: ToolResultBlockParam[] = [];
    for (const call of toolCalls) {
      const result = await toolRuntime.callTool(call);
      yield userToolResultEvent(result);
      toolResults.push({ type: 'tool_result', tool_use_id: result.id, is_error: result.isError, content: result.content });
    }
    const toolResultMessage: MessageParam = { role: 'user', content: toolResults };
    messages.push(toolResultMessage);
    pendingHistory.push(toolResultMessage);
  }

  history.append(...pendingHistory);
  yield resultEvent('error', startedAt, config.model, usage, `tool loop exhausted after ${config.maxToolTurns} turns`, 'tool_loop_exhaustion', turn);
}

function ensureAnthropicSchema(schema: Record<string, unknown>): Tool.InputSchema {
  return {
    ...schema,
    type: 'object',
  } as Tool.InputSchema;
}

function isTextBlock(block: ContentBlock): block is Extract<ContentBlock, { type: 'text' }> {
  return block.type === 'text';
}

function isToolUseBlock(block: ContentBlock): block is ToolUseBlock {
  return block.type === 'tool_use';
}

function toAgentToolCall(block: ToolUseBlock): AgentToolCall {
  return {
    id: block.id,
    name: block.name,
    input: block.input,
  };
}
