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
import { safeErrorMessage } from './logging.js';

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
  console.log(`[ai-sdk-turn] start provider=${config.kind} model=${config.model} thinking=${providerOptions ? 'on' : 'off'} tools=${availableTools.length}`);

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
    if (signal?.aborted) { console.log(`[ai-sdk-turn] signal aborted before turn ${turn + 1}`); return; }
    turn++;
    console.log(`[ai-sdk-turn] turn ${turn} -> streamText`);

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

    try {
      for await (const part of result.fullStream) {
        if (part.type === 'reasoning-delta') {
          if (!thinkingStarted) { yield streamThinkingStart(0, Date.now() - startedAt); thinkingStarted = true; }
          yield streamThinkingDelta(part.text);
        } else if (part.type === 'text-delta') {
          if (!textStarted) { yield streamTextStart(0, Date.now() - startedAt); textStarted = true; }
          yield streamTextDelta(part.text);
          stepText += part.text;
        } else if (part.type === 'error') {
          console.error(`[ai-sdk-turn] turn ${turn} stream error part: ${safeErrorMessage(part.error)}`);
          throw part.error;
        }
      }
    } catch (streamErr) {
      console.error(`[ai-sdk-turn] turn ${turn} fullStream 异常 (provider=${config.kind} model=${config.model}): ${safeErrorMessage(streamErr)}`);
      throw streamErr;
    }

    if (textStarted || thinkingStarted) yield streamBlockStop();

    const toolCalls = await result.toolCalls;
    const u = await result.usage;
    usage = { inputTokens: u.inputTokens, outputTokens: u.outputTokens };
    aggregateText += stepText;
    console.log(`[ai-sdk-turn] turn ${turn} done text=${stepText.length}c reasoning=${thinkingStarted} toolCalls=${toolCalls.length} in=${u.inputTokens ?? 0} out=${u.outputTokens ?? 0}`);

    // 空响应检测：首轮零 token + 零内容 = 调用未真正成功（多为 provider 与模型不匹配，或网关/API 静默返回错误）。
    // 不报错的话上层只会看到 success 却没有任何回复，用户无从判断；这里转成明确错误结果。
    if (turn === 1 && stepText.length === 0 && toolCalls.length === 0
        && (u.inputTokens ?? 0) === 0 && (u.outputTokens ?? 0) === 0) {
      const hint = `模型返回空响应（provider=${config.kind} model=${config.model}）。`
        + `常见原因：provider 与模型不匹配（如用 anthropic 调 gpt-* 模型，或反之），或网关/API 返回了错误。`
        + `请确认：GPT 系模型用 openai / openai-compatible，Claude 系模型才用 anthropic / claude-sdk。`;
      console.error(`[ai-sdk-turn] ${hint}`);
      yield resultEvent('error', startedAt, config.model, usage, hint, 'empty_response', turn);
      return;
    }

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
