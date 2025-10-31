using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace UnityLocalAi.UI
{
    public static class ChatUI
    {
        private static Vector2 chatScrollPosition;
        private static GUIStyle userStyle;
        private static GUIStyle assistantStyle;

        public static void InitializeStyles()
        {
            userStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true, normal = { textColor = new Color(0.6f, 0.8f, 1.0f) }, padding = new RectOffset(10, 10, 5, 5) };
            assistantStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true, normal = { textColor = Color.white }, padding = new RectOffset(10, 10, 5, 5) };
        }

        public static void Draw(List<ChatMessage> chatHistory, ref string userInput, Action<string> onSendMessage)
        {
            if (userStyle == null)
            {
                InitializeStyles();
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Chat with Unity via LM Studio", EditorStyles.boldLabel);

            using (var chatScroll = new EditorGUILayout.ScrollViewScope(chatScrollPosition, GUILayout.Height(300)))
            {
                chatScrollPosition = chatScroll.scrollPosition;
                foreach (var msg in chatHistory)
                {
                    var style = msg.sender == "You" ? userStyle : assistantStyle;
                    EditorGUILayout.LabelField($"<b>{msg.sender}:</b>", style);
                    EditorGUILayout.LabelField(msg.content, style);
                    EditorGUILayout.Space(5);
                }
            }

            userInput = EditorGUILayout.TextArea(userInput, GUILayout.Height(60));
            if (GUILayout.Button("Send") && !string.IsNullOrEmpty(userInput))
            {
                onSendMessage(userInput);
                userInput = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndVertical();
        }
    }

    [Serializable]
    public readonly struct ChatMessage
    {
        public readonly string sender;
        public readonly string content;
        public ChatMessage(string sender, string content) { this.sender = sender; this.content = content; }
    }
}
