using System;
using System.Linq;
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
            try
            {
                Log.Message("[agent-harmony] 开始安装 Harmony 补丁...");
                _harmony.PatchAll();
                var patched = _harmony.GetPatchedMethods().ToList();
                Log.Message($"[agent-harmony] Harmony 补丁已安装，共 {patched.Count} 个方法");
                foreach (var m in patched)
                    Log.Message($"[agent-harmony]   - {m.DeclaringType?.FullName}.{m.Name}");
            }
            catch (Exception ex)
            {
                Log.Error($"[agent-harmony] 安装 Harmony 失败: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// 退出存档到主菜单时触发。Patch Game.DeinitAndRemoveMap 确保任何退出路径都能捕获。
    /// </summary>
    [HarmonyPatch(typeof(Game), "DeinitAndRemoveMap")]
    public static class Patch_Game_DeinitAndRemoveMap
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Log.Message("[agent-harmony] Game.DeinitAndRemoveMap 被调用 → 退出存档");
            try
            {
                var gc = Current.Game?.GetComponent<GameComponent_RimWorldAgent>();
                if (gc != null)
                {
                    gc.ShutdownEngine();
                    return;
                }
                Log.Warning("[agent-harmony] 无法获取 GameComponent 实例，仅 Kill CCB");
                CcbManager.KillStaleProcesses();
            }
            catch (Exception ex)
            {
                Log.Error($"[agent-harmony] 退出存档关闭异常: {ex.Message}");
            }
        }
    }
}
