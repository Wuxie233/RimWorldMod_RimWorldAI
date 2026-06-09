import { AsyncStream } from '../agent-runtime/async-stream.js';

export type AgentProviderKind = 'claude-sdk' | 'anthropic' | 'openai' | 'openai-compatible';

export interface ProviderConfig {
  kind: AgentProviderKind;
  model: string;
  apiBaseUrl?: string;
  apiKey?: string;
  requestTimeoutMs: number;
  maxRetries: number;
  maxToolTurns: number;
  historyMaxMessages: number;
  historyMaxChars: number;
  mcpToolCacheTtlMs: number;
}

export interface SessionOptions {
  generationId: string;
  thinking?: {
    mode: 'adaptive' | 'disabled';
    effort?: 'low' | 'medium' | 'high' | 'xhigh' | 'max';
  };
}

export interface ProviderCapabilities {
  supportsThinking: boolean;
  supportsStreaming: boolean;
  requiresApiKey: boolean;
}

export class ProviderError extends Error {
  readonly provider: AgentProviderKind;
  readonly code: string;
  readonly status?: number;

  constructor(provider: AgentProviderKind, code: string, message: string, status?: number) {
    super(message);
    this.name = 'ProviderError';
    this.provider = provider;
    this.code = code;
    this.status = status;
  }
}

export interface AgentInboundMessage {
  type: 'user';
  message: {
    role: 'user';
    content: string;
  };
}

export type AgentEvent = Record<string, unknown>;

export interface AgentSessionContext {
  abortController?: AbortController;
  options: SessionOptions;
}

export interface AgentSession {
  inputStream: AsyncStream<AgentInboundMessage>;
  queryIterator: AsyncIterable<AgentEvent>;
}

export interface AgentProvider {
  kind: AgentProviderKind;
  config: ProviderConfig;
  capabilities: ProviderCapabilities;
  createSession(context: AgentSessionContext): AgentSession;
}

export interface AgentToolDefinition {
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
}

export interface AgentToolCall {
  id: string;
  name: string;
  input: unknown;
}

export interface AgentToolResult {
  id: string;
  name: string;
  content: string;
  isError: boolean;
}

export interface AgentUsage {
  inputTokens?: number;
  outputTokens?: number;
  cacheReadInputTokens?: number;
  cacheCreationInputTokens?: number;
}
