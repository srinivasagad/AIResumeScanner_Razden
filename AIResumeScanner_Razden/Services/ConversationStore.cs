using AIResumeScanner_Razden.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIResumeScanner_Razden.Services
{
    public class ConversationStore
    {
        private readonly Dictionary<string, ConversationState> _store = new();
        private readonly string _filePath = "conversations.json";

        public ConversationStore()
        {
            LoadFromFile();
        }

        public ConversationState GetOrCreate(string sessionId)
        {
            if (!_store.ContainsKey(sessionId))
            {
                _store[sessionId] = new ConversationState
                {
                    SessionId = sessionId,
                    LastUpdated = DateTime.UtcNow
                };
            }
            return _store[sessionId];
        }

        public void SaveMessage(string sessionId, string role, string content)
        {
            var state = GetOrCreate(sessionId);
            state.Messages.Add(new ChatMessage
            {
                Role = role,
                Content = content,
                Timestamp = DateTime.UtcNow
            });
            state.LastUpdated = DateTime.UtcNow;
            SaveToFile();
        }

        public List<ChatMessage> GetHistory(string sessionId)
        {
            return GetOrCreate(sessionId).Messages;
        }

        private void SaveToFile()
        {
            var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_filePath, json);
        }

        private void LoadFromFile()
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, ConversationState>>(json);
                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        _store[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        public void ClearHistory(string sessionId)
        {
            if (_store.ContainsKey(sessionId))
            {
                _store[sessionId].Messages.Clear();
                _store[sessionId].LastUpdated = DateTime.UtcNow;
                SaveToFile();
            }
        }

        public void DeleteSession(string sessionId)
        {
            if (_store.ContainsKey(sessionId))
            {
                _store.Remove(sessionId);
                SaveToFile();
            }
        }

        public void ClearAllHistory()
        {
            _store.Clear();
            SaveToFile();
        }
    }
}
