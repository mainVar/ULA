using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityLocalAi; // <-- Add the new namespace


public class MCPEditorWindow : EditorWindow
{
    // --- UI State & Configuration ---
    private string pythonServerStatus = "Checking...";
    private Color pythonServerColor = Color.yellow;

    private string lmstudioStatusMessage = "N/A";

    private string lmstudioHost = "localhost";
    private int lmstudioPort = 1234;
    private string lmstudioModel = "qwen/qwen3-14b";
    private float lmstudioTemperature = 0.2f;

    // --- Model Selection ---
    private List<string> availableModels = new List<string>();
    private int selectedModelIndex = -1;
    private bool fetchingModels = false;

    // --- Chat ---
    private List<ChatMessage> chatHistory = new List<ChatMessage>();

    // --- UI Toolkit Elements ---
    private VisualElement pythonServerStatusIndicator;
    private Label pythonServerStatusLabel;
    private Button startServerButton;
    private Label lmstudioStatusLabel;
    private Button refreshLMStatusButton;
    private TextField lmstudioHostField;
    private IntegerField lmstudioPortField;
    private PopupField<string> lmstudioModelDropdown;
    private Slider lmstudioTemperatureSlider;
    private Button applyLMConfigButton;
    private ScrollView chatScrollView;
    private TextField chatInput;
    private Button sendButton;


    // --- Networking ---
    private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    private const int MCP_PORT = 6500;
    private float lastCheckTime = -10f; // Initial value to ensure an immediate check
    private const float CONNECTION_CHECK_INTERVAL = 5f;

    [MenuItem("Window/Unity Local AI")]
    public static void ShowWindow() => GetWindow<MCPEditorWindow>("Local AI");

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        // Import UXML
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UnityLocalAi/Editor/UI/UnityLocalAiEditor.uxml");
        VisualElement labelFromUXML = visualTree.Instantiate();
        root.Add(labelFromUXML);

        // Import USS
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UnityLocalAi/Editor/UI/UnityLocalAiEditor.uss");
        root.styleSheets.Add(styleSheet);

        // Get references to UI elements
        pythonServerStatusIndicator = root.Q<VisualElement>("PythonServerStatusIndicator");
        pythonServerStatusLabel = root.Q<Label>("PythonServerStatusLabel");
        startServerButton = root.Q<Button>("StartServerButton");
        lmstudioStatusLabel = root.Q<Label>("LMStudioStatusLabel");
        refreshLMStatusButton = root.Q<Button>("RefreshLMStatusButton");
        lmstudioHostField = root.Q<TextField>("LMStudioHost");
        lmstudioPortField = root.Q<IntegerField>("LMStudioPort");
        lmstudioModelDropdown = new PopupField<string>("Model:", availableModels, 0);
        root.Q<VisualElement>("LMStudioSection").Insert(4, lmstudioModelDropdown);
        lmstudioTemperatureSlider = root.Q<Slider>("LMStudioTemperature");
        applyLMConfigButton = root.Q<Button>("ApplyLMConfigButton");
        chatScrollView = root.Q<ScrollView>("ChatScrollView");
        chatInput = root.Q<TextField>("ChatInput");
        sendButton = root.Q<Button>("SendButton");

        // Register event callbacks
        startServerButton.clicked += StartPythonServer;
        refreshLMStatusButton.clicked += () => CheckLMStudioStatus();
        applyLMConfigButton.clicked += UpdateLMStudioConfigOnServer;
        sendButton.clicked += () => SendChatMessage(chatInput.value);

        lmstudioHostField.RegisterValueChangedCallback(evt => lmstudioHost = evt.newValue);
        lmstudioPortField.RegisterValueChangedCallback(evt => lmstudioPort = evt.newValue);
        lmstudioModelDropdown.RegisterValueChangedCallback(evt => lmstudioModel = evt.newValue);
        lmstudioTemperatureSlider.RegisterValueChangedCallback(evt => lmstudioTemperature = evt.newValue);

        // Load configuration and perform initial status check
        LoadLMStudioConfig();

