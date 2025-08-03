using System;
using System.Collections.Generic;

namespace UnityLocalAi
{
    [Serializable]
    public class MCPCommand
    {
        public string function;
        public Args args;
    }

    [Serializable]
    public class Args
    {
        // Universal arguments
        public string action;
        public string name;
        public string path;
        public string contents;
        public string target;

        // GameObject arguments
        public string primitive_type;
        public float[] position;
        public float[] rotation;
        public float[] scale;
        public string parent;
        public string tag;
        public string layer;
        public string[] components_to_add;
        public string[] components_to_remove;
        public ComponentProperties component_properties;
        public bool set_active;
        public bool save_as_prefab;
        public string prefab_path;

        // Asset arguments
        public string asset_type;
        public AssetProperties properties;
        public string destination;

        // Scripting arguments
        public string script_type;
        public string anamespace;

        // Console arguments
        public string[] types;

        // Menu Item arguments
        public string menu_path;
    }

    [Serializable]
    public class ComponentProperties
    {
        // This is a placeholder. We will need a more dynamic way to handle this,
        // likely by parsing this part of the JSON manually.
        // For now, we'll add properties we know we need, like for MeshRenderer.
        public RigidbodyProps Rigidbody;
        public MeshRendererProps MeshRenderer;
    }

    [Serializable]
    public class RigidbodyProps
    {
        public bool useGravity;
        public string constraints;
    }

    [Serializable]
    public class MeshRendererProps
    {
        public string sharedMaterial;
    }

    [Serializable]
    public class AssetProperties
    {
        public float[] color;
        public string shader;
    }
}
