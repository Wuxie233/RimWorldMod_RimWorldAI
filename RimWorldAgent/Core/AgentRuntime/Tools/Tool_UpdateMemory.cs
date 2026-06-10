using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace RimWorldAgent.Core.AgentRuntime.Tools
{
    public class Tool_UpdateMemory : IInternalTool
    {
        public string Name => "update_memory";
        public string Description =>
            "更新殖民地记忆（整段替换指定章节，幂等）。scope=save 维护本局固定 schema 章节；scope=global 维护跨存档通用知识。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                scope = new { type = "string", description = "save(本局存档记忆, 默认) / global(跨存档通用知识)" },
                section = new
                {
                    type = "string",
                    description = "目标章节标题。save 模式必须是固定章节之一：殖民地概况 / 殖民者 / 基地布局与规划 / 当前目标 / 教训 / 其他备注；global 模式可自由命名。"
                },
                content = new { type = "string", description = "该章节的完整新内容（整段替换，非追加）。" }
            },
            required = new[] { "section", "content" }
        });

        public Task<(string result, bool exit)> ExecuteAsync(JsonElement? args)
        {
            if (args == null)
                return Task.FromResult(("参数缺失：需要 section, content（scope 可选，默认 save）。", false));

            var scope = args.Value.TryGetProperty("scope", out var sc) ? sc.GetString() : null;
            if (string.IsNullOrEmpty(scope)) scope = "save";
            if (scope != "save" && scope != "global")
                return Task.FromResult(($"非法 scope '{scope}'，只能是 save 或 global。", false));

            if (!args.Value.TryGetProperty("section", out var se) || string.IsNullOrEmpty(se.GetString()))
                return Task.FromResult(("参数缺失：section。", false));
            if (!args.Value.TryGetProperty("content", out var co))
                return Task.FromResult(("参数缺失：content。", false));

            var section = se.GetString()!;
            var content = co.GetString() ?? "";

            try
            {
                MemoryStore.ReplaceSection(scope!, section, content);
            }
            catch (ArgumentException ex)
            {
                // 非法章节名等可预期错误：返回给 agent 让其纠正，不视为异常崩溃
                return Task.FromResult((ex.Message, false));
            }

            var scopeLabel = scope == "global" ? "通用知识(global)" : "本局存档记忆(save)";
            return Task.FromResult(($"已更新{scopeLabel}章节 '{section}'。", false));
        }
    }
}
