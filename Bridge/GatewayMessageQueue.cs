using System;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldMCP
{
    public enum MessageCategory
    {
        DailyMorning = 0,
        Alert = 10,
        RaidEnd = 20,
        RaidStart = 30,
        SessionInit = 40,
    }

    internal struct PendingMessage
    {
        public MessageCategory Category;
        public string Text;
    }

    /// <summary>消息队列 — 同类覆盖 + 等 agent stream 完成才发下一条</summary>
    public static class GatewayMessageQueue
    {
        private static readonly Dictionary<MessageCategory, PendingMessage> _pending = new();
        private static bool _sending;
        private static int _idleFrames;
        private const int IdleFramesBeforeSend = 30; // ~0.5s 窗口让同类消息覆盖
        private static int _lastDailyDaySent = -1;
        private static bool _sessionPromptSent;

        public static void Enqueue(MessageCategory category, string text)
        {
            if (!GatewayClient.IsConnected) return;

            _pending[category] = new PendingMessage { Category = category, Text = text };
            _idleFrames = IdleFramesBeforeSend;
        }

        /// <summary>每帧调用</summary>
        public static void Tick()
        {
            if (!GatewayClient.IsConnected)
            {
                _pending.Clear();
                _sending = false;
                _lastDailyDaySent = -1;
                _sessionPromptSent = false;
                return;
            }

            if (!GatewayClient.IsReady) return;

            // 正在发消息，等 SendMessage 完成
            if (_sending) return;
            if (_pending.Count == 0) return;

            // 短暂稳定窗口让同类消息覆盖
            if (_idleFrames > 0)
            {
                _idleFrames--;
                return;
            }

            // 取最高优先级发送
            var best = _pending.Values.OrderByDescending(m => (int)m.Category).First();
            _pending.Remove(best.Category);
            _ = DoSend(best.Category, best.Text);
        }

        public static void SendNow(MessageCategory category, string text)
        {
            if (!GatewayClient.IsReady || _sending) return;
            _ = DoSend(category, text);
        }

        public static void MarkDailySent(int day) => _lastDailyDaySent = day;
        public static bool WasDailySentToday(int day) => _lastDailyDaySent == day;
        public static void MarkSessionPromptSent() => _sessionPromptSent = true;
        public static bool WasSessionPromptSent => _sessionPromptSent;

        public static void Reset()
        {
            _pending.Clear();
            _sending = false;
            _lastDailyDaySent = -1;
            _sessionPromptSent = false;
        }

        private static async System.Threading.Tasks.Task DoSend(MessageCategory category, string text)
        {
            if (!GatewayClient.IsReady) return;
            _sending = true;
            try
            {
                await GatewayClient.SendMessage(text);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[queue] 发送失败 ({category}): {ex.Message}");
            }
            finally
            {
                _sending = false;
            }
        }
    }
}
