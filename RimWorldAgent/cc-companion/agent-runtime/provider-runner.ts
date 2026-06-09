import { resultEvent } from './sdk-like-events.js';
import { safeErrorMessage } from './logging.js';
import type { AgentEvent, AgentUsage } from '../providers/types.js';

export interface RunRoundOptions {
  model: string;
  startedAt: number;
  requestTimeoutMs: number;
  maxRetries: number;
  signal?: AbortSignal;
  numTurns?: number;
}

export async function* runProviderRound(
  options: RunRoundOptions,
  run: (signal?: AbortSignal) => AsyncIterable<AgentEvent>,
): AsyncIterable<AgentEvent> {
  let usage: AgentUsage | undefined;
  let lastResult = '';
  let attempt = 0;
  while (attempt <= options.maxRetries) {
    const timeout = createTimeoutSignal(options.requestTimeoutMs, options.signal);
    try {
      for await (const event of run(timeout.signal)) {
        usage = readUsage(event) ?? usage;
        if (event.type === 'result') lastResult = typeof event.result === 'string' ? event.result : lastResult;
        yield event;
      }
      return;
    } catch (err: unknown) {
      if (options.signal?.aborted || timeout.signal.aborted && timeout.reason === 'abort') {
        yield resultEvent('error', options.startedAt, options.model, usage, 'aborted', 'abort', options.numTurns ?? 0);
        return;
      }
      if (attempt >= options.maxRetries) {
        yield resultEvent('error', options.startedAt, options.model, usage, safeErrorMessage(err) || lastResult, 'provider_api_error', options.numTurns ?? 0);
        return;
      }
      attempt++;
    } finally {
      timeout.dispose();
    }
  }
}

function createTimeoutSignal(timeoutMs: number, parent?: AbortSignal): { signal: AbortSignal; reason: 'timeout' | 'abort' | undefined; dispose: () => void } {
  const controller = new AbortController();
  let reason: 'timeout' | 'abort' | undefined;
  const timer = setTimeout(() => {
    reason = 'timeout';
    controller.abort(new Error(`provider request timeout after ${timeoutMs}ms`));
  }, timeoutMs);
  const onAbort = () => {
    reason = 'abort';
    controller.abort(parent?.reason);
  };
  if (parent) parent.addEventListener('abort', onAbort, { once: true });
  return {
    signal: controller.signal,
    get reason() { return reason; },
    dispose: () => {
      clearTimeout(timer);
      if (parent) parent.removeEventListener('abort', onAbort);
    },
  };
}

function readUsage(event: AgentEvent): AgentUsage | undefined {
  const usage = event.usage;
  if (!usage || typeof usage !== 'object') return undefined;
  const obj = usage as Record<string, unknown>;
  return {
    inputTokens: typeof obj.input_tokens === 'number' ? obj.input_tokens : undefined,
    outputTokens: typeof obj.output_tokens === 'number' ? obj.output_tokens : undefined,
    cacheReadInputTokens: typeof obj.cache_read_input_tokens === 'number' ? obj.cache_read_input_tokens : undefined,
    cacheCreationInputTokens: typeof obj.cache_creation_input_tokens === 'number' ? obj.cache_creation_input_tokens : undefined,
  };
}
