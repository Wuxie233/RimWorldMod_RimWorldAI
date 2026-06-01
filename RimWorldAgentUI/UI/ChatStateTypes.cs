using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Verse;

namespace RimWorldAgent
{
    public enum ChatRole { User, Assistant }
    public enum ChatState { Streaming, Done, Error }
    public enum ToolStatus { Running, Completed, Failed }

    public class ToolCallInfo
    {
        public string ItemId = "";
        public string Name = "";
        public string Meta = "";
        public ToolStatus Status;
        public DateTime StartTime = DateTime.UtcNow;
        public double DurationMs;
    }

    public class ChatEntry
    {
        public ChatRole Role;
        public string Text = "";
        public string ThinkingText = "";
        public ChatState State;
        public string AgentId = "";
        public string AgentType = "";
        public bool IsContext;
        public float CachedHeight;
        public int CachedTextLen;
        public int CachedThinkingLen;
    }

    /// <summary>由 BridgeBus UiMessage 协议驱动，和 WebUI 共享同一数据源</summary>
    public static class ChatDisplayState
    {
        public static event Action? OnChanged;
        public static string CurrentModel = "";
        public static string SessionId = "";
        public static string AgentStatus = "";

        private static readonly List<ChatEntry> _entries = new();
        private static readonly List<ToolCallInfo> _toolCalls = new();
        private static readonly object _lock = new();
        private static readonly Queue<Action> _pendingEvents = new();
        private static readonly object _eventLock = new();

        public static void EnqueueUiEvent(Action action)
        {
            lock (_eventLock) { _pendingEvents.Enqueue(action); }
        }

        public static void DrainEvents()
        {
            List<Action> batch;
            lock (_eventLock)
            {
                if (_pendingEvents.Count == 0) return;
                batch = new List<Action>(_pendingEvents);
                _pendingEvents.Clear();
            }
            foreach (var act in batch)
            {
                try { act(); }
                catch (Exception ex) { Log.Warning($"[ChatDisplayState] 事件处理异常: {ex.Message}"); }
            }
        }

        public static List<ChatEntry> Snapshot
        { get { lock (_lock) return _entries.ToList(); } }

        public static List<ToolCallInfo> ToolCallsSnapshot
        { get { lock (_lock) return _toolCalls.ToList(); } }

        public static void OnUserMessage(string text)
        {
            lock (_lock)
            {
                if (_entries.Count > 0 && _entries[_entries.Count - 1].State == ChatState.Streaming)
                    _entries[_entries.Count - 1].State = ChatState.Done;
                _entries.Add(new ChatEntry { Role = ChatRole.User, Text = text, State = ChatState.Done });
            }
            OnChanged?.Invoke();
        }

        public static void AddSystemMessage(string text)
        {
            lock (_lock) { _entries.Add(new ChatEntry { Role = ChatRole.Assistant, Text = text, State = ChatState.Done, IsContext = true }); }
            OnChanged?.Invoke();
        }

        public static void Clear()
        {
            lock (_lock) { _entries.Clear(); _toolCalls.Clear(); }
            OnChanged?.Invoke();
        }

        // ===== UiMessage JSON 解析（由 BridgeClient.OnMessage → 直接调用） =====

        public static void ProcessMessage(string uiJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(uiJson);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : "";
                switch (type)
                {
                    case "text_block":
                        EnqueueUiEvent(() => OnTextBlock(root)); break;
                    case "text_delta":
                        EnqueueUiEvent(() => OnTextDelta(root)); break;
                    case "thinking_delta":
                        EnqueueUiEvent(() => OnThinkingDelta(root)); break;
                    case "tool_call":
                        EnqueueUiEvent(() => OnToolCall(root)); break;
                    case "result":
                        EnqueueUiEvent(() => OnResult()); break;
                    case "aborted":
                        EnqueueUiEvent(() => OnAborted()); break;
                    case "system_init":
                        var m = root.TryGetProperty("model", out var mm) ? mm.GetString() : null;
                        if (m != null) CurrentModel = m;
                        SessionId = root.TryGetProperty("session_id", out var sid) ? sid.GetString() ?? "" : "";
                        break;
                    case "user":
                        var txt = root.TryGetProperty("text", out var ut) ? ut.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(txt)) OnUserMessage(txt);
                        break;
                }
            }
            catch { /* ignore parse errors */ }
        }

        private static void OnTextBlock(JsonElement root)
        {
            var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            lock (_lock)
            {
                var last = _entries.Count > 0 ? _entries[_entries.Count - 1] : null;
                if (last == null || last.State != ChatState.Streaming || last.Role != ChatRole.Assistant)
                {
                    last = new ChatEntry { Role = ChatRole.Assistant, State = ChatState.Streaming };
                    _entries.Add(last);
                }
                last.Text += text;
            }
            OnChanged?.Invoke();
        }

        private static void OnTextDelta(JsonElement root)
        {
            var delta = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (delta.Length == 0) return;
            lock (_lock)
            {
                if (_entries.Count == 0) return;
                var last = _entries[_entries.Count - 1];
                if (last.Role != ChatRole.Assistant || last.State != ChatState.Streaming) return;
                last.Text += delta;
            }
            OnChanged?.Invoke();
        }

        private static void OnThinkingDelta(JsonElement root)
        {
            var delta = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            lock (_lock)
            {
                if (delta.Length == 0)
                {
                    if (_entries.Count > 0 && _entries[_entries.Count - 1].State == ChatState.Streaming)
                        _entries[_entries.Count - 1].ThinkingText = "";
                    return;
                }
                if (_entries.Count == 0) return;
                var last = _entries[_entries.Count - 1];
                if (last.Role != ChatRole.Assistant || last.State != ChatState.Streaming) return;
                last.ThinkingText += delta;
            }
            OnChanged?.Invoke();
        }

        private static void OnToolCall(JsonElement root)
        {
            var id = root.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var input = root.TryGetProperty("input", out var inp) ? inp.GetRawText() : "{}";
            lock (_lock)
            {
                _toolCalls.Add(new ToolCallInfo
                {
                    ItemId = id,
                    Name = name.Replace("mcp__agent__", "").Replace("mcp__rimworld__", ""),
                    Meta = input,
                    Status = ToolStatus.Running,
                });
            }
            OnChanged?.Invoke();
        }

        private static void OnResult()
        {
            lock (_lock)
            {
                if (_entries.Count > 0 && _entries[_entries.Count - 1].State == ChatState.Streaming)
                    _entries[_entries.Count - 1].State = ChatState.Done;
            }
            OnChanged?.Invoke();
        }

        private static void OnAborted()
        {
            lock (_lock)
            {
                if (_entries.Count > 0 && _entries[_entries.Count - 1].State == ChatState.Streaming)
                    _entries[_entries.Count - 1].State = ChatState.Error;
            }
            OnChanged?.Invoke();
        }
    }
}
