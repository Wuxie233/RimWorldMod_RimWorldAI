using System;
using System.Diagnostics;
using System.IO;
using HarmonyLib;
using Verse;

namespace RimWorldAgent
{
    /// <summary>返回主菜单时 Game.Dispose() 被调用，Postfix 方式确保在游戏清理完成后杀 CCB</summary>
    [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
    internal static class Hook_GameDispose
    {
        static void Postfix()
        {
            Log.Message("[Hook_GameDispose] Game.Dispose Postfix 触发");
            try
            {
                var asmDir = Path.GetDirectoryName(typeof(Hook_GameDispose).Assembly.Location);
                if (string.IsNullOrEmpty(asmDir)) { Log.Warning("[Hook_GameDispose] asmDir 为空"); return; }

                var pidFile = Path.GetFullPath(Path.Combine(asmDir, "cc-companion", ".pid"));
                if (!File.Exists(pidFile)) { Log.Warning($"[Hook_GameDispose] pid 文件不存在: {pidFile}"); return; }

                var pidText = File.ReadAllText(pidFile).Trim();
                if (!int.TryParse(pidText, out var pid)) { Log.Warning($"[Hook_GameDispose] pid 解析失败: '{pidText}'"); return; }

                try
                {
                    using var proc = Process.GetProcessById(pid);
                    var name = proc.ProcessName.ToLowerInvariant();
                    if (name != "node" && name != "node.exe") { Log.Warning($"[Hook_GameDispose] 进程名不匹配: {name}"); return; }
                    Log.Message($"[Hook_GameDispose] 开始 kill CCB (PID={pid})...");
                    proc.Kill();
                    proc.WaitForExit(5000);
                    Log.Message($"[Hook_GameDispose] CCB kill 完成 (PID={pid})");
                }
                catch (ArgumentException)
                { Log.Warning($"[Hook_GameDispose] 进程已不存在 (PID={pid})"); }
                catch (Exception ex)
                { Log.Warning($"[Hook_GameDispose] kill 异常: {ex.Message}"); }

                try { File.Delete(pidFile); } catch { }
            }
            catch (Exception ex) { Log.Warning($"[Hook_GameDispose] 异常: {ex.Message}"); }
        }
    }
}
