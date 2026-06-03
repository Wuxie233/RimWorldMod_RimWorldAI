using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace RimWorldMCP
{
    public class McpCommand
    {
        public Func<object?> Action { get; set; } = null!;
        public TaskCompletionSource<object?> Completion { get; set; } = new();
        public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
    }

    public static class McpCommandQueue
    {
        private static readonly ConcurrentQueue<McpCommand> _queue = new();
        private const int MaxCommandsPerFrame = 5;

        public static void Enqueue(McpCommand command)
        {
            _queue.Enqueue(command);
        }

        /// <summary>主线程每帧调用。每帧最多处理 MaxCommandsPerFrame 个命令，防止卡帧。</summary>
        public static void ProcessPending()
        {
            for (int i = 0; i < MaxCommandsPerFrame && _queue.TryDequeue(out var command); i++)
            {
                var waitMs = (DateTime.UtcNow - command.EnqueuedAt).TotalMilliseconds;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var result = command.Action();
                    sw.Stop();
                    command.Completion.TrySetResult(result);
                    if (sw.Elapsed.TotalMilliseconds > 100 || waitMs > 500)
                        McpLog.Debug($"[McpCommandQueue] 命令执行: 排队等待 {waitMs:F0}ms, 主线程执行 {sw.Elapsed.TotalMilliseconds:F0}ms");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    McpLog.Warn($"[McpCommandQueue] 命令执行异常: 排队等待 {waitMs:F0}ms, 执行 {sw.Elapsed.TotalMilliseconds:F0}ms, {ex.GetType().Name}: {ex.Message}");
                    command.Completion.TrySetException(ex);
                }
            }

            if (_queue.Count > 0)
                McpLog.Debug($"[McpCommandQueue] ProcessPending 本轮结束, 仍积压 {_queue.Count} 个命令");
        }

        public static int PendingCount => _queue.Count;

        private static readonly ConcurrentQueue<Action> _deferredCleanup = new();

        /// <summary>调度一个延迟到下一帧执行的操作（用于需要 Unity 帧末完成后再执行的清理）</summary>
        public static void ScheduleDeferred(Action action)
        {
            _deferredCleanup.Enqueue(action);
        }

        /// <summary>主线程每帧调用，在 ProcessPending 和 ProcessPendingUploads 之后执行延迟清理</summary>
        public static void ProcessDeferredCleanup()
        {
            while (_deferredCleanup.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { McpLog.Warn($"延迟清理操作异常: {ex.Message}"); }
            }
        }

        /// <summary>调度同步操作到主线程执行，等待结果（超时 5 分钟，适应 advance_tick 等长耗时 Tool）</summary>
        public static async Task<T> DispatchAsync<T>(Func<T> action, int timeoutMs = 300000)
        {
            var preCount = _queue.Count;
            if (preCount > 0)
                McpLog.Debug($"[McpCommandQueue] DispatchAsync 入队前积压 {preCount} 个命令");
            var command = new McpCommand { Action = () => action() };
            _queue.Enqueue(command);

            var timeout = Task.Delay(timeoutMs);
            var completed = await Task.WhenAny(command.Completion.Task, timeout);
            if (completed == timeout)
                throw new TimeoutException($"主线程命令执行超时 (积压 {_queue.Count})");

            return (T)command.Completion.Task.Result!;
        }
    }
}
