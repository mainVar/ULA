using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System;
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
        // Add USS stylesheet with a safe try/catch so we get styles back (animations + toggle visuals),
        // but avoid editor crashes if the stylesheet causes parsing/runtime errors in some Unity builds.
        if (styleSheet != null)
        {
            try
            {
                root.styleSheets.Add(styleSheet);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCPTools] Skipped adding USS due to error: {ex.Message}");
            }
        }

        toolItemTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UnityLocalAi/Editor/MCPTools/ToolItem.uxml");

        // Populate simple DropdownField choices from code to avoid UXML <Choice> deserialization issues.
        var typeDropdown = root.Q<DropdownField>("type-dropdown");
        if (typeDropdown != null)
        {
            typeDropdown.choices = new List<string> { "Enabled", "Disabled", "All" };
            typeDropdown.index = 0;
        }

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
            // Ensure the Toggle has a 'checked' class when initialized so USS can style it.
            if (toolToggle != null) toolToggle.EnableInClassList("checked", tool.IsEnabled);

            // The UXML uses class="tool-item-container" (not name). Query by class to find it.
            var toolItemContainer = toolItem.Q<VisualElement>(null, "tool-item-container");
            // Fallback: if not found, use the root element of the instantiated template.
            if (toolItemContainer == null) toolItemContainer = toolItem;

            UpdateToolItemClasses(toolItemContainer, toolToggle.value);

            // Capture the container reference for the callback to avoid re-querying.
            var capturedContainer = toolItemContainer;
            var capturedToggle = toolToggle;
            toolToggle.RegisterValueChangedCallback(evt => {
                // Update checked class on the Toggle itself so USS rules targeting .unity-toggle.checked apply.
                if (capturedToggle != null) capturedToggle.EnableInClassList("checked", evt.newValue);
                UpdateToolItemClasses(capturedContainer, evt.newValue);
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
                    var argItem = new VisualElement();
                    argItem.AddToClassList("argument-item");

                    var nameLabel = new Label(arg.Name);
                    nameLabel.AddToClassList("argument-name");
                    argItem.Add(nameLabel);

                    var descLabel = new Label(arg.Description);
                    descLabel.AddToClassList("argument-description");
                    argItem.Add(descLabel);

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
        if (toolItemContainer == null) return;
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
