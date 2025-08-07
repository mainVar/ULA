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
using System.Linq;

namespace UnityLocalAi
{
    public class UnityLocalAiWindow : EditorWindow
    {
        // --- Constants ---
        private const string EDITOR_PREFS_KEY = "UnityLocalAi_CurrentSessionId";
        private const int MCP_PORT = 6500;

        // --- UI State & Configuration ---
        private string lmstudioHost = "localhost";
        private int lmstudioPort = 1234;
        private string lmstudioModel = "qwen/qwen3-14b";
        private float lmstudioTemperature = 0.2f;

        // --- Model Selection ---
        private List<string> availableModels = new List<string>();
        private bool fetchingModels = false;

        // --- Chat ---
        private ChatSession currentSession;
        private List<ChatSession> allSessions;

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

        [MenuItem("Window/Unity Local AI v2")]
        public static void ShowWindow() => GetWindow<UnityLocalAiWindow>("Local AI v2");

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Initialize();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (currentSession != null)
            {
                EditorPrefs.SetString(EDITOR_PREFS_KEY, currentSession.sessionId);
                ChatHistoryManager.SaveSession(currentSession);
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode && currentSession != null)
            {
                EditorPrefs.SetString(EDITOR_PREFS_KEY, currentSession.sessionId);
                ChatHistoryManager.SaveSession(currentSession);
            }
        }

        public void CreateGUI()
        {
            // GUI creation is now handled in Initialize() called from OnEnable()
        }

        private void Initialize()
        {
            rootVisualElement.Clear();

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UnityLocalAi/Editor/UI/UnityLocalAiEditor_v2.uxml");
            if (visualTree == null)
            {
                rootVisualElement.Add(new Label("Error: Could not find UXML file."));
                return;
            }
            visualTree.CloneTree(rootVisualElement);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UnityLocalAi/Editor/UI/UnityLocalAiEditor_v2.uss");
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            QueryUIElements();
            RegisterCallbacks();
            LoadConfigAndState();

            SetServerStatus(true, "Ready");
            FetchAvailableModels();
        }

        private void QueryUIElements()
        {
            pythonServerStatusIndicator = rootVisualElement.Q<VisualElement>("PythonServerStatusIndicator");
            pythonServerStatusLabel = rootVisualElement.Q<Label>("PythonServerStatusLabel");
            startServerButton = rootVisualElement.Q<Button>("StartServerButton");
            lmstudioStatusLabel = rootVisualElement.Q<Label>("LMStudioStatusLabel");
            refreshLMStatusButton = rootVisualElement.Q<Button>("RefreshLMStatusButton");
            lmstudioHostField = rootVisualElement.Q<TextField>("LMStudioHost");
            lmstudioPortField = rootVisualElement.Q<IntegerField>("LMStudioPort");
            lmstudioModelDropdown = rootVisualElement.Q<PopupField<string>>("LMStudioModel");
            lmstudioTemperatureSlider = rootVisualElement.Q<Slider>("LMStudioTemperature");
            lmstudioTemperatureField = rootVisualElement.Q<FloatField>("LMStudioTemperatureField");
            applyLMConfigButton = rootVisualElement.Q<Button>("ApplyLMConfigButton");
            chatScrollView = rootVisualElement.Q<ScrollView>("ChatScrollView");
            chatInput = rootVisualElement.Q<TextField>("ChatInput");
            sendButton = rootVisualElement.Q<Button>("SendButton");
            chatHistoryList = rootVisualElement.Q<ListView>("ChatHistoryList");
            newChatButton = rootVisualElement.Q<Button>("NewChatButton");
        }

