using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

namespace AIResumeScanner_Razden.Services
{

    public class TokenUsageService
    {
        private readonly List<TokenUsageRecord> _usageHistory = new();
        private readonly object _lock = new object();
        private readonly string _historyFilePath = "token_usage_history.json";

        // Token limits (configure based on your Azure tier)
        public int TokensPerMinuteLimit { get; set; } = 200000;  // TPM limit
        public int MonthlyTokenLimit { get; set; } = 10000000;   // Optional monthly limit

        public event Action OnUsageUpdated;

        public class TokenUsageRecord
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            public DateTime Timestamp { get; set; }
            public string Operation { get; set; }
            public int PromptTokens { get; set; }
            public int CompletionTokens { get; set; }
            public int TotalTokens { get; set; }
            public string Model { get; set; }
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public string SessionId { get; set; }
            public string UserQuery { get; set; }
            public double DurationSeconds { get; set; }
            public double EstimatedCost { get; set; }
        }

        public TokenUsageService()
        {
            // Load history from file on startup
            LoadHistoryFromFile();
        }

        public void RecordUsage(
            string operation,
            int promptTokens,
            int completionTokens,
            string model = "gpt-5-nano",
            bool success = true,
            string errorMessage = null,
            string sessionId = null,
            string userQuery = null,
            double durationSeconds = 0,
            double costPerThousandTokens = 0.03)
        {
            lock (_lock)
            {
                var totalTokens = promptTokens + completionTokens;
                var estimatedCost = (totalTokens / 1000.0) * costPerThousandTokens;

                var record = new TokenUsageRecord
                {
                    Timestamp = DateTime.UtcNow,
                    Operation = operation,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = totalTokens,
                    Model = model,
                    Success = success,
                    ErrorMessage = errorMessage,
                    SessionId = sessionId ?? "default",
                    UserQuery = userQuery,
                    DurationSeconds = durationSeconds,
                    EstimatedCost = estimatedCost
                };

                _usageHistory.Add(record);
                OnUsageUpdated?.Invoke();

                // Auto-save to file after each record (optional, can be batched)
                SaveHistoryToFile();
            }
        }

        public int GetTokensUsedLastMinute()
        {
            lock (_lock)
            {
                var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
                return _usageHistory
                    .Where(r => r.Timestamp >= oneMinuteAgo && r.Success)
                    .Sum(r => r.TotalTokens);
            }
        }

        public int GetTokensUsedToday()
        {
            lock (_lock)
            {
                var today = DateTime.UtcNow.Date;
                return _usageHistory
                    .Where(r => r.Timestamp.Date == today && r.Success)
                    .Sum(r => r.TotalTokens);
            }
        }

        public int GetTokensUsedThisMonth()
        {
            lock (_lock)
            {
                var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                return _usageHistory
                    .Where(r => r.Timestamp >= thisMonth && r.Success)
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

        public List<TokenUsageRecord> GetUsageByDateRange(DateTime startDate, DateTime endDate)
        {
            lock (_lock)
            {
                return _usageHistory
                    .Where(r => r.Timestamp >= startDate && r.Timestamp <= endDate)
                    .OrderByDescending(r => r.Timestamp)
                    .ToList();
            }
        }

        public List<TokenUsageRecord> GetUsageBySession(string sessionId)
        {
            lock (_lock)
            {
                return _usageHistory
                    .Where(r => r.SessionId == sessionId)
                    .OrderByDescending(r => r.Timestamp)
                    .ToList();
            }
        }

        public Dictionary<string, int> GetUsageByOperation()
        {
            lock (_lock)
            {
                return _usageHistory
                    .Where(r => r.Success)
                    .GroupBy(r => r.Operation)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalTokens));
            }
        }

