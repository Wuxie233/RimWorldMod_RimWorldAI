using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Verse;

namespace RimWorldMCP.Tools
{
    /// <summary>
    /// 两级交互扫描可挖掘矿脉：
    /// 1. 不传 defName → 类型汇总表（名称 | defName | 数量）
    /// 2. 传 defName → 该类型坐标分页
    /// </summary>
    public class Tool_FindMineable : ITool
    {
        public string Name => "find_mineable";
        public string Description => "扫描全图可挖掘矿脉。默认列出所有类型汇总，传 defName 则按坐标分页查看。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                keyword = new { type = "string", description = "模糊匹配产出名称（可选），如 \"钢\"、\"零件\"。不填返回全部" },
                defName = new { type = "string", description = "精确 defName（可选）。不传 = 类型汇总表；传了 = 该类型坐标分页" },
                min_count = new { type = "integer", description = "最少矿脉数阈值（可选），汇总模式下只返回数量 >= 此值的类型", @default = 1 },
                page = new { type = "integer", description = "页码（1起始），坐标模式下控制该类型坐标页，默认1", @default = 1 },
                page_size = new { type = "integer", description = "每页坐标数，默认20，最大50", @default = 20 }
            }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            string keyword = "";
            if (args?.TryGetProperty("keyword", out var jk) == true)
                keyword = jk.GetString() ?? "";
            string defName = "";
            if (args?.TryGetProperty("defName", out var dn) == true)
                defName = dn.GetString() ?? "";
            else if (args?.TryGetProperty("thingDef", out var td) == true)
                defName = td.GetString() ?? "";
            int minCount = 1;
            if (args?.TryGetProperty("min_count", out var jm) == true)
                minCount = Math.Max(1, jm.GetInt32());
            int page = 1, pageSize = 20;
            if (args?.TryGetProperty("page", out var jp) == true) page = Math.Max(1, jp.GetInt32());
            if (args?.TryGetProperty("page_size", out var jps) == true) pageSize = Math.Max(1, Math.Min(50, jps.GetInt32()));

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

                        var dname = t.def.defName;
                        if (!groups.ContainsKey(dname))
                            groups[dname] = (t.def.label ?? dname, dname, new List<IntVec3>());
                        groups[dname].positions.Add(t.Position);
                    }

                    // 关键词过滤
                    if (!string.IsNullOrEmpty(keyword))
                    {
                        groups = groups
                            .Where(kv => kv.Value.label.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                                      || kv.Value.defName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                    }

                    if (groups.Count == 0)
                    {
                        string hint = !string.IsNullOrEmpty(keyword)
                            ? $"未找到匹配 \"{keyword}\" 的矿脉。"
                            : "地图上没有任何可挖掘的矿脉。";
                        return ToolResult.Success(hint);
                    }

                    // ──── 模式 A：指定 defName → 坐标分页 ────
                    if (!string.IsNullOrEmpty(defName))
                    {
                        if (!groups.TryGetValue(defName, out var g))
                            return ToolResult.Success($"未找到 defName=\"{defName}\" 的矿脉。可用类型: {string.Join(", ", groups.Keys)}");

                        var positions = g.positions.OrderBy(p => p.x).ThenBy(p => p.z).ToList();
                        int totalCoords = positions.Count;
                        int totalPages = (int)Math.Ceiling((double)totalCoords / pageSize);
                        if (page > totalPages) page = totalPages;
                        var paged = positions.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                        var sb = new StringBuilder();
                        sb.AppendLine($"## {g.label} (`{defName}`) — {totalCoords} 块");
                        sb.AppendLine();

                        var coords = paged.Select(p => $"[{p.x},{p.z}]").ToList();
                        for (int i = 0; i < coords.Count; i += 6)
                        {
                            var chunk = coords.Skip(i).Take(6);
                            sb.AppendLine("  " + string.Join("  ", chunk));
                        }

                        sb.AppendLine();
                        sb.AppendLine("---");
                        sb.Append($"第 {page}/{totalPages} 页");
                        if (page < totalPages) sb.Append($" | page={page + 1} 下一页");
                        if (page > 1) sb.Append($" | page={page - 1} 上一页");
                        return ToolResult.Success(sb.ToString());
                    }

                    // ──── 模式 B：汇总表 ────
                    var filtered = groups.Values
                        .Where(g => g.positions.Count >= minCount)
                        .OrderByDescending(g => g.positions.Count)
                        .ToList();

                    if (filtered.Count == 0)
                        return ToolResult.Success($"没有数量 >= {minCount} 的矿脉类型。");

                    int totalBlocks = filtered.Sum(g => g.positions.Count);

                    var summary = new StringBuilder();
                    summary.AppendLine($"## 可挖掘矿脉 共 {filtered.Count} 种，{totalBlocks} 块");
                    summary.AppendLine();
                    summary.AppendLine($"| 名称 | defName | 数量 |");
                    summary.AppendLine($"|------|---------|------|");
                    foreach (var g in filtered)
                        summary.AppendLine($"| {g.label} | `{g.defName}` | {g.positions.Count} 块 |");
                    summary.AppendLine();
                    summary.AppendLine("传 `defName` 查看具体坐标分页，如 `find_mineable(defName:\"MineableSteel\", page:1)`");

                    return ToolResult.Success(summary.ToString());
                }
                catch (Exception ex) { return ToolResult.Error($"扫描矿脉失败: {ex.Message}"); }
            });
        }

        public (int minX, int minZ, int maxX, int maxZ)? GetTargetRange(JsonElement? args)
        {
            return null;
        }
    }
}
