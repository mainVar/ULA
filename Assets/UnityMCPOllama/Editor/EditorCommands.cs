using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UnityMCPOllama
{
    public static class EditorCommands
    {
        public static void Handle(MCPCommand command)
        {
            // The function name from the prompt maps directly to the handler class.
            // So we can switch on the function name here.
            switch (command.function)
            {
                case "manage_editor":
                    ManageEditor(command.args);
                    break;
                case "read_console":
                    ReadConsole(command.args);
                    break;
                case "execute_menu_item":
                    ExecuteMenuItem(command.args);
                    break;
                default:
                    Debug.LogError($"[EditorCommands] Unknown function dispatched: {command.function}");
                    break;
            }
        }

        private static void ManageEditor(Args args)
        {
            switch (args.action)
            {
                case "play":
                    EditorApplication.isPlaying = true;
                    Debug.Log("[EditorCommands] Editor is now playing.");
                    break;
                case "pause":
                    EditorApplication.isPaused = true;
                    Debug.Log("[EditorCommands] Editor is now paused.");
                    break;
                case "stop":
                    EditorApplication.isPlaying = false;
                    Debug.Log("[EditorCommands] Editor has stopped playing.");
                    break;
                case "get_state":
                    // This would require sending a response back to the server,
                    // which complicates our current one-way command flow.
                    // For now, we just log the state.
                    Debug.Log($"[EditorCommands] Editor State: isPlaying={EditorApplication.isPlaying}, isPaused={EditorApplication.isPaused}");
                    break;
                default:
                    Debug.LogError($"[EditorCommands] Unknown 'manage_editor' action: {args.action}");
                    break;
            }
        }

        private static void ReadConsole(Args args)
        {
            // Reading the console and sending it back also requires a response mechanism.
            // For now, this is a placeholder to show where the logic would go.
            // A full implementation would require a way to send data back to the python server.
             if (args.action == "clear")
            {
                // This is a bit of a hack, but it works.
                var logEntries = System.Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
                var clearMethod = logEntries.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                clearMethod.Invoke(null, null);
                Debug.Log("[EditorCommands] Console cleared.");
            }
            else if (args.action == "get")
            {
                 Debug.LogWarning("[EditorCommands] 'get' action for 'read_console' is not fully implemented as it requires sending data back to the server.");
            }
        }

        private static void ExecuteMenuItem(Args args)
        {
            if (string.IsNullOrEmpty(args.menu_path))
            {
                Debug.LogError("[EditorCommands] 'menu_path' is required for 'execute_menu_item'.");
                return;
            }

            bool success = EditorApplication.ExecuteMenuItem(args.menu_path);
            if (success)
            {
                Debug.Log($"[EditorCommands] Successfully executed menu item: {args.menu_path}");
            }
            else
            {
                Debug.LogWarning($"[EditorCommands] Failed to execute menu item: {args.menu_path}. It may not exist or is currently disabled.");
            }
        }
    }
}
