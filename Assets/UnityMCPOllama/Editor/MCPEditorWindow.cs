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
    private string userInput = "";
    private List<ChatMessage> chatHistory = new List<ChatMessage>();
    private Vector2 chatScrollPosition;
    
    // --- Styles ---
    private GUIStyle userStyle;
    private GUIStyle assistantStyle;
    
    // --- Networking ---
    private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    private const int MCP_PORT = 6500;
    private float lastCheckTime = -10f; // Initial value to ensure an immediate check
    private const float CONNECTION_CHECK_INTERVAL = 5f;

    [MenuItem("Window/Unity MCP (LM Studio)")]
    public static void ShowWindow() => GetWindow<MCPEditorWindow>("MCP Editor");

    private void OnEnable()
    {
        // Style setup
        userStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true, normal = { textColor = new Color(0.6f, 0.8f, 1.0f) }, padding = new RectOffset(10, 10, 5, 5) };
        assistantStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true, normal = { textColor = Color.white }, padding = new RectOffset(10, 10, 5, 5) };
        
        // Load configuration and perform initial status check
        LoadLMStudioConfig();
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

    private void OnGUI()
    {
        EditorGUILayout.LabelField("MCP Editor (LM Studio)", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        DrawPythonServerSection();
        DrawLMStudioSection();
        DrawChatSection();
    }

    // --- UI Drawing Methods ---

    private void DrawPythonServerSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Python Server Status", EditorStyles.boldLabel);
        var rect = EditorGUILayout.GetControlRect();
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 10, 18), pythonServerColor);
        EditorGUI.LabelField(new Rect(rect.x + 15, rect.y, rect.width - 15, 18), pythonServerStatus);

        if (GUILayout.Button("Start Python Server"))
        {
            StartPythonServer();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(10);
    }

    private void DrawLMStudioSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("LM Studio Configuration", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Status: {lmstudioStatusMessage}", EditorStyles.wordWrappedLabel);

        if (GUILayout.Button("Refresh Status"))
        {
            CheckLMStudioStatus();
        }
        
        lmstudioHost = EditorGUILayout.TextField("Host:", lmstudioHost);
        lmstudioPort = EditorGUILayout.IntField("Port:", lmstudioPort);

        // --- Model Dropdown ---
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(fetchingModels || availableModels.Count == 0);

        int newIndex = EditorGUILayout.Popup("Model:", selectedModelIndex, availableModels.ToArray());
        if (newIndex != selectedModelIndex && newIndex >= 0)
        {
            selectedModelIndex = newIndex;
            lmstudioModel = availableModels[selectedModelIndex];
        }

        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button(fetchingModels ? "..." : "Refresh", GUILayout.Width(70)))
        {
            FetchAvailableModels();
        }
        EditorGUILayout.EndHorizontal();
        if (availableModels.Count == 0 && !fetchingModels)
        {
            EditorGUILayout.HelpBox("Could not find any models. Is LM Studio running? Press Refresh to try again.", MessageType.Info);
        }
        // --- End Model Dropdown ---

        lmstudioTemperature = EditorGUILayout.Slider("Temperature:", lmstudioTemperature, 0f, 1f);

        if (GUILayout.Button("Apply & Save Configuration"))
        {
            UpdateLMStudioConfigOnServer();
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(10);
    }
    
    private void DrawChatSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Chat with Unity via LM Studio", EditorStyles.boldLabel);

        using (var chatScroll = new EditorGUILayout.ScrollViewScope(chatScrollPosition, GUILayout.Height(300)))
        {
            chatScrollPosition = chatScroll.scrollPosition;
            foreach (var msg in chatHistory)
            {
                var style = msg.sender == "You" ? userStyle : assistantStyle;
                EditorGUILayout.LabelField($"<b>{msg.sender}:</b>", style);
                EditorGUILayout.LabelField(msg.content, style);
                EditorGUILayout.Space(5);
            }
        }
        
        userInput = EditorGUILayout.TextArea(userInput, GUILayout.Height(60));
        if (GUILayout.Button("Send") && !string.IsNullOrEmpty(userInput))
        {
            SendChatMessage(userInput);
            userInput = "";
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndVertical();
    }

    // --- Server Interaction Methods ---

    private async void SendChatMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        chatHistory.Add(new ChatMessage("You", message));
        int responseIndex = chatHistory.Count;
        chatHistory.Add(new ChatMessage("Assistant", "<i>Processing...</i>"));
        Repaint();

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

            chatHistory[responseIndex] = new ChatMessage("Assistant", llmResponse);
            
            if (commands != null && commands.Count > 0)
            {
                EditorApplication.delayCall += () => UnityMCPBridge.ProcessExtractedCommands(commands);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MCP] Error sending chat message: {ex.Message}");
            chatHistory[responseIndex] = new ChatMessage("Assistant", $"<b>Error:</b> {ex.Message}");
        }
        finally
        {
            Repaint();
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
                        Repaint();
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
                Repaint();
            }
            pythonServerColor = Color.red;
        }
    }
    
    private async Task CheckLMStudioStatus()
    {
        lmstudioStatusMessage = "Checking...";
        Repaint();
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
        Repaint();
    }

    private async Task FetchAvailableModels()
    {
        if (fetchingModels) return;
        fetchingModels = true;
        availableModels.Clear(); // Clear previous results
        selectedModelIndex = -1;
        Repaint();

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
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MCP] Failed to fetch available models: {ex.Message}");
            // Ensure the list is cleared on error
            availableModels.Clear();
            selectedModelIndex = -1;
        }
        finally
        {
            fetchingModels = false;
            Repaint();
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