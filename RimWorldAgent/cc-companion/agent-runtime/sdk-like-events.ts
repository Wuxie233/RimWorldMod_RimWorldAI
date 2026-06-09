import { randomUUID } from 'crypto';
import { CONFIG } from '../companion/config.js';
import type { AgentEvent, AgentToolCall, AgentToolDefinition, AgentToolResult, AgentUsage, ProviderCapabilities } from '../providers/types.js';

function usageJson(usage?: AgentUsage): Record<string, unknown> | undefined {
  if (!usage) return undefined;
  return {
    input_tokens: usage.inputTokens ?? 0,
    output_tokens: usage.outputTokens ?? 0,
    cache_read_input_tokens: usage.cacheReadInputTokens ?? 0,
    cache_creation_input_tokens: usage.cacheCreationInputTokens ?? 0,
  };
}

export function systemInitEvent(provider: string, model: string, tools: AgentToolDefinition[], capabilities?: ProviderCapabilities): AgentEvent {
  return {
    type: 'system',
    subtype: 'init',
    model,
    claude_code_version: `provider:${provider}`,
    permissionMode: 'bypassPermissions',
    cwd: CONFIG.projectPath,
    apiKeySource: CONFIG.apiKey ? 'settings' : 'environment',
    tools: tools.map(t => t.name),
    skills: CONFIG.skills.map(s => s.split(':')[0]),
    mcp_servers: [{ name: 'agent', status: 'connected' }],
    slash_commands: [],
    output_style: 'default',
    agents: [],
    plugins: [],
    betas: [],
    provider_capabilities: capabilities,
  };
}

export function systemStatusEvent(status: string): AgentEvent {
  return { type: 'system', subtype: 'session_state_changed', state: status };
}

export function streamTextStart(index = 0, ttftMs?: number): AgentEvent {
  return {
    type: 'stream_event',
    ttft_ms: ttftMs,
    event: { type: 'content_block_start', index, content_block: { type: 'text', text: '' } },
  };
}

export function streamTextDelta(text: string, index = 0): AgentEvent {
  return {
    type: 'stream_event',
    event: { type: 'content_block_delta', index, delta: { type: 'text_delta', text } },
  };
}

export function streamThinkingStart(index = 0, ttftMs?: number): AgentEvent {
  return {
    type: 'stream_event',
    ttft_ms: ttftMs,
    event: { type: 'content_block_start', index, content_block: { type: 'thinking', thinking: '' } },
  };
}

export function streamThinkingDelta(thinking: string, index = 0): AgentEvent {
  return {
    type: 'stream_event',
    event: { type: 'content_block_delta', index, delta: { type: 'thinking_delta', thinking } },
  };
}

export function streamBlockStop(index = 0): AgentEvent {
  return { type: 'stream_event', event: { type: 'content_block_stop', index } };
}

export function streamUsage(usage?: AgentUsage): AgentEvent | undefined {
  const mapped = usageJson(usage);
  if (!mapped) return undefined;
  return { type: 'stream_event', event: { type: 'message_delta', index: 0, usage: mapped } };
}

export function assistantEvent(model: string, text: string, toolCalls: AgentToolCall[], usage?: AgentUsage, stopReason?: string): AgentEvent {
  const content: Record<string, unknown>[] = [];
  if (text.length > 0) content.push({ type: 'text', text });
  for (const call of toolCalls) {
    content.push({ type: 'tool_use', id: call.id, name: call.name, input: call.input ?? {} });
  }
  return {
    type: 'assistant',
    uuid: randomUUID(),
    message: {
      id: `msg_${randomUUID().replaceAll('-', '')}`,
      type: 'message',
      role: 'assistant',
      model,
      stop_reason: stopReason ?? (toolCalls.length > 0 ? 'tool_use' : 'end_turn'),
      stop_sequence: null,
      content,
      usage: usageJson(usage),
    },
  };
}

export function userToolResultEvent(result: AgentToolResult): AgentEvent {
  return {
    type: 'user',
    isSynthetic: true,
    message: {
      role: 'user',
      content: [{ type: 'tool_result', tool_use_id: result.id, is_error: result.isError, content: result.content }],
    },
  };
}

export function resultEvent(
  subtype: 'success' | 'error',
  startedAt: number,
  model: string,
  usage?: AgentUsage,
  result?: string,
  terminalReason?: string,
  numTurns = 1,
): AgentEvent {
  const duration = Date.now() - startedAt;
  const isError = subtype === 'error';
  return {
    type: 'result',
    subtype,
    stop_reason: isError ? (terminalReason ?? 'error') : 'end_turn',
    is_error: isError,
    num_turns: numTurns,
    duration_ms: duration,
    duration_api_ms: duration,
    result: result ?? '',
    total_cost_usd: 0,
    usage: usageJson(usage),
    modelUsage: {
      [model || 'unknown']: {
        inputTokens: usage?.inputTokens ?? 0,
        outputTokens: usage?.outputTokens ?? 0,
        cacheReadInputTokens: usage?.cacheReadInputTokens ?? 0,
        cacheCreationInputTokens: usage?.cacheCreationInputTokens ?? 0,
        webSearchRequests: 0,
        costUSD: 0,
        contextWindow: 0,
      },
    },
    permission_denials: [],
    errors: isError && result ? [result] : [],
    terminal_reason: terminalReason ?? (isError ? 'provider_api_error' : 'end_turn'),
  };
}
