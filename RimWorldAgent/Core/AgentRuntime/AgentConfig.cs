namespace RimWorldAgent.Core.AgentRuntime
{
    public class AgentConfig
    {
        public string Name { get; set; } = "";
        public string SystemPrompt { get; set; } = "";
        public int IntervalGameHours { get; set; }
    }

    public static class AgentConfigs
    {
        public static readonly AgentConfig Default = new()
        {
            Name = "commander",
            IntervalGameHours = 4,
            SystemPrompt = @"你是 RimWorld 殖民地的 AI 指挥官。游戏操作策略和规则已通过系统提示词注入，此处不再重复。"
        };
    }
}
