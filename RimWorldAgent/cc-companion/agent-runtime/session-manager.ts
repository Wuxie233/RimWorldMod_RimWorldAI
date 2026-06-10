import { randomUUID } from 'crypto';
import { createResponseProcessor } from './response-processor.js';
import { resultEvent } from './sdk-like-events.js';
import type { AgentEvent, AgentInboundMessage, AgentProvider, AgentSession } from '../providers/types.js';

export interface ManagedSession {
  generationId: string;
  abortController: AbortController;
  session: AgentSession;
}

export class SessionManager {
  private current: ManagedSession | null = null;
  private pendingMessages: AgentInboundMessage[] = [];

  constructor(
    private provider: AgentProvider,
    private readonly onMessage: (msg: AgentEvent) => void,
    private readonly log: (text: string) => void,
  ) {
    // 不在构造时创建会话：等待 C# 经 configure_session 注入稳定记忆后再建，
    // 以保证 systemPrompt 含记忆、且整条前缀只构造一次（缓存友好）。
  }

  configure(): void {
    if (this.current) {
      this.log('configure_session 重复调用，已忽略（会话已就绪）');
      return;
    }
    this.current = this.createSession();
    this.startProcessor(this.current);
    const pending = this.pendingMessages.splice(0);
    for (const message of pending) this.current.session.inputStream.enqueue(message);
    this.log(`会话已配置并启动（flush ${pending.length} 条暂存消息）`);
  }

  setProvider(provider: AgentProvider): void {
    this.log('热切换 provider，重建会话（历史重置）');
    this.provider = provider;
    if (!this.current) return;
    this.current.abortController.abort('provider swapped');
    this.current.session.inputStream.done();
    this.pendingMessages.splice(0);
    this.current = this.createSession();
    this.startProcessor(this.current);
  }

  enqueue(message: AgentInboundMessage): void {
    if (!this.current) {
      this.pendingMessages.push(message);
      return;
    }
    this.current.session.inputStream.enqueue(message);
  }

  rebuild(reason: string): void {
    if (!this.current) return;
    this.log(`重建会话: ${reason}`);
    this.current.abortController.abort(reason);
    this.current.session.inputStream.done();
    this.current = this.createSession();
    const pending = this.pendingMessages.splice(0);
    for (const message of pending) this.current.session.inputStream.enqueue(message);
    this.startProcessor(this.current);
  }

  abort(reason = 'abort'): void {
    const old = this.current;
    if (old) {
      old.abortController.abort(reason);
      old.session.inputStream.done();
    }
    this.onMessage({ type: 'aborted' });
    this.onMessage(resultEvent('error', Date.now(), this.provider.config.model, undefined, 'aborted', 'abort', 0));
    if (!old) return;
    this.current = this.createSession();
    this.startProcessor(this.current);
  }

  queueDuringRebuild(message: AgentInboundMessage): void {
    this.pendingMessages.push(message);
  }

  private createSession(): ManagedSession {
    const generationId = randomUUID();
    const abortController = new AbortController();
    const session = this.provider.createSession({ abortController, options: { generationId } });
    return { generationId, abortController, session };
  }

  private startProcessor(managed: ManagedSession): void {
    const generationId = managed.generationId;
    const proc = createResponseProcessor(managed.session.queryIterator, (message) => {
      if (this.current?.generationId !== generationId) return;
      this.onMessage(message);
    });
    proc.process().catch((err: unknown) => this.log(`AI provider 处理异常: ${err instanceof Error ? err.message : String(err)}`));
  }
}
