using UnityEngine;
using UnityEditor;
using System.IO;

namespace UnityLocalAi
{
    public static class AssetCommands
    {
        public static void Handle(MCPCommand command)
        {
            switch (command.args.action)
            {
                case "create":
                    CreateAsset(command.args);
                    break;
                case "create_folder":
                    CreateFolder(command.args);
                    break;
                default:
                    Debug.LogError($"[AssetCommands] Unknown action: {command.args.action}");
                    break;
            }
        }

        private static void CreateAsset(Args args)
        {
            if (string.IsNullOrEmpty(args.asset_type))
            {
                Debug.LogError("[AssetCommands] 'asset_type' is required for the 'create' action.");
                return;
            }

            if (string.IsNullOrEmpty(args.path))
            {
                Debug.LogError("[AssetCommands] 'path' is required for the 'create' action.");
                return;
            }

            // Ensure the directory exists
            string dirPath = Path.GetDirectoryName(args.path);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            if (args.asset_type.ToLower() == "material")
            {
                Material newMat = new Material(Shader.Find("Standard")); // Default to standard shader

                if (args.properties != null)
                {
                    if (!string.IsNullOrEmpty(args.properties.shader))
                    {
                        Shader shader = Shader.Find(args.properties.shader);
                        if (shader != null)
                        {
                            newMat.shader = shader;
                        }
                    }
                    if (args.properties.color != null && args.properties.color.Length == 4)
                    {
                        newMat.color = new Color(args.properties.color[0], args.properties.color[1], args.properties.color[2], args.properties.color[3]);
                    }
                }

                AssetDatabase.CreateAsset(newMat, args.path);
                Debug.Log($"[AssetCommands] Created Material at {args.path}");
            }
            else
            {
                Debug.LogWarning($"[AssetCommands] Asset type '{args.asset_type}' not yet supported for creation.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CreateFolder(Args args)
        {
            if (string.IsNullOrEmpty(args.path))
            {
                Debug.LogError("[AssetCommands] 'path' is required for the 'create_folder' action.");
                return;
            }

            if (!Directory.Exists(args.path))
            {
                Directory.CreateDirectory(args.path);
                AssetDatabase.Refresh();
                Debug.Log($"[AssetCommands] Created folder at {args.path}");
            }
            else
            {
                Debug.LogWarning($"[AssetCommands] Folder already exists at {args.path}");
            }
        }
    }
}
