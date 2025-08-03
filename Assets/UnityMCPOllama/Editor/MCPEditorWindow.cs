using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading; // <-- ОСЬ ВИПРАВЛЕННЯ: Додано цей рядок
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

public class MCPEditorWindow : EditorWindow
{
    // --- Стан UI та конфігурація ---
    private string pythonServerStatus = "Checking...";
    private Color pythonServerColor = Color.yellow;
    
    private string lmstudioStatusMessage = "N/A";

    private string lmstudioHost = "localhost";
    private int lmstudioPort = 1234;
    private string lmstudioModel = "qwen/qwen3-14b";
    private float lmstudioTemperature = 0.2f;

    // --- Чат ---
    private string userInput = "";
    private List<ChatMessage> chatHistory = new List<ChatMessage>();
    private Vector2 chatScrollPosition;
    
    // --- Стилі ---
    private GUIStyle userStyle;
    private GUIStyle assistantStyle;
    
    // --- Мережева взаємодія ---
    private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    private const int MCP_PORT = 6500;
    private float lastCheckTime = -10f; // Початкове значення для негайної перевірки
    private const float CONNECTION_CHECK_INTERVAL = 5f;

    [MenuItem("Window/Unity MCP (LM Studio)")]
    public static void ShowWindow() => GetWindow<MCPEditorWindow>("MCP Editor");

    private void OnEnable()
    {
        // Налаштування стилів
        userStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true, normal = { textColor = new Color(0.6f, 0.8f, 1.0f) }, padding = new RectOffset(10, 10, 5, 5) };
        assistantStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true, normal = { textColor = Color.white }, padding = new RectOffset(10, 10, 5, 5) };
        
        // Завантаження конфігурації та початкова перевірка статусу
        LoadLMStudioConfig();
        // Запускаємо повну перевірку при відкритті вікна
        InitialStatusCheck();
    }

    private void Update()
    {
        // Періодична легка перевірка "життя" Python-сервера
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

    // --- Методи малювання UI ---

    private void DrawPythonServerSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Python Server Status", EditorStyles.boldLabel);
        var rect = EditorGUILayout.GetControlRect();
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 10, 18), pythonServerColor);
        EditorGUI.LabelField(new Rect(rect.x + 15, rect.y, rect.width - 15, 18), pythonServerStatus);
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
        lmstudioModel = EditorGUILayout.TextField("Model:", lmstudioModel);
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

    // --- Методи для взаємодії з сервером ---

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
        await CheckPythonServerLiveness();
        if (pythonServerStatus == "Connected")
        {
            await CheckLMStudioStatus();
        }
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
                    if (pythonServerStatus != "Connected")
                    {
                        pythonServerStatus = "Connected";
                        Repaint();
                    }
                    pythonServerColor = Color.green;
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

    private readonly struct ChatMessage
    {
        public readonly string sender;
        public readonly string content;
        public ChatMessage(string sender, string content) { this.sender = sender; this.content = content; }
    }
}