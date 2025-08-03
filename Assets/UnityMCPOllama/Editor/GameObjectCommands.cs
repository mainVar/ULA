using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace UnityMCPOllama
{
    public static class GameObjectCommands
    {
        public static void Handle(MCPCommand command)
        {
            switch (command.args.action)
            {
                case "create":
                    CreateGameObject(command.args);
                    break;
                case "modify":
                    ModifyGameObject(command.args);
                    break;
                case "delete":
                    DeleteGameObject(command.args);
                    break;
                case "add_component":
                    AddComponent(command.args);
                    break;
                // Add other cases like find, remove_component etc. later
                default:
                    Debug.LogError($"[GameObjectCommands] Unknown action: {command.args.action}");
                    break;
            }
        }

        private static void CreateGameObject(Args args)
        {
            GameObject newObj;
            if (!string.IsNullOrEmpty(args.primitive_type))
            {
                PrimitiveType pt;
                if (System.Enum.TryParse<PrimitiveType>(args.primitive_type, true, out pt))
                {
                    newObj = GameObject.CreatePrimitive(pt);
                }
                else
                {
                    Debug.LogError($"[GameObjectCommands] Invalid primitive type: {args.primitive_type}");
                    return;
                }
            }
            else
            {
                newObj = new GameObject();
            }

            newObj.name = args.name ?? "NewGameObject";

            if (args.position != null && args.position.Length == 3)
                newObj.transform.position = new Vector3(args.position[0], args.position[1], args.position[2]);
            if (args.rotation != null && args.rotation.Length == 3)
                newObj.transform.eulerAngles = new Vector3(args.rotation[0], args.rotation[1], args.rotation[2]);
            if (args.scale != null && args.scale.Length == 3)
                newObj.transform.localScale = new Vector3(args.scale[0], args.scale[1], args.scale[2]);

            if (!string.IsNullOrEmpty(args.parent))
            {
                GameObject parentObj = GameObject.Find(args.parent);
                if (parentObj != null)
                {
                    newObj.transform.SetParent(parentObj.transform);
                }
            }

            if (!string.IsNullOrEmpty(args.tag))
            {
                newObj.tag = args.tag;
            }

            if (args.components_to_add != null)
            {
                foreach (var componentName in args.components_to_add)
                {
                    // For built-in components, we might need a mapping from string to Type
                    // This is a simplified approach.
                    var type = System.Type.GetType($"UnityEngine.{componentName}, UnityEngine");
                     if (type == null) {
                        // If it's not a built-in, search for it in the project (custom scripts)
                        type = FindTypeByName(componentName);
                    }

                    if (type != null)
                    {
                        newObj.AddComponent(type);
                    }
                    else
                    {
                        Debug.LogWarning($"[GameObjectCommands] Could not find component type: {componentName}");
                    }
                }
            }

            if (args.component_properties != null)
            {
                if (args.component_properties.Rigidbody != null) {
                    var rb = newObj.GetComponent<Rigidbody>();
                    if (rb != null) {
                        rb.useGravity = args.component_properties.Rigidbody.useGravity;
                        // Parsing string to enum for constraints would be needed here
                    }
                }
                if (args.component_properties.MeshRenderer != null) {
                    var mr = newObj.GetComponent<MeshRenderer>();
                    if (mr != null && !string.IsNullOrEmpty(args.component_properties.MeshRenderer.sharedMaterial)) {
                        Material mat = AssetDatabase.LoadAssetAtPath<Material>(args.component_properties.MeshRenderer.sharedMaterial);
                        if (mat != null) {
                            mr.sharedMaterial = mat;
                        } else {
                            Debug.LogWarning($"Could not find material at path: {args.component_properties.MeshRenderer.sharedMaterial}");
                        }
                    }
                }
            }

            if (args.save_as_prefab && !string.IsNullOrEmpty(args.prefab_path))
            {
                string dirPath = Path.GetDirectoryName(args.prefab_path);
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }
                PrefabUtility.SaveAsPrefabAsset(newObj, args.prefab_path);
                Debug.Log($"[GameObjectCommands] Saved {newObj.name} as prefab at {args.prefab_path}");
            }

            Debug.Log($"[GameObjectCommands] Created GameObject: {newObj.name}");
        }

        private static void ModifyGameObject(Args args)
        {
            GameObject targetObj = GameObject.Find(args.target);
            if (targetObj == null)
            {
                Debug.LogError($"[GameObjectCommands] Could not find target GameObject: {args.target}");
                return;
            }

            if (!string.IsNullOrEmpty(args.name))
            {
                targetObj.name = args.name;
            }
             if (args.position != null && args.position.Length == 3)
                targetObj.transform.position = new Vector3(args.position[0], args.position[1], args.position[2]);
            if (args.rotation != null && args.rotation.Length == 3)
                targetObj.transform.eulerAngles = new Vector3(args.rotation[0], args.rotation[1], args.rotation[2]);
            if (args.scale != null && args.scale.Length == 3)
                targetObj.transform.localScale = new Vector3(args.scale[0], args.scale[1], args.scale[2]);

            if (args.set_active != null)
            {
                targetObj.SetActive(args.set_active);
            }

            Debug.Log($"[GameObjectCommands] Modified GameObject: {targetObj.name}");
        }

        private static void DeleteGameObject(Args args)
        {
            GameObject targetObj = GameObject.Find(args.target);
            if (targetObj != null)
            {
                Object.DestroyImmediate(targetObj);
                Debug.Log($"[GameObjectCommands] Deleted GameObject: {args.target}");
            }
            else
            {
                Debug.LogError($"[GameObjectCommands] Could not find target GameObject to delete: {args.target}");
            }
        }

        private static void AddComponent(Args args)
        {
            GameObject targetObj = GameObject.Find(args.target);
            if (targetObj == null)
            {
                Debug.LogError($"[GameObjectCommands] Could not find target GameObject: {args.target}");
                return;
            }

            if (args.components_to_add != null)
            {
                foreach (var componentName in args.components_to_add)
                {
                     var type = System.Type.GetType($"UnityEngine.{componentName}, UnityEngine");
                     if (type == null) {
                        type = FindTypeByName(componentName);
                    }
                    if (type != null)
                    {
                        targetObj.AddComponent(type);
                        Debug.Log($"[GameObjectCommands] Added component {componentName} to {targetObj.name}");
                    }
                    else
                    {
                        Debug.LogWarning($"[GameObjectCommands] Could not find component type to add: {componentName}");
                    }
                }
            }
        }

        // Helper to find custom script types by name
        private static System.Type FindTypeByName(string name)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == name)
                    {
                        return type;
                    }
                }
            }
            return null;
        }
    }
}
