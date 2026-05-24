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
    public class Tool_RescuePawn : ITool
    {
        public string Name => "rescue_pawn";
        public string Description => "救援倒地受伤的殖民者/盟友。需要可用的医疗床。利用游戏 Job 系统（Rescue）。";
        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                colonist_name = new { type = "string", description = "执行救援的殖民者名称" },
                target_name = new { type = "string", description = "目标名称（倒地受伤者）" }
            },
            required = new[] { "colonist_name", "target_name" }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            if (args == null) return ToolResult.Error("缺少参数");
            if (!args.Value.TryGetProperty("colonist_name", out var jRescuer))
                return ToolResult.Error("缺少必填参数: colonist_name");

            string colonistName = jRescuer.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(colonistName))
                return ToolResult.Error("colonist_name 不能为空");

            if (!args.Value.TryGetProperty("target_name", out var jTarget))
                return ToolResult.Error("缺少必填参数: target_name");

            string targetName = jTarget.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(targetName))
                return ToolResult.Error("target_name 不能为空");

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
                        return ToolResult.Error($"找不到执行救援的殖民者: {colonistName}");

                    Map map = Find.CurrentMap;
                    if (map == null)
                        return ToolResult.Error("没有当前地图。");

                    // 在全部地图 Pawn 中查找目标
                    Pawn? target = map.mapPawns.AllPawnsSpawned.FirstOrDefault(c =>
                        c.Name.ToStringShort.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        c.Name.ToStringFull.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (target == null)
                        return ToolResult.Error($"找不到目标: {targetName}");

                    // 验证 —— 对齐 FloatMenuOptionProvider_RescuePawn
                    if (!HealthAIUtility.CanRescueNow(pawn, target, true))
                        return ToolResult.Error("无法救援该目标。");

                    if (target.mindState.WillJoinColonyIfRescued)
                        return ToolResult.Error("该目标救援后会加入，由其他流程处理。");

                    if (target.IsPrisonerOfColony || target.IsSlaveOfColony || target.IsColonyMech)
                        return ToolResult.Error("该目标已是俘虏/奴隶/已方机械体，无需救援。");

                    if (target.Faction != null && target.Faction.HostileTo(Faction.OfPlayer))
                        return ToolResult.Error("敌对目标，无法救援。");

                    if (!HealthAIUtility.ShouldSeekMedicalRest(target) && !target.ageTracker.CurLifeStage.alwaysDowned)
                        return ToolResult.Error("该目标无需医疗救援。");

                    if (target.playerSettings?.medCare == MedicalCareCategory.NoCare)
                        return ToolResult.Error("该目标医疗设置为无，无法救援。");

                    // 查找医疗床，先找非囚犯床再找囚犯床
                    Building_Bed? bed = RestUtility.FindBedFor(target, pawn, false, false, null) as Building_Bed;
                    if (bed == null)
                        bed = RestUtility.FindBedFor(target, pawn, false, true, null) as Building_Bed;

                    if (bed == null)
                        return ToolResult.Error("没有可用的医疗床。");

                    // 执行救援 Job
                    Job job = JobMaker.MakeJob(JobDefOf.Rescue, target, bed);
                    job.count = 1;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                    return ToolResult.Success($"小人已前往救援: {target.Name}");
                }
                catch (Exception ex)
                {
                    return ToolResult.Error($"救援失败: {ex.Message}");
                }
            });
        }
    }
}
