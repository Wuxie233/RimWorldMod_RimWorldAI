import { streamText, tool, jsonSchema, stepCountIs } from 'ai';
import type { JSONValue, LanguageModel, ModelMessage, ToolSet } from 'ai';
import { McpToolRuntime } from './mcp-tools.js';
import { ConversationState } from './conversation-state.js';
import { buildAgentSystemPrompt } from './system-prompt.js';
import {
  assistantEvent,
  resultEvent,
  streamBlockStop,
  streamTextDelta,
  streamTextStart,
  streamThinkingDelta,
  streamThinkingStart,
  streamUsage,
  systemInitEvent,
  userToolResultEvent,
} from './sdk-like-events.js';
import type { AgentEvent, AgentToolCall, AgentUsage, ProviderCapabilities, ProviderConfig } from '../providers/types.js';

export interface AiSdkTurnParams {
  model: LanguageModel;
  toolRuntime: McpToolRuntime;
  config: ProviderConfig;
  capabilities: ProviderCapabilities;
  history: ConversationState<ModelMessage>;
  text: string;
  startedAt: number;
  providerOptions?: Record<string, Record<string, JSONValue>>;
  signal?: AbortSignal;
}

/**
 * 基于 Vercel AI SDK 的统一回合实现：openai / anthropic / openai-compatible 共用。
 * 用 streamText + fullStream 产出真流式 text/reasoning delta，工具仍由 McpToolRuntime 执行（手动循环），
 * emit 的事件序列与原生 provider 保持一致（systemInit → stream delta → assistant → tool_result → result）。
 */
export async function* runAiSdkTurn(params: AiSdkTurnParams): AsyncIterable<AgentEvent> {
  const { model, toolRuntime, config, capabilities, history, text, startedAt, providerOptions, signal } = params;

  const availableTools = await toolRuntime.listTools();
  yield systemInitEvent(config.kind, config.model, availableTools, capabilities);

  const tools: ToolSet = {};
  for (const def of availableTools) {
    tools[def.name] = tool({ description: def.description, inputSchema: jsonSchema(def.inputSchema) });
  }

  const userMessage: ModelMessage = { role: 'user', content: text };
  const messages: ModelMessage[] = [...history.getHistory(), userMessage];
  const pendingHistory: ModelMessage[] = [userMessage];

  let usage: AgentUsage | undefined;
  let aggregateText = '';
  let turn = 0;

  while (turn < config.maxToolTurns) {
    if (signal?.aborted) return;
    turn++;

    const result = streamText({
      model,
      system: buildAgentSystemPrompt(),
      messages,
      tools,
      stopWhen: stepCountIs(1),
      abortSignal: signal,
      ...(providerOptions ? { providerOptions } : {}),
    });

    let textStarted = false;
    let thinkingStarted = false;
    let stepText = '';

    for await (const part of result.fullStream) {
      if (part.type === 'reasoning-delta') {
        if (!thinkingStarted) { yield streamThinkingStart(0, Date.now() - startedAt); thinkingStarted = true; }
        yield streamThinkingDelta(part.text);
      } else if (part.type === 'text-delta') {
        if (!textStarted) { yield streamTextStart(0, Date.now() - startedAt); textStarted = true; }
        yield streamTextDelta(part.text);
        stepText += part.text;
      } else if (part.type === 'error') {
        throw part.error;
      }
    }

    if (textStarted || thinkingStarted) yield streamBlockStop();

    const toolCalls = await result.toolCalls;
    const u = await result.usage;
    usage = { inputTokens: u.inputTokens, outputTokens: u.outputTokens };
    aggregateText += stepText;

    const normalizedCalls: AgentToolCall[] = toolCalls.map(c => ({ id: c.toolCallId, name: c.toolName, input: c.input }));
    yield assistantEvent(config.model, stepText, normalizedCalls, usage, normalizedCalls.length > 0 ? 'tool_use' : 'end_turn');
    const usageEvent = streamUsage(usage);
    if (usageEvent) yield usageEvent;

    const assistantContent: Array<{ type: 'text'; text: string } | { type: 'tool-call'; toolCallId: string; toolName: string; input: unknown }> = [];
    if (stepText.length > 0) assistantContent.push({ type: 'text', text: stepText });
    for (const call of normalizedCalls) assistantContent.push({ type: 'tool-call', toolCallId: call.id, toolName: call.name, input: call.input });
    const assistantMsg: ModelMessage = { role: 'assistant', content: assistantContent };
    pendingHistory.push(assistantMsg);

    if (normalizedCalls.length === 0) {
      history.append(...pendingHistory);
      yield resultEvent('success', startedAt, config.model, usage, aggregateText, 'end_turn', turn);
      return;
    }

    messages.push(assistantMsg);
    const toolResultContent: Array<{ type: 'tool-result'; toolCallId: string; toolName: string; output: { type: 'text'; value: string } }> = [];
    for (const call of normalizedCalls) {
      const result2 = await toolRuntime.callTool(call);
      yield userToolResultEvent(result2);
      toolResultContent.push({ type: 'tool-result', toolCallId: call.id, toolName: call.name, output: { type: 'text', value: result2.content } });
    }
    const toolMsg: ModelMessage = { role: 'tool', content: toolResultContent };
    messages.push(toolMsg);
    pendingHistory.push(toolMsg);
  }

  history.append(...pendingHistory);
  yield resultEvent('error', startedAt, config.model, usage, `tool loop exhausted after ${config.maxToolTurns} turns`, 'tool_loop_exhaustion', turn);
}
