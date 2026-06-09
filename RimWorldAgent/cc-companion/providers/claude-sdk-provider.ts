import { createSession } from '../bridge/session.js';
import { buildProviderConfig, providerCapabilities } from '../companion/config.js';
import { loadClaudeSdk } from '../companion/sdk-loader.js';
import type { AgentProvider } from './types.js';

const DEFAULT_CLAUDE_SDK_MODEL = '';

export async function createClaudeSdkProvider(): Promise<AgentProvider> {
  const sdk = await loadClaudeSdk();
  const config = buildProviderConfig('claude-sdk', DEFAULT_CLAUDE_SDK_MODEL);
  const capabilities = providerCapabilities('claude-sdk');
  return {
    kind: 'claude-sdk',
    config,
    capabilities,
    createSession: ({ abortController }) => createSession(sdk, abortController),
  };
}
