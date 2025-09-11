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
        private bool isCopyModeEnabled = false;

        // --- UI Toolkit Elements ---
        private VisualElement pythonServerStatusIndicator;
        private Label pythonServerStatusLabel;
        private Button startServerButton;
        private Label lmstudioStatusLabel;
        private Button refreshLMStatusButton;
        private TextField lmstudioHostField;
        private IntegerField lmstudioPortField;
        private DropdownField lmstudioModelDropdown;
        private Slider lmstudioTemperatureSlider;
        private FloatField lmstudioTemperatureField;
        private Button applyLMConfigButton;
        private ScrollView chatScrollView;
        private TextField chatInput;
        private Button sendButton;
        private ListView chatHistoryList;
        private Button newChatButton;
        private Button deleteChatButton;
        private Button settingsButton;
        private Button copyModeButton;

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

        private void OnFocus()
        {
            // When the window gets focus, check if the chat history needs to be reloaded.
            // This is a simple way to update the list if the path was changed in the settings.
            if(allSessions != null && chatHistoryList != null)
            {
                var sessionsOnDisk = ChatHistoryManager.LoadAllSessions();
                if (sessionsOnDisk.Count != allSessions.Count || !sessionsOnDisk.SequenceEqual(allSessions))
                {
                    LoadConfigAndState();
                }
            }
        }

        private void ToggleCopyMode()
        {
            isCopyModeEnabled = !isCopyModeEnabled;
            copyModeButton.text = isCopyModeEnabled ? "View Mode" : "Select Text";
            RefreshChatView();
        }

        private void RefreshChatView()
        {
            if (chatScrollView == null || currentSession?.messages == null) return;

            chatScrollView.Clear();
            foreach (var message in currentSession.messages)
            {
                VisualElement messageElement;
                string messageName = "";

                // Assign a name to the last message so it can be found and updated
                if (currentSession.messages.IndexOf(message) == currentSession.messages.Count - 1)
                {
                    var lastMessage = currentSession.messages.LastOrDefault();
                    if (lastMessage != null && lastMessage.sender == "Assistant" && lastMessage.content == "...")
                    {
                        messageName = "processing-message";
                    }
                }

                if (isCopyModeEnabled)
                {
                    var messageField = new TextField
                    {
                        value = message.content,
                        name = messageName,
                        isReadOnly = true,
                        multiline = true
                    };
                    messageField.AddToClassList(message.sender == "You" ? "chat-message-user" : "chat-message-assistant");
                    messageField.style.whiteSpace = WhiteSpace.Normal;
                    var textInput = messageField.Q(TextField.textInputUssName);
                    if (textInput != null)
                    {
                        textInput.style.borderTopWidth = 0;
                        textInput.style.borderBottomWidth = 0;
                        textInput.style.borderLeftWidth = 0;
                        textInput.style.borderRightWidth = 0;
                        textInput.style.backgroundColor = new StyleColor(StyleKeyword.None);
                    }
                    messageElement = messageField;
                }
                else
                {
                    var messageLabel = new Label(message.content)
                    {
                        name = messageName
                    };
                    messageLabel.AddToClassList(message.sender == "You" ? "chat-message-user" : "chat-message-assistant");
                    messageLabel.style.whiteSpace = WhiteSpace.Normal;
                    messageElement = messageLabel;
                }
                chatScrollView.Add(messageElement);
            }
            chatScrollView.schedule.Execute(() => {
                if (chatScrollView.contentContainer.childCount > 0)
                    chatScrollView.ScrollTo(chatScrollView.contentContainer.ElementAt(chatScrollView.contentContainer.childCount - 1));
            }).StartingIn(10);
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
            lmstudioHostField = rootVisualElement.Q<TextField>("LMStudioHost");
            lmstudioPortField = rootVisualElement.Q<IntegerField>("LMStudioPort");
            lmstudioModelDropdown = rootVisualElement.Q<DropdownField>("LMStudioModel");
            refreshLMStatusButton = rootVisualElement.Q<Button>("RefreshModelsButton");
            lmstudioTemperatureSlider = rootVisualElement.Q<Slider>("LMStudioTemperature");
            lmstudioTemperatureField = rootVisualElement.Q<FloatField>("LMStudioTemperatureField");
            applyLMConfigButton = rootVisualElement.Q<Button>("ApplyLMConfigButton");
            chatScrollView = rootVisualElement.Q<ScrollView>("ChatScrollView");
            chatInput = rootVisualElement.Q<TextField>("ChatInput");
            sendButton = rootVisualElement.Q<Button>("SendButton");
            chatHistoryList = rootVisualElement.Q<ListView>("ChatHistoryList");
            newChatButton = rootVisualElement.Q<Button>("NewChatButton");
            deleteChatButton = rootVisualElement.Q<Button>("DeleteChatButton");
            settingsButton = rootVisualElement.Q<Button>("SettingsButton");
            copyModeButton = rootVisualElement.Q<Button>("CopyModeButton");
        }

        private void RegisterCallbacks()
        {
            startServerButton.clicked += StartPythonServer;
            refreshLMStatusButton.clicked += () => FetchAvailableModels();
            applyLMConfigButton.clicked += UpdateLMStudioConfigOnServer;
            sendButton.clicked += OnSendButtonPressed;
            newChatButton.clicked += StartNewChatSession;
            deleteChatButton.clicked += OnDeleteChatButtonPressed;
            settingsButton.clicked += SettingsWindow.ShowWindow;
            copyModeButton.clicked += ToggleCopyMode;

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

            chatHistoryList.onSelectionChange -= OnChatSelectionChanged; // Unsubscribe to prevent multiple handlers
            chatHistoryList.onSelectionChange += OnChatSelectionChanged; // Subscribe with the named method

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
            UpdateDeleteButtonState();
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
            UpdateDeleteButtonState();
        }

        private void OnDeleteChatButtonPressed()
        {
            var selectedSession = chatHistoryList.selectedItem as ChatSession;
            if (selectedSession == null)
            {
                Debug.LogWarning("[MCP] No chat session selected to delete.");
                return;
            }

            if (EditorUtility.DisplayDialog("Delete Chat Session?",
                "Are you sure you want to permanently delete this chat session?", "Delete", "Cancel"))
            {
                ChatHistoryManager.DeleteSession(selectedSession);
                LoadConfigAndState(); // Reload the entire state from disk
            }
        }

        private void OnChatSelectionChanged(IEnumerable<object> enumerable)
        {
            var selectedSession = enumerable.FirstOrDefault() as ChatSession;
            if (selectedSession != null && selectedSession != currentSession)
            {
                LoadChatSession(selectedSession);
            }
        }

        private void UpdateDeleteButtonState()
        {
            deleteChatButton.SetEnabled(allSessions != null && allSessions.Count > 1);
        }

        private void LoadChatSession(ChatSession session)
        {
            currentSession = session;
            if (currentSession.messages == null) currentSession.messages = new List<ChatMessage>();
            RefreshChatView();
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

            var assistantMessage = new ChatMessage("Assistant", "...");
            currentSession.messages.Add(assistantMessage);

            RefreshChatView();
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
                string reasoning = result["reasoning"]?.ToString();

                string finalResponse = llmResponse;
                if (!string.IsNullOrEmpty(reasoning))
                {
                    finalResponse = $"<b>Reasoning:</b>\n{reasoning}\n\n{llmResponse}";
                }

                JArray commands = result["commands"] as JArray;

                UpdateLastAssistantMessage(finalResponse);

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

            RefreshChatView();
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

                    if (lmstudioModelDropdown == null)
                    {
                        Debug.LogError("[MCP] Model dropdown is null, cannot fetch models.");
                        return;
                    }

                    if (models != null && models.Count > 0)
                    {
                        availableModels = models;
                        lmstudioModelDropdown.choices = availableModels;
                        var idx = availableModels.IndexOf(lmstudioModel);
                        lmstudioModelDropdown.index = idx >= 0 ? idx : 0;
                    }
                    else
                    {
                        lmstudioModelDropdown.choices = new List<string> { "No models found" };
                        lmstudioModelDropdown.index = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Failed to fetch models: {ex.Message}");
                SetServerStatus(false, "Model Fetch Error");
                if (lmstudioModelDropdown != null)
                {
                    lmstudioModelDropdown.choices = new List<string> { "Error fetching models" };
                    lmstudioModelDropdown.index = 0;
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
