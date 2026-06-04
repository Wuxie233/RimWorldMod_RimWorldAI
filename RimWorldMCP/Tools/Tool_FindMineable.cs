using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Verse;

namespace RimWorldMCP.Tools
{
    /// <summary>扫描全图可挖掘矿脉（defName 以 Mineable 开头的建筑），按产出类型分组返回坐标。</summary>
    public class Tool_FindMineable : ITool
    {
        public string Name => "find_mineable";
        public string Description => "扫描全图可挖掘矿脉，按产出类型分组返回坐标。用于快速定位钢铁、零部件、金银等矿脉。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                keyword = new { type = "string", description = "模糊匹配产出名称（可选），如 \"钢\"、\"零件\"、\"金\"。不填返回全部" },
                min_count = new { type = "integer", description = "最少矿脉数阈值（可选），只返回数量 >= 此值的矿脉类型", @default = 1 }
            }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            string keyword = "";
            if (args?.TryGetProperty("keyword", out var jk) == true)
                keyword = jk.GetString() ?? "";
            int minCount = 1;
            if (args?.TryGetProperty("min_count", out var jm) == true)
                minCount = Math.Max(1, jm.GetInt32());

            return await McpCommandQueue.DispatchAsync(() =>
            {
                try
                {
                    Map map = Find.CurrentMap;
                    if (map == null) return ToolResult.Error("没有当前地图。");

                    // 收集所有 Mineable 建筑，按 defName 分组
                    var groups = new Dictionary<string, (string label, string defName, List<IntVec3> positions)>();
                    foreach (var t in map.listerThings.AllThings)
                    {
                        if (t is not Building) continue;
                        if (t.Fogged()) continue;
                        if (!t.def.defName.StartsWith("Mineable")) continue;

                        var dn = t.def.defName;
                        if (!groups.ContainsKey(dn))
                            groups[dn] = (t.def.label ?? dn, dn, new List<IntVec3>());
                        groups[dn].positions.Add(t.Position);
                    }

                    // 关键词过滤
                    if (!string.IsNullOrEmpty(keyword))
                    {
                        groups = groups
                            .Where(kv => kv.Value.label.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                                      || kv.Value.defName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                    }

                    // 数量阈值
                    groups = groups.Where(kv => kv.Value.positions.Count >= minCount)
                                   .ToDictionary(kv => kv.Key, kv => kv.Value);

                    if (groups.Count == 0)
                    {
                        string hint = !string.IsNullOrEmpty(keyword)
                            ? $"未找到匹配 \"{keyword}\" 的矿脉。"
                            : "地图上没有任何可挖掘的矿脉。";
                        return ToolResult.Success(hint);
                    }

                    // 按数量降序
                    var sorted = groups.Values.OrderByDescending(g => g.positions.Count).ToList();

                    var sb = new StringBuilder();
                    sb.AppendLine($"## 可挖掘矿脉 共 {sorted.Count} 种，{sorted.Sum(g => g.positions.Count)} 块");
                    sb.AppendLine();

                    foreach (var g in sorted)
                    {
                        var positions = g.positions;
                        sb.AppendLine($"### {g.label} (`{g.defName}`) — {positions.Count} 块");

                        // 按 x 排序相邻坐标，每行输出 6 个坐标
                        var sortedPos = positions.OrderBy(p => p.x).ThenBy(p => p.z).ToList();
                        var coords = new List<string>();
                        foreach (var p in sortedPos)
                            coords.Add($"[{p.x},{p.z}]");
                        for (int i = 0; i < coords.Count; i += 6)
                        {
                            var chunk = coords.Skip(i).Take(6);
                            sb.AppendLine("  " + string.Join("  ", chunk));
                        }
                        sb.AppendLine();
                    }

                    return ToolResult.Success(sb.ToString().TrimEnd());
                }
                catch (Exception ex) { return ToolResult.Error($"扫描矿脉失败: {ex.Message}"); }
            });
        }

        public (int minX, int minZ, int maxX, int maxZ)? GetTargetRange(JsonElement? args)
        {
            // 全图扫描，不需要移动视角
            return null;
        }
    }
}
