# Unity Local AI Agent (ULA)

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2022.3%2B-000000?style=for-the-badge&logo=unity" alt="Unity 2022.3+"/>
  <img src="https://img.shields.io/badge/Python-3.10%2B-3776AB?style=for-the-badge&logo=python&logoColor=white" alt="Python 3.10+"/>
  <img src="https://img.shields.io/badge/LM%20Studio-Compatible-00A67E?style=for-the-badge" alt="LM Studio"/>
  <img src="https://img.shields.io/badge/License-MIT-blue?style=for-the-badge" alt="MIT License"/>
  <a href="https://github.com/mainVar/ULA/releases/latest">
    <img src="https://img.shields.io/github/v/release/mainVar/ULA?label=Download&style=for-the-badge" alt="Download"/>
  </a>
</p>

> ⚠️ **This is a legacy/learning project.** See [Important Notes](#-important-notes--why-this-approach-has-limitations) below before using.

**Control your Unity Editor with natural language.** ULA is a Unity Editor extension that lets you create, modify, and manage GameObjects, scripts, materials, and scenes using plain English commands — powered by your local LLM via LM Studio.

---

## 🚨 Important Notes — Why This Approach Has Limitations

**This is an older project that I'm publishing for educational purposes.** It demonstrates the concept of building a bridge between local LLMs and Unity. However, this approach has significant problems that I discovered while building it. I'm sharing this so you can learn from my experience and avoid making the same mistakes.

### ❌ Key Problems with This Approach

#### 1. Local Models Struggle with Standard MCP Protocol
Local models have difficulty working reliably with the standard MCP (Model Context Protocol). That's why I created a simplified version here. But this simplification comes at a cost — see point 2.

#### 2. Not a Real MCP Tool
Although we use an MCP-like approach, this project **cannot be connected as an MCP tool** to code editors (Cursor, Cline, etc.) without significant modifications. Any variations of building a separate chat window inside Unity are fundamentally flawed for this reason. **I don't recommend building in-editor chat windows.**

#### 3. System Prompt Explosion
As you add more tools, the system prompt (defined in the Python server's `config.py`) grows enormous. Models start to hallucinate and become overloaded. This is not a scalable approach. At minimum, you need a way to dynamically control which tools are available to the LLM.

#### 4. Hardware Requirements Are Prohibitive
To host a local model with decent context length, you need **at least 16GB of VRAM**. Even then, the models you can run will perform roughly like OpenAI/Anthropic/Google models from 7-9 months ago. The cost of "intelligence" is constantly dropping — it's more efficient to use a cloud API.

### ✅ What I Recommend Instead

**Use another open source project I participate in that solves these problems:**

<p align="center">
  <a href="https://github.com/IvanMurzak/Unity-MCP">
    <img src="https://img.shields.io/badge/Recommended-Unity--MCP-success?style=for-the-badge&logo=github" alt="Unity-MCP"/>
  </a>
</p>

👉 **[github.com/IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP)**

This project implements proper MCP protocol support, allowing you to connect Unity tools directly to code editors like Cursor, Cline, and others.

### 🏢 For Businesses Concerned About Data Privacy

If privacy is critical, I recommend:
1. Deploy a single LLM server on your company's local network
2. Connect it to Kilo Code, Cline, or another tool that supports custom endpoints (like LM Studio server)
3. Use proper MCP tools for Unity integration

If you want more details on this approach, feel free to open a Pull Request or Issue — I'll provide more comprehensive guidance.

---

## ✨ Features

### 🎮 Natural Language Control
- Create and manipulate GameObjects with simple commands
- Generate C# scripts and shaders on the fly
- Manage scenes, materials, and prefabs through conversation

### 🔌 Local-First Architecture
- **No cloud dependencies** — runs entirely on your machine
- Works with any LLM model loaded in LM Studio
- FastAPI Python bridge handles communication

### 💬 Built-in Chat Interface
- Clean, modern UI built with Unity UI Toolkit
- Persistent chat history across sessions
- Multiple conversation support with easy switching

### 🎛️ Extensive Command Set

| Category | Capabilities |
|----------|-------------|
| **GameObjects** | Create, find, modify, delete, add/remove components, set properties |
| **Scripts** | Create, read, update, delete C# scripts |
| **Shaders** | Create and manage custom shaders |
| **Assets** | Create materials, prefabs, folders; duplicate, move, rename |
| **Scenes** | New, save, load scenes |
| **Editor** | Play, pause, stop; read console logs; execute menu items |

---

## 🚀 Quick Start

### Prerequisites
- **Unity** 2022.3 LTS or later
- **Python** 3.10+
- **LM Studio** with any compatible model loaded

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/mainVar/ULA.git
   ```

2. **Install Python dependencies**
   ```bash
   cd ULA/Assets/Python/Python
   pip install -r requirements.txt
   ```
   
   Or using [uv](https://github.com/astral-sh/uv) (faster):
   ```bash
   uv sync
   ```

3. **Open in Unity**
   - Open Unity Hub → Add → Select the cloned folder
   - Unity will import the project

4. **Open the ULA Window**
   - In Unity: `Window → Unity Local AI`

---

## 🛠️ Usage

### Starting the Server

1. Make sure **LM Studio** is running with a model loaded
2. In the ULA window, click **"Start Python Server"**
3. The status indicator will turn green when connected

### Configuration

Configure the connection in the ULA window:
- **LM Studio Host** — default: `localhost`
- **LM Studio Port** — default: `1234`
- **Model** — select from dropdown (auto-fetched from LM Studio)
- **Temperature** — controls response randomness

### Example Commands

Try these prompts in the chat:

```
Create a red cube at position 0, 2, 0
```

```
Add a Rigidbody component to the Cube and enable gravity
```

```
Create a new C# script called PlayerController with basic movement
```

```
Save the current scene as Scenes/TestLevel
```

---

## 📁 Project Structure

```
Assets/
├── Python/
│   └── Python/
│       ├── lmstudio_mcp_server.py   # FastAPI server
│       ├── config.py                 # Server & LLM configuration
│       └── requirements.txt          # Python dependencies
│
└── UnityLocalAi/
    └── Editor/
        ├── UnityLocalAiWindow.cs     # Main editor window
        ├── UnityMCPBridge.cs         # Command dispatcher
        ├── GameObjectCommands.cs     # GameObject operations
        ├── SceneCommands.cs          # Scene management
        ├── ScriptCommands.cs         # Script/shader operations
        ├── AssetCommands.cs          # Asset operations
        ├── EditorCommands.cs         # Editor state control
        ├── ChatHistoryManager.cs     # Chat persistence
        └── ui/                       # UI Toolkit files (UXML/USS)
```

---

## 🔧 Available Commands

The AI understands these command functions:

### `manage_gameobject`
Create, modify, delete GameObjects and their components.

```json
{
  "function": "manage_gameobject",
  "args": {
    "action": "create",
    "name": "Player",
    "primitive_type": "Capsule",
    "position": [0, 1, 0],
    "components_to_add": ["Rigidbody", "CapsuleCollider"]
  }
}
```

### `manage_script`
Create and manage C# scripts.

```json
{
  "function": "manage_script",
  "args": {
    "action": "create",
    "name": "EnemyAI",
    "path": "Assets/Scripts/",
    "contents": "using UnityEngine;\n\npublic class EnemyAI : MonoBehaviour { }"
  }
}
```

### `manage_asset`
Create materials, prefabs, and manage project assets.

```json
{
  "function": "manage_asset",
  "args": {
    "action": "create",
    "asset_type": "Material",
    "path": "Assets/Materials/Gold.mat",
    "properties": {
      "color": [1, 0.84, 0, 1]
    }
  }
}
```

### `manage_scene`
New, save, and load scenes.

### `manage_editor`
Control play mode: `play`, `pause`, `stop`, `get_state`.

### `read_console`
Read and clear Unity console logs.

### `execute_menu_item`
Execute any Unity Editor menu command.

---

## 🤝 Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

---

## 📄 License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- Built for use with [LM Studio](https://lmstudio.ai/)
- Powered by [FastAPI](https://fastapi.tiangolo.com/)
- Unity UI built with [UI Toolkit](https://docs.unity3d.com/Manual/UIElements.html)

---

## 👉 See Also

For a production-ready solution, check out **[Unity-MCP](https://github.com/IvanMurzak/Unity-MCP)** — proper MCP protocol implementation for Unity that integrates with modern AI code editors.
