using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace UnityLocalAi
{
    [Serializable]
    public struct ChatMessage
    {
        public string sender;
        public string content;

        public ChatMessage(string sender, string content)
        {
            this.sender = sender;
            this.content = content;
        }
    }

    [Serializable]
    public class ChatSession
    {
        public string sessionId;
        public List<ChatMessage> messages;
        public long createdAt;

        public ChatSession()
        {
            sessionId = Guid.NewGuid().ToString();
            messages = new List<ChatMessage>();
            createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    public static class ChatHistoryManager
    {
        private static readonly string chatLogsPath = Path.Combine(Application.dataPath, "..", "ChatLogs");

        static ChatHistoryManager()
        {
            if (!Directory.Exists(chatLogsPath))
            {
                Directory.CreateDirectory(chatLogsPath);
            }
        }

        public static void SaveSession(ChatSession session)
        {
            try
            {
                string filePath = Path.Combine(chatLogsPath, $"{session.sessionId}.json");
                string json = JsonConvert.SerializeObject(session, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatHistoryManager] Failed to save chat session: {ex.Message}");
            }
        }

        public static ChatSession LoadSession(string sessionId)
        {
            try
            {
                string filePath = Path.Combine(chatLogsPath, $"{sessionId}.json");
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonConvert.DeserializeObject<ChatSession>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatHistoryManager] Failed to load chat session: {ex.Message}");
            }
            return null;
        }

        public static List<ChatSession> LoadAllSessions()
        {
            var sessions = new List<ChatSession>();
            try
            {
                var files = Directory.GetFiles(chatLogsPath, "*.json");
                foreach (var file in files)
                {
                    string json = File.ReadAllText(file);
                    var session = JsonConvert.DeserializeObject<ChatSession>(json);
                    if (session != null)
                    {
                        sessions.Add(session);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatHistoryManager] Failed to load all chat sessions: {ex.Message}");
            }
            // Sort by creation date, newest first
            sessions.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));
            return sessions;
        }
    }
}
