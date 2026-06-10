using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorldAgent.Core.Data;

namespace RimWorldAgent.Core.AgentRuntime
{
    /// <summary>
    /// 两层记忆存储：
    ///  - 全局 knowledge.md（{ProjectPath}/knowledge.md）：跨存档沉淀的通用游戏知识。
    ///  - per-save AGENTS.md（{SaveDir}/AGENTS.md）：本局存档的固定 schema 事实。
    /// 章节一律精确全等匹配（标题行 Trim() == "## " + section），杜绝模糊 Contains 造成的重复矛盾章节。
    /// </summary>
    public static class MemoryStore
    {
        /// <summary>AGENTS.md 固定 schema 章节（顺序固定）。</summary>
        public static readonly string[] AgentsSchema =
        {
            "殖民地概况",
            "殖民者",
            "基地布局与规划",
            "当前目标",
            "教训",
            "其他备注",
        };

        private const string AgentsTitle = "# 本局存档记忆";
        private const string KnowledgeTitle = "# 通用游戏知识（跨存档）";

        /// <summary>全局通用知识文件路径（跨存档共用）。</summary>
        public static string KnowledgePath => Path.Combine(SessionStore.ProjectPath, "knowledge.md");

        /// <summary>当前存档记忆文件路径（per-save）。</summary>
        public static string AgentsMdPath => Path.Combine(SessionStore.SaveDir, "AGENTS.md");

        /// <summary>幂等初始化两层记忆文件：缺失则写入空骨架，已存在则不动。</summary>
        public static void EnsureInitialized()
        {
            EnsureDir(KnowledgePath);
            if (!File.Exists(KnowledgePath))
                File.WriteAllText(KnowledgePath, BuildKnowledgeSkeleton());

            EnsureDir(AgentsMdPath);
            if (!File.Exists(AgentsMdPath))
                File.WriteAllText(AgentsMdPath, BuildAgentsSkeleton());
        }

        /// <summary>
        /// 拼接两层记忆为一段稳定记忆文本，供 C# 侧注入 companion 的 systemPrompt（一次性、缓存友好）。
        /// 文件缺失时给占位，保证返回非 null。
        /// </summary>
        public static string ReadStableMemory()
        {
            var knowledge = File.Exists(KnowledgePath) ? File.ReadAllText(KnowledgePath).Trim() : "";
            var agents = File.Exists(AgentsMdPath) ? File.ReadAllText(AgentsMdPath).Trim() : "";

            var sb = new StringBuilder();
            sb.AppendLine("## 通用游戏知识");
            sb.AppendLine(string.IsNullOrEmpty(knowledge) ? "（暂无）" : knowledge);
            sb.AppendLine();
            sb.AppendLine("## 本局存档记忆");
            sb.AppendLine(string.IsNullOrEmpty(agents) ? "（暂无）" : agents);
            return sb.ToString().TrimEnd();
        }

        /// <summary>读取指定章节正文。scope ∈ {"save","global"}。未找到返回 null。</summary>
        public static string? ReadSection(string scope, string section)
        {
            var path = ResolvePath(scope);
            if (!File.Exists(path)) return null;
            var lines = new List<string>(File.ReadAllText(path).Split('\n'));
            var idx = FindSection(lines, section);
            if (idx < 0) return null;
            var end = FindSectionEnd(lines, idx);
            return string.Join("\n", lines.GetRange(idx, end - idx)).TrimEnd();
        }

        /// <summary>
        /// 整段替换指定章节内容（精确匹配）。
        ///  - scope="save"：section 必须是固定 schema 之一，否则抛 ArgumentException。
        ///  - scope="global"：section 自由；不存在则在文件末尾按 "## {section}" 新建。
        /// 替换不移动其它章节的相对位置，保证 schema 顺序稳定。
        /// </summary>
        public static void ReplaceSection(string scope, string section, string content)
        {
            var path = ResolvePath(scope);

            if (scope == "save" && !AgentsSchema.Contains(section))
                throw new ArgumentException(
                    $"非法章节 '{section}'。本局记忆(save)合法章节：{string.Join("、", AgentsSchema)}。");

            EnsureDir(path);

            // 确保文件存在且带正确顶层标题
            if (!File.Exists(path))
                File.WriteAllText(path, scope == "save" ? BuildAgentsSkeleton() : BuildKnowledgeSkeleton());

            var lines = new List<string>(File.ReadAllText(path).Split('\n'));
            var idx = FindSection(lines, section);

            var block = new List<string> { "## " + section };
            block.AddRange(content.Trim().Split('\n'));

            if (idx < 0)
            {
                // 章节不存在：追加到文件末尾（global 自由章节场景）
                while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                    lines.RemoveAt(lines.Count - 1);
                lines.Add("");
                lines.AddRange(block);
            }
            else
            {
                var end = FindSectionEnd(lines, idx);
                // 保留章节后的空行分隔
                if (end < lines.Count && !string.IsNullOrWhiteSpace(lines[end]))
                    block.Add("");
                else if (end < lines.Count)
                    block.Add(lines[end]); // 保留原有空行
                var removeEnd = (end < lines.Count && string.IsNullOrWhiteSpace(lines[end])) ? end + 1 : end;
                lines.RemoveRange(idx, removeEnd - idx);
                lines.InsertRange(idx, block);
            }

            File.WriteAllText(path, string.Join("\n", lines).TrimEnd() + "\n");
        }

        // ===== 内部辅助 =====

        private static string ResolvePath(string scope) =>
            scope == "global" ? KnowledgePath : AgentsMdPath;

        /// <summary>精确匹配 "## {section}" 标题行，返回行号；未找到 -1。</summary>
        private static int FindSection(List<string> lines, string section)
        {
            var target = "## " + section;
            for (var i = 0; i < lines.Count; i++)
                if (lines[i].Trim() == target) return i;
            return -1;
        }

        /// <summary>章节范围结束行 = 下一个 "## " 或 "# " 标题行（不含），否则文件末尾。</summary>
        private static int FindSectionEnd(List<string> lines, int sectionIdx)
        {
            for (var i = sectionIdx + 1; i < lines.Count; i++)
            {
                var t = lines[i].TrimStart();
                if (t.StartsWith("## ") || t.StartsWith("# ")) return i;
            }
            return lines.Count;
        }

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        private static string BuildAgentsSkeleton()
        {
            var sb = new StringBuilder();
            sb.AppendLine(AgentsTitle);
            sb.AppendLine();
            foreach (var s in AgentsSchema)
            {
                sb.AppendLine("## " + s);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd() + "\n";
        }

        private static string BuildKnowledgeSkeleton()
        {
            var sb = new StringBuilder();
            sb.AppendLine(KnowledgeTitle);
            sb.AppendLine();
            sb.AppendLine("（此文件跨存档共用，记录通用 RimWorld 玩法教训，例如「冬季前囤够食物」。用 update_memory scope=global 维护。）");
            return sb.ToString().TrimEnd() + "\n";
        }
    }
}
