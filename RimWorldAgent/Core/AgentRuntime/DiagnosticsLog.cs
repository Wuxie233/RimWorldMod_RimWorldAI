using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RimWorldAgent.Core.AgentRuntime
{
    /// <summary>结构化自诊断日志：每次运行写一份 JSONL（会话/工具/错误事件），便于跑测后一眼定位问题。</summary>
    public static class DiagnosticsLog
    {
        private static readonly object _lock = new object();
        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(false);

        public static string RunId { get; private set; } = "";
        public static string? FilePath { get; private set; }

        public static void Init(string projectPath)
        {
            try
            {
                var folder = Path.Combine(projectPath, "diagnostics");
                Directory.CreateDirectory(folder);
                RunId = Guid.NewGuid().ToString("N").Substring(0, 12);
                var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                FilePath = Path.Combine(folder, $"session-diagnostics-{ts}-{RunId}.jsonl");
                Event("run_start", new { pid = System.Diagnostics.Process.GetCurrentProcess().Id });
            }
            catch (Exception ex)
            {
                FilePath = null;
                CoreLog.Warn($"[Diag] 初始化失败: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void Event(string evt, object? data = null)
        {
            var path = FilePath;
            if (path == null) return;
            try
            {
                var record = new Dictionary<string, object?>
                {
                    ["ts"] = DateTime.UtcNow.ToString("O"),
                    ["runId"] = RunId,
                    ["event"] = evt,
                };
                if (data != null) record["data"] = data;
                var line = JsonSerializer.Serialize(record, _json) + "\n";
                lock (_lock) { File.AppendAllText(path, line, _utf8NoBom); }
            }
            catch (Exception ex)
            {
                CoreLog.Debug($"[Diag] 写入失败: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void Log(string level, string message)
        {
            Event("log", new { level, message });
        }

        public static string? Truncate(string? value, int max = 500)
        {
            if (value == null) return null;
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }
    }
}
