using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RimWorld;
using RimWorldMCP.Helpers;
using Verse;

namespace RimWorldMCP.Tools
{
    /// <summary>
    /// 全局状态快照 — 一次调用拿到时间/速度/殖民者/食物/资源/电力/研究/威胁/天气/警报/建造 的紧凑 JSON。
    /// 供 agent 决策前快速态势感知；每次实时计算，绝不缓存（游戏数据易变）。
    /// </summary>
    public class Tool_GetStateSnapshot : ITool
    {
        public string Name => "get_state_snapshot";
        public string Description => "一次性获取殖民地全局状态快照(JSON)：时间/倍速/殖民者/食物/资源/电力/研究/威胁/天气/警报/建造。决策前先调它快速了解全局，避免逐个慢查。include_details=true 附殖民者/敌人/腐坏明细。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                include_details = new { type = "boolean", description = "是否附带殖民者/附近敌人/腐坏物品明细（默认 false）" }
            }
        });

        public async Task<ToolResult> ExecuteAsync(JsonElement? args)
        {
            bool includeDetails = false;
            if (args != null && args.Value.TryGetProperty("include_details", out var jDetails) && jDetails.ValueKind == JsonValueKind.True)
                includeDetails = true;
            else if (args != null && args.Value.TryGetProperty("include_details", out var jDetails2) && jDetails2.ValueKind == JsonValueKind.False)
                includeDetails = false;

            return await McpCommandQueue.DispatchAsync(() =>
            {
                try
                {
                    var map = Find.CurrentMap;
                    var tm = Find.TickManager;
                    if (map == null || tm == null)
                        return ToolResult.Success(JsonSerializer.Serialize(new { map_loaded = false }));

                    int ticks = tm.TicksGame;
                    var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                    int total = colonists.Count;

                    int idle = 0, breakRisk = 0, healthIssue = 0;
                    foreach (var pawn in colonists)
                    {
                        var mood = pawn.needs?.mood?.CurLevelPercentage ?? 1f;
                        if (mood < 0.35f) breakRisk++;
                        if (pawn.mindState?.IsIdle == true) idle++;
                        if (pawn.health.hediffSet.HasTendableHediff()) healthIssue++;
                    }

                    var res = map.resourceCounter;
                    float foodDays = 0f;
                    if (total > 0 && res != null)
                    {
                        foreach (var kv in res.AllCountedAmounts)
                        {
                            if (kv.Key.IsNutritionGivingIngestible && kv.Key.ingestible?.HumanEdible == true)
                                foodDays += kv.Value * kv.Key.ingestible.CachedNutrition / (total * 1.6f);
                        }
                    }

                    var keyMaterials = new Dictionary<string, int>
                    {
                        { "steel", res?.GetCount(ThingDefOf.Steel) ?? 0 },
                        { "wood", res?.GetCount(ThingDefOf.WoodLog) ?? 0 },
                        { "components", res?.GetCount(ThingDefOf.ComponentIndustrial) ?? 0 },
                        { "silver", res?.GetCount(ThingDefOf.Silver) ?? 0 },
                        { "plasteel", res?.GetCount(ThingDefOf.Plasteel) ?? 0 },
                        { "cloth", res?.GetCount(ThingDefOf.Cloth) ?? 0 },
                    };

                    float generated = 0f, used = 0f, stored = 0f, storedMax = 0f;
                    if (map.powerNetManager?.AllNetsListForReading != null)
                    {
                        foreach (var net in map.powerNetManager.AllNetsListForReading)
                        {
                            foreach (var comp in net.powerComps)
                            {
                                if (comp.PowerOutput > 0) generated += comp.PowerOutput;
                                else if (comp.PowerOutput < 0) used += -comp.PowerOutput;
                            }
                            foreach (var batt in net.batteryComps)
                            {
                                stored += batt.StoredEnergy;
                                storedMax += batt.Props.storedEnergyMax;
                            }
                        }
                    }

                    string researchName = "无";
                    int researchPct = 0;
                    var rm = Find.ResearchManager;
                    var curProj = rm?.GetProject();
                    if (curProj != null)
                    {
                        researchName = curProj.LabelCap;
                        if (curProj.baseCost > 0)
                            researchPct = (int)Math.Max(0f, Math.Min(100f, rm.GetProgress(curProj) / curProj.baseCost * 100f));
                    }

                    var center = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
                    int enemiesAlive = 0;
                    int nearestDist = -1;
                    var hostiles = new List<Pawn>();
                    foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        if (pawn.Faction == null || !pawn.Faction.HostileTo(Faction.OfPlayer)) continue;
                        if (pawn.Downed) continue;
                        enemiesAlive++;
                        hostiles.Add(pawn);
                        int dist = (int)pawn.Position.DistanceTo(center);
                        if (nearestDist < 0 || dist < nearestDist) nearestDist = dist;
                    }

                    int blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)?.Count ?? 0;
                    int frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)?.Count ?? 0;

                    int criticalCount = 0, warningCount = 0;
                    var alertSummary = new List<string>();
                    try
                    {
                        var alerts = NativeAlertHelper.GetActiveAlerts();
                        foreach (var a in alerts.OrderByDescending(a => a.Priority))
                        {
                            if (a.Priority >= 2) criticalCount++;
                            else if (a.Priority == 1) warningCount++;
                            if (alertSummary.Count < 5) alertSummary.Add(a.Label);
                        }
                    }
                    catch (Exception ex) { McpLog.Warn($"[Snapshot] 读取警报失败: {ex.Message}"); }

                    var snapshot = new Dictionary<string, object>
                    {
                        ["map_loaded"] = true,
                        ["time"] = new
                        {
                            day = ticks / 60000,
                            hour = (ticks / 2500) % 24,
                            season = GenLocalDate.Season(map).ToString(),
                            day_of_quadrum = GenLocalDate.DayOfQuadrum(map),
                        },
                        ["speed"] = new { label = SpeedLabel(tm), paused = tm.Paused },
                        ["colonists"] = new
                        {
                            total,
                            idle_count = idle,
                            mental_break_risk_count = breakRisk,
                            health_issue_count = healthIssue,
                        },
                        ["food"] = new { days_left = (float)Math.Round(foodDays, 1) },
                        ["resources"] = new { key_materials = keyMaterials },
                        ["power"] = new
                        {
                            production_kw = (int)(generated / 1000f),
                            consumption_kw = (int)(used / 1000f),
                            stored_pct = storedMax > 0 ? (int)(stored / storedMax * 100f) : 0,
                        },
                        ["research"] = new { current = researchName, progress_pct = researchPct },
                        ["threats"] = new { enemies_alive = enemiesAlive, nearest_dist = nearestDist },
                        ["weather"] = new
                        {
                            outdoor_temp_c = (int)Math.Round(map.mapTemperature.OutdoorTemp),
                            current = map.weatherManager?.curWeather?.label ?? "未知",
                        },
                        ["alerts"] = new { critical_count = criticalCount, warning_count = warningCount, summary = alertSummary },
                        ["construction"] = new { blueprints, frames_in_progress = frames },
                    };

                    if (includeDetails)
                    {
                        var colonistDetails = colonists.Select(p => new
                        {
                            id = p.thingIDNumber,
                            name = p.LabelShort,
                            mood_pct = (int)((p.needs?.mood?.CurLevelPercentage ?? 1f) * 100f),
                            health = p.health.hediffSet.HasTendableHediff() ? "需治疗" : "OK",
                            current_job = p.mindState?.IsIdle == true ? "空闲" : (p.CurJob?.GetReport(p) ?? "待命中"),
                            drafted = p.Drafted,
                        }).ToList();

                        var nearbyEnemies = hostiles
                            .OrderBy(p => p.Position.DistanceTo(center))
                            .Take(5)
                            .Select(p => new
                            {
                                id = p.thingIDNumber,
                                kind = p.KindLabel,
                                pos_x = p.Position.x,
                                pos_y = p.Position.z,
                                dist = (int)p.Position.DistanceTo(center),
                            }).ToList();

                        var deteriorating = new List<(Thing thing, float value)>();
                        foreach (var t in map.listerThings.AllThings)
                        {
                            if (t.def.category != ThingCategory.Item) continue;
                            if (t.Position.Roofed(map)) continue;
                            if (StoreUtility.IsInValidBestStorage(t)) continue;
                            deteriorating.Add((t, t.MarketValue * t.stackCount));
                        }
                        var deterioratingTop = deteriorating
                            .OrderByDescending(d => d.value)
                            .Take(3)
                            .Select(d => new
                            {
                                id = d.thing.thingIDNumber,
                                label = d.thing.LabelCap.ToString(),
                                pos_x = d.thing.Position.x,
                                pos_y = d.thing.Position.z,
                                value = (int)d.value,
                            }).ToList();

                        snapshot["colonist_details"] = colonistDetails;
                        snapshot["nearby_enemies"] = nearbyEnemies;
                        snapshot["deteriorating_items_top3"] = deterioratingTop;
                    }

                    return ToolResult.Success(JsonSerializer.Serialize(snapshot));
                }
                catch (Exception ex)
                {
                    return ToolResult.Error($"获取状态快照失败: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }

        // 与 get_game_speed 保持一致的玩家面板口径：1x/2x/3x/最快
        private static string SpeedLabel(TickManager tm)
        {
            if (tm.Paused) return "已暂停";
            return tm.CurTimeSpeed switch
            {
                TimeSpeed.Normal => "1 倍速",
                TimeSpeed.Fast => "2 倍速",
                TimeSpeed.Superfast => "3 倍速",
                TimeSpeed.Ultrafast => "最快",
                _ => tm.CurTimeSpeed.ToString()
            };
        }

        public (int minX, int minZ, int maxX, int maxZ)? GetTargetRange(JsonElement? args) => null;
    }
}
