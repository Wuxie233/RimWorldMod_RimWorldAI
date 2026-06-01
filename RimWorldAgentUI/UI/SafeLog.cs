using System.Collections.Concurrent;
using Verse;

namespace RimWorldAgent
{
    /// <summary>线程安全日志封装，后台线程入队、主线程 Flush 时写入 Verse.Log</summary>
    internal static class SafeLog
    {
        private static readonly ConcurrentQueue<(byte Level, string Message)> _pending = new();

        public static void Info(string msg) => _pending.Enqueue((0, msg));
        public static void Warning(string msg) => _pending.Enqueue((1, msg));
        public static void Error(string msg) => _pending.Enqueue((2, msg));

        /// <summary>必须在主线程调用</summary>
        public static void Flush()
        {
            while (_pending.TryDequeue(out var entry))
            {
                switch (entry.Level)
                {
                    case 2: Log.Error(entry.Message); break;
                    case 1: Log.Warning(entry.Message); break;
                    default: Log.Message(entry.Message); break;
                }
            }
        }
    }
}
