import { CONFIG } from '../companion/config.js';
import type { AgentProvider } from './types.js';
import { createAnthropicProvider } from './anthropic-provider.js';
import { createClaudeSdkProvider } from './claude-sdk-provider.js';
import { createOpenAiProvider } from './openai-provider.js';

export async function createAgentProvider(): Promise<AgentProvider> {
  switch (CONFIG.provider) {
    case 'anthropic':
      return createAnthropicProvider();
    case 'openai':
    case 'openai-compatible':
      return createOpenAiProvider();
    case 'claude-sdk':
    default:
      return createClaudeSdkProvider();
  }
}
