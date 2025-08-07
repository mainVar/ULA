using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityLocalAi;
using System.Linq;

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
    private ChatSession currentSession;
    private List<ChatSession> chatSessions;

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
    private FloatField lmstudioTemperatureField;
    private Button applyLMConfigButton;
    private ScrollView chatScrollView;
    private TextField chatInput;
    private Button sendButton;
    private ListView chatHistoryList;
    private Button newChatButton;

    // --- Networking ---
    private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    private const int MCP_PORT = 6500;

    [MenuItem("Window/Unity Local AI")]
    public static void ShowWindow() => GetWindow<MCPEditorWindow>("Local AI");

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        // Ensure the current session is saved when the window is closed or Unity recompiles
        if (currentSession != null)
        {
            ChatHistoryManager.SaveSession(currentSession);
        }
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // Save the current session when entering play mode to prevent data loss
        if (state == PlayModeStateChange.ExitingEditMode && currentSession != null)
        {
            ChatHistoryManager.SaveSession(currentSession);
        }
    }

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UnityLocalAi/Editor/UI/UnityLocalAiEditor.uxml");
        visualTree.CloneTree(root);
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UnityLocalAi/Editor/UI/UnityLocalAiEditor.uss");
        root.styleSheets.Add(styleSheet);

        // --- Query UI Elements ---
        pythonServerStatusIndicator = root.Q<VisualElement>("PythonServerStatusIndicator");
        pythonServerStatusLabel = root.Q<Label>("PythonServerStatusLabel");
        startServerButton = root.Q<Button>("StartServerButton");
        lmstudioStatusLabel = root.Q<Label>("LMStudioStatusLabel");
        refreshLMStatusButton = root.Q<Button>("RefreshLMStatusButton");
        lmstudioHostField = root.Q<TextField>("LMStudioHost");
        lmstudioPortField = root.Q<IntegerField>("LMStudioPort");
        lmstudioModelDropdown = new PopupField<string>("Model:", availableModels, 0);
        root.Q<VisualElement>("LMStudioSection").Insert(3, lmstudioModelDropdown);
        lmstudioTemperatureSlider = root.Q<Slider>("LMStudioTemperature");
        lmstudioTemperatureField = root.Q<FloatField>("LMStudioTemperatureField");
        applyLMConfigButton = root.Q<Button>("ApplyLMConfigButton");
        chatScrollView = root.Q<ScrollView>("ChatScrollView");
        chatInput = root.Q<TextField>("ChatInput");
        sendButton = root.Q<Button>("SendButton");
        chatHistoryList = root.Q<ListView>("ChatHistoryList");
        newChatButton = root.Q<Button>("NewChatButton");

        // --- Register Callbacks ---
        startServerButton.clicked += StartPythonServer;
        refreshLMStatusButton.clicked += () => CheckLMStudioStatus();
        applyLMConfigButton.clicked += UpdateLMStudioConfigOnServer;
        sendButton.clicked += () => SendChatMessage(chatInput.value);
        newChatButton.clicked += StartNewChatSession;

        lmstudioHostField.RegisterValueChangedCallback(evt => lmstudioHost = evt.newValue);
        lmstudioPortField.RegisterValueChangedCallback(evt => lmstudioPort = evt.newValue);
        lmstudioModelDropdown.RegisterValueChangedCallback(evt => lmstudioModel = evt.newValue);

        lmstudioTemperatureSlider.RegisterValueChangedCallback(evt => {
            lmstudioTemperature = Mathf.Clamp(evt.newValue, 0, 1);
            lmstudioTemperatureField.SetValueWithoutNotify(lmstudioTemperature);
        });
        lmstudioTemperatureField.RegisterValueChangedCallback(evt => {
            lmstudioTemperature = Mathf.Clamp(evt.newValue, 0, 1);
            lmstudioTemperatureSlider.SetValueWithoutNotify(lmstudioTemperature);
        });

        // --- Load Config & Initial State ---
        LoadLMStudioConfig();
        lmstudioHostField.value = lmstudioHost;
        lmstudioPortField.value = lmstudioPort;
        lmstudioTemperatureSlider.value = lmstudioTemperature;
        lmstudioTemperatureField.value = lmstudioTemperature;

        SetupChatHistory();

        SetServerStatus(true, "Ready");
        CheckLMStudioStatus();
    }

    // --- Chat History ---
    private void SetupChatHistory()
    {
        chatSessions = ChatHistoryManager.LoadAllSessions();

        chatHistoryList.makeItem = () => new Label();
        chatHistoryList.bindItem = (element, i) =>
        {
            var label = element as Label;
            var session = chatSessions[i];
            var dateTime = DateTimeOffset.FromUnixTimeSeconds(session.createdAt).LocalDateTime;
            label.text = $"Chat from {dateTime:g}";
        };

        chatHistoryList.itemsSource = chatSessions;

        chatHistoryList.onSelectionChange += (enumerable) =>
        {
            var selectedSession = enumerable.FirstOrDefault() as ChatSession;
            if (selectedSession != null)
            {
                LoadChatSession(selectedSession);
            }
        };

        if (chatSessions.Any())
        {
            chatHistoryList.selectedIndex = 0;
            LoadChatSession(chatSessions[0]);
        }
        else
        {
            StartNewChatSession();
        }
    }

    private void StartNewChatSession()
    {
        currentSession = new ChatSession();
        chatSessions.Insert(0, currentSession);
        ChatHistoryManager.SaveSession(currentSession);

        chatHistoryList.itemsSource = chatSessions;
        chatHistoryList.Rebuild();
        chatHistoryList.selectedIndex = 0;

        LoadChatSession(currentSession);
    }

    private void LoadChatSession(ChatSession session)
    {
        currentSession = session;
        chatScrollView.Clear();
        foreach (var message in currentSession.messages)
        {
            AddMessageToView(message);
        }
    }

    private void AddMessageToView(ChatMessage message, bool isProcessing = false)
    {
        var messageLabel = new Label($"<b>{message.sender}:</b>\n{message.content}")
        {
            style = { whiteSpace = WhiteSpace.Normal, paddingBottom = 5, paddingLeft = 5, paddingRight = 5, paddingTop = 5, color = Color.white }
        };
        messageLabel.AddToClassList(message.sender == "You" ? "chat-message-user" : "chat-message-assistant");

        if (isProcessing) messageLabel.name = "processing-message";

        chatScrollView.Add(messageLabel);
        chatScrollView.ScrollTo(chatScrollView.contentContainer[chatScrollView.contentContainer.childCount - 1]);
    }

    // --- Server Interaction Methods ---
    private void SetServerStatus(bool isConnected, string customMessage = null)
    {
        if (pythonServerStatusIndicator == null) return;
        if (isConnected)
        {
            pythonServerColor = Color.green;
            pythonServerStatus = customMessage ?? "Connected";
        }
        else
        {
            pythonServerColor = Color.red;
            pythonServerStatus = customMessage ?? "Not Connected";
            lmstudioStatusMessage = "N/A (Python server down)";
        }
        pythonServerStatusIndicator.style.backgroundColor = pythonServerColor;
        pythonServerStatusLabel.text = pythonServerStatus;
        if (!isConnected || customMessage != null) lmstudioStatusLabel.text = lmstudioStatusMessage;
    }

    private async void SendChatMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var userMessage = new ChatMessage("You", message);
        currentSession.messages.Add(userMessage);
        AddMessageToView(userMessage);
        chatInput.value = "";

        var assistantMessage = new ChatMessage("Assistant", "<i>Processing...</i>");
        currentSession.messages.Add(assistantMessage);
        AddMessageToView(assistantMessage, true);

        ChatHistoryManager.SaveSession(currentSession);

        try
        {
            var requestPayload = new { prompt = message };
            string jsonPayload = JsonConvert.SerializeObject(requestPayload);
            string url = $"http://localhost:{MCP_PORT}/api/process";
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(url, content);
            SetServerStatus(response.IsSuccessStatusCode);
            string responseJson = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception($"Server returned error {response.StatusCode}: {responseJson}");

            var result = JObject.Parse(responseJson);
            string llmResponse = result["llm_response"]?.ToString() ?? "No text response.";
            JArray commands = result["commands"] as JArray;

            UpdateLastAssistantMessage(llmResponse);

            if (commands != null && commands.Count > 0)
            {
                EditorApplication.delayCall += () => UnityMCPBridge.ExecuteCommands(commands.ToString());
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MCP] Error sending chat message: {ex.Message}");
            UpdateLastAssistantMessage($"<b>Error:</b> {ex.Message}");
            SetServerStatus(false, "Error");
        }
    }

    private void UpdateLastAssistantMessage(string newMessage)
    {
        if (currentSession.messages.Any())
        {
            currentSession.messages[currentSession.messages.Count - 1] = new ChatMessage("Assistant", newMessage);
            ChatHistoryManager.SaveSession(currentSession);
        }

        var processingLabel = chatScrollView.Q<Label>("processing-message");
        if (processingLabel != null)
        {
            processingLabel.text = $"<b>Assistant:</b>\n{newMessage}";
            processingLabel.name = "";
        }
    }

    private async Task CheckLMStudioStatus()
    {
        lmstudioStatusMessage = "Checking...";
        SetServerStatus(true, "Checking LM Studio...");
        try
        {
            string statusUrl = $"http://localhost:{MCP_PORT}/api/status";
            HttpResponseMessage statusResponse = await httpClient.GetAsync(statusUrl);
            SetServerStatus(statusResponse.IsSuccessStatusCode);
            string statusJson = await statusResponse.Content.ReadAsStringAsync();
            if (!statusResponse.IsSuccessStatusCode) throw new Exception(statusJson);
            var status = JObject.Parse(statusJson);
            if (status["status"]?.ToString() == "connected") lmstudioStatusMessage = $"Connected to model '{status["model"]}'";
            else lmstudioStatusMessage = $"<color=orange>LM Studio Disconnected.</color> Reason: {status["message"] ?? "Unknown"}";
        }
        catch (Exception ex)
        {
            lmstudioStatusMessage = $"<color=red>Error checking status.</color>";
            Debug.LogError($"[MCP] Failed to check LM Studio status: {ex.Message}");
            SetServerStatus(false, "Error");
        }
        lmstudioStatusLabel.text = lmstudioStatusMessage;
    }

    private async Task FetchAvailableModels()
    {
        if (fetchingModels) return;
        fetchingModels = true;
        SetServerStatus(true, "Fetching Models...");
        try
        {
            string url = $"http://localhost:{MCP_PORT}/api/models";
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                HttpResponseMessage response = await httpClient.GetAsync(url, cts.Token);
                SetServerStatus(response.IsSuccessStatusCode);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                var models = JsonConvert.DeserializeObject<List<string>>(json);
                if (models != null && models.Count > 0)
                {
                    availableModels = models;
                    selectedModelIndex = availableModels.IndexOf(lmstudioModel);
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
            SetServerStatus(false, "Error Fetching Models");
            availableModels.Clear();
            selectedModelIndex = -1;
            if (lmstudioModelDropdown != null)
            {
                lmstudioModelDropdown.choices = availableModels;
                lmstudioModelDropdown.index = selectedModelIndex;
            }
        }
        finally { fetchingModels = false; }
    }

    private async void UpdateLMStudioConfigOnServer()
    {
        SetServerStatus(true, "Updating Config...");
        try
        {
            var configPayload = new { host = lmstudioHost, port = lmstudioPort, model = lmstudioModel, temperature = lmstudioTemperature };
            string jsonPayload = JsonConvert.SerializeObject(configPayload);
            string url = $"http://localhost:{MCP_PORT}/api/configure";
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(url, content);
            SetServerStatus(response.IsSuccessStatusCode);
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
            SetServerStatus(false, "Config Error");
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
            EditorUtility.DisplayDialog("Failed to Start Server", "Could not start the Python server...", "OK");
        }
    }
}
