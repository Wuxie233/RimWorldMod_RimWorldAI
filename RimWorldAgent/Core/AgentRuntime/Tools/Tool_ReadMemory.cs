using System.Text.Json;
using System.Threading.Tasks;

namespace RimWorldAgent.Core.AgentRuntime.Tools
{
    public class Tool_ReadMemory : IInternalTool
    {
        public string Name => "read_memory";
        public string Description =>
            "读取殖民地记忆。默认返回两层全貌（通用知识 + 本局存档记忆）；也可按 scope+section 精确读取单个章节。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                scope = new { type = "string", description = "可选: save(本局存档记忆) / global(跨存档通用知识) / both(两层全貌, 默认)" },
                section = new { type = "string", description = "可选, 只读取指定章节标题(精确匹配); 不传则返回该 scope 全文" }
            }
        });

        public Task<(string result, bool exit)> ExecuteAsync(JsonElement? args)
        {
            var scope = args?.TryGetProperty("scope", out var sc) == true ? sc.GetString() : null;
            var section = args?.TryGetProperty("section", out var se) == true ? se.GetString() : null;
            if (string.IsNullOrEmpty(scope)) scope = "both";

            string content;

            // both: 返回两层全貌（稳定记忆块）
            if (scope == "both")
            {
                content = MemoryStore.ReadStableMemory();
            }
            // 指定 scope + section: 精确章节读取
            else if (!string.IsNullOrEmpty(section))
            {
                var sectionContent = MemoryStore.ReadSection(scope!, section!);
                if (sectionContent == null)
                {
                    var hint = scope == "save"
                        ? $"未找到章节 '{section}'。本局记忆合法章节：{string.Join("、", MemoryStore.AgentsSchema)}。"
                        : $"未找到章节 '{section}'。";
                    return Task.FromResult((hint, false));
                }
                content = sectionContent;
            }
            // 指定 scope, 不指定 section: 读该 scope 全文
            else
            {
                var path = scope == "global" ? MemoryStore.KnowledgePath : MemoryStore.AgentsMdPath;
                content = System.IO.File.Exists(path)
                    ? System.IO.File.ReadAllText(path)
                    : "记忆文件不存在，殖民地刚开局尚无记录。";
            }

            // both 全貌可能较长，放宽截断阈值；超出提示用 section 精确读取
            var limit = scope == "both" ? 12000 : 8000;
            if (content.Length > limit)
                content = content.Substring(0, limit)
                    + "\n\n（内容过长，已截断。用 scope+section 参数读取指定章节以获取完整内容。）";

            return Task.FromResult((content, false));
        }
    }
}
