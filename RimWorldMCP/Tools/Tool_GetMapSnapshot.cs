using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace RimWorldMCP.Tools
{
    public class Tool_GetMapSnapshot : ITool
    {
        public string Name => "get_map_snapshot";
        public string Description => "获取指定区域的结构化地图快照（JSON，逐格返回 terrain/fertility/tempC/roofed/polluted，可选 things），替代符号网格便于精确决策。坐标闭区间；左下为原点(0,0)，x 向东、z 向北。单次最多返回 2000 格，超出请缩小范围或翻页。";

        private const int MaxCells = 2000;

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                pos_x = new { type = "integer", description = "左下角 X（闭区间，含此坐标）" },
                pos_y = new { type = "integer", description = "左下角 Y（映射到 z，闭区间，含此坐标）" },
                end_x = new { type = "integer", description = "右上角 X（可选，闭区间）" },
                end_y = new { type = "integer", description = "右上角 Y（可选，闭区间）" },
                include_things = new { type = "boolean", description = "是否包含每格物体列表（id/def/count），默认 false" },
                page = new { type = "integer", description = "页码，从 0 开始，默认 0" },
                page_size = new { type = "integer", description = $"每页格数，默认自适应，最大 {MaxCells}" }
            },
            required = new[] { "pos_x", "pos_y" }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            if (args == null) return ToolResult.Error("缺少参数");

            if (!args.Value.TryGetProperty("pos_x", out var jX) || !jX.TryGetInt32(out var posX))
                return ToolResult.Error("缺少必填参数: pos_x");
            if (!args.Value.TryGetProperty("pos_y", out var jY) || !jY.TryGetInt32(out var posY))
                return ToolResult.Error("缺少必填参数: pos_y");

            int endX = posX, endY = posY;
            if (args.Value.TryGetProperty("end_x", out var jEX) && jEX.TryGetInt32(out var ex)) endX = ex;
            if (args.Value.TryGetProperty("end_y", out var jEY) && jEY.TryGetInt32(out var ey)) endY = ey;

            bool includeThings = args.Value.TryGetProperty("include_things", out var jT)
                && (jT.ValueKind == JsonValueKind.True || (jT.ValueKind == JsonValueKind.String && jT.GetString() == "true"));

            int page = 0;
            if (args.Value.TryGetProperty("page", out var jP) && jP.TryGetInt32(out var p) && p >= 0) page = p;
            int? pageSizeArg = null;
            if (args.Value.TryGetProperty("page_size", out var jPS) && jPS.TryGetInt32(out var ps) && ps > 0) pageSizeArg = ps;

            int reqMinX = Math.Min(posX, endX), reqMaxX = Math.Max(posX, endX);
            int reqMinZ = Math.Min(posY, endY), reqMaxZ = Math.Max(posY, endY);

            return await McpCommandQueue.DispatchAsync(() =>
            {
                try
                {
                    var map = Find.CurrentMap;
                    if (map == null) return ToolResult.Error("当前没有可用地图。");

                    int minX = Math.Max(0, reqMinX);
                    int minZ = Math.Max(0, reqMinZ);
                    int maxX = Math.Min(map.Size.x - 1, reqMaxX);
                    int maxZ = Math.Min(map.Size.z - 1, reqMaxZ);
                    if (minX > maxX || minZ > maxZ)
                        return ToolResult.Error("查询范围在地图之外。");

                    int w = maxX - minX + 1;
                    int h = maxZ - minZ + 1;
                    int totalCells = w * h;

                    int pageSize = pageSizeArg ?? Math.Min(totalCells, MaxCells);
                    if (pageSize > MaxCells) pageSize = MaxCells;
                    if (pageSize < 1) pageSize = 1;
                    int startIndex = page * pageSize;

                    bool biotech = ModsConfig.BiotechActive;
                    var cells = new List<object>();
                    int index = -1;
                    for (int z = minZ; z <= maxZ && cells.Count < pageSize; z++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            index++;
                            if (index < startIndex) continue;
                            if (cells.Count >= pageSize) break;

                            var pos = new IntVec3(x, 0, z);
                            var terrain = map.terrainGrid.TerrainAt(pos);
                            List<object>? things = null;
                            if (includeThings)
                            {
                                things = new List<object>();
                                foreach (var t in pos.GetThingList(map))
                                {
                                    if (t?.def == null) continue;
                                    things.Add(new { id = t.thingIDNumber, def = t.def.defName, count = t.stackCount });
                                }
                            }

                            cells.Add(new
                            {
                                x,
                                z,
                                terrain = terrain?.defName,
                                fertility = (float)Math.Round(map.fertilityGrid.FertilityAt(pos), 2),
                                tempC = (float)Math.Round(GenTemperature.GetTemperatureForCell(pos, map), 1),
                                roofed = pos.Roofed(map),
                                polluted = biotech ? (bool?)map.pollutionGrid.IsPolluted(pos) : null,
                                things
                            });
                        }
                    }

                    var payload = new
                    {
                        map = new { sizeX = map.Size.x, sizeZ = map.Size.z },
                        range = new { x = minX, z = minZ, endX = maxX, endZ = maxZ },
                        page,
                        pageSize,
                        totalCells,
                        returnedCells = cells.Count,
                        includeThings,
                        biotech,
                        cells
                    };

                    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
                    return ToolResult.Success(json);
                }
                catch (Exception ex)
                {
                    McpLog.Error($"[get_map_snapshot] 失败: {ex.GetType().Name}: {ex.Message}");
                    return ToolResult.Error($"地图快照失败: {ex.Message}");
                }
            });
        }

        public (int minX, int minZ, int maxX, int maxZ)? GetTargetRange(JsonElement? args)
        {
            if (args == null) return null;
            if (!args.Value.TryGetProperty("pos_x", out var jX) || !jX.TryGetInt32(out var posX)) return null;
            if (!args.Value.TryGetProperty("pos_y", out var jY) || !jY.TryGetInt32(out var posY)) return null;
            int endX = posX, endY = posY;
            if (args.Value.TryGetProperty("end_x", out var jEX) && jEX.TryGetInt32(out var ex)) endX = ex;
            if (args.Value.TryGetProperty("end_y", out var jEY) && jEY.TryGetInt32(out var ey)) endY = ey;
            return (Math.Min(posX, endX), Math.Min(posY, endY), Math.Max(posX, endX), Math.Max(posY, endY));
        }
    }
}
