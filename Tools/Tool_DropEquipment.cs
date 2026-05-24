using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using RimWorld;
using RimWorldMCP;

namespace RimWorldMCP.Tools
{
    public class Tool_DropEquipment : ITool
    {
        public string Name => "drop_equipment";
        public string Description => "强制殖民者丢弃当前装备的主武器。利用游戏 Job 系统（DropEquipment），小人将执行丢弃动作。";
        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                colonist_name = new { type = "string", description = "殖民者名称" }
            },
            required = new[] { "colonist_name" }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            if (args == null) return ToolResult.Error("缺少参数");
            if (!args.Value.TryGetProperty("colonist_name", out var jName))
                return ToolResult.Error("缺少必填参数: colonist_name");

            string colonistName = jName.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(colonistName))
                return ToolResult.Error("colonist_name 不能为空");

            return await McpCommandQueue.DispatchAsync(() =>
            {
                try
                {
                    var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                    if (colonists == null || colonists.Count == 0)
                        return ToolResult.Error("当前没有自由殖民者。");

                    Pawn pawn = colonists.FirstOrDefault(c =>
                        c.Name.ToStringShort.IndexOf(colonistName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        c.Name.ToStringFull.IndexOf(colonistName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (pawn == null)
                        return ToolResult.Error($"找不到殖民者: {colonistName}");

                    // 验证：是否有装备武器
                    if (pawn.equipment == null || pawn.equipment.Primary == null)
                        return ToolResult.Error($"{pawn.Name.ToStringShort} 没有装备武器。");

                    ThingWithComps weapon = pawn.equipment.Primary;

                    // 验证：任务旅居者限制
                    if (pawn.IsQuestLodger() && !EquipmentUtility.QuestLodgerCanUnequip(weapon, pawn))
                        return ToolResult.Error($"{pawn.Name.ToStringShort} 是任务旅居者，无法丢弃武器。");

                    // 执行丢弃
                    Job job = JobMaker.MakeJob(JobDefOf.DropEquipment, weapon);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                    return ToolResult.Success($"{pawn.Name.ToStringShort} 将丢弃武器: {weapon.Label}");
                }
                catch (Exception ex)
                {
                    return ToolResult.Error($"丢弃武器失败: {ex.Message}");
                }
            });
        }
    }
}
