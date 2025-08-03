# Unity Local AI Agent (ULA)

This project provides a Unity Editor window that acts as an interface to a local Large Language Model (LLM) running via LM Studio. It allows developers to send natural language commands to the LLM, which are then translated into executable Unity Editor commands.

This setup enables a powerful workflow where you can manipulate objects, trigger actions, and query the scene using text prompts directly within the Unity Editor.

## Features

The editor window, accessible via **Window > Unity MCP (LM Studio)**, includes the following features:

-   **Chat Interface:** A simple chat window to send prompts to the LLM.
-   **Python Backend Server:** A FastAPI server acts as a bridge between Unity and your local LM Studio instance.
-   **Dynamic Model Selection:** The editor window automatically fetches and displays a list of all available models from your LM Studio server, allowing you to switch between them easily.
-   **Start Server Button:** A button within the editor window to start the Python backend server directly, without needing to open a separate terminal.
-   **Configuration:** UI fields to configure the connection to the LM Studio server (host, port, temperature).

## How it Works

1.  **Unity Editor Window (`MCPEditorWindow.cs`):** This is the main user interface within Unity. It sends user prompts to the Python server.
2.  **Python Server (`lmstudio_mcp_server.py`):** This FastAPI server receives requests from Unity. It formats the prompt with a system message and forwards it to the LM Studio API.
3.  **LM Studio:** Your local LLM server processes the prompt and returns a response, which should contain JSON-formatted commands.
4.  **Command Execution:** The Python server extracts the JSON commands from the LLM's response and sends them back to Unity. The `UnityMCPBridge.cs` script in Unity then parses and executes these commands, affecting the scene or editor state.
