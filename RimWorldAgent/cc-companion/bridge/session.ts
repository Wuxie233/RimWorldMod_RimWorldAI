/** SDK 会话 — AsyncStream + query + onMessage 回调 */

import { join, resolve, dirname } from 'path';
import { homedir } from 'os';
import { CONFIG, Thinking, errorMessage } from '../companion/config.js';
import { buildSystemPrompt } from '../rimworld/context.js';
import { buildStableMemorySegment, buildSkillsSection } from '../agent-runtime/system-prompt.js';
import { Options, SYSTEM_PROMPT_DYNAMIC_BOUNDARY } from '@anthropic-ai/claude-agent-sdk';
import { AsyncStream } from '../agent-runtime/async-stream.js';
import type { AgentEvent, AgentInboundMessage } from '../providers/types.js';

export interface ClaudeAgentSdkModule {
  query(args: { prompt: AsyncIterable<AgentInboundMessage>; options: Options }): AsyncIterable<AgentEvent>;
}

// ========== SDK 会话 ==========

export function createSession(sdk: ClaudeAgentSdkModule, abortController?: AbortController) {
  const inputStream = new AsyncStream<AgentInboundMessage>();

  const claudeMdExcludes: string[] = [];
  const addExclude = (p: string) => {
    const n = p.replaceAll('\\', '/');
    claudeMdExcludes.push(n);
    const lower = n.toLowerCase();
    if (lower !== n) claudeMdExcludes.push(lower);
  };
  const projectRoot = resolve(CONFIG.projectPath);
  addExclude(join(projectRoot, 'CLAUDE.md'));
  let cursor = projectRoot;
  while (true) {
    const parent = dirname(cursor);
    if (parent === cursor) break;
    cursor = parent;
    addExclude(join(cursor, 'CLAUDE.md'));
  }
  addExclude(join(homedir(), '.claude', 'CLAUDE.md'));

  const memorySegment = buildStableMemorySegment();
  const skillsSection = buildSkillsSection();

  const options = {
    cwd: CONFIG.projectPath,
    model: CONFIG.modelName || undefined,
    abortController,
    permissionMode: 'bypassPermissions',
    allowDangerouslySkipPermissions: true,
    disallowedTools: ['Bash', 'Write', 'Edit', 'NotebookEdit', 'WebFetch', 'EnterWorktree', 'ExitWorktree', 'CronCreate', 'CronDelete', 'CronList', 'ScheduleWakeup', 'AskUserQuestion', 'EnterPlanMode', 'ExitPlanMode', 'Skill', 'Task', 'TaskCreate', 'TaskUpdate', 'TaskList', 'TaskGet', 'TaskOutput', 'TaskStop', 'Glob', 'Grep', 'Read'],
    autoCompactEnabled: true,
    includePartialMessages: true,
    settingSources: [],
    claudeMdExcludes,
    systemPrompt: [buildSystemPrompt(CONFIG.projectPath), memorySegment, skillsSection, SYSTEM_PROMPT_DYNAMIC_BOUNDARY].filter(p => p !== ''),
    stderr: (data: string | Buffer) => {
      process.stderr.write(`[sdk] ${typeof data === 'string' ? data : data.toString()}`);
    },
  } as Options;

  const tm = Thinking.mode;
  if (tm === 'disabled') {
    options.thinking = { type: 'disabled' };
  } else {
    options.thinking = { type: 'adaptive' };
    if (Thinking.effort) options.effort = Thinking.effort;
  }

  const queryIterator = sdk.query({ prompt: inputStream, options });
  return { inputStream, queryIterator };
}

// ========== 响应处理 → onMessage ==========

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
      // AbortError 是正常中断，不打印错误
      const message = errorMessage(err);
      if (err instanceof Error && (err.name === 'AbortError' || message.includes('aborted'))) {
        if (CONFIG.debug) console.log(`[bridge] process AbortError: name=${err.name} msg=${message}`);
        return;
      }
      const name = err instanceof Error ? err.name : typeof err;
      const stack = err instanceof Error ? err.stack : '';
      console.error(`SDK 处理错误: ${message} name=${name} stack=${stack}`);
    } finally {
      processing = false;
    }
  }

  return { process };
}
