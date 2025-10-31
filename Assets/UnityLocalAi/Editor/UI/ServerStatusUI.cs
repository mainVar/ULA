using UnityEngine;
using UnityEditor;
using System;

namespace UnityLocalAi.UI
{
    public static class ServerStatusUI
    {
        public static void DrawPythonServerSection(string status, Color color, Action onStartServer)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Python Server Status", EditorStyles.boldLabel);
            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 10, 18), color);
            EditorGUI.LabelField(new Rect(rect.x + 15, rect.y, rect.width - 15, 18), status);

            if (GUILayout.Button("Start Python Server"))
            {
                onStartServer();
            }

            EditorGUILayout.EndVertical();
        }

        public static void DrawLMStudioSection(string status, Action onRefreshStatus)
        {
            EditorGUILayout.LabelField($"Status: {status}", EditorStyles.wordWrappedLabel);

            if (GUILayout.Button("Refresh Status"))
            {
                onRefreshStatus();
            }
        }
    }
}
