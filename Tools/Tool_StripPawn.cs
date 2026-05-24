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
    public class Tool_StripPawn : ITool
    {
        public string Name => "strip_pawn";
        public string Description => "强制殖民者剥除目标（尸体或活体）的所有衣物和装备。利用游戏 Job 系统（Strip）。";
        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                colonist_name = new { type = "string", description = "执行剥除的殖民者名称" },
                target_name = new { type = "string", description = "目标名称（尸体或活体）" }
            },
            required = new[] { "colonist_name", "target_name" }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            if (args == null) return ToolResult.Error("缺少参数");
            if (!args.Value.TryGetProperty("colonist_name", out var jName))
                return ToolResult.Error("缺少必填参数: colonist_name");

            string colonistName = jName.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(colonistName))
                return ToolResult.Error("colonist_name 不能为空");

            string targetName = "";
            if (args.Value.TryGetProperty("target_name", out var jTarget))
                targetName = jTarget.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(targetName))
                return ToolResult.Error("缺少必填参数: target_name");

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

                    Map map = Find.CurrentMap;
                    if (map == null)
                        return ToolResult.Error("没有当前地图。");

                    // 查找目标：活体殖民者/囚犯/地图上所有活体 + 尸体
                    var allLivingTargets = PawnsFinder.AllMaps_FreeColonistsSpawned
                        .Concat(PawnsFinder.AllMaps_PrisonersOfColonySpawned)
                        .Concat(map.mapPawns.AllPawnsSpawned);

                    Thing target = allLivingTargets.FirstOrDefault(c =>
                        c.Name.ToStringShort.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        c.Name.ToStringFull.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (target == null)
                    {
                        // 搜索尸体
                        var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
                        if (corpses != null && corpses.Count > 0)
                        {
                            target = corpses.FirstOrDefault(c =>
                            {
                                Corpse corpse = c as Corpse;
                                if (corpse?.InnerPawn?.Name == null) return false;
                                return corpse.InnerPawn.Name.ToStringShort.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       corpse.InnerPawn.Name.ToStringFull.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0;
                            });
                        }
                    }

                    if (target == null)
                        return ToolResult.Error($"找不到目标: {targetName}");

                    // 验证：是否可以剥除
                    if (!StrippableUtility.CanBeStrippedByColony(target))
                        return ToolResult.Error($"{target.Label} 无法被剥除。");

                    // 验证：可达性
                    if (!pawn.CanReach(target, PathEndMode.ClosestTouch, Danger.Deadly))
                        return ToolResult.Error($"{pawn.Name.ToStringShort} 无法到达 {target.Label}。");

                    // 验证：任务相关（仅活体）
                    Pawn targetPawn = target as Pawn;
                    if (targetPawn != null && targetPawn.HasExtraHomeFaction((Quest)null))
                        return ToolResult.Error($"{target.Label} 与任务相关，无法剥除。");

                    // 执行剥除
                    target.SetForbidden(false, false);
                    StrippableUtility.CheckSendStrippingImpactsGoodwillMessage(target);
                    Job job = JobMaker.MakeJob(JobDefOf.Strip, target);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                    return ToolResult.Success($"{pawn.Name.ToStringShort} 已前往剥除: {target.Label}");
                }
                catch (Exception ex)
                {
                    return ToolResult.Error($"剥除失败: {ex.Message}");
                }
            });
        }
    }
}
