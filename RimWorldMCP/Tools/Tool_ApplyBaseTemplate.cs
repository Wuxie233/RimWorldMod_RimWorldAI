using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RimWorldMCP.Tools
{
    public class Tool_ApplyBaseTemplate : ITool
    {
        public string Name => "apply_base_template";
        public string Description => "应用基地模板，根据模板名和中心点坐标返回所有房间和墙壁的精确坐标。坐标可直接用于 designate_room。调用前先用 list_base_templates 查看可用模板。坐标范围为闭区间（两端坐标均包含）。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                template_name = new { type = "string", description = "模板名称: single_room, nine_grid, nine_grid_walled, bedroom_row, starter_core, food_block, workshop_lab, hospital_prison, greenhouse_block, killbox_trap_corridor" },
                center_x = new { type = "integer", description = "基地中心 X 坐标" },
                center_y = new { type = "integer", description = "基地中心 Y 坐标" },
                internal_size = new { type = "integer", description = "房间内径（默认 13，13=13x13内径/15x15外径）", @default = 13 },
                options = new { type = "string", description = "模板特定选项，JSON 格式字符串。single_room: {\"door_sides\":\"bottom\"}; bedroom_row: {\"count\":5,\"internal_width\":5,\"internal_height\":5}; nine_grid_walled: {\"wall_thickness\":2}; starter_core: {\"room\":9,\"freezer\":7}; food_block: {\"freezer_w\":9,\"freezer_h\":9}; killbox_trap_corridor: {\"corridor_len\":15,\"corridor_w\":3,\"firing_w\":11}" }
            },
            required = new[] { "template_name", "center_x", "center_y" }
        });

        public Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            if (args == null) return Task.FromResult(ToolResult.Error("缺少参数"));

            if (!args.Value.TryGetProperty("template_name", out var jName) || jName.GetString() is not string templateName)
                return Task.FromResult(ToolResult.Error("缺少必填参数: template_name"));

            if (!args.Value.TryGetProperty("center_x", out var jCx) || !jCx.TryGetInt32(out var centerX))
                return Task.FromResult(ToolResult.Error("缺少必填参数: center_x"));
            if (!args.Value.TryGetProperty("center_y", out var jCy) || !jCy.TryGetInt32(out var centerY))
                return Task.FromResult(ToolResult.Error("缺少必填参数: center_y"));

            int internalSize = 13;
            if (args.Value.TryGetProperty("internal_size", out var jIs) && jIs.TryGetInt32(out var isVal))
                internalSize = isVal;
            internalSize = ClampInt(internalSize, 3, 25);

            JsonElement? options = null;
            if (args.Value.TryGetProperty("options", out var jOpt) && jOpt.ValueKind == JsonValueKind.String)
            {
                try { options = JsonSerializer.Deserialize<JsonElement>(jOpt.GetString()!); }
                catch (Exception ex) { McpLog.Warn($"[ApplyBaseTemplate] JSON 解析失败: {ex.Message}"); }
            }

            return Task.FromResult(templateName switch
            {
                "single_room" => BuildSingleRoom(centerX, centerY, internalSize, options),
                "nine_grid" => BuildNineGrid(centerX, centerY, internalSize),
                "nine_grid_walled" => BuildNineGridWalled(centerX, centerY, internalSize, options),
                "bedroom_row" => BuildBedroomRow(centerX, centerY, options),
                "starter_core" => BuildStarterCore(centerX, centerY, options),
                "food_block" => BuildFoodBlock(centerX, centerY, options),
                "workshop_lab" => BuildWorkshopLab(centerX, centerY, options),
                "hospital_prison" => BuildHospitalPrison(centerX, centerY, options),
                "greenhouse_block" => BuildGreenhouseBlock(centerX, centerY, options),
                "killbox_trap_corridor" => BuildKillboxTrapCorridor(centerX, centerY, options),
                _ => ToolResult.Error($"未知模板: {templateName}。请用 list_base_templates 查看可用模板。")
            });
        }

        public (int minX, int minZ, int maxX, int maxZ)? GetTargetRange(JsonElement? args)
        {
            if (args == null) return null;
            if (!args.Value.TryGetProperty("center_x", out var jX) || !jX.TryGetInt32(out var cx)) return null;
            if (!args.Value.TryGetProperty("center_y", out var jY) || !jY.TryGetInt32(out var cy)) return null;
            return (cx, cy, cx, cy);
        }

        // ── single_room ──────────────────────────────────────────────
        private static ToolResult BuildSingleRoom(int cx, int cy, int internalSize, JsonElement? options)
        {
            string doorSides = "bottom";
            if (options != null && options.Value.TryGetProperty("door_sides", out var jDs))
                doorSides = jDs.GetString() ?? "bottom";

            int external = internalSize + 2;
            int posX = cx - external / 2;
            int posY = cy - external / 2;
            int endX = posX + external - 1;
            int endY = posY + external - 1;
            int internalArea = internalSize * internalSize;

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: single_room (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine("### 房间");
            sb.AppendLine($"- 范围: pos=({posX},{posY}) end=({endX},{endY})");
            sb.AppendLine($"- 内径: {internalSize}×{internalSize} = {internalArea} 格");
            sb.AppendLine($"- 外径: {external}×{external}（含墙体）");
            sb.AppendLine($"- 建议门: {doorSides}");
            sb.AppendLine();
            sb.AppendLine("### 建造");
            sb.AppendLine($"designate_room(pos_x={posX}, pos_y={posY}, end_x={endX}, end_y={endY}, door_positions={doorSides})");

            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        // ── nine_grid ─────────────────────────────────────────────────
        private static ToolResult BuildNineGrid(int cx, int cy, int internalSize)
        {
            int stride = internalSize + 1;       // +1 = shared wall
            int total = 3 * stride + 1;          // +1 = first room's left/top wall
            int originX = cx - total / 2;
            int originY = cy - total / 2;

            var rooms = new List<(int row, int col, int px, int py, int ex, int ey, string doors, string label)>();
            string[,] labels = { { "左下", "下中", "右下" }, { "左中", "中心", "右中" }, { "左上", "上中", "右上" } };

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int px = originX + col * stride;
                    int py = originY + row * stride;
                    int ex = px + internalSize + 1;
                    int ey = py + internalSize + 1;

                    // Door suggestions: outer-facing sides + center room connects to all
                    var doorList = new List<string>();
                    bool isTop = row == 2, isBottom = row == 0;
                    bool isLeft = col == 0, isRight = col == 2;
                    bool isCenter = row == 1 && col == 1;

                    if (isCenter)
                    {
                        doorList.AddRange(new[] { "top", "bottom", "left", "right" });
                    }
                    else
                    {
                        if (isTop) doorList.Add("top");
                        if (isBottom) doorList.Add("bottom");
                        if (isLeft) doorList.Add("left");
                        if (isRight) doorList.Add("right");
                    }

                    rooms.Add((row, col, px, py, ex, ey, string.Join(",", doorList), labels[row, col]));
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: nine_grid (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine($"网格: 3×3 房间，内径 {internalSize}×{internalSize}，共用墙，总占地 {total}×{total}");
            sb.AppendLine();

            sb.AppendLine("### 房间 (9间)");
            foreach (var (row, col, px, py, ex, ey, doors, label) in rooms)
            {
                int area = internalSize * internalSize;
                sb.AppendLine($"[{row},{col}] {label} — pos=({px},{py}) end=({ex},{ey}) 内部{area}格 建议门: {doors}");
            }

            sb.AppendLine();
            sb.AppendLine("### 建造顺序");
            sb.AppendLine("1. designate_room 建造所有9个房间（任意顺序，共用墙自动跳过）");

            sb.AppendLine();
            sb.AppendLine("### ASCII 布局");
            sb.AppendLine("   ┌────────┬────────┬────────┐");
            for (int row = 0; row < 3; row++)
            {
                sb.AppendLine("   │        │        │        │");
                var labels_row = new string[3];
                for (int col = 0; col < 3; col++)
                    labels_row[col] = $" [{row},{col}]";
                sb.AppendLine($"   │{labels_row[0],-8}│{labels_row[1],-8}│{labels_row[2],-8}│");
                sb.AppendLine("   │        │        │        │");
                if (row < 2)
                    sb.AppendLine("   ├────────┼────────┼────────┤");
                else
                    sb.AppendLine("   └────────┴────────┴────────┘");
            }

            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        // ── nine_grid_walled ──────────────────────────────────────────
        private static ToolResult BuildNineGridWalled(int cx, int cy, int internalSize, JsonElement? options)
        {
            int wallThickness = 2;
            if (options != null && options.Value.TryGetProperty("wall_thickness", out var jWt) && jWt.TryGetInt32(out var wt))
                wallThickness = wt;
            wallThickness = ClampInt(wallThickness, 1, 5);

            int buffer = 2; // gap between rooms and outer wall
            int stride = internalSize + 1;
            int innerTotal = 3 * stride + 1;
            int originX = cx - innerTotal / 2;
            int originY = cy - innerTotal / 2;

            // Inner rooms (same as nine_grid)
            var rooms = new List<(int row, int col, int px, int py, int ex, int ey, string doors, string label)>();
            string[,] labels = { { "左下", "下中", "右下" }, { "左中", "中心", "右中" }, { "左上", "上中", "右上" } };

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int px = originX + col * stride;
                    int py = originY + row * stride;
                    int ex = px + internalSize + 1;
                    int ey = py + internalSize + 1;

                    var doorList = new List<string>();
                    bool isTop = row == 2, isBottom = row == 0;
                    bool isLeft = col == 0, isRight = col == 2;
                    bool isCenter = row == 1 && col == 1;

                    if (isCenter) doorList.AddRange(new[] { "top", "bottom", "left", "right" });
                    else
                    {
                        if (isTop) doorList.Add("top");
                        if (isBottom) doorList.Add("bottom");
                        if (isLeft) doorList.Add("left");
                        if (isRight) doorList.Add("right");
                    }

                    rooms.Add((row, col, px, py, ex, ey, string.Join(",", doorList), labels[row, col]));
                }
            }

            // Outer wall segments
            int wallStartX = originX - buffer - wallThickness;
            int wallStartY = originY - buffer - wallThickness;
            int wallEndX = originX + innerTotal + buffer + wallThickness - 1;
            int wallEndY = originY + innerTotal + buffer + wallThickness - 1;
            int outerTotalW = innerTotal + 2 * buffer + 2 * wallThickness;
            int outerTotalH = innerTotal + 2 * buffer + 2 * wallThickness;

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: nine_grid_walled (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine($"内层: 3×3 房间，内径 {internalSize}×{internalSize}，共用墙");
            sb.AppendLine($"围墙: {wallThickness} 格厚，缓冲带 {buffer} 格");
            sb.AppendLine($"总占地: {outerTotalW}×{outerTotalH}");
            sb.AppendLine();

            sb.AppendLine("### 内层房间 (9间)");
            foreach (var (row, col, px, py, ex, ey, doors, label) in rooms)
            {
                int area = internalSize * internalSize;
                sb.AppendLine($"[{row},{col}] {label} — pos=({px},{py}) end=({ex},{ey}) 内部{area}格 建议门: {doors}");
            }

            sb.AppendLine();
            sb.AppendLine("### 外围防御墙 (4段)");
            sb.AppendLine("用 designate_room 建造（不设门和地板，纯墙体）:");
            sb.AppendLine(
                $"- 南墙: pos=({wallStartX},{wallStartY}) end=({wallEndX},{wallStartY + wallThickness - 1})");
            sb.AppendLine(
                $"- 北墙: pos=({wallStartX},{wallEndY - wallThickness + 1}) end=({wallEndX},{wallEndY})");
            sb.AppendLine(
                $"- 西墙: pos=({wallStartX},{wallStartY + wallThickness}) end=({wallStartX + wallThickness - 1},{wallEndY - wallThickness})");
            sb.AppendLine(
                $"- 东墙: pos=({wallEndX - wallThickness + 1},{wallStartY + wallThickness}) end=({wallEndX},{wallEndY - wallThickness})");

            sb.AppendLine();
            sb.AppendLine("### 建造顺序");
            sb.AppendLine("1. designate_room 建造内层9个房间（任意顺序）");
            sb.AppendLine("2. designate_room 建造4段外围墙（北→南→西→东，不设门）");

            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        // ── bedroom_row ────────────────────────────────────────────────
        private static ToolResult BuildBedroomRow(int cx, int cy, JsonElement? options)
        {
            int count = 5;
            int internalW = 5;
            int internalH = 5;

            if (options != null)
            {
                if (options.Value.TryGetProperty("count", out var jCt) && jCt.TryGetInt32(out var ct)) count = ct;
                if (options.Value.TryGetProperty("internal_width", out var jIw) && jIw.TryGetInt32(out var iw)) internalW = iw;
                if (options.Value.TryGetProperty("internal_height", out var jIh) && jIh.TryGetInt32(out var ih)) internalH = ih;
            }
            count = ClampInt(count, 1, 50);
            internalW = ClampInt(internalW, 3, 25);
            internalH = ClampInt(internalH, 3, 25);

            int stride = internalW + 1;              // +1 = shared wall
            int rowWidth = count * stride + 1;       // +1 = first room's left wall
            int rowHeight = internalH + 2;           // +2 = top + bottom walls

            int originX = cx - rowWidth / 2;
            int originY = cy - rowHeight / 2;

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: bedroom_row (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine($"{count} 间卧室，内径 {internalW}×{internalH}，共用墙，总占地 {rowWidth}×{rowHeight}");
            sb.AppendLine();

            sb.AppendLine("### 房间");
            for (int i = 0; i < count; i++)
            {
                int px = originX + i * stride;
                int py = originY;
                int ex = px + internalW + 1;
                int ey = py + internalH + 1;
                int area = internalW * internalH;
                sb.AppendLine($"[{i}] 卧室{i + 1} — pos=({px},{py}) end=({ex},{ey}) 内部{area}格 建议门: bottom");
            }

            sb.AppendLine();
            sb.AppendLine("### 建造顺序");
            sb.AppendLine($"designate_room 从左到右逐一建造 {count} 间卧室（共用墙自动跳过）");

            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        private static int ReadInt(JsonElement? options, string key, int defaultValue)
        {
            if (options != null && options.Value.TryGetProperty(key, out var value) && value.TryGetInt32(out var parsed))
                return parsed;
            return defaultValue;
        }

        private static int ReadClampedInt(JsonElement? options, string key, int defaultValue, int min, int max)
        {
            return ClampInt(ReadInt(options, key, defaultValue), min, max);
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static void AppendRoom(StringBuilder sb, string label, int px, int py, int ex, int ey, string doors, string note)
        {
            int internalW = Math.Max(0, ex - px - 1);
            int internalH = Math.Max(0, ey - py - 1);
            sb.AppendLine($"- {label}: pos=({px},{py}) end=({ex},{ey}) 内径约 {internalW}×{internalH} 建议门: {doors}");
            if (!string.IsNullOrWhiteSpace(note)) sb.AppendLine($"  - 用途: {note}");
            sb.AppendLine($"  - designate_room(pos_x={px}, pos_y={py}, end_x={ex}, end_y={ey}, door_positions=\"{doors}\")");
        }

        private static void AppendWallSegment(StringBuilder sb, string label, int px, int py, int ex, int ey)
        {
            sb.AppendLine($"- {label}: pos=({px},{py}) end=({ex},{ey})");
            sb.AppendLine($"  - 先 plan_add(pos_x={px}, pos_y={py}, end_x={ex}, end_y={ey})，再 designate_room(pos_x={px}, pos_y={py}, end_x={ex}, end_y={ey})");
        }

        private static ToolResult BuildStarterCore(int cx, int cy, JsonElement? options)
        {
            int room = ReadClampedInt(options, "room", 9, 5, 25);
            int freezer = ReadClampedInt(options, "freezer", 7, 5, 25);
            int roomExt = room + 2;
            int freezerExt = freezer + 2;
            int totalW = Math.Max(roomExt * 2 - 1, freezerExt * 2 - 1);
            int totalH = roomExt + freezerExt - 1;
            int originX = cx - totalW / 2;
            int originY = cy - totalH / 2;
            int topY = originY + freezerExt - 1;

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: starter_core (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine("开局优先级: 先封闭屋顶与室内存储，再补冷库/厨房/研究；可以用木墙临时落地，之后换石墙。");
            sb.AppendLine();
            sb.AppendLine("### 房间");
            AppendRoom(sb, "冷库/易腐品库", originX, originY, originX + freezerExt - 1, originY + freezerExt - 1, "right", "靠近厨房，后续加双墙、气闸和冷机，温度 -1°C 到 -10°C");
            AppendRoom(sb, "厨房/屠宰间", originX + freezerExt - 1, originY, originX + freezerExt * 2 - 2, originY + freezerExt - 1, "left,top", "与冷库相邻但隔离，保持清洁，先营火/炉灶后电炉");
            AppendRoom(sb, "临时宿舍/普通仓库", originX, topY, originX + roomExt - 1, topY + roomExt - 1, "bottom,right", "开局床位、药品、零部件、装备先进屋，后续改餐厅/仓库");
            AppendRoom(sb, "研究/手工工坊", originX + roomExt - 1, topY, originX + roomExt * 2 - 2, topY + roomExt - 1, "bottom,left", "研究台、手工点、石材切割，避免殖民者空闲");
            sb.AppendLine();
            sb.AppendLine("### 后续动作");
            sb.AppendLine("1. 冷库外墙补第二层墙和双门气闸。");
            sb.AppendLine("2. 厨房只让厨师和清洁工高频进入，降低食物中毒。");
            sb.AppendLine("3. 宿舍过渡到独立卧室后，把临时宿舍改餐厅/娱乐室。");
            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        private static ToolResult BuildFoodBlock(int cx, int cy, JsonElement? options)
        {
            int freezerW = ReadClampedInt(options, "freezer_w", 9, 5, 25);
            int freezerH = ReadClampedInt(options, "freezer_h", 9, 5, 25);
            int kitchenW = ReadClampedInt(options, "kitchen_w", 7, 5, 25);
            int kitchenH = ReadClampedInt(options, "kitchen_h", 7, 5, 25);
            int diningW = ReadClampedInt(options, "dining_w", 13, 7, 31);
            int diningH = ReadClampedInt(options, "dining_h", 9, 7, 25);
            int freezerExtW = freezerW + 2, freezerExtH = freezerH + 2;
            int kitchenExtW = kitchenW + 2, kitchenExtH = kitchenH + 2;
            int diningExtW = diningW + 2, diningExtH = diningH + 2;
            int totalW = freezerExtW + kitchenExtW + diningExtW - 2;
            int totalH = Math.Max(freezerExtH, Math.Max(kitchenExtH, diningExtH));
            int originX = cx - totalW / 2;
            int originY = cy - totalH / 2;
            int kitchenX = originX + freezerExtW - 1;
            int diningX = kitchenX + kitchenExtW - 1;

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: food_block (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine("冷库、厨房、餐厅/娱乐室一字相邻；餐厅可直接拿饭，不穿过厨房，减少脏污和食物中毒。");
            sb.AppendLine();
            sb.AppendLine("### 房间");
            AppendRoom(sb, "双墙冷库", originX, originY, originX + freezerExtW - 1, originY + freezerExtH - 1, "right", "靠种植区和屠宰点；加气闸、2台冷机冗余，食物设 critical/preferred");
            AppendRoom(sb, "隔离厨房", kitchenX, originY, kitchenX + kitchenExtW - 1, originY + kitchenExtH - 1, "left,right", "炉灶贴近冷库门，屠宰台可分隔或放同室早期过渡");
            AppendRoom(sb, "餐厅/娱乐室", diningX, originY, diningX + diningExtW - 1, originY + diningExtH - 1, "left,bottom", "桌椅、娱乐、雕塑和好地板叠心情，是全殖民地最划算美观房");
            sb.AppendLine();
            sb.AppendLine("### 运营建议");
            sb.AppendLine("- 冷库温度先设 -1°C 到 -10°C；热带/热浪用更低温和双冷机。");
            sb.AppendLine("- 厨房铺清洁地板，限制穿行；餐厅保持明亮、美观、靠近卧室和工作区。");
            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        private static ToolResult BuildWorkshopLab(int cx, int cy, JsonElement? options)
        {
            int room = ReadClampedInt(options, "room", 13, 7, 25);
            int storageW = ReadClampedInt(options, "storage_w", 15, 7, 31);
            int storageH = ReadClampedInt(options, "storage_h", 11, 7, 25);
            int storageExtW = storageW + 2, storageExtH = storageH + 2;
            int roomExt = room + 2;
            int totalW = storageExtW + roomExt * 2 - 2;
            int totalH = Math.Max(storageExtH, roomExt);
            int originX = cx - totalW / 2;
            int originY = cy - totalH / 2;
            int workshopX = originX + storageExtW - 1;
            int labX = workshopX + roomExt - 1;

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: workshop_lab (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine("把原料仓、工坊、研究室并排，减少钢铁/零部件/布料搬运；研究室保持少通行、干净、可后续铺无菌地板。");
            sb.AppendLine();
            sb.AppendLine("### 房间");
            AppendRoom(sb, "原料/成品仓", originX, originY, originX + storageExtW - 1, originY + storageExtH - 1, "right", "钢铁、零部件、药品、布料、武器护甲；用货架和筛选分区");
            AppendRoom(sb, "综合工坊", workshopX, originY, workshopX + roomExt - 1, originY + roomExt - 1, "left,right", "石材切割、裁缝、锻造、机械加工、工具柜和局部原料架");
            AppendRoom(sb, "研究室", labX, originY, labX + roomExt - 1, originY + roomExt - 1, "left", "普通/高级研究台、多重分析仪、无菌地板、舒适椅和灯");
            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        private static ToolResult BuildHospitalPrison(int cx, int cy, JsonElement? options)
        {
            int hospitalW = ReadClampedInt(options, "hospital_w", 11, 7, 25);
            int hospitalH = ReadClampedInt(options, "hospital_h", 9, 7, 25);
            int prisonW = ReadClampedInt(options, "prison_w", 9, 5, 25);
            int prisonH = ReadClampedInt(options, "prison_h", 9, 5, 25);
            int medicineW = ReadClampedInt(options, "medicine_w", 5, 3, 15);
            int medicineH = ReadClampedInt(options, "medicine_h", 5, 3, 15);
            int hospitalExtW = hospitalW + 2, hospitalExtH = hospitalH + 2;
            int medicineExtW = medicineW + 2, medicineExtH = medicineH + 2;
            int prisonExtW = prisonW + 2, prisonExtH = prisonH + 2;
            int totalW = hospitalExtW + medicineExtW + prisonExtW - 2;
            int totalH = Math.Max(hospitalExtH, Math.Max(medicineExtH, prisonExtH));
            int originX = cx - totalW / 2;
            int originY = cy - totalH / 2;
            int medicineX = originX + hospitalExtW - 1;
            int prisonX = medicineX + medicineExtW - 1;

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: hospital_prison (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine("医院靠近防线，药品在中间，监狱独立但不远；医生治疗、喂饭、招募路径短，越狱也更容易封堵。");
            sb.AppendLine();
            sb.AppendLine("### 房间");
            AppendRoom(sb, "医院/急救室", originX, originY, originX + hospitalExtW - 1, originY + hospitalExtH - 1, "right,bottom", "医疗床、生命体征监测仪、无菌地板、电视和灯；战后第一优先级");
            AppendRoom(sb, "药品小库", medicineX, originY, medicineX + medicineExtW - 1, originY + medicineExtH - 1, "left,right", "草药/普通药/闪耀世界医药 critical，避免室外腐坏");
            AppendRoom(sb, "监狱/招募间", prisonX, originY, prisonX + prisonExtW - 1, originY + prisonExtH - 1, "left,bottom", "囚犯床、桌椅、温控；不与医院同室，防止心情和感染问题");
            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        private static ToolResult BuildGreenhouseBlock(int cx, int cy, JsonElement? options)
        {
            int growW = ReadClampedInt(options, "grow_w", 13, 7, 25);
            int growH = ReadClampedInt(options, "grow_h", 13, 7, 25);
            int serviceW = ReadClampedInt(options, "service_w", 5, 3, 15);
            int growExtW = growW + 2, growExtH = growH + 2;
            int serviceExtW = serviceW + 2;
            int totalW = growExtW * 2 + serviceExtW - 2;
            int originX = cx - totalW / 2;
            int originY = cy - growExtH / 2;
            int serviceX = originX + growExtW - 1;
            int rightGrowX = serviceX + serviceExtW - 1;

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: greenhouse_block (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine("双温室加中央设备通道；用于太阳灯/水栽培或普通室内种植，适合冬季、有毒尘埃、火山冬天和贫瘠地图。");
            sb.AppendLine();
            sb.AppendLine("### 房间");
            AppendRoom(sb, "左温室", originX, originY, originX + growExtW - 1, originY + growExtH - 1, "right", "水稻/药草/魔鬼菇等关键作物，按电力能力逐步铺设太阳灯或水栽培盆");
            AppendRoom(sb, "设备/气闸通道", serviceX, originY, serviceX + serviceExtW - 1, originY + growExtH - 1, "left,right,bottom", "加热器、冷机、电池隔离门、灭火通道；不要堆易燃物");
            AppendRoom(sb, "右温室", rightGrowX, originY, rightGrowX + growExtW - 1, originY + growExtH - 1, "left", "长期粮食或经济作物，靠近冷库减少收获搬运");
            sb.AppendLine();
            sb.AppendLine("### 种植建议");
            sb.AppendLine("- 生存压力大先 Plant_Rice；长期储备改 Plant_Corn；贫瘠土/低肥力选 Plant_Potato；尽早补 Plant_Healroot。");
            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        private static ToolResult BuildKillboxTrapCorridor(int cx, int cy, JsonElement? options)
        {
            int corridorLen = ReadClampedInt(options, "corridor_len", 15, 5, 80);
            int corridorW = ReadClampedInt(options, "corridor_w", 3, 1, 9);
            int firingW = ReadClampedInt(options, "firing_w", 11, 3, 31);
            int corridorLeft = cx - corridorW / 2;
            int corridorRight = corridorLeft + corridorW - 1;
            int startY = cy - corridorLen / 2;
            int endY = startY + corridorLen - 1;
            int sandbagY = endY + 1;
            int shooterY = endY + 2;
            int firingLeft = cx - firingW / 2;
            int firingRight = firingLeft + firingW - 1;

            var sb = new StringBuilder();
            sb.AppendLine($"## 模板: killbox_trap_corridor (中心: {cx}, {cy})");
            sb.AppendLine();
            sb.AppendLine("南侧进敌，北侧射击。它是中前期陷阱/掩体防线，不要替代后期多层防御、EMP、迫击炮和备用入口。");
            sb.AppendLine();
            sb.AppendLine("### 墙段");
            AppendWallSegment(sb, "西侧引导墙", corridorLeft - 1, startY, corridorLeft - 1, endY);
            AppendWallSegment(sb, "东侧引导墙", corridorRight + 1, startY, corridorRight + 1, endY);
            AppendWallSegment(sb, "射击位后墙", firingLeft, shooterY + 1, firingRight, shooterY + 1);
            sb.AppendLine();
            sb.AppendLine("### 陷阱点（用 WoodenSpikeTrap/SteelSpikeTrap 逐格建造）");
            for (int y = startY + 1; y < endY; y += 2)
            {
                int x = corridorLeft + ((y - startY) % corridorW);
                if (x > corridorRight) x = corridorRight;
                sb.AppendLine($"- trap: ({x},{y}) — designate_build(thingDef_name=\"TrapSpike\", pos_x={x}, pos_y={y})");
            }
            sb.AppendLine();
            sb.AppendLine("### 掩体与防守位");
            sb.AppendLine($"- 沙袋线: x={firingLeft}..{firingRight}, y={sandbagY} — 逐格 designate_build(thingDef_name=\"Sandbags\")");
            sb.AppendLine("- 防守位:");
            var defendPositions = new List<string>();
            for (int x = firingLeft; x <= firingRight; x += 2)
            {
                sb.AppendLine($"  - ({x},{shooterY}) behind sandbag");
                defendPositions.Add($"{{pos_x:{x},pos_y:{shooterY},label:\"killbox\",priority:1}}");
            }
            sb.AppendLine($"- 可登记: defend_position(action=\"set\", positions=[{string.Join(",", defendPositions)}])");
            sb.AppendLine();
            sb.AppendLine("### 使用规则");
            sb.AppendLine("1. 敌人来袭先 find_enemies(show_movement=true)，确认会走入口而不是绕墙/打墙。");
            sb.AppendLine("2. 远程站沙袋后，近战/盾牌在门口堵位，战斗始终 1 倍速。");
            sb.AppendLine("3. 补陷阱会消耗大量木材/钢铁，不要在食物或医疗危机时过度扩建。");
            return ToolResult.Success(sb.ToString().TrimEnd());
        }
    }
}
