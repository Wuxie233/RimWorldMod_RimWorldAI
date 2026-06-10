using System.Collections.Generic;
using Verse;

namespace RimWorldAgent
{
    public class AgentModSettings : ModSettings
    {
        // 模型
        public string ModelName = "";
        public string AiProvider = "claude-sdk";
        public string ApiBaseUrl = "";
        public string ApiKey = "";
        public List<string> CachedModelIds = new List<string>();
        public string CachedModelProvider = "";
        public string CachedModelBaseUrl = "";
        public string CachedModelFetchedAt = "";

        // 思考
        public string ThinkingMode = "adaptive";
        public string ThinkingEffort = "high";

        // Token 预算
        public long TokenBudgetLimit;
        public string TokenBudgetAction = "Block";

        // MCP 服务地址
        public string GameMcpHost = "localhost";
        public int GameMcpPort = 9877;
        public int AgentMcpPort = 9878;

        // Agent 行为
        public bool AgentAutoRun = true;
        public string PlanSpeed = "paused";
        public string SkillsDir = "";
        public string ProjectPath = "";

        // 用户偏好（注入 systemPrompt，跨存档全局；provider/语言中立）
        public string ReplyLanguage = "auto";
        public string Autonomy = "balanced";

        // CC Companion 依赖
        public bool CcbAutoInstall = true;

        // UIMessageBus（Web 前端 WS 服务）
        public string BridgeHost = "127.0.0.1";
        public int BridgePort = 19999;

        // 日志
        public bool LogSdkMessages;
        public bool LogCcbWsMessages;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ModelName, "modelName", "");
            Scribe_Values.Look(ref AiProvider, "aiProvider", "claude-sdk");
            Scribe_Values.Look(ref ApiBaseUrl, "apiBaseUrl", "");
            Scribe_Values.Look(ref ApiKey, "apiKey", "");
            Scribe_Collections.Look(ref CachedModelIds, "cachedModelIds", LookMode.Value);
            Scribe_Values.Look(ref CachedModelProvider, "cachedModelProvider", "");
            Scribe_Values.Look(ref CachedModelBaseUrl, "cachedModelBaseUrl", "");
            Scribe_Values.Look(ref CachedModelFetchedAt, "cachedModelFetchedAt", "");
            Scribe_Values.Look(ref ThinkingMode, "thinkingMode", "adaptive");
            Scribe_Values.Look(ref ThinkingEffort, "thinkingEffort", "high");
            Scribe_Values.Look(ref TokenBudgetLimit, "tokenBudgetLimit", 0L);
            Scribe_Values.Look(ref TokenBudgetAction, "tokenBudgetAction", "Block");
            Scribe_Values.Look(ref GameMcpHost, "gameMcpHost", "localhost");
            Scribe_Values.Look(ref GameMcpPort, "gameMcpPort", 9877);
            Scribe_Values.Look(ref AgentMcpPort, "agentMcpPort", 9878);
            Scribe_Values.Look(ref AgentAutoRun, "agentAutoRun", true);
            Scribe_Values.Look(ref PlanSpeed, "planSpeed", "paused");
            Scribe_Values.Look(ref SkillsDir, "skillsDir", "");
            Scribe_Values.Look(ref ProjectPath, "projectPath", "");
            Scribe_Values.Look(ref ReplyLanguage, "replyLanguage", "auto");
            Scribe_Values.Look(ref Autonomy, "autonomy", "balanced");
            Scribe_Values.Look(ref CcbAutoInstall, "ccbAutoInstall", true);
            Scribe_Values.Look(ref BridgeHost, "bridgeHost", "127.0.0.1");
            Scribe_Values.Look(ref BridgePort, "bridgePort", 19999);
            Scribe_Values.Look(ref LogSdkMessages, "logSdkMessages", false);
            Scribe_Values.Look(ref LogCcbWsMessages, "logCcbWsMessages", false);
            if (CachedModelIds == null) CachedModelIds = new List<string>();
        }
    }
}
