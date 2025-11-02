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
using UnityLocalAi.UI;

namespace UnityLocalAi
{
    public class MCPEditorWindow : EditorWindow
    {
        // --- UI State & Configuration ---
        private string pythonServerStatus = "Checking...";
        private Color pythonServerColor = Color.yellow;
        private string lmstudioStatusMessage = "N/A";

        // --- Model Selection ---
        private LLMConfig llmConfig = new LLMConfig();

        // --- Chat ---
        private string userInput = "";
        private List<ChatMessage> chatHistory = new List<ChatMessage>();

        // --- Networking ---
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        private float lastCheckTime = -10f;

        [MenuItem("Window/Unity Local AI")]
        public static void ShowWindow() => GetWindow<MCPEditorWindow>("Local AI");

        private void OnEnable()
        {
            LoadLMStudioConfig();
            InitialStatusCheck();
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup - lastCheckTime >= Constants.CONNECTION_CHECK_INTERVAL)
            {
                lastCheckTime = Time.realtimeSinceStartup;
                CheckPythonServerLiveness();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Unity Local AI", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            ServerStatusUI.DrawPythonServerSection(pythonServerStatus, pythonServerColor, StartPythonServer);
            EditorGUILayout.Space(10);

            // Wrap async methods so they match the Action delegate expected by the UI helpers.
            LLMConfigUI.Draw(llmConfig, UpdateLMStudioConfigOnServer, FetchAvailableModelsWrapper);
            EditorGUILayout.Space(10);

            ServerStatusUI.DrawLMStudioSection(lmstudioStatusMessage, CheckLMStudioStatusWrapper);
            EditorGUILayout.Space(10);

            ChatUI.Draw(chatHistory, ref userInput, SendChatMessage);
        }

        // Helper wrappers to call async Task methods from UI callbacks that expect Action.
        private void FetchAvailableModelsWrapper() => _ = FetchAvailableModels();
        private void CheckLMStudioStatusWrapper() => _ = CheckLMStudioStatus();

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
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await httpClient.PostAsync(Constants.API.Process, content);
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
                    EditorApplication.delayCall += () => UnityMCPBridge.ExecuteCommands(commands.ToString());
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
        }

        private async Task CheckPythonServerLiveness()
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    HttpResponseMessage healthResponse = await httpClient.GetAsync(Constants.API.Health, cts.Token);
                    if (healthResponse.IsSuccessStatusCode)
                    {
                        bool wasConnected = pythonServerStatus == "Connected";
                        pythonServerStatus = "Connected";
                        pythonServerColor = Color.green;

                        if (!wasConnected)
                        {
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
                HttpResponseMessage statusResponse = await httpClient.GetAsync(Constants.API.Status);
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
            if (llmConfig.fetchingModels) return;
            llmConfig.fetchingModels = true;
            llmConfig.availableModels.Clear();
            llmConfig.selectedModelIndex = -1;
            Repaint();

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    HttpResponseMessage response = await httpClient.GetAsync(Constants.API.Models, cts.Token);
                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync();
                    var models = JsonConvert.DeserializeObject<List<string>>(json);

                    if (models != null && models.Count > 0)
                    {
                        llmConfig.availableModels = models;
                        llmConfig.selectedModelIndex = llmConfig.availableModels.IndexOf(llmConfig.lmstudioModel);
                        if (llmConfig.selectedModelIndex < 0)
                        {
                            llmConfig.selectedModelIndex = 0;
                            llmConfig.lmstudioModel = llmConfig.availableModels[0];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Failed to fetch available models: {ex.Message}");
                llmConfig.availableModels.Clear();
                llmConfig.selectedModelIndex = -1;
            }
            finally
            {
                llmConfig.fetchingModels = false;
                Repaint();
            }
        }

        private async void UpdateLMStudioConfigOnServer()
        {
            try
            {
                var configPayload = new { host = llmConfig.lmstudioHost, port = llmConfig.lmstudioPort, model = llmConfig.lmstudioModel, temperature = llmConfig.lmstudioTemperature };
                string jsonPayload = JsonConvert.SerializeObject(configPayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await httpClient.PostAsync(Constants.API.Configure, content);
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
                var cfg = new { lmstudio_host = llmConfig.lmstudioHost, lmstudio_port = llmConfig.lmstudioPort, lmstudio_model = llmConfig.lmstudioModel, lmstudio_temperature = llmConfig.lmstudioTemperature };
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
                llmConfig.lmstudioHost = cfg["lmstudio_host"]?.ToString() ?? llmConfig.lmstudioHost;
                llmConfig.lmstudioPort = cfg["lmstudio_port"]?.Value<int>() ?? llmConfig.lmstudioPort;
                llmConfig.lmstudioModel = cfg["lmstudio_model"]?.ToString() ?? llmConfig.lmstudioModel;
                llmConfig.lmstudioTemperature = cfg["lmstudio_temperature"]?.Value<float>() ?? llmConfig.lmstudioTemperature;
            }
            catch (Exception ex) { Debug.LogError($"[MCP] Failed to load local config: {ex.Message}"); }
        }

        private string GetLocalConfigPath()
        {
            string dir = Path.Combine(Application.dataPath, "..", Constants.CONFIG_DIRECTORY);
            return Path.Combine(dir, Constants.CONFIG_FILE);
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
}
