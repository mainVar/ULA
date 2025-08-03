using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

namespace UnityLocalAi
{
    /// <summary>
    /// Виконує команди, отримані від Python MCP сервера, в редакторі Unity.
    /// Цей клас є виконавцем команд, що викликається з головного потоку редактора.
    /// </summary>
    [InitializeOnLoad]
    [Obsolete("This is the old bridge and is replaced by the new dispatcher-based UnityMCPBridge. It is kept for reference only.")]
    internal static class UnityMCPBridge_Old
    {
        static UnityMCPBridge_Old()
        {
            //Debug.Log("[MCP Bridge Old] Initialized. Ready to execute commands.");
        }

        /// <summary>
        /// Головний метод для обробки команд, отриманих від LLM.
        /// </summary>
        /// <param name="commands">Масив JSON-об'єктів, де кожен об'єкт - це команда.</param>
        public static void ProcessExtractedCommands(JArray commands)
        {
            if (commands == null || !commands.Any())
            {
                Debug.LogWarning("[MCP Bridge Old] Received no commands to execute.");
                return;
            }

            // --- ДІАГНОСТИЧНИЙ ЛОГ ---
            Debug.Log($"[MCP Bridge Old] Received a command array with {commands.Count} element(s). Full array: {commands.ToString(Newtonsoft.Json.Formatting.None)}");

            foreach (JToken cmdToken in commands)
            {
                // Перевіряємо, чи є елемент масиву валідним об'єктом команди
                if (cmdToken is JObject cmd)
                {
                    try
                    {
                        string function = cmd["function"]?.ToString();
                        JObject arguments = cmd["arguments"] as JObject ?? new JObject();

                        if (string.IsNullOrEmpty(function))
                        {
                            Debug.LogWarning("[MCP Bridge Old] Skipping command with empty function name.");
                            continue;
                        }

                        // --- ДІАГНОСТИЧНИЙ ЛОГ ---
                        Debug.Log($"[MCP Bridge Old] ==> Executing command: '{function}' with arguments: {arguments.ToString(Newtonsoft.Json.Formatting.None)}");

                        ExecuteCommandByName(function, arguments);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[MCP Bridge Old] Error while processing a command object: {ex.Message}\n{ex.StackTrace}");
                    }
                }
                else
                {
                    // --- ДІАГНОСТИЧНИЙ ЛОГ ---
                    Debug.LogWarning($"[MCP Bridge Old] Skipping an element in the command array because it's not a valid JSON object. Element type: {cmdToken.Type}, Value: {cmdToken.ToString(Newtonsoft.Json.Formatting.None)}");
                }
            }
        }

        /// <summary>
        /// Маршрутизатор команд, викликає відповідний приватний метод.
        /// </summary>
        private static void ExecuteCommandByName(string functionName, JObject arguments)
        {
            switch (functionName.ToLower())
            {
                case "create_object":
                    CreateObject(arguments);
                    break;
                case "set_object_transform":
                case "transform":
                    SetObjectTransform(arguments);
                    break;
                case "delete_object":
                    DeleteObject(arguments);
                    break;
                case "editor_action":
                    EditorAction(arguments);
                    break;
                // Додайте інші команди за потреби
                default:
                    Debug.LogWarning($"[MCP Bridge Old] Unimplemented or unknown command: {functionName}");
                    break;
            }
        }

        // --- Приватні методи для виконання конкретних команд ---

        private static void CreateObject(JObject args)
        {
            string name = args["name"]?.ToString() ?? "NewGameObject";
            string typeStr = args["type"]?.ToString()?.ToUpper() ?? "CUBE";

            GameObject obj;
            switch (typeStr)
            {
                case "CUBE": obj = GameObject.CreatePrimitive(PrimitiveType.Cube); break;
                case "SPHERE": obj = GameObject.CreatePrimitive(PrimitiveType.Sphere); break;
                case "CAPSULE": obj = GameObject.CreatePrimitive(PrimitiveType.Capsule); break;
                case "CYLINDER": obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder); break;
                case "PLANE": obj = GameObject.CreatePrimitive(PrimitiveType.Plane); break;
                case "EMPTY": obj = new GameObject(name); break;
                default:
                    Debug.LogWarning($"[MCP Bridge Old] Unknown object type '{typeStr}'. Creating a CUBE instead.");
                    obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    break;
            }

            obj.name = name;
            Debug.Log($"[MCP Bridge Old] Created '{typeStr}' named '{name}'.");

            // Встановлюємо трансформацію, якщо вона передана разом зі створенням
            SetObjectTransform(args, obj);
        }

        private static void SetObjectTransform(JObject args, GameObject target = null)
        {
            if (target == null)
            {
                string name = args["name"]?.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    // Якщо ім'я не вказано, можливо, це частина команди create_object,
                    // де target передається напряму. Тоді просто виходимо.
                    return;
                }
                target = GameObject.Find(name);
                if (target == null)
                {
                    Debug.LogError($"[MCP Bridge Old] Object '{name}' not found in scene for transform operation.");
                    return;
                }
            }

            if (args["position"] is JArray posArray)
            {
                target.transform.position = ParseVector3(posArray);
                Debug.Log($"[MCP Bridge Old] Set position of '{target.name}' to {target.transform.position}.");
            }
            if (args["rotation"] is JArray rotArray)
            {
                target.transform.eulerAngles = ParseVector3(rotArray);
                Debug.Log($"[MCP Bridge Old] Set rotation of '{target.name}' to {target.transform.eulerAngles}.");
            }
            if (args["scale"] is JArray scaleArray)
            {
                target.transform.localScale = ParseVector3(scaleArray);
                Debug.Log($"[MCP Bridge Old] Set scale of '{target.name}' to {target.transform.localScale}.");
            }
        }

        private static void DeleteObject(JObject args)
        {
            string name = args["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogError("[MCP Bridge Old] 'name' is required for delete_object.");
                return;
            }

            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                UnityEngine.Object.DestroyImmediate(obj);
                Debug.Log($"[MCP Bridge Old] Deleted object '{name}'.");
            }
            else
            {
                Debug.LogWarning($"[MCP Bridge Old] Object '{name}' not found for deletion.");
            }
        }

        private static void EditorAction(JObject args)
        {
            string action = args["action"]?.ToString()?.ToUpper();
            switch (action)
            {
                case "PLAY": EditorApplication.isPlaying = true; break;
                case "STOP": EditorApplication.isPlaying = false; break;
                case "PAUSE": EditorApplication.isPaused = !EditorApplication.isPaused; break;
                case "SAVE":
                    EditorSceneManager.SaveOpenScenes();
                    AssetDatabase.SaveAssets();
                    Debug.Log("[MCP Bridge Old] Scene and assets saved.");
                    break;
                default: Debug.LogWarning($"[MCP Bridge Old] Unknown editor action: {action}"); break;
            }
        }

        // --- Допоміжні методи ---

        private static Vector3 ParseVector3(JArray jArray)
        {
            if (jArray == null || jArray.Count < 3) return Vector3.zero;
            try
            {
                return new Vector3(
                    jArray[0].Value<float>(),
                    jArray[1].Value<float>(),
                    jArray[2].Value<float>()
                );
            }
            catch
            {
                Debug.LogWarning($"[MCP Bridge Old] Failed to parse Vector3 from JArray: {jArray.ToString()}");
                return Vector3.zero;
            }
        }
    }
}
