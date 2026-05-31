namespace RimWorldAgent.Core.AgentRuntime
{
    public class AgentConfig
    {
        public string Name { get; set; } = "";
        public int IntervalGameHours { get; set; }
    }

    public static class AgentConfigs
    {
        public static readonly AgentConfig Default = new()
        {
            Name = "commander",
            IntervalGameHours = 4,
        };
    }
}
