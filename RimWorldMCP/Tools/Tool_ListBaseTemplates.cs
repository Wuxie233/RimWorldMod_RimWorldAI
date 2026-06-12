using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RimWorldMCP.Tools
{
    public class Tool_ListBaseTemplates : ITool, INoMapRequired
    {
        public string Name => "list_base_templates";
        public string Description => "列出所有可用的基地模板，供 AI 选择合适的模板来规划殖民地布局。返回每个模板的名称、描述、参数和适用场景。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new object(),
            required = new string[] { }
        });

        public Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 可用基地模板");
            sb.AppendLine();
            sb.AppendLine("使用 apply_base_template 工具传入模板名和中心点坐标来应用模板。");
            sb.AppendLine();

            // single_room
            sb.AppendLine("### single_room — 单房间");
            sb.AppendLine("标准矩形房间，四面墙体，可选门和地板。");
            sb.AppendLine("参数: internal_size=13 (内径), door_sides=bottom");
            sb.AppendLine("占地: (internal_size+2)×(internal_size+2) — 如内径13则外径15x15");
            sb.AppendLine("适用: 独立建筑、工坊、厨房、研究室、初期避难所");
            sb.AppendLine();

            // nine_grid
            sb.AppendLine("### nine_grid — 九宫格（3×3 房间矩阵）");
            sb.AppendLine("3行×3列共9个房间，相邻房间共用墙壁。中心房间四通八达，角房间两面向外。");
            sb.AppendLine("参数: internal_size=13 (每间内径)");
            sb.AppendLine("占地: 43×43（内径13时）");
            sb.AppendLine("建议: 中心作餐厅/娱乐室，外围作工坊/卧室/厨房/研究室/牢房/冷库/医院/仓库");
            sb.AppendLine("适用: 综合性基地核心区，紧凑高效");
            sb.AppendLine();

            // nine_grid_walled
            sb.AppendLine("### nine_grid_walled — 带围墙九宫格");
            sb.AppendLine("在 nine_grid 外加一圈外围防御墙（厚2格，默认花岗岩），墙与房间间留2格缓冲带。");
            sb.AppendLine("参数: internal_size=13 (每间内径), wall_thickness=2 (围墙厚度)");
            sb.AppendLine("占地: 51×51（内径13时）");
            sb.AppendLine("适用: 有防御需求的综合基地，适合中期建设");
            sb.AppendLine();

            // bedroom_row
            sb.AppendLine("### bedroom_row — 卧室排");
            sb.AppendLine("一排紧邻的小卧室，共用墙壁。每间朝同一方向开门，前有走廊空间。");
            sb.AppendLine("参数: count=5 (房间数), internal_width=5, internal_height=5 (每间内径)");
            sb.AppendLine("占地: (count×(internal_width+1)+1) × (internal_height+2) — 如5间5x5则31x7");
            sb.AppendLine("适用: 殖民者居住区，可多排并列");
            sb.AppendLine();

            sb.AppendLine("### starter_core — 开局核心区");
            sb.AppendLine("紧凑开局组合：临时宿舍/仓库、冷库、厨房、研究工坊。先活下来，后续可替换为专用房间。");
            sb.AppendLine("参数: room=9 (标准小房内径), freezer=7 (冷库内径)");
            sb.AppendLine("适用: 落地前3天、Crashlanded/富有探险家开局、先解决屋顶/储物/食物/研究。");
            sb.AppendLine();

            sb.AppendLine("### food_block — 冷库厨房餐娱区");
            sb.AppendLine("冷库、隔离厨房、餐厅/娱乐室三件套；冷库靠种植区，餐厅不穿过厨房取餐。");
            sb.AppendLine("参数: freezer_w=9, freezer_h=9, kitchen_w=7, kitchen_h=7, dining_w=13, dining_h=9");
            sb.AppendLine("适用: 中期稳定食物、减少搬运、降低食物中毒、堆叠餐厅娱乐心情加成。");
            sb.AppendLine();

            sb.AppendLine("### workshop_lab — 工坊研究仓储区");
            sb.AppendLine("普通仓库、生产工坊、研究室相邻；生产靠原料，研究室独立少走脏路。");
            sb.AppendLine("参数: room=13, storage_w=15, storage_h=11");
            sb.AppendLine("适用: 石材切割、裁缝、锻造、机械加工、研究台/高级研究台扩展。");
            sb.AppendLine();

            sb.AppendLine("### hospital_prison — 医疗监狱区");
            sb.AppendLine("医院居中、药品小库、监狱相邻但分门；靠近防线以缩短战后救治距离。");
            sb.AppendLine("参数: hospital_w=11, hospital_h=9, prison_w=9, prison_h=9, medicine_w=5, medicine_h=5");
            sb.AppendLine("适用: 战后救治、囚犯招募、感染控制、减少医生喂饭/治疗路程。");
            sb.AppendLine();

            sb.AppendLine("### greenhouse_block — 温室种植区");
            sb.AppendLine("两个种植室 + 中央通道/设备间，适合水栽培或太阳灯温室；靠近冷库。");
            sb.AppendLine("参数: grow_w=13, grow_h=13, service_w=5");
            sb.AppendLine("适用: 冬季/有毒尘埃/火山冬天/贫瘠地形的稳定食物和药草生产。");
            sb.AppendLine();

            sb.AppendLine("### killbox_trap_corridor — 陷阱走廊防线");
            sb.AppendLine("外侧引导走廊 + 内侧射击口，返回墙段、陷阱点、沙袋线和防守位建议。");
            sb.AppendLine("参数: corridor_len=15, corridor_w=3, firing_w=11");
            sb.AppendLine("适用: 中前期袭击、疯狂动物、低殖民者数量防守；不要当作唯一后期防线。");
            sb.AppendLine();

            sb.AppendLine("---");
            sb.AppendLine("选定模板后调用 apply_base_template(template_name, center_x, center_y, ...) 获取所有房间的精确坐标。");

            return Task.FromResult(ToolResult.Success(sb.ToString().TrimEnd()));
        }

        public (int minX, int minZ, int maxX, int maxZ)? GetTargetRange(JsonElement? args) => null;
    }
}
