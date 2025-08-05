using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UnityMCPOllama
{
    public class UnityMCPBridge
    {
        // This is the single entry point for all commands from the Python server.
        public static void ExecuteCommands(string jsonCommands)
        {
            if (string.IsNullOrEmpty(jsonCommands))
            {
                Debug.LogWarning("[UnityMCPBridge] Received empty or null command string.");
                return;
            }

            // Unity's JsonUtility doesn't support deserializing a root array directly.
            // We need to wrap it in an object.
            string wrappedJson = $"{{\"commands\":{jsonCommands}}}";
            CommandList commandList = JsonUtility.FromJson<CommandList>(wrappedJson);

            if (commandList == null || commandList.commands == null)
            {
                Debug.LogError("[UnityMCPBridge] Failed to deserialize command JSON.");
                // As a fallback, try to deserialize a single command object
                MCPCommand singleCommand = JsonUtility.FromJson<MCPCommand>(jsonCommands);
                if (singleCommand != null && !string.IsNullOrEmpty(singleCommand.function))
                {
                    commandList = new CommandList { commands = new List<MCPCommand> { singleCommand } };
                }
                else
                {
                    Debug.LogError("[UnityMCPBridge] Fallback deserialization also failed.");
                    return;
                }
            }

            foreach (var command in commandList.commands)
            {
                // Ensure all Unity API calls are made on the main thread.
                EditorApplication.delayCall += () =>
                {
                    DispatchCommand(command);
                };
            }
        }

        private static void DispatchCommand(MCPCommand command)
        {
            if (command == null || string.IsNullOrEmpty(command.function))
            {
                Debug.LogError("[UnityMCPBridge] Received an invalid command object.");
                return;
            }

            Debug.Log($"[UnityMCPBridge] Dispatching function: {command.function} with action: {command.args?.action ?? "N/A"}");

            switch (command.function)
            {
                case "manage_gameobject":
                    GameObjectCommands.Handle(command);
                    break;
                case "manage_scene":
                    SceneCommands.Handle(command);
                    break;
                case "manage_script":
                    ScriptCommands.Handle(command);
                    break;
                 case "manage_shader":
                    // We can reuse ScriptCommands for shaders as the logic is identical
                    ScriptCommands.Handle(command);
                    break;
                case "manage_asset":
                    AssetCommands.Handle(command);
                    break;

                // These three are all handled by the EditorCommands class
                case "manage_editor":
                case "read_console":
                case "execute_menu_item":
                    EditorCommands.Handle(command);
                    break;

                default:
                    Debug.LogError($"[UnityMCPBridge] No handler found for function: {command.function}");
                    break;
            }
        }

        // Helper class to wrap the JSON array for deserialization
        [System.Serializable]
        private class CommandList
        {
            public List<MCPCommand> commands;
        }
    }
}
