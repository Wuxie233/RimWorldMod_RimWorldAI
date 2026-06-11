/**
 * RimWorld 游戏上下文 — 系统提示词
 *
 * 从 Mod 根目录 Prompt.md 加载
 */

import { readFileSync } from 'fs';
import { join } from 'path';

const promptCache = new Map<string, string>();

export function buildSystemPrompt(projectPath: string): string {
  const cached = promptCache.get(projectPath);
  if (cached) return cached;

  const promptPath = join(process.cwd(), 'Prompt.md');
  console.log(`[cc-companion] 加载 Prompt: ${promptPath}`);
  let content = readFileSync(promptPath, 'utf8');
  content = content.replace(/\{projectPath\}/g, projectPath);
  promptCache.set(projectPath, content);
  return content;
}
