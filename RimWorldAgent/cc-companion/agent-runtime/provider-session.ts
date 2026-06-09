import { AsyncStream } from './async-stream.js';
import type { AgentEvent, AgentInboundMessage, AgentSession } from '../providers/types.js';

export type TurnRunner = (text: string, signal?: AbortSignal) => AsyncIterable<AgentEvent>;

export function createProviderSession(runTurn: TurnRunner, abortController?: AbortController): AgentSession {
  const inputStream = new AsyncStream<AgentInboundMessage>();

  async function* queryIterator(): AsyncIterable<AgentEvent> {
    for await (const inbound of inputStream) {
      if (abortController?.signal.aborted) return;
      yield* runTurn(inbound.message.content, abortController?.signal);
    }
  }

  return { inputStream, queryIterator: queryIterator() };
}