        public Dictionary<string, int> GetUsageByModel()
        {
            lock (_lock)
            {
                return _usageHistory
                    .Where(r => r.Success)
                    .GroupBy(r => r.Model)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalTokens));
            }
        }

        public Dictionary<DateTime, int> GetDailyUsageLastWeek()
        {
            lock (_lock)
            {
                var weekAgo = DateTime.UtcNow.Date.AddDays(-7);
                return _usageHistory
                    .Where(r => r.Timestamp.Date >= weekAgo && r.Success)
                    .GroupBy(r => r.Timestamp.Date)
                    .OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalTokens));
            }
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                _usageHistory.Clear();
                OnUsageUpdated?.Invoke();
                SaveHistoryToFile();
            }
        }

        public void ClearOldHistory(int daysToKeep = 30)
        {
            lock (_lock)
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
                var removedCount = _usageHistory.RemoveAll(r => r.Timestamp < cutoffDate);

                if (removedCount > 0)
                {
                    OnUsageUpdated?.Invoke();
                    SaveHistoryToFile();
                }
            }
        }

        public double GetAverageCostEstimate(double costPerThousandTokens = 0.03)
        {
            lock (_lock)
            {
                var totalTokens = _usageHistory
                    .Where(r => r.Success)
                    .Sum(r => r.TotalTokens);
                return (totalTokens / 1000.0) * costPerThousandTokens;
            }
        }

        public double GetTotalCostThisMonth()
        {
            lock (_lock)
            {
                var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                return _usageHistory
                    .Where(r => r.Timestamp >= thisMonth && r.Success)
                    .Sum(r => r.EstimatedCost);
            }
        }

        public TokenUsageStats GetStatistics()
        {
            lock (_lock)
            {
                var successfulRecords = _usageHistory.Where(r => r.Success).ToList();

                return new TokenUsageStats
                {
                    TotalRequests = _usageHistory.Count,
                    SuccessfulRequests = successfulRecords.Count,
                    FailedRequests = _usageHistory.Count - successfulRecords.Count,
                    TotalTokens = successfulRecords.Sum(r => r.TotalTokens),
                    TotalPromptTokens = successfulRecords.Sum(r => r.PromptTokens),
                    TotalCompletionTokens = successfulRecords.Sum(r => r.CompletionTokens),
                    AverageTokensPerRequest = successfulRecords.Any()
                        ? successfulRecords.Average(r => r.TotalTokens) : 0,
                    TotalCost = successfulRecords.Sum(r => r.EstimatedCost),
                    AverageDuration = successfulRecords.Any()
                        ? successfulRecords.Average(r => r.DurationSeconds) : 0,
                    FirstRequestDate = _usageHistory.Any()
                        ? _usageHistory.Min(r => r.Timestamp) : DateTime.MinValue,
                    LastRequestDate = _usageHistory.Any()
                        ? _usageHistory.Max(r => r.Timestamp) : DateTime.MinValue
                };
            }
        }

        // File storage methods
        public void SaveHistoryToFile()
        {
            try
            {
                lock (_lock)
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    var json = JsonSerializer.Serialize(_usageHistory, options);
                    File.WriteAllText(_historyFilePath, json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving token history: {ex.Message}");
            }
        }

        public void LoadHistoryFromFile()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    lock (_lock)
                    {
                        var json = File.ReadAllText(_historyFilePath);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        };

                        var records = JsonSerializer.Deserialize<List<TokenUsageRecord>>(json, options);

                        if (records != null)
                        {
                            _usageHistory.Clear();
                            _usageHistory.AddRange(records);
                            OnUsageUpdated?.Invoke();

                            Console.WriteLine($"Loaded {records.Count} token usage records from history");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading token history: {ex.Message}");
            }
        }

        public void ExportToCsv(string filePath)
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Id,Timestamp,Operation,PromptTokens,CompletionTokens,TotalTokens,Model,Success,ErrorMessage,SessionId,UserQuery,DurationSeconds,EstimatedCost");

                foreach (var record in _usageHistory.OrderBy(r => r.Timestamp))
                {
                    sb.AppendLine($"\"{record.Id}\"," +
                                $"\"{record.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                                $"\"{record.Operation}\"," +
                                $"{record.PromptTokens}," +
                                $"{record.CompletionTokens}," +
                                $"{record.TotalTokens}," +
                                $"\"{record.Model}\"," +
                                $"{record.Success}," +
                                $"\"{record.ErrorMessage?.Replace("\"", "\"\"")}\"," +
                                $"\"{record.SessionId}\"," +
                                $"\"{record.UserQuery?.Replace("\"", "\"\"").Substring(0, Math.Min(100, record.UserQuery?.Length ?? 0))}\"," +
                                $"{record.DurationSeconds:F2}," +
                                $"{record.EstimatedCost:F4}");
                }

                File.WriteAllText(filePath, sb.ToString());
            }
        }

        public int GetHistoryCount()
        {
            lock (_lock)
            {
                return _usageHistory.Count;
            }
        }

        public class TokenUsageStats
        {
            public int TotalRequests { get; set; }
            public int SuccessfulRequests { get; set; }
            public int FailedRequests { get; set; }
            public int TotalTokens { get; set; }
            public int TotalPromptTokens { get; set; }
            public int TotalCompletionTokens { get; set; }
            public double AverageTokensPerRequest { get; set; }
            public double TotalCost { get; set; }
            public double AverageDuration { get; set; }
            public DateTime FirstRequestDate { get; set; }
            public DateTime LastRequestDate { get; set; }
        }
    }
}

