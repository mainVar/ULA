using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

namespace UnityMCPOllama
{
    public static class ScriptCommands
    {
        public static void Handle(MCPCommand command)
        {
            // The python script base64 encodes the contents. We need to decode it.
            // Note: The python script sends 'contents' for create/update, but it's base64 encoded.
            // For simplicity, we'll assume the 'contents' field in args is always the base64 string.
            string decodedContents = "";
            if (!string.IsNullOrEmpty(command.args.contents))
            {
                try
                {
                    byte[] data = System.Convert.FromBase64String(command.args.contents);
                    decodedContents = Encoding.UTF8.GetString(data);
                }
                catch (System.FormatException)
                {
                    // If it's not a valid base64 string, use it as is.
                    // This handles the case where the python script might not encode it.
                    // However, our new prompt will always ask for encoding.
                    decodedContents = command.args.contents;
                }
            }


            switch (command.args.action)
            {
                case "create":
                    CreateScript(command.args, decodedContents);
                    break;
                // 'read', 'update', 'delete' can be added later.
                default:
                    Debug.LogError($"[ScriptCommands] Unknown action: {command.args.action}");
                    break;
            }
        }

        private static void CreateScript(Args args, string contents)
        {
            string path = args.path ?? "Assets/Scripts/";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string fullPath = Path.Combine(path, $"{args.name}.cs");

            if (File.Exists(fullPath))
            {
                Debug.LogWarning($"[ScriptCommands] Script already exists at {fullPath}. Aborting.");
                return;
            }

            // A simple template if no content is provided.
            if (string.IsNullOrEmpty(contents))
            {
                contents = $"using UnityEngine;\n\npublic class {args.name} : MonoBehaviour\n{{\n    // Start is called before the first frame update\n    void Start()\n    {{\n        \n    }}\n\n    // Update is called once per frame\n    void Update()\n    {{\n        \n    }}\n}}";
            }

            File.WriteAllText(fullPath, contents);
            AssetDatabase.Refresh();
            Debug.Log($"[ScriptCommands] Created script at {fullPath}");
        }
    }
}
