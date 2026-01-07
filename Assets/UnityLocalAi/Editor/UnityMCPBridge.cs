using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UnityLocalAi
{
    public static class UnityMCPBridge
    {
        public static void ExecuteCommands(string jsonCommands)
        {
            if (string.IsNullOrEmpty(jsonCommands))
            {
                Debug.LogWarning("[UnityMCPBridge] Received empty or null command string.");
                return;
            }

            string wrappedJson = $"{{\"commands\":{jsonCommands}}}";
            CommandList commandList = JsonUtility.FromJson<CommandList>(wrappedJson);

            if (commandList == null || commandList.commands == null)
            {
                Debug.LogError("[UnityMCPBridge] Failed to deserialize command JSON.");
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
                case "manage_shader":
                    ScriptCommands.Handle(command);
                    break;
                case "manage_asset":
                    AssetCommands.Handle(command);
                    break;
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

        [System.Serializable]
        private class CommandList
        {
            public List<MCPCommand> commands;
        }
    }
}
