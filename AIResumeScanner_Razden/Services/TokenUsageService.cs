using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AIResumeScanner_Razden.Services
{

    public class TokenUsageService
    {
        private readonly List<TokenUsageRecord> _usageHistory = new();
        private readonly object _lock = new object();

        // Token limits (configure based on your Azure tier)
        public int TokensPerMinuteLimit { get; set; } = 200000;  // TPM limit
        public int MonthlyTokenLimit { get; set; } = 5000000;   // Optional monthly limit

        public event Action OnUsageUpdated;

        public class TokenUsageRecord
        {
            public DateTime Timestamp { get; set; }
            public string Operation { get; set; }
            public int PromptTokens { get; set; }
            public int CompletionTokens { get; set; }
            public int TotalTokens { get; set; }
            public string Model { get; set; }
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
        }

        public void RecordUsage(
            string operation,
            int promptTokens,
            int completionTokens,
            string model = "gpt-5-nano",
            bool success = true,
            string errorMessage = null)
        {
            lock (_lock)
            {
                var record = new TokenUsageRecord
                {
                    Timestamp = DateTime.UtcNow,
                    Operation = operation,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = promptTokens + completionTokens,
                    Model = model,
                    Success = success,
                    ErrorMessage = errorMessage
                };

                _usageHistory.Add(record);
                OnUsageUpdated?.Invoke();
            }
        }

        public int GetTokensUsedLastMinute()
        {
            lock (_lock)
            {
                var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
                return _usageHistory
                    .Where(r => r.Timestamp >= oneMinuteAgo)
                    .Sum(r => r.TotalTokens);
            }
        }

        public int GetTokensUsedToday()
        {
            lock (_lock)
            {
                var today = DateTime.UtcNow.Date;
                return _usageHistory
                    .Where(r => r.Timestamp.Date == today)
                    .Sum(r => r.TotalTokens);
            }
        }

        public int GetTokensUsedThisMonth()
        {
            lock (_lock)
            {
                var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                return _usageHistory
                    .Where(r => r.Timestamp >= thisMonth)
                    .Sum(r => r.TotalTokens);
            }
        }

        public int GetRemainingTokensPerMinute()
        {
            return Math.Max(0, TokensPerMinuteLimit - GetTokensUsedLastMinute());
        }

        public int GetRemainingMonthlyTokens()
        {
            return Math.Max(0, MonthlyTokenLimit - GetTokensUsedThisMonth());
        }

        public List<TokenUsageRecord> GetRecentUsage(int count = 50)
        {
            lock (_lock)
            {
                return _usageHistory
                    .OrderByDescending(r => r.Timestamp)
                    .Take(count)
                    .ToList();
            }
        }

        public Dictionary<string, int> GetUsageByOperation()
        {
            lock (_lock)
            {
                return _usageHistory
                    .GroupBy(r => r.Operation)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalTokens));
            }
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                _usageHistory.Clear();
                OnUsageUpdated?.Invoke();
            }
        }

        public double GetAverageCostEstimate(double costPerThousandTokens = 0.002)
        {
            lock (_lock)
            {
                var totalTokens = _usageHistory.Sum(r => r.TotalTokens);
                return (totalTokens / 1000.0) * costPerThousandTokens;
            }
        }
    }
}

