using System.Text.Json;

namespace RimWorldAgent.Core
{
    /// <summary>UI 显示消息 — BridgeBus 统一输出格式，所有客户端消费同一协议</summary>
    public static class UiMessage
    {
        public static string TextDelta(string text) => Serialize(new { type = "text_delta", text });
        public static string ThinkingDelta(string thinking) => Serialize(new { type = "thinking_delta", thinking });
        public static string TextBlock(string text) => Serialize(new { type = "text_block", text });
        public static string ToolCall(string id, string name, string input) => Serialize(new { type = "tool_call", id, name, input });
        public static string ToolResult(string id, bool isError, double durationMs) => Serialize(new { type = "tool_result", id, isError, durationMs });
        public static string Result(string subtype, string? stopReason) => Serialize(new { type = "result", subtype, stop_reason = stopReason ?? "" });
        public static string Aborted() => Serialize(new { type = "aborted" });
        public static string SystemInit(string? model, string? sessionId) => Serialize(new { type = "system_init", model, session_id = sessionId });
        public static string Error(string error) => Serialize(new { type = "error", error });
        public static string User(string text) => Serialize(new { type = "user", text });
        public static string System(string text) => Serialize(new { type = "system", text });
        public static string BudgetStatus(long used, long limit, string action, long cacheRead, long totalInput, long cacheCreate) => Serialize(new { type = "budget_status", used, limit, action, cacheRead, totalInput, cacheCreate });

        private static string Serialize(object obj) => JsonSerializer.Serialize(obj);
    }
}
