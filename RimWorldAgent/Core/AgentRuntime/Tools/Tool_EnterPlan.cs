using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace RimWorldAgent.Core.AgentRuntime.Tools
{
    public class Tool_EnterPlan : IInternalTool
    {
        public string Name => "enter_plan";
        public string Description => "进入 Plan 阶段，暂停游戏进行思考规划。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                speed = new { type = "string", description = "Plan 阶段游戏速度: paused, normal, fast, superfast, ultrafast（默认 paused）" },
                reason = new { type = "string", description = "规划原因（可选，日志用）" }
            }
        });

        public async Task<(string result, bool exit)> ExecuteAsync(JsonElement? args)
        {
            var speed = "paused";
            if (args?.TryGetProperty("speed", out var speedEl) == true)
                speed = speedEl.GetString() ?? "paused";
            var reason = args?.TryGetProperty("reason", out var reasonEl) == true ? reasonEl.GetString() ?? "" : "";

            var mcp = AgentOrchestrator.SessionMcp ?? AgentEngine.Current?.McpClient;
            if (mcp == null)
                return ("无法进入 Plan 阶段：MCP 未就绪（Agent 尚未初始化或已关闭），请稍后重试。", false);

            var pace = AgentOrchestrator.PaceController;
            if (pace == null)
            {
                pace = new GamePaceController();
                AgentOrchestrator.PaceController = pace;
            }

            AgentOrchestrator.EnterPlanPhase();
            await pace.PauseForPlanning(mcp, speed);
            return ($"已进入 Plan 阶段，游戏速度: {speed}。{reason}\n\n可使用 get_skills 和 active_skill 工具获取领域知识。", false);
        }
    }
}
