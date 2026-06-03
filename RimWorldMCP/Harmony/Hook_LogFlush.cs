using System;
using Verse;

namespace RimWorldMCP.Harmony
{
    /// <summary>
    /// Harmony Patch: 在 UIRoot.UIRootUpdate() 每帧执行 McpLog.Flush()。
    /// 覆盖 Entry / Menu / Play 全部游戏状态，不受 GameComponent 存档生命周期限制。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Hook_LogFlush
    {
        private static readonly HarmonyLib.Harmony _harmony = new HarmonyLib.Harmony("RimWorldMCP.LogFlush");

        static Hook_LogFlush()
        {
            // UIRoot.UIRootUpdate() 是所有 UI Root (Entry/Menu/Play) 的基类方法，每帧调用
            var original = HarmonyLib.AccessTools.Method(typeof(UIRoot), "UIRootUpdate");
            if (original == null)
            {
                McpLog.Error("Hook_LogFlush: UIRoot.UIRootUpdate 方法不存在");
                return;
            }
            _harmony.Patch(original,
                postfix: new HarmonyLib.HarmonyMethod(typeof(Hook_LogFlush), nameof(Postfix_FlushLog)));
        }

        public static void Postfix_FlushLog()
        {
            try { McpLog.Flush(); }
            catch (Exception ex) { McpLog.Error($"McpLog.Flush 异常: {ex}"); }
        }
    }
}
