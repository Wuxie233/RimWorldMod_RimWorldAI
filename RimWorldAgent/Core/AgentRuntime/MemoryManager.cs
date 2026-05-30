using System.Collections.Generic;
using RimWorldAgent.Core.Data;

namespace RimWorldAgent.Core.AgentRuntime
{
    /// <summary>记忆管理器 — 数据存储委托给 Core.Data.MemoryStore。</summary>
    public static class MemoryManager
    {
        public static string GetMemoryText(string agentName)
            => MemoryStore.GetMemoryText(agentName);

        public static void Append(string agentName, MemoryEntry entry)
            => MemoryStore.Append(agentName, entry);

        public static void ReplaceAll(string agentName, List<MemoryEntry> entries)
            => MemoryStore.ReplaceAll(agentName, entries);
    }
}
