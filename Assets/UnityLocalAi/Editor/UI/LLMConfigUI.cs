using UnityEngine;
using UnityEditor;
using System;

namespace UnityLocalAi.UI
{
    public static class LLMConfigUI
    {
        public static void Draw(LLMConfig config, Action onApplyChanges, Action onFetchModels)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("LM Studio Configuration", EditorStyles.boldLabel);

            config.lmstudioHost = EditorGUILayout.TextField("Host:", config.lmstudioHost);
            config.lmstudioPort = EditorGUILayout.IntField("Port:", config.lmstudioPort);

            DrawModelDropdown(config, onFetchModels);

            config.lmstudioTemperature = EditorGUILayout.Slider("Temperature:", config.lmstudioTemperature, 0f, 1f);

            if (GUILayout.Button("Apply & Save Configuration"))
            {
                onApplyChanges();
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawModelDropdown(LLMConfig config, Action onFetchModels)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(config.fetchingModels || config.availableModels.Count == 0);

            int newIndex = EditorGUILayout.Popup("Model:", config.selectedModelIndex, config.availableModels.ToArray());
            if (newIndex != config.selectedModelIndex && newIndex >= 0)
            {
                config.selectedModelIndex = newIndex;
                config.lmstudioModel = config.availableModels[config.selectedModelIndex];
            }

            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button(config.fetchingModels ? "..." : "Refresh", GUILayout.Width(70)))
            {
                onFetchModels();
            }
            EditorGUILayout.EndHorizontal();

            if (config.availableModels.Count == 0 && !config.fetchingModels)
            {
                EditorGUILayout.HelpBox("Could not find any models. Is LM Studio running? Press Refresh to try again.", MessageType.Info);
            }
        }
    }
}
