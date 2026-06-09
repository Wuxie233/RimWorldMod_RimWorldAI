import OpenAI from 'openai';
import type {
  ChatCompletionAssistantMessageParam,
  ChatCompletionMessageParam,
  ChatCompletionMessageToolCall,
  ChatCompletionTool,
} from 'openai/resources/chat/completions.js';
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
import { CONFIG, buildProviderConfig, providerCapabilities } from '../companion/config.js';
import type { AgentEvent, AgentProvider, AgentToolCall, AgentUsage, ProviderCapabilities, ProviderConfig } from './types.js';

const DEFAULT_OPENAI_MODEL = 'gpt-4o-mini';

export function createOpenAiProvider(): AgentProvider {
  const kind = CONFIG.provider === 'openai-compatible' ? 'openai-compatible' : 'openai';
  const config = buildProviderConfig(kind, DEFAULT_OPENAI_MODEL);
  const capabilities = providerCapabilities(kind);
  const client = new OpenAI({
    apiKey: config.apiKey || process.env.OPENAI_API_KEY || '',
    baseURL: config.apiBaseUrl,
  });
  const tools = new McpToolRuntime(config.mcpToolCacheTtlMs);
  const history = new ConversationState<ChatCompletionMessageParam>(config.historyMaxMessages, config.historyMaxChars);

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
      }, (turnSignal) => runOpenAiTurn(client, tools, config, capabilities, history, text, startedAt, turnSignal));
    }, abortController),
  };
}

async function* runOpenAiTurn(
  client: OpenAI,
  toolRuntime: McpToolRuntime,
  config: ProviderConfig,
  capabilities: ProviderCapabilities,
  history: ConversationState<ChatCompletionMessageParam>,
  text: string,
  startedAt: number,
  signal?: AbortSignal,
): AsyncIterable<AgentEvent> {
  const availableTools = await toolRuntime.listTools();
  yield systemInitEvent(config.kind, config.model, availableTools, capabilities);

  const userMessage: ChatCompletionMessageParam = { role: 'user', content: text };
  const messages: ChatCompletionMessageParam[] = [
    { role: 'system', content: buildAgentSystemPrompt() },
    ...history.getHistory(),
    userMessage,
  ];
  const pendingHistory: ChatCompletionMessageParam[] = [userMessage];
  const openAiTools: ChatCompletionTool[] = availableTools.map(tool => ({
    type: 'function',
    function: {
      name: tool.name,
      description: tool.description,
      parameters: tool.inputSchema,
    },
  }));

  let usage: AgentUsage | undefined;
  let lastText = '';
  let turn = 0;

  while (turn < config.maxToolTurns) {
    if (signal?.aborted) return;
    turn++;
    const completion = await client.chat.completions.create({
      model: config.model,
      messages,
      tools: openAiTools.length > 0 ? openAiTools : undefined,
      tool_choice: openAiTools.length > 0 ? 'auto' : undefined,
    }, { signal });

    const choice = completion.choices[0];
    const message = choice?.message;
    const content = message?.content ?? '';
    const toolCalls = (message?.tool_calls ?? []).filter(isFunctionToolCall);
    usage = {
      inputTokens: completion.usage?.prompt_tokens,
      outputTokens: completion.usage?.completion_tokens,
    };

    if (content.length > 0) {
      lastText += content;
      yield streamTextStart(0, Date.now() - startedAt);
      yield streamTextDelta(content, 0);
      yield streamBlockStop(0);
    }

    const normalizedCalls = toolCalls.map(toAgentToolCall);
    const assistantMessage = toAssistantMessage(content, toolCalls);
    pendingHistory.push(assistantMessage);
    yield assistantEvent(config.model, content, normalizedCalls, usage, normalizedCalls.length > 0 ? 'tool_use' : choice?.finish_reason ?? undefined);
    const usageEvent = streamUsage(usage);
    if (usageEvent) yield usageEvent;

    if (normalizedCalls.length === 0) {
      history.append(...pendingHistory);
      yield resultEvent('success', startedAt, config.model, usage, lastText, 'end_turn', turn);
      return;
    }

    messages.push(assistantMessage);
    for (const call of normalizedCalls) {
      const result = await toolRuntime.callTool(call);
      const toolMessage: ChatCompletionMessageParam = { role: 'tool', tool_call_id: result.id, content: result.content };
      yield userToolResultEvent(result);
      messages.push(toolMessage);
      pendingHistory.push(toolMessage);
    }
  }

  history.append(...pendingHistory);
  yield resultEvent('error', startedAt, config.model, usage, `tool loop exhausted after ${config.maxToolTurns} turns`, 'tool_loop_exhaustion', turn);
}

function isFunctionToolCall(call: ChatCompletionMessageToolCall): call is Extract<ChatCompletionMessageToolCall, { type: 'function' }> {
  return call.type === 'function';
}

function parseArguments(raw: string): unknown {
  if (!raw.trim()) return {};
  try { return JSON.parse(raw); }
  catch { return {}; }
}

function toAgentToolCall(call: Extract<ChatCompletionMessageToolCall, { type: 'function' }>): AgentToolCall {
  return {
    id: call.id,
    name: call.function.name,
    input: parseArguments(call.function.arguments),
  };
}

function toAssistantMessage(content: string, toolCalls: Extract<ChatCompletionMessageToolCall, { type: 'function' }>[]): ChatCompletionAssistantMessageParam {
  return {
    role: 'assistant',
    content: content.length > 0 ? content : null,
    tool_calls: toolCalls,
  };
}