        // Set initial values
        lmstudioHostField.value = lmstudioHost;
        lmstudioPortField.value = lmstudioPort;
        lmstudioTemperatureSlider.value = lmstudioTemperature;

        // Run a full check when the window is opened
        InitialStatusCheck();
    }

    private void Update()
    {
        // Periodically check if the Python server is alive
        if (Time.realtimeSinceStartup - lastCheckTime >= CONNECTION_CHECK_INTERVAL)
        {
            lastCheckTime = Time.realtimeSinceStartup;
            CheckPythonServerLiveness();
        }
    }

    private void RedrawUI()
    {
        if (pythonServerStatusIndicator != null)
        {
            pythonServerStatusIndicator.style.backgroundColor = pythonServerColor;
            pythonServerStatusLabel.text = pythonServerStatus;
            lmstudioStatusLabel.text = lmstudioStatusMessage;
        }
    }

    // --- Server Interaction Methods ---

    private async void SendChatMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        AddMessageToChat("You", message);
        chatInput.value = "";

        AddMessageToChat("Assistant", "<i>Processing...</i>", true);

        try
        {
            var requestPayload = new { prompt = message };
            string jsonPayload = JsonConvert.SerializeObject(requestPayload);
            string url = $"http://localhost:{MCP_PORT}/api/process";

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(url, content);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Server returned error {response.StatusCode}: {responseJson}");
            }

            var result = JObject.Parse(responseJson);
            string llmResponse = result["llm_response"]?.ToString() ?? "No text response.";
            JArray commands = result["commands"] as JArray;

            UpdateLastAssistantMessage(llmResponse);

            if (commands != null && commands.Count > 0)
            {
                // *** MODIFIED LINE ***
                EditorApplication.delayCall += () => UnityMCPBridge.ExecuteCommands(commands.ToString());
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MCP] Error sending chat message: {ex.Message}");
            UpdateLastAssistantMessage($"<b>Error:</b> {ex.Message}");
        }
    }

    private void AddMessageToChat(string sender, string message, bool isProcessing = false)
    {
        var chatMessage = new ChatMessage(sender, message);
        chatHistory.Add(chatMessage);

        var messageLabel = new Label($"<b>{sender}:</b>\n{message}")
        {
            style =
            {
                whiteSpace = WhiteSpace.Normal,
                paddingBottom = 5,
                paddingLeft = 5,
                paddingRight = 5,
                paddingTop = 5,
                color = Color.white
            }
        };
        messageLabel.AddToClassList(sender == "You" ? "chat-message-user" : "chat-message-assistant");

        if (isProcessing)
        {
            messageLabel.name = "processing-message";
        }

        chatScrollView.Add(messageLabel);
        chatScrollView.ScrollTo(chatScrollView.contentContainer[chatScrollView.contentContainer.childCount - 1]);
    }

    private void UpdateLastAssistantMessage(string newMessage)
    {
        var processingLabel = chatScrollView.Q<Label>("processing-message");
        if (processingLabel != null)
        {
            processingLabel.text = $"<b>Assistant:</b>\n{newMessage}";
            processingLabel.name = ""; // remove the name so we don't find it again
        }
    }


    private async void InitialStatusCheck()
    {
        // Kicks off the check. The rest is handled asynchronously and via the Update loop.
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
                    bool wasConnected = pythonServerStatus == "Connected";
                    pythonServerStatus = "Connected";
                    pythonServerColor = Color.green;

                    if (!wasConnected)
                    {
                        // This is the first time we've connected in a while.
                        // Let's refresh everything.
                        await CheckLMStudioStatus();
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
            if (pythonServerStatus != "Not Connected")
            {
                pythonServerStatus = "Not Connected";
                lmstudioStatusMessage = "N/A (Python server down)";
            }
            pythonServerColor = Color.red;
        }
        RedrawUI();
    }

    private async Task CheckLMStudioStatus()
    {
        lmstudioStatusMessage = "Checking...";
        RedrawUI();
        try
        {
            string statusUrl = $"http://localhost:{MCP_PORT}/api/status";
            HttpResponseMessage statusResponse = await httpClient.GetAsync(statusUrl);
            string statusJson = await statusResponse.Content.ReadAsStringAsync();

            if (!statusResponse.IsSuccessStatusCode) throw new Exception(statusJson);

            var status = JObject.Parse(statusJson);
            if (status["status"]?.ToString() == "connected")
            {
                lmstudioStatusMessage = $"Connected to model '{status["model"]}'";
            }
            else
            {
                lmstudioStatusMessage = $"<color=orange>LM Studio Disconnected.</color> Reason: {status["message"] ?? "Unknown"}";
            }
        }
        catch (Exception ex)
        {
            lmstudioStatusMessage = $"<color=red>Error checking status.</color>";
            Debug.LogError($"[MCP] Failed to check LM Studio status: {ex.Message}");
        }
        RedrawUI();
    }

    private async Task FetchAvailableModels()
    {
        if (fetchingModels) return;
        fetchingModels = true;

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
                    selectedModelIndex = availableModels.IndexOf(lmstudioModel);
                    // If the saved model isn't in the list, default to the first one.
                    if (selectedModelIndex < 0)
                    {
                        selectedModelIndex = 0;
                        lmstudioModel = availableModels[0];
                    }
                    lmstudioModelDropdown.choices = availableModels;
                    lmstudioModelDropdown.index = selectedModelIndex;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MCP] Failed to fetch available models: {ex.Message}");
            availableModels.Clear();
            selectedModelIndex = -1;
            lmstudioModelDropdown.choices = availableModels;
            lmstudioModelDropdown.index = selectedModelIndex;
        }
        finally
        {
            fetchingModels = false;
        }
    }

    private async void UpdateLMStudioConfigOnServer()
    {
        try
        {
            var configPayload = new { host = lmstudioHost, port = lmstudioPort, model = lmstudioModel, temperature = lmstudioTemperature };
            string jsonPayload = JsonConvert.SerializeObject(configPayload);
string url = $"http://localhost:{MCP_PORT}/api/configure";
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
            {
                Debug.Log("[MCP] Configuration successfully updated on the server.");
                SaveLMStudioConfig();
                await CheckLMStudioStatus();
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
        try
        {
            string path = GetLocalConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var cfg = new { lmstudio_host = lmstudioHost, lmstudio_port = lmstudioPort, lmstudio_model = lmstudioModel, lmstudio_temperature = lmstudioTemperature };
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
            lmstudioHost = cfg["lmstudio_host"]?.ToString() ?? lmstudioHost;
            lmstudioPort = cfg["lmstudio_port"]?.Value<int>() ?? lmstudioPort;
            lmstudioModel = cfg["lmstudio_model"]?.ToString() ?? lmstudioModel;
            lmstudioTemperature = cfg["lmstudio_temperature"]?.Value<float>() ?? lmstudioTemperature;
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
        string pythonExecutable = "python3"; // Default to python3 for Mac/Linux

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
                pythonExecutable = "python"; // Windows usually uses 'python'
                process.StartInfo.FileName = "cmd.exe";
                // /c to carry out the command and then terminate
                // start to run in a new window, /D sets the working directory
                process.StartInfo.Arguments = $"/c start \"Unity MCP Server\" /D \"{Path.GetDirectoryName(pythonScriptPath)}\" {pythonExecutable} \"{Path.GetFileName(pythonScriptPath)}\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
            }
            else if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // Using osascript to tell the Terminal app to run a command
                string script = $"tell application \"Terminal\" to do script \"cd \\\"{Path.GetDirectoryName(pythonScriptPath)}\\\" && {pythonExecutable} \\\"{Path.GetFileName(pythonScriptPath)}\\\"\"";
                process.StartInfo.FileName = "osascript";
                process.StartInfo.Arguments = $"-e '{script}'";
                process.StartInfo.UseShellExecute = true;
            }
            else // Assuming Linux
            {
                 // Try to use gnome-terminal, which is common. User might need to adapt for other terminals.
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

    private readonly struct ChatMessage
    {
        public readonly string sender;
        public readonly string content;
        public ChatMessage(string sender, string content) { this.sender = sender; this.content = content; }
    }
}
