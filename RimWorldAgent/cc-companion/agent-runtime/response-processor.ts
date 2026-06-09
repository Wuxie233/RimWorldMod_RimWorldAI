import { CONFIG, errorMessage } from '../companion/config.js';
import { safeLogError } from './logging.js';
import type { AgentEvent } from '../providers/types.js';

export function createResponseProcessor(
  queryIterator: AsyncIterable<AgentEvent>,
  onMessage: (msg: AgentEvent) => void,
) {
  let processing = false;

  async function process(): Promise<void> {
    if (processing) return;
    processing = true;
    try {
      for await (const message of queryIterator) {
        onMessage(message);
      }
    } catch (err: unknown) {
      const message = errorMessage(err);
      if (err instanceof Error && (err.name === 'AbortError' || message.includes('aborted'))) {
        if (CONFIG.debug) console.log(`[bridge] process AbortError: name=${err.name} msg=${message}`);
        return;
      }
      safeLogError('AI provider 处理错误', err);
    } finally {
      processing = false;
    }
  }

  return { process };
}
