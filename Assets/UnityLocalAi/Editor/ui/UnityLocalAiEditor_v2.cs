using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityLocalAi;

public class UnityLocalAiEditor_v2 : EditorWindow
{
    // --- UI Elements ---
    private Button newChatButton;
    private Button deleteChatButton;
    private ScrollView chatList;
    private VisualElement serverStatusIndicator;
    private Label serverStatusLabel;
    private Button startServerButton;
    private TextField hostTextField;
    private TextField portTextField;
    private DropdownField modelDropdown;
    private Button refreshModelsButton;
    private Slider temperatureSlider;
    private Label temperatureValueLabel;
    private Button applyButton;
    private ScrollView chatHistory;
    private TextField userInputTextField;
    private Button sendButton;

    // --- Icons ---
    private Texture2D headerIcon;
    private Texture2D sendIcon;
    private Texture2D userIcon;
    private Texture2D assistantIcon;

    // --- UI State & Configuration ---
    private string lmstudioHost = "localhost";
    private int lmstudioPort = 1234;
    private string lmstudioModel = "qwen/qwen3-14b";
    private float lmstudioTemperature = 0.8f;

    // --- Model Selection ---
    private List<string> availableModels = new List<string>();
    private bool fetchingModels = false;

    // --- Networking ---
    private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    private const int MCP_PORT = 6500;
    private float lastCheckTime = -10f;
    private const float CONNECTION_CHECK_INTERVAL = 5f;

    [MenuItem("Window/Unity Local AI v2")]
    public static void ShowWindow()
    {
        UnityLocalAiEditor_v2 wnd = GetWindow<UnityLocalAiEditor_v2>();
        wnd.titleContent = new GUIContent("Unity Local AI");
    }

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;

        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UnityLocalAi/Editor/ui/UnityLocalAiEditor_v2.uxml");
        visualTree.CloneTree(root);

        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UnityLocalAi/Editor/ui/UnityLocalAiEditor_v2.uss");
        root.styleSheets.Add(styleSheet);

        LoadIcons();
        QueryUIElements(root);
        AssignIcons();
        RegisterCallbacks();

