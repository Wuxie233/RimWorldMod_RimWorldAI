using System;
using System.Collections.Generic;

namespace RimWorldAgent.Core.Data
{
    /// <summary>Token 记录 + 持久化 + 模型用量查询</summary>
    public interface IDbStore
    {
        string CurrentModel { get; set; }

        void Record(string model, long inputTokens, long outputTokens, long cacheRead, long cacheCreate, long durationMs);
        /// <summary>仅记录会话耗时，不增加 TotalRequests 和 per-model 计数</summary>
        void AddDuration(long ms);
        void RecordToolResult(bool isError);

        long TotalInputTokens { get; }
        long TotalOutputTokens { get; }
        long TotalCacheReadTokens { get; }
        long TotalCacheCreateTokens { get; }
        long TotalAllTokens { get; }
        int TotalRequests { get; }
        int TotalToolSuccess { get; }
        int TotalToolFailure { get; }
        long TotalDurationMs { get; }

        Dictionary<string, TokenModelUsage> GetModelUsages();
        void Clear();

        event Action? OnRecorded;

        string GetCompactDisplay(long budgetLimit);
        string GetSummary(long budgetLimit);
    }
}
