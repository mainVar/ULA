# Unity Local AI Agent (ULA)

This project provides a Unity Editor window that acts as an interface to a local Large Language Model (LLM) running via LM Studio. It allows developers to send natural language commands to the LLM, which are then translated into executable Unity Editor commands. This setup enables a powerful workflow where you can manipulate objects, trigger actions, and query the scene using text prompts directly within the Unity Editor.

## Features

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

## Setup and Usage

1.  **Clone the repository:**
    ```
    git clone https://github.com/your-username/unity-local-ai-agent.git
    ```
2.  **Install Python dependencies:**
    ```
    pip install -r Assets/Python/Python/requirements.txt
    ```
3.  **Open the project in Unity:**
    -   Open Unity Hub and click "Add".
    -   Select the cloned repository's folder.
    -   Open the project in Unity.
4.  **Open the Unity Local AI window:**
    -   In the Unity Editor, go to `Window > Unity Local AI`.
5.  **Start the Python server:**
    -   Click the "Start Python Server" button in the Unity Local AI window.
6.  **Configure the LM Studio connection:**
    -   Enter the host, port, and model of your LM Studio server.
    -   Click "Apply & Save Configuration".
7.  **Start chatting with the AI:**
    -   Enter a prompt in the chat input field and click "Send".
    -   The AI will respond with a message and execute any commands it generates.

## Contributing

Contributions are welcome! If you'd like to contribute to the project, please follow these steps:

1.  **Fork the repository.**
2.  **Create a new branch for your feature or bug fix.**
3.  **Make your changes and commit them with a clear and descriptive commit message.**
4.  **Push your changes to your fork.**
5.  **Create a pull request to the main repository.**

## To-Do

-   **Add support for more LLM providers:**
    -   Add support for other LLM providers, such as Ollama and Jan.
-   **Improve the UI:**
    -   Add more features to the UI, such as a history of commands and a way to customize the system prompt.
-   **Add more commands:**
    -   Add more commands to the `UnityMCPBridge.cs` script to allow the AI to control more of the Unity Editor.
-   **Improve the documentation:**
    -   Add more detailed documentation to the `README.md` and the code.

## License

This project is licensed under the MIT License. See the `LICENSE` file for more details.
