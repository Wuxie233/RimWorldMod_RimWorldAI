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
    public class Tool_CapturePawn : ITool
    {
        public string Name => "capture_pawn";
        public string Description => "俘虏倒地敌人。需要可用的囚犯床。利用游戏 Job 系统（Capture）。";
        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                colonist_name = new { type = "string", description = "执行俘虏的殖民者名称" },
                target_name = new { type = "string", description = "目标名称（倒地敌人）" }
            },
            required = new[] { "colonist_name", "target_name" }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            if (args == null) return ToolResult.Error("缺少参数");
            if (!args.Value.TryGetProperty("colonist_name", out var jCapturer))
                return ToolResult.Error("缺少必填参数: colonist_name");

            string colonistName = jCapturer.GetString() ?? "";
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
                        return ToolResult.Error($"找不到执行俘虏的殖民者: {colonistName}");

                    Map map = Find.CurrentMap;
                    if (map == null)
                        return ToolResult.Error("没有当前地图。");

                    // 在全部地图 Pawn 中查找目标
                    Pawn? target = map.mapPawns.AllPawnsSpawned.FirstOrDefault(c =>
                        c.Name.ToStringShort.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        c.Name.ToStringFull.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (target == null)
                        return ToolResult.Error($"找不到目标: {targetName}");

                    // 验证 —— 对齐 FloatMenuOptionProvider_CapturePawn
                    if (!target.CanBeCaptured())
                        return ToolResult.Error("该目标无法被俘虏。");

                    if (!HealthAIUtility.CanRescueNow(pawn, target, true))
                        return ToolResult.Error("无法到达该目标进行俘虏。");

                    // 查找囚犯床
                    Building_Bed? bed = RestUtility.FindBedFor(target, pawn, false, false, GuestStatus.Prisoner) as Building_Bed;
                    if (bed == null)
                        bed = RestUtility.FindBedFor(target, pawn, false, true, GuestStatus.Prisoner) as Building_Bed;

                    if (bed == null)
                        return ToolResult.Error("没有可用的囚犯床。");

                    // 执行俘虏 Job
                    Job job = JobMaker.MakeJob(JobDefOf.Capture, target, bed);
                    job.count = 1;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                    // 演示俘虏概念（首次俘获时触发教程，无副作用）
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.Capturing, KnowledgeAmount.Total);

                    return ToolResult.Success($"小人已前往俘虏: {target.Name}");
                }
                catch (Exception ex)
                {
                    return ToolResult.Error($"俘虏失败: {ex.Message}");
                }
            });
        }
    }
}
