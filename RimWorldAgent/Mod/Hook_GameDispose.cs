using System;
using System.Diagnostics;
using HarmonyLib;
using Verse;

namespace RimWorldAgent
{
    /// <summary>返回主菜单时 Game.Dispose() 被调用，扫描进程列表杀 CCB 释放端口</summary>
    [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
    internal static class Hook_GameDispose
    {
        private static void Postfix()
        {
            try
            {
                var procs = Process.GetProcesses();
                var killed = 0;
                foreach (var proc in procs)
                {
                    try
                    {
                        var name = proc.ProcessName.ToLowerInvariant();
                        if (name != "node" && name != "node.exe") continue;
                        var fileName = proc.MainModule?.FileName ?? "";
                        if (!fileName.Contains("cc-companion")) continue;

                        Log.Message($"[Hook_GameDispose] 发现残留 CCB 进程 PID={proc.Id} path={fileName}，正在关闭...");
                        proc.Kill();
                        proc.WaitForExit(5000);
                        killed++;
                        Log.Message($"[Hook_GameDispose] CCB 进程 {proc.Id} 已关闭");
                    }
                    catch (Exception ex) { Log.Warning($"[Hook_GameDispose] 处理进程 {proc.Id} 异常: {ex.Message}"); }
                    finally { proc.Dispose(); }
                }

                if (killed > 0) Log.Message($"[Hook_GameDispose] 进程扫描清理完成，共关闭 {killed} 个 CCB 进程");
            }
            catch (Exception ex) { Log.Warning($"[Hook_GameDispose] 异常: {ex.Message}"); }
        }
    }
}
