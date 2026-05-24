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
    public class Tool_ForceDress : ITool
    {
        public string Name => "force_dress";
        public string Description => "强制殖民者给另一位殖民者穿戴衣物。通过游戏 Job 系统（ForceTargetWear），A 将拿取衣物给 B 穿上。";
        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                colonist_name = new { type = "string", description = "执行穿戴操作的殖民者名称" },
                target_name = new { type = "string", description = "目标殖民者名称（被穿者）" },
                thing_defName = new { type = "string", description = "衣物 DefName" }
            },
            required = new[] { "colonist_name", "target_name", "thing_defName" }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            if (args == null) return ToolResult.Error("缺少参数");
            if (!args.Value.TryGetProperty("colonist_name", out var jDoer))
                return ToolResult.Error("缺少必填参数: colonist_name");

            string colonistName = jDoer.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(colonistName))
                return ToolResult.Error("colonist_name 不能为空");

            if (!args.Value.TryGetProperty("target_name", out var jTarget))
                return ToolResult.Error("缺少必填参数: target_name");

            string targetName = jTarget.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(targetName))
                return ToolResult.Error("target_name 不能为空");

            if (!args.Value.TryGetProperty("thing_defName", out var jDef))
                return ToolResult.Error("缺少必填参数: thing_defName");

            string thingDefName = jDef.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(thingDefName))
                return ToolResult.Error("thing_defName 不能为空");

            return await McpCommandQueue.DispatchAsync(() =>
            {
                try
                {
                    var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                    if (colonists == null || colonists.Count == 0)
                        return ToolResult.Error("当前没有自由殖民者。");

                    // 查找执行者
                    Pawn pawn = colonists.FirstOrDefault(c =>
                        c.Name.ToStringShort.IndexOf(colonistName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        c.Name.ToStringFull.IndexOf(colonistName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (pawn == null)
                        return ToolResult.Error($"找不到执行穿戴的殖民者: {colonistName}");

                    // 查找目标（被穿者）
                    Pawn targetPawn = colonists.FirstOrDefault(c =>
                        c.Name.ToStringShort.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        c.Name.ToStringFull.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (targetPawn == null)
                        return ToolResult.Error($"找不到目标殖民者: {targetName}");

                    if (pawn == targetPawn)
                        return ToolResult.Error("执行者和目标不能是同一人，请使用 equip_pawn 或 force_equip 自行装备。");

                    Map map = Find.CurrentMap;
                    if (map == null)
                        return ToolResult.Error("没有当前地图。");

                    // 查找衣物
                    var apparelList = map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel);
                    if (apparelList == null || apparelList.Count == 0)
                        return ToolResult.Error("地图上没有任何衣物。");

                    Thing? thing = apparelList.FirstOrDefault(t => t.def.defName == thingDefName);
                    if (thing == null)
                    {
                        var available = apparelList.Take(10).Select(t => $"{t.Label} ({t.def.defName})").ToArray();
                        return ToolResult.Error($"找不到匹配 '{thingDefName}' 的衣物。可用: {string.Join(", ", available)}");
                    }

                    // 验证 —— 对齐 FloatMenuOptionProvider_DressOtherPawn
                    Apparel apparel = thing as Apparel;
                    if (apparel == null)
                        return ToolResult.Error($"{thing.Label} 不是有效的衣物。");

                    if (!pawn.CanReach(apparel, PathEndMode.ClosestTouch, Danger.Deadly))
                        return ToolResult.Error($"{pawn.Name.ToStringShort} 无法到达 {apparel.Label}。");

                    if (apparel.IsBurning())
                        return ToolResult.Error($"{apparel.Label} 正在燃烧，无法穿戴。");

                    if (targetPawn.apparel == null)
                        return ToolResult.Error($"{targetPawn.Name.ToStringShort} 没有衣物管理器，无法穿戴。");

                    if (targetPawn.apparel.WouldReplaceLockedApparel(apparel))
                        return ToolResult.Error($"穿戴 {apparel.Label} 会替换 {targetPawn.Name.ToStringShort} 已锁定的衣物。");

                    if (targetPawn.IsMutant && targetPawn.mutant.Def.disableApparel)
                        return ToolResult.Error($"{targetPawn.Name.ToStringShort} 是变异体，无法穿戴衣物。");

                    if (!ApparelUtility.HasPartsToWear(targetPawn, apparel.def))
                        return ToolResult.Error($"{targetPawn.Name.ToStringShort} 没有适合穿戴 {apparel.Label} 的身体部位。");

                    if (!EquipmentUtility.CanEquip(apparel, targetPawn, out string reason, true))
                        return ToolResult.Error($"无法给 {targetPawn.Name.ToStringShort} 穿戴 {apparel.Label}：{reason}");

                    // 执行穿戴 Job
                    apparel.SetForbidden(false, true);
                    Job job = JobMaker.MakeJob(JobDefOf.ForceTargetWear, targetPawn, apparel);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                    return ToolResult.Success($"小人已前往给 {targetPawn.Name} 穿戴: {apparel.Label}");
                }
                catch (Exception ex)
                {
                    return ToolResult.Error($"强制穿戴失败: {ex.Message}");
                }
            });
        }
    }
}
