import { CONFIG } from '../companion/config.js';
import { buildSystemPrompt } from '../rimworld/context.js';

export function buildAgentSystemPrompt(): string {
  const skills = CONFIG.skills || [];
  const skillsSection = skills.length > 0
    ? '\n## 可用领域知识 (Skills)\n以下是可使用 active_skill 工具加载的领域知识。处理相关任务前先激活对应 skill 获取详细指导。\n\n' +
      skills.map(s => `- **${s.split(':')[0]}**: ${s.substring(s.indexOf(':') + 1).trim()}`).join('\n')
    : '';
  return [buildSystemPrompt(CONFIG.projectPath), skillsSection].filter(Boolean).join('\n');
}
