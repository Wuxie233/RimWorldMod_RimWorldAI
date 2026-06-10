using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Verse;
using RimWorld;
using RimWorldMCP;

namespace RimWorldMCP.Tools
{
    public class Tool_DesignateBuildRect : ITool
    {
        public string Name => "designate_build_rect";
        public string Description => "在矩形范围批量放置建造蓝图（地板/墙等）。fill_mode=fill 填满整个矩形（适合铺地板），perimeter 仅放边框（适合围墙）。坐标为闭区间。建筑类调用前建议先 get_structure_layout。默认仅在规划区内放置。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                thingDef_name = new { type = "string", description = "要建造的 DefName。墙=Wall, 木地板=WoodFloor 等" },
                pos_x = new { type = "integer", description = "起点 X 坐标（水平）" },
                pos_y = new { type = "integer", description = "起点 Y 坐标（垂直）" },
                end_x = new { type = "integer", description = "终点 X 坐标" },
                end_y = new { type = "integer", description = "终点 Y 坐标" },
                fill_mode = new { type = "string", description = "fill(填满整个矩形, 默认) / perimeter(仅边框, 适合围墙)", @enum = new[] { "fill", "perimeter" } },
                stuff_defName = new { type = "string", description = "建筑材料 DefName（可选，默认 Steel；地板忽略此项）" },
                check_plan = new { type = "boolean", description = "是否仅在规划区域内放置（默认 true，传 false 跳过）" }
            },
            required = new[] { "thingDef_name", "pos_x", "pos_y", "end_x", "end_y" }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            if (args == null) return ToolResult.Error("缺少参数");
            if (!args.Value.TryGetProperty("thingDef_name", out var jDef)) return ToolResult.Error("缺少必填参数: thingDef_name");
            if (!args.Value.TryGetProperty("pos_x", out var jX) || !jX.TryGetInt32(out var posX)) return ToolResult.Error("缺少必填参数: pos_x");
            if (!args.Value.TryGetProperty("pos_y", out var jY) || !jY.TryGetInt32(out var posY)) return ToolResult.Error("缺少必填参数: pos_y");
            if (!args.Value.TryGetProperty("end_x", out var jEx) || !jEx.TryGetInt32(out var endX)) return ToolResult.Error("缺少必填参数: end_x");
            if (!args.Value.TryGetProperty("end_y", out var jEy) || !jEy.TryGetInt32(out var endY)) return ToolResult.Error("缺少必填参数: end_y");

            var thingDefName = jDef.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(thingDefName)) return ToolResult.Error("thingDef_name 不能为空");

            var fillMode = args.Value.TryGetProperty("fill_mode", out var jFm) ? (jFm.GetString() ?? "fill") : "fill";
            var stuffDefName = args.Value.TryGetProperty("stuff_defName", out var jStuff) ? (jStuff.GetString() ?? "") : "";
            var checkPlan = !(args.Value.TryGetProperty("check_plan", out var jCp) && jCp.ValueKind == JsonValueKind.False);

            return await McpCommandQueue.DispatchAsync(() =>
            {
                try
                {
                    var map = Find.CurrentMap;
                    if (map == null) return ToolResult.Error("没有当前地图，请先加载游戏存档。");

                    var def = DefDatabase<ThingDef>.GetNamed(thingDefName, false);
                    var terrainDef = def == null ? DefDatabase<TerrainDef>.GetNamed(thingDefName, false) : null;
                    if (def == null && terrainDef == null)
                        return ToolResult.Error($"找不到 Def: {thingDefName}。用 search_thing_def(keyword=\"{thingDefName}\", category=\"building\") 查找。");

                    bool isFloor = terrainDef != null;

                    ThingDef? stuff = null;
                    if (!isFloor)
                    {
                        if (!string.IsNullOrEmpty(stuffDefName))
                        {
                            stuff = DefDatabase<ThingDef>.GetNamed(stuffDefName, false);
                            if (stuff == null) return ToolResult.Error($"找不到材料 ThingDef: {stuffDefName}");
                        }
                        else if (def!.MadeFromStuff)
                        {
                            stuff = ThingDef.Named("Steel");
                        }
                        if (stuff != null && !def!.MadeFromStuff)
                            return ToolResult.Error($"{def!.label} ({thingDefName}) 不支持材料选择，请勿指定 stuff_defName。");
                        if (def!.graphicData == null)
                            return ToolResult.Error($"{thingDefName} 缺少 graphicData 图形定义，无法创建设计器。");
                    }

                    int minX = Math.Min(posX, endX), maxX = Math.Max(posX, endX);
                    int minZ = Math.Min(posY, endY), maxZ = Math.Max(posY, endY);
                    var area = CellRect.FromLimits(minX, minZ, maxX, maxZ);
                    area.ClipInsideMap(map);
                    if (area.IsEmpty) return ToolResult.Error($"指定范围 ({minX},{minZ})~({maxX},{maxZ}) 完全在地图外。");

                    var designator = isFloor ? new Designator_Build(terrainDef!) : new Designator_Build(def!);
                    if (!isFloor && stuff != null) designator.SetStuffDef(stuff);

                    int designated = 0, skipped = 0;
                    foreach (var cell in area)
                    {
                        bool onPerimeter = cell.x == minX || cell.x == maxX || cell.z == minZ || cell.z == maxZ;
                        if (fillMode == "perimeter" && !onPerimeter) continue;
                        if (cell.Fogged(map)) { skipped++; continue; }
                        if (checkPlan && map.planManager.PlanAt(cell) == null) { skipped++; continue; }
                        if (!designator.CanDesignateCell(cell).Accepted) { skipped++; continue; }
                        designator.DesignateSingleCell(cell);
                        designated++;
                    }

                    var label = isFloor ? terrainDef!.label : def!.label;
                    var sb = new StringBuilder();
                    sb.Append($"已在范围 ({minX},{minZ})~({maxX},{maxZ}) 批量放置 {label} ({thingDefName})");
                    sb.Append(fillMode == "perimeter" ? "[边框]" : "[填满]");
                    sb.Append($"：{designated} 格（跳过 {skipped}）。");
                    if (!isFloor && stuff != null) sb.Append($" 材料: {stuff.label}。");
                    if (checkPlan) sb.Append(" 仅在规划区内放置（传 check_plan=false 可跳过）。");
                    return ToolResult.Success(sb.ToString());
                }
                catch (Exception ex) { return ToolResult.Error($"批量建造失败: {ex.Message}"); }
            });
        }

        public (int minX, int minZ, int maxX, int maxZ)? GetTargetRange(JsonElement? args)
        {
            if (args == null) return null;
            if (!args.Value.TryGetProperty("pos_x", out var jX) || !jX.TryGetInt32(out var posX)) return null;
            if (!args.Value.TryGetProperty("pos_y", out var jY) || !jY.TryGetInt32(out var posY)) return null;
            if (args.Value.TryGetProperty("end_x", out var jEX) && jEX.TryGetInt32(out var endX)
                && args.Value.TryGetProperty("end_y", out var jEY) && jEY.TryGetInt32(out var endY))
                return (Math.Min(posX, endX), Math.Min(posY, endY), Math.Max(posX, endX), Math.Max(posY, endY));
            return (posX, posY, posX, posY);
        }
    }
}
