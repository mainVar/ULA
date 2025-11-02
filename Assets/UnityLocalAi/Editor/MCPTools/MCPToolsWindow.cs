using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class MCPToolsWindow : EditorWindow
{
    private VisualTreeAsset toolItemTemplate;

    [MenuItem("Window/MCP Tools")]
    public static void ShowWindow()
    {
        MCPToolsWindow wnd = GetWindow<MCPToolsWindow>();
        wnd.titleContent = new GUIContent("MCP Tools");
    }

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UnityLocalAi/Editor/MCPTools/MCPToolsWindow.uxml");
        visualTree.CloneTree(root);

        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UnityLocalAi/Editor/MCPTools/MCPToolsWindow.uss");
        root.styleSheets.Add(styleSheet);

        toolItemTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UnityLocalAi/Editor/MCPTools/ToolItem.uxml");

        PopulateToolList();
    }

    private void PopulateToolList()
    {
        var scrollView = rootVisualElement.Q<ScrollView>("tool-list-scrollview");
        var tools = GetMockTools(); // Replace with your actual data source

        foreach (var tool in tools)
        {
            var toolItem = toolItemTemplate.Instantiate();

            toolItem.Q<Label>("tool-title").text = tool.Title;
            toolItem.Q<Label>("tool-id").text = tool.Id;
            var toolToggle = toolItem.Q<Toggle>("tool-toggle");
            toolToggle.value = tool.IsEnabled;

            var toolItemContainer = toolItem.Q<VisualElement>("tool-item-container");
            UpdateToolItemClasses(toolItemContainer, toolToggle.value);

            toolToggle.RegisterValueChangedCallback(evt => {
                UpdateToolItemClasses(toolItemContainer, evt.newValue);
            });

            var descriptionFoldout = toolItem.Q<Foldout>("description-foldout");
            if (!string.IsNullOrEmpty(tool.Description))
            {
                descriptionFoldout.Q<Label>("description-text").text = tool.Description;
            }
            else
            {
                descriptionFoldout.style.display = DisplayStyle.None;
            }

            var argumentsFoldout = toolItem.Q<Foldout>("arguments-foldout");
            if (tool.Arguments != null && tool.Arguments.Count > 0)
            {
                argumentsFoldout.text = $"Input arguments ({tool.Arguments.Count})";
                var argumentsContainer = toolItem.Q("arguments-container");
                foreach (var arg in tool.Arguments)
                {
                    var argItem = new VisualElement() { className = "argument-item" };
                    argItem.Add(new Label(arg.Name) { className = "argument-name" });
                    argItem.Add(new Label(arg.Description) { className = "argument-description" });
                    argumentsContainer.Add(argItem);
                }
            }
            else
            {
                argumentsFoldout.style.display = DisplayStyle.None;
            }

            scrollView.Add(toolItem);
        }
    }

    private void UpdateToolItemClasses(VisualElement toolItemContainer, bool isEnabled)
    {
        toolItemContainer.EnableInClassList("enabled", isEnabled);
        toolItemContainer.EnableInClassList("disabled", !isEnabled);
    }

    // --- Mock Data ---
    private class ToolData
    {
        public string Title { get; set; }
        public string Id { get; set; }
        public bool IsEnabled { get; set; }
        public string Description { get; set; }
        public List<ArgumentData> Arguments { get; set; }
    }

    private class ArgumentData
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

    private List<ToolData> GetMockTools()
    {
        return new List<ToolData>
        {
            new ToolData
            {
                Title = "Tool title, could be multiple lines long",
                Id = "tool-id-single-line",
                IsEnabled = false,
                Description = "Tool description abc abc abc abc abc abc abc abc abc abc abc abc abc abc abc abc abc abc abc abc abc.",
                Arguments = new List<ArgumentData>
                {
                    new ArgumentData { Name = "dateOfBirth", Description = "Input arguments description. May contain multiple lines of the text here and to have dynamic height" },
                    new ArgumentData { Name = "fullName", Description = "Input arguments description. May contain multiple lines of the text here and to have dynamic height" }
                }
            },
            new ToolData
            {
                Title = "Tool title, could be multiple lines long",
                Id = "tool-id-single-line",
                IsEnabled = true,
                Description = "A different description for this tool.",
                Arguments = new List<ArgumentData>
                {
                    new ArgumentData { Name = "arg1", Description = "Desc 1" },
                    new ArgumentData { Name = "arg2", Description = "Desc 2" },
                    new ArgumentData { Name = "arg3", Description = "Desc 3" },
                    new ArgumentData { Name = "arg4", Description = "Desc 4" }
                }
            },
            new ToolData
            {
                Title = "This tool doesn't have input arguments",
                Id = "tool-id-single-line",
                IsEnabled = true,
                Description = "This is a simple tool with no inputs.",
                Arguments = new List<ArgumentData>()
            },
            new ToolData
            {
                Title = "This tool doesn't have description either, abc abc",
                Id = "tool-id-single-line",
                IsEnabled = true,
                Description = "",
                Arguments = new List<ArgumentData>()
            }
        };
    }
}
