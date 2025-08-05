using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace UnityLocalAi
{
    public static class SceneCommands
    {
        public static void Handle(MCPCommand command)
        {
            switch (command.args.action)
            {
                case "new":
                    NewScene();
                    break;
                case "save":
                    SaveScene(command.args);
                    break;
                case "load":
                    LoadScene(command.args);
                    break;
                default:
                    Debug.LogError($"[SceneCommands] Unknown action: {command.args.action}");
                    break;
            }
        }

        private static void NewScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
            Debug.Log("[SceneCommands] New scene created.");
        }

        private static void SaveScene(Args args)
        {
            if (string.IsNullOrEmpty(args.name))
            {
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("[SceneCommands] Current scene saved.");
            }
            else
            {
                string path = $"Assets/{args.name}.unity";
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
                Debug.Log($"[SceneCommands] Scene saved to {path}");
            }
        }

        private static void LoadScene(Args args)
        {
            if (string.IsNullOrEmpty(args.name))
            {
                Debug.LogError("[SceneCommands] Scene name cannot be empty for 'load' action.");
                return;
            }

            string path = $"Assets/{args.name}.unity";
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(path);
                Debug.Log($"[SceneCommands] Scene loaded from {path}");
            }
        }
    }
}
