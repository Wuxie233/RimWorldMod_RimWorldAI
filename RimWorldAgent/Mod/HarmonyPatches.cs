using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorldAgent.Core.CcbManager;
using Verse;

namespace RimWorldAgent
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        private static readonly Harmony _harmony = new Harmony("RimWorldAgent");

        static HarmonyPatches()
        {
            // 纯日志：验证调用时机
            TryPatch(typeof(Map), "DeinitAndRemoveMap", nameof(Postfix_DeinitAndRemoveMap), null);

            // 主功能：退出存档时 Kill CCB（用 Prefix 在 ClearAllMapsAndWorld 执行前拿 GameComponent）
            TryPatch(typeof(Verse.Profile.MemoryUtility), "ClearAllMapsAndWorld", null, nameof(Prefix_ClearAllMapsAndWorld));
        }

        private static void TryPatch(Type targetType, string methodName, string? postfixMethod, string? prefixMethod)
        {
            try
            {
                var original = AccessTools.Method(targetType, methodName);
                if (original == null)
                {
                    var all = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Select(m => m.Name).Distinct().Take(20);
                    SafeLog.Warning($"[agent-harmony] 跳过 {targetType.FullName}.{methodName}: 方法不存在 (前20: {string.Join(", ", all)})");
                    return;
                }
                var prefix = prefixMethod != null ? new HarmonyMethod(typeof(HarmonyPatches), prefixMethod) : null;
                var postfix = postfixMethod != null ? new HarmonyMethod(typeof(HarmonyPatches), postfixMethod) : null;
                _harmony.Patch(original, prefix: prefix, postfix: postfix);
                SafeLog.Info($"[agent-harmony] Patch {targetType.Name}.{methodName} 成功");
            }
            catch (Exception ex)
            {
                SafeLog.Error($"[agent-harmony] Patch {targetType.FullName}.{methodName} 失败: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ===== 回调 =====

        public static void Postfix_DeinitAndRemoveMap()
        {
            SafeLog.Info("[agent-harmony] Map.DeinitAndRemoveMap 被调用");
        }

        public static void Prefix_ClearAllMapsAndWorld()
        {
            SafeLog.Info("[agent-harmony] MemoryUtility.ClearAllMapsAndWorld 即将执行 → 关闭 Agent");
            try
            {
                var gc = Current.Game?.GetComponent<GameComponent_RimWorldAgent>();
                if (gc != null)
                {
                    gc.ShutdownEngine();
                    return;
                }
                // Fallback: GameComponent 不可用时直接 Kill CCB
                SafeLog.Warning("[agent-harmony] 无法获取 GameComponent，直接 Kill CCB");
                CcbManager.KillStaleProcesses();
            }
            catch (Exception ex)
            {
                SafeLog.Error($"[agent-harmony] 关闭异常: {ex.Message}");
            }
        }
    }
}