        LoadLMStudioConfig();
        InitialStatusCheck();
    }

    private void LoadIcons()
    {
        // Placeholder paths - replace with actual icon assets when available
        headerIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UnityLocalAi/Editor/ui/Icons/icon_header.png");
        sendIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UnityLocalAi/Editor/ui/Icons/icon_send.png");
        userIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UnityLocalAi/Editor/ui/Icons/icon_user.png");
        assistantIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UnityLocalAi/Editor/ui/Icons/icon_ai.png");
    }

    private void AssignIcons()
    {
        rootVisualElement.Q<Image>("header-icon").image = headerIcon;
        rootVisualElement.Q<Image>("send-button-icon").image = sendIcon;
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup - lastCheckTime >= CONNECTION_CHECK_INTERVAL)
        {
            lastCheckTime = Time.realtimeSinceStartup;
            CheckPythonServerLiveness();
        }
    }

    private void QueryUIElements(VisualElement root)
    {
        newChatButton = root.Q<Button>("new-chat-button");
        deleteChatButton = root.Q<Button>("delete-chat-button");
        chatList = root.Q<ScrollView>("chat-list");
        serverStatusIndicator = root.Q<VisualElement>("server-status-indicator");
        serverStatusLabel = root.Q<Label>("server-status-label");
        startServerButton = root.Q<Button>("start-server-button");
        hostTextField = root.Q<TextField>("host-text-field");
        portTextField = root.Q<TextField>("port-text-field");
        modelDropdown = root.Q<DropdownField>("model-dropdown");
        refreshModelsButton = root.Q<Button>("refresh-models-button");
        temperatureSlider = root.Q<Slider>("temperature-slider");
        temperatureValueLabel = root.Q<Label>("temperature-value-label");
        applyButton = root.Q<Button>("apply-button");
        chatHistory = root.Q<ScrollView>("chat-history");
        userInputTextField = root.Q<TextField>("user-input-text-field");
        sendButton = root.Q<Button>("send-button");
    }

    private void RegisterCallbacks()
    {
        startServerButton.clicked += StartPythonServer;
        refreshModelsButton.clicked += () => FetchAvailableModels();
        applyButton.clicked += UpdateLMStudioConfigOnServer;
        sendButton.clicked += () => SendChatMessage(userInputTextField.text);
        userInputTextField.RegisterCallback<KeyDownEvent>(evt => {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                SendChatMessage(userInputTextField.text);
            }
        });

        temperatureSlider.RegisterValueChangedCallback(evt =>
        {
            temperatureValueLabel.text = evt.newValue.ToString("F1");
        });

        newChatButton.clicked += () => Debug.Log("New Chat button clicked - functionality not yet implemented.");
        deleteChatButton.clicked += () => Debug.Log("Delete Chat button clicked - functionality not yet implemented.");
    }

    private void AddMessageToChatHistory(string sender, string message)
    {
        var messageContainer = new VisualElement();
        messageContainer.AddToClassList("message-container");

        var icon = new Image
        {
            image = (sender == "You" ? userIcon : assistantIcon)
        };
        icon.AddToClassList("message-icon");

        var messageBubble = new VisualElement();
        messageBubble.AddToClassList("message-bubble");

        var messageLabel = new Label(message);
        messageBubble.Add(messageLabel);

        if (sender == "You")
        {
            messageContainer.AddToClassList("message-container--user");
            messageBubble.AddToClassList("message-bubble--user");
            messageContainer.Add(messageBubble);
            messageContainer.Add(icon);
        }
        else
        {
            messageBubble.AddToClassList("message-bubble--assistant");
            messageContainer.Add(icon);
            messageContainer.Add(messageBubble);
        }

        chatHistory.Add(messageContainer);
        chatHistory.schedule.Execute(() => chatHistory.ScrollTo(messageContainer));
    }


    private async void SendChatMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        AddMessageToChatHistory("You", message);
        userInputTextField.value = "";

        var thinkingContainer = new VisualElement();
        thinkingContainer.AddToClassList("message-container");
        var thinkingIcon = new Image { image = assistantIcon };
        thinkingIcon.AddToClassList("message-icon");
        var thinkingBubble = new Label("<i>Processing...</i>");
        thinkingBubble.AddToClassList("message-bubble");
        thinkingBubble.AddToClassList("message-bubble--assistant");
        thinkingContainer.Add(thinkingIcon);
        thinkingContainer.Add(thinkingBubble);
        chatHistory.Add(thinkingContainer);
        chatHistory.schedule.Execute(() => chatHistory.ScrollTo(thinkingContainer));


        try
        {
            var requestPayload = new { prompt = message };
            string jsonPayload = JsonConvert.SerializeObject(requestPayload);
            string url = $"http://localhost:{MCP_PORT}/api/process";

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(url, content);
            string responseJson = await response.Content.ReadAsStringAsync();

            chatHistory.Remove(thinkingContainer);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Server returned error {response.StatusCode}: {responseJson}");
            }

            var result = JObject.Parse(responseJson);
            string llmResponse = result["llm_response"]?.ToString() ?? "No text response.";
            JArray commands = result["commands"] as JArray;

            AddMessageToChatHistory("Assistant", llmResponse);

            if (commands != null && commands.Count > 0)
            {
                EditorApplication.delayCall += () => UnityMCPBridge.ExecuteCommands(commands.ToString());
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MCP] Error sending chat message: {ex.Message}");
            chatHistory.Remove(thinkingContainer);
            AddMessageToChatHistory("Assistant", $"<b>Error:</b> {ex.Message}");
        }
    }

    private async void InitialStatusCheck()
    {
        await CheckPythonServerLiveness();
    }

    private async Task CheckPythonServerLiveness()
    {
        try
        {
            string healthUrl = $"http://localhost:{MCP_PORT}/health";
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            {
                HttpResponseMessage healthResponse = await httpClient.GetAsync(healthUrl, cts.Token);
                if (healthResponse.IsSuccessStatusCode)
                {
                    bool wasConnected = serverStatusLabel.text == "Connected";
                    serverStatusLabel.text = "Connected";
                    ColorUtility.TryParseHtmlString("#22c55e", out var connectedColor);
                    serverStatusIndicator.style.backgroundColor = connectedColor;

                    if (!wasConnected)
                    {
                        await FetchAvailableModels();
                    }
                }
                else
                {
                    throw new Exception("Health check failed.");
                }
            }
        }
        catch
        {
            if (serverStatusLabel.text != "Disconnected")
            {
                serverStatusLabel.text = "Disconnected";
                 ColorUtility.TryParseHtmlString("#ef4444", out var disconnectedColor);
                serverStatusIndicator.style.backgroundColor = disconnectedColor;
            }
        }
    }

    private async Task FetchAvailableModels()
    {
        if (fetchingModels) return;
        fetchingModels = true;
        modelDropdown.choices.Clear();

        try
        {
            string url = $"http://localhost:{MCP_PORT}/api/models";
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                HttpResponseMessage response = await httpClient.GetAsync(url, cts.Token);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                var models = JsonConvert.DeserializeObject<List<string>>(json);

                if (models != null && models.Count > 0)
                {
                    availableModels = models;
                    modelDropdown.choices = availableModels;
                    int currentIndex = availableModels.IndexOf(lmstudioModel);
                    if (currentIndex < 0)
                    {
                        currentIndex = 0;
                    }
                    modelDropdown.index = currentIndex;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MCP] Failed to fetch available models: {ex.Message}");
            modelDropdown.choices.Clear();
        }
        finally
        {
            fetchingModels = false;
        }
    }

    private async void UpdateLMStudioConfigOnServer()
    {
        if (!int.TryParse(portTextField.value, out int port))
        {
            Debug.LogError("[MCP] Invalid port number. Please enter a valid integer.");
            return;
        }

        try
        {
            var configPayload = new { host = hostTextField.value, port, model = modelDropdown.value, temperature = temperatureSlider.value };
            string jsonPayload = JsonConvert.SerializeObject(configPayload);
            string url = $"http://localhost:{MCP_PORT}/api/configure";
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
            {
                Debug.Log("[MCP] Configuration successfully updated on the server.");
                SaveLMStudioConfig();
            }
            else
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to update config: {error}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MCP] Error updating configuration: {ex.Message}");
        }
    }

    private void SaveLMStudioConfig()
    {
        if (!int.TryParse(portTextField.value, out int port))
        {
            Debug.LogError("[MCP] Cannot save config: Invalid port number.");
            return;
        }

        try
        {
            string path = GetLocalConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var cfg = new { lmstudio_host = hostTextField.value, lmstudio_port = port, lmstudio_model = modelDropdown.value, lmstudio_temperature = temperatureSlider.value };
            File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
            Debug.Log($"[MCP] Local config saved to {path}");
        }
        catch (Exception ex) { Debug.LogError($"[MCP] Failed to save local config: {ex.Message}"); }
    }

    private void LoadLMStudioConfig()
    {
        try
        {
            string path = GetLocalConfigPath();
            if (!File.Exists(path)) return;
            string json = File.ReadAllText(path);
            var cfg = JObject.Parse(json);
            hostTextField.value = cfg["lmstudio_host"]?.ToString() ?? lmstudioHost;
            portTextField.value = (cfg["lmstudio_port"]?.Value<int>() ?? lmstudioPort).ToString();
            lmstudioModel = cfg["lmstudio_model"]?.ToString() ?? lmstudioModel;
            temperatureSlider.value = cfg["lmstudio_temperature"]?.Value<float>() ?? lmstudioTemperature;
            temperatureValueLabel.text = temperatureSlider.value.ToString("F1");
        }
        catch (Exception ex) { Debug.LogError($"[MCP] Failed to load local config: {ex.Message}"); }
    }

    private string GetLocalConfigPath()
    {
        string dir = Path.Combine(Application.dataPath, "..", "Temp", "LMStudioConfig");
        return Path.Combine(dir, "lmstudio_config.json");
    }

    private string GetProjectRootPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private void StartPythonServer()
    {
        string pythonScriptPath = Path.Combine(GetProjectRootPath(), "Assets", "Python", "Python", "lmstudio_mcp_server.py");
        string pythonExecutable = "python3";

        if (!File.Exists(pythonScriptPath))
        {
            Debug.LogError($"[MCP] Python script not found at: {pythonScriptPath}");
            return;
        }

        try
        {
            var process = new System.Diagnostics.Process();

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                pythonExecutable = "python";
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c start \"Unity MCP Server\" /D \"{Path.GetDirectoryName(pythonScriptPath)}\" {pythonExecutable} \"{Path.GetFileName(pythonScriptPath)}\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
            }
            else if (Application.platform == RuntimePlatform.OSXEditor)
            {
                string script = $"tell application \"Terminal\" to do script \"cd \\\"{Path.GetDirectoryName(pythonScriptPath)}\\\" && {pythonExecutable} \\\"{Path.GetFileName(pythonScriptPath)}\\\"\"";
                process.StartInfo.FileName = "osascript";
                process.StartInfo.Arguments = $"-e '{script}'";
                process.StartInfo.UseShellExecute = true;
            }
            else
            {
                process.StartInfo.FileName = "gnome-terminal";
                process.StartInfo.Arguments = $"--working-directory=\"{Path.GetDirectoryName(pythonScriptPath)}\" -- {pythonExecutable} \"{Path.GetFileName(pythonScriptPath)}\"";
                process.StartInfo.UseShellExecute = true;
            }

            process.Start();
            Debug.Log("[MCP] Attempting to start Python server in a new terminal window.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MCP] Failed to start Python server: {ex.Message}");
            EditorUtility.DisplayDialog(
                "Failed to Start Server",
                "Could not start the Python server. Please ensure you have Python (and the correct terminal for your OS) installed and in your system's PATH. " +
                "You may need to start it manually by running:\n\n" +
                $"python3 {pythonScriptPath}\n\n" +
                $"Error: {ex.Message}",
                "OK"
            );
        }
    }
}
