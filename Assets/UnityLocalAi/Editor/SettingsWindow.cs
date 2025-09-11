using UnityEditor;
using UnityEngine;

namespace UnityLocalAi
{
    public class SettingsWindow : EditorWindow
    {
        public const string ChatHistoryPathKey = "UnityLocalAi_ChatHistoryPath";
        private string chatHistoryPath;

        public static void ShowWindow()
        {
            GetWindow<SettingsWindow>("Local AI Settings");
        }

        private void OnEnable()
        {
            chatHistoryPath = EditorPrefs.GetString(ChatHistoryPathKey, GetDefaultChatPath());
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Chat History Settings", EditorStyles.boldLabel);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            chatHistoryPath = EditorGUILayout.TextField("History Path", chatHistoryPath);
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string selectedPath = EditorUtility.OpenFolderPanel("Select Chat History Folder", chatHistoryPath, "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    chatHistoryPath = selectedPath;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (GUILayout.Button("Save Settings"))
            {
                SaveSettings();
                ShowNotification(new GUIContent("Settings Saved!"));
            }

            if (GUILayout.Button("Reset to Default"))
            {
                chatHistoryPath = GetDefaultChatPath();
                SaveSettings();
                ShowNotification(new GUIContent("Path reset to default."));
            }
        }

        private void SaveSettings()
        {
            EditorPrefs.SetString(ChatHistoryPathKey, chatHistoryPath);
            Debug.Log($"[MCP] Chat history path saved: {chatHistoryPath}");
        }

        public static string GetDefaultChatPath()
        {
            return System.IO.Path.Combine(System.IO.Path.GetFullPath(Application.dataPath + "/.."), "ChatLogs");
        }
    }
}