        private void RegisterCallbacks()
        {
            startServerButton.clicked += StartPythonServer;
            refreshLMStatusButton.clicked += () => FetchAvailableModels();
            applyLMConfigButton.clicked += UpdateLMStudioConfigOnServer;
            sendButton.clicked += OnSendButtonPressed;
            newChatButton.clicked += StartNewChatSession;

            lmstudioHostField.RegisterValueChangedCallback(evt => lmstudioHost = evt.newValue);
            lmstudioPortField.RegisterValueChangedCallback(evt => lmstudioPort = evt.newValue);
            lmstudioModelDropdown.RegisterValueChangedCallback(evt => lmstudioModel = evt.newValue);

            lmstudioTemperatureSlider.RegisterValueChangedCallback(evt => {
                lmstudioTemperature = Mathf.Clamp(evt.newValue, 0, 2);
                lmstudioTemperatureField.SetValueWithoutNotify(lmstudioTemperature);
            });
            lmstudioTemperatureField.RegisterValueChangedCallback(evt => {
                lmstudioTemperature = Mathf.Clamp(evt.newValue, 0, 2);
                lmstudioTemperatureSlider.SetValueWithoutNotify(lmstudioTemperature);
            });

            chatInput.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.keyCode == KeyCode.Return && (evt.shiftKey || evt.ctrlKey))
                {
                    OnSendButtonPressed();
                    evt.PreventDefault();
                }
            });
        }

        private void LoadConfigAndState()
        {
            LoadLMStudioConfig();
            lmstudioHostField.value = lmstudioHost;
            lmstudioPortField.value = lmstudioPort;
            lmstudioTemperatureSlider.value = lmstudioTemperature;
            lmstudioTemperatureField.value = lmstudioTemperature;
            SetupChatHistory();
        }

        private void SetupChatHistory()
        {
            allSessions = ChatHistoryManager.LoadAllSessions();

            chatHistoryList.makeItem = () => new Label();
            chatHistoryList.bindItem = (element, i) =>
            {
                var label = element as Label;
                var session = allSessions[i];
                var dateTime = DateTimeOffset.FromUnixTimeSeconds(session.createdAt).LocalDateTime;
                label.text = session.messages.Any() ? session.messages[0].content : $"Chat from {dateTime:g}";
                label.tooltip = $"Chat from {dateTime:g}";
            };

            chatHistoryList.itemsSource = allSessions;

            chatHistoryList.onSelectionChange += (enumerable) =>
            {
                var selectedSession = enumerable.FirstOrDefault() as ChatSession;
                if (selectedSession != null && selectedSession != currentSession)
                {
                    LoadChatSession(selectedSession);
                }
            };

            string lastSessionId = EditorPrefs.GetString(EDITOR_PREFS_KEY, null);
            var lastSession = allSessions.FirstOrDefault(s => s.sessionId == lastSessionId);

            if (lastSession != null)
            {
                LoadChatSession(lastSession);
                chatHistoryList.selectedIndex = allSessions.IndexOf(lastSession);
            }
            else if (allSessions.Any())
            {
                LoadChatSession(allSessions[0]);
                chatHistoryList.selectedIndex = 0;
            }
            else
            {
                StartNewChatSession();
            }
        }

        private void StartNewChatSession()
        {
            if (currentSession != null && !currentSession.messages.Any()) return;

            var newSession = new ChatSession();
            allSessions.Insert(0, newSession);
            ChatHistoryManager.SaveSession(newSession);

            chatHistoryList.itemsSource = allSessions;
            chatHistoryList.Rebuild();
            chatHistoryList.selectedIndex = 0;
            LoadChatSession(newSession);
        }

        private void LoadChatSession(ChatSession session)
        {
            currentSession = session;
            chatScrollView.Clear();
            if(currentSession.messages == null) currentSession.messages = new List<ChatMessage>();

            foreach (var message in currentSession.messages)
            {
                AddMessageToView(message);
            }
        }

        private void AddMessageToView(ChatMessage message, bool isProcessing = false)
        {
            var messageLabel = new Label(message.content) { name = isProcessing ? "processing-message" : "" };
            messageLabel.AddToClassList(message.sender == "You" ? "chat-message-user" : "chat-message-assistant");
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            chatScrollView.Add(messageLabel);
            chatScrollView.schedule.Execute(() => chatScrollView.ScrollTo(chatScrollView.contentContainer[chatScrollView.contentContainer.childCount-1])).StartingIn(10);
        }

        private void OnSendButtonPressed()
        {
            if(!string.IsNullOrWhiteSpace(chatInput.value))
            {
                SendChatMessage(chatInput.value);
                chatInput.value = "";
                chatInput.Focus();
            }
        }

        private void SetServerStatus(bool isConnected, string customMessage = null)
        {
            if (pythonServerStatusIndicator == null) return;
            pythonServerStatusIndicator.style.backgroundColor = isConnected ? Color.green : Color.red;
            pythonServerStatusLabel.text = customMessage ?? (isConnected ? "Connected" : "Not Connected");
            if (!isConnected)
            {
                lmstudioStatusLabel.text = "N/A (Python server down)";
            }
        }

        private async void SendChatMessage(string message)
        {
            var userMessage = new ChatMessage("You", message);
            currentSession.messages.Add(userMessage);
            AddMessageToView(userMessage);

            var assistantMessage = new ChatMessage("Assistant", "...");
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
                if (!response.IsSuccessStatusCode) throw new Exception(responseJson);

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
                var lastMsgIndex = currentSession.messages.Count - 1;
                currentSession.messages[lastMsgIndex] = new ChatMessage("Assistant", newMessage);
                ChatHistoryManager.SaveSession(currentSession);
            }

            var processingLabel = chatScrollView.Q<Label>("processing-message");
            if (processingLabel != null)
            {
                processingLabel.text = newMessage;
                processingLabel.name = "";
            }
        }

        private async Task FetchAvailableModels()
        {
            if (fetchingModels) return;
            fetchingModels = true;
            SetServerStatus(true, "Fetching Models...");
            try
            {
                string url = $"http://localhost:{MCP_PORT}/api/models";
                using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    HttpResponseMessage response = await httpClient.GetAsync(url, cts.Token);
                    SetServerStatus(response.IsSuccessStatusCode);
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync();
                    var models = JsonConvert.DeserializeObject<List<string>>(json);
                    if (models != null && models.Count > 0)
                    {
                        availableModels = models;
                        var idx = availableModels.IndexOf(lmstudioModel);
                        lmstudioModelDropdown.choices = availableModels;
                        lmstudioModelDropdown.index = idx >= 0 ? idx : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Failed to fetch models: {ex.Message}");
                SetServerStatus(false, "Model Fetch Error");
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
                    Debug.Log("[MCP] Config updated on server.");
                    SaveLMStudioConfig();
                }
                else
                {
                    throw new Exception(await response.Content.ReadAsStringAsync());
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Error updating config: {ex.Message}");
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
            }
            catch (Exception ex) { Debug.LogError($"[MCP] Failed to save config: {ex.Message}"); }
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
            catch (Exception ex) { Debug.LogError($"[MCP] Failed to load config: {ex.Message}"); }
        }

        private string GetLocalConfigPath()
        {
            return Path.Combine(Application.dataPath, "..", "Temp", "UnityLocalAiConfig.json");
        }

        private string GetProjectRootPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private void StartPythonServer()
        {
            string pythonScriptPath = Path.Combine(GetProjectRootPath(), "Assets", "Python", "Python", "lmstudio_mcp_server.py");
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
                    process.StartInfo.FileName = "cmd.exe";
                    process.StartInfo.Arguments = $"/c start \"Unity MCP Server\" /D \"{Path.GetDirectoryName(pythonScriptPath)}\" python \"{Path.GetFileName(pythonScriptPath)}\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                }
                else
                {
                    string script = $"tell application \"Terminal\" to do script \"cd \\\"{Path.GetDirectoryName(pythonScriptPath)}\\\" && python3 \\\"{Path.GetFileName(pythonScriptPath)}\\\"\"";
                    process.StartInfo.FileName = "osascript";
                    process.StartInfo.Arguments = $"-e '{script}'";
                    process.StartInfo.UseShellExecute = true;
                }
                process.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Failed to start Python server: {ex.Message}");
            }
        }
    }
}
