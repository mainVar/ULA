using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace UnityLocalAi
{
    [Serializable]
    public struct ChatMessage : IEquatable<ChatMessage>
    {
        public string sender;
        public string content;

        public ChatMessage(string sender, string content)
        {
            this.sender = sender;
            this.content = content;
        }

        public bool Equals(ChatMessage other)
        {
            return sender == other.sender && content == other.content;
        }

        public override bool Equals(object obj)
        {
            return obj is ChatMessage other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((sender != null ? sender.GetHashCode() : 0) * 397) ^ (content != null ? content.GetHashCode() : 0);
            }
        }
    }

    [Serializable]
    public class ChatSession : IEquatable<ChatSession>
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

        public bool Equals(ChatSession other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return sessionId == other.sessionId && createdAt == other.createdAt && messages.SequenceEqual(other.messages);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ChatSession) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (sessionId != null ? sessionId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ createdAt.GetHashCode();
                // Note: Hashing the messages list content might be slow for long chats,
                // but it's necessary for correct SequenceEqual comparison logic.
                if (messages != null)
                {
                    foreach (var msg in messages)
                    {
                        hashCode = (hashCode * 397) ^ msg.GetHashCode();
                    }
                }
                return hashCode;
            }
        }
    }

    public static class ChatHistoryManager
    {
    public static string ChatLogsPath
    {
        get
        {
            return EditorPrefs.GetString(SettingsWindow.ChatHistoryPathKey, SettingsWindow.GetDefaultChatPath());
        }
    }

        static ChatHistoryManager()
        {
        if (!Directory.Exists(ChatLogsPath))
            {
            Directory.CreateDirectory(ChatLogsPath);
            }
        }

        public static void SaveSession(ChatSession session)
        {
            try
            {
            if (!Directory.Exists(ChatLogsPath)) Directory.CreateDirectory(ChatLogsPath);
            string filePath = Path.Combine(ChatLogsPath, $"{session.sessionId}.json");
                string json = JsonConvert.SerializeObject(session, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatHistoryManager] Failed to save chat session: {ex.Message}");
            }
        }

        public static List<ChatSession> LoadAllSessions()
        {
            var sessions = new List<ChatSession>();
            try
            {
            if (!Directory.Exists(ChatLogsPath)) Directory.CreateDirectory(ChatLogsPath);
            var files = Directory.GetFiles(ChatLogsPath, "*.json");
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
            sessions.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));
            return sessions;
        }

        public static void DeleteSession(ChatSession session)
        {
            if (session == null) return;
            try
            {
            string filePath = Path.Combine(ChatLogsPath, $"{session.sessionId}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatHistoryManager] Failed to delete chat session {session.sessionId}: {ex.Message}");
            }
        }
    }
}
