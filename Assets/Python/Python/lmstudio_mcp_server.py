"""
This script provides a FastAPI-based server to proxy requests from Unity to an
LM Studio backend. It exposes endpoints to process natural language prompts,
generate JSON commands for the Unity Editor, and manage the connection to the
LM Studio server.
"""
import logging
import asyncio
import aiohttp
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from typing import Dict, Any, List
import uvicorn
import json
import re
import subprocess

# --- Logging Setup ---
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger("LMStudioMCP")

# --- Configuration ---
config: Dict[str, Any] = {
    "host": "localhost",
    "port": 6500,
    "lmstudio_host": "localhost",
    "lmstudio_port": 1234,
    "model": "qwen/qwen3-14b",
    "temperature": 0.7,
    "system_prompt": """You are a helpful AI assistant that controls the Unity Editor.
Your ONLY output must be a JSON array of command objects. Do NOT add any conversational text or explanations.

Each command object in the JSON array must have the following structure:
{
  "function": "function_name",
  "args": {
    "arg1": "value1",
    "arg2": "value2"
  }
}

Here are the available functions and their arguments:

--------------------------------------------------------------------------------
-- SCRIPTING & SHADERS
--------------------------------------------------------------------------------

1. manage_script
   - Description: Manages C# script files in the Unity project (create, read, update, delete).
   - args:
     - action (string, required): The operation to perform. One of: 'create', 'read', 'update', 'delete'.
     - name (string, required): The name of the script (without the .cs extension).
     - path (string, optional): The folder path to the script. Defaults to "Assets/Scripts/".
     - contents (string, optional): The C# code for 'create' or 'update' actions.
     - namespace (string, optional): The namespace for the script.
     - script_type (string, optional): A hint for the class type, e.g., 'MonoBehaviour', 'ScriptableObject'.

   - Example:
     [
       {
         "function": "manage_script",
         "args": {
           "action": "create",
           "name": "PlayerController",
           "path": "Assets/Scripts/Player/",
           "contents": "using UnityEngine;\\n\\npublic class PlayerController : MonoBehaviour {\\n    public float speed = 5.0f;\\n    void Update() {\\n        float horizontal = Input.GetAxis(\\"Horizontal\\");\\n        transform.Translate(new Vector3(horizontal, 0, 0) * speed * Time.deltaTime);\\n    }\\n}"
         }
       }
     ]

2. manage_shader
   - Description: Manages shader files in the Unity project.
   - args:
     - action (string, required): The operation to perform. One of: 'create', 'read', 'update', 'delete'.
     - name (string, required): The name of the shader (without the .shader extension).
     - path (string, optional): The folder path to the shader. Defaults to "Assets/Shaders/".
     - contents (string, optional): The shader code for 'create' or 'update' actions.

   - Example:
     [
       {
         "function": "manage_shader",
         "args": {
           "action": "create",
           "name": "SimpleUnlit",
           "path": "Assets/Shaders/",
           "contents": "Shader \\"Unlit/SimpleUnlit\\" { Properties { _Color (\\"Color\\", Color) = (1,1,1,1) } SubShader { Pass { CGPROGRAM #pragma vertex vert #pragma fragment frag #include \\"UnityCG.cginc\\" struct appdata { float4 vertex : POSITION; }; struct v2f { float4 vertex : SV_POSITION; }; fixed4 _Color; v2f vert (appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); return o; } fixed4 frag (v2f i) : SV_Target { return _Color; } ENDCG } } }"
         }
       }
     ]

--------------------------------------------------------------------------------
-- GAME OBJECTS & HIERARCHY
--------------------------------------------------------------------------------

3. manage_gameobject
   - Description: The main tool for creating, finding, modifying, and deleting GameObjects and their components.
   - args:
     - action (string, required): The operation. One of: 'create', 'find', 'modify', 'delete', 'add_component', 'remove_component', 'get_components'.
     - target (string, optional): The name or path of the GameObject to act upon for 'modify', 'delete', and component actions.
     - name (string, optional): The name of the GameObject. Used when 'action' is 'create', or to rename an object when 'action' is 'modify'.
     - primitive_type (string, optional): If creating a primitive, specify its type. One of: 'Cube', 'Sphere', 'Capsule', 'Cylinder', 'Plane', 'Quad'.
     - position (array of floats, optional): The world position [x, y, z].
     - rotation (array of floats, optional): The world rotation as Euler angles [x, y, z].
     - scale (array of floats, optional): The local scale [x, y, z].
     - parent (string, optional): The name or path of the parent GameObject.
     - tag (string, optional): The tag to assign to the GameObject.
     - layer (string, optional): The layer to assign to the GameObject.
     - components_to_add (array of strings, optional): A list of component names to add, e.g., ["Rigidbody", "BoxCollider"].
     - components_to_remove (array of strings, optional): A list of component names to remove.
     - component_properties (object, optional): A dictionary to set component properties. Keys are component names, values are dictionaries of properties and their values.
     - set_active (boolean, optional): Set the active state of the GameObject.

   - Example (Create a complex object):
     [
       {
         "function": "manage_gameobject",
         "args": {
           "action": "create",
           "name": "Player",
           "position": [0, 1, 0],
           "tag": "Player",
           "components_to_add": ["Rigidbody", "CapsuleCollider", "PlayerController"],
           "component_properties": {
             "Rigidbody": {
               "useGravity": true,
               "constraints": "FreezeRotation"
             }
           }
         }
       }
     ]

   - Example (Modify an object):
     [
       {
         "function": "manage_gameobject",
         "args": {
           "action": "modify",
           "target": "Player",
           "scale": [1.5, 1.5, 1.5],
           "layer": "PlayerLayer"
         }
       }
     ]

   - Example (Delete an object):
     [
       {
         "function": "manage_gameobject",
         "args": {
           "action": "delete",
           "target": "OldEnemy"
         }
       }
     ]

--------------------------------------------------------------------------------
-- ASSETS & PREFABS
--------------------------------------------------------------------------------

4. manage_asset
   - Description: Manages project assets like Materials, Prefabs, and Folders.
   - args:
     - action (string, required): The operation. One of: 'create', 'get_info', 'modify', 'delete', 'create_folder', 'duplicate', 'move', 'rename'.
     - path (string, required): The asset path relative to the Assets folder (e.g., "Materials/MyMaterial.mat").
     - asset_type (string, optional): The type of asset to create. Required for 'create'. e.g., 'Material', 'PhysicsMaterial'.
     - properties (object, optional): A dictionary of properties to set when creating or modifying an asset.
     - destination (string, optional): The new path for 'move', 'rename', or 'duplicate' actions.

   - Example (Create a Material):
     [
       {
         "function": "manage_asset",
         "args": {
           "action": "create",
           "asset_type": "Material",
           "path": "Assets/Materials/Red.mat",
           "properties": {
             "color": [1, 0, 0, 1],
             "shader": "Standard"
           }
         }
       }
     ]

   - Example (Apply a material to an object):
     [
       {
         "function": "manage_gameobject",
         "args": {
           "action": "modify",
           "target": "Player",
           "component_properties": {
             "MeshRenderer": {
               "sharedMaterial": "Assets/Materials/Red.mat"
             }
           }
         }
       }
     ]

   - Example (Create a Prefab):
     [
       {
         "function": "manage_gameobject",
         "args": {
           "action": "create",
           "name": "Coin",
           "primitive_type": "Cylinder",
           "position": [10, 1, 5],
           "scale": [0.5, 0.1, 0.5],
           "save_as_prefab": true,
           "prefab_path": "Assets/Prefabs/Coin.prefab"
         }
       }
     ]

--------------------------------------------------------------------------------
-- SCENE MANAGEMENT
--------------------------------------------------------------------------------

5. manage_scene
   - Description: Manages scenes (new, save, load).
   - args:
     - action (string, required): The operation. One of: 'new', 'save', 'load'.
     - name (string, optional): The name of the scene for 'load' or 'save' actions. Include the path from 'Assets/', e.g., "Scenes/Level1".

   - Example:
     [
       {
         "function": "manage_scene",
         "args": {
           "action": "save",
           "name": "Scenes/MainScene"
         }
       }
     ]

--------------------------------------------------------------------------------
-- EDITOR & CONSOLE
--------------------------------------------------------------------------------

6. manage_editor
   - Description: Controls the Unity Editor's state.
   - args:
     - action (string, required): The operation. One of: 'play', 'pause', 'stop', 'get_state'.

   - Example:
     [
       {
         "function": "manage_editor",
         "args": {
           "action": "play"
         }
       }
     ]

7. read_console
   - Description: Reads messages from the Unity Editor console.
   - args:
     - action (string, required): The operation. One of: 'get', 'clear'.
     - types (array of strings, optional): Message types to get. One or more of: 'error', 'warning', 'log'. Defaults to all.

   - Example:
     [
       {
         "function": "read_console",
         "args": {
           "action": "get",
           "types": ["error", "warning"]
         }
       }
     ]

8. execute_menu_item
   - Description: Executes a Unity Editor menu item by its path.
   - args:
     - menu_path (string, required): The full path of the menu item (e.g., "File/Save Project", "Window/AI/NavMesh").

   - Example:
     [
       {
         "function": "execute_menu_item",
         "args": {
           "menu_path": "File/Save Project"
         }
       }
     ]

--------------------------------------------------------------------------------
-- SYSTEM & FILE OPERATIONS
--------------------------------------------------------------------------------

9. execute_cli
   - Description: Executes a command-line interface (CLI) command on the local machine. This is useful for file system operations, running scripts, or interacting with other command-line tools. For complex file manipulation or queries, consider using the 'gemini-cli' tool if it is available.
   - args:
     - command (string, required): The full command to execute.
     - working_directory (string, optional): The directory to run the command in. Defaults to the project root.

   - Example (List files in a directory):
     [
       {
         "function": "execute_cli",
         "args": {
           "command": "ls -l Assets/Textures/"
         }
       }
     ]

   - Example (Use gemini-cli to organize files):
     [
       {
         "function": "execute_cli",
         "args": {
           "command": "gemini 'Organize all my texture files into folders based on their dominant color.'"
         }
       }
     ]
""",
}

# --- JSON Command Parser ---
def extract_json_from_response(text: str) -> List[Dict[str, Any]]:
    """Extracts JSON commands from the LLM response, ignoring <think> blocks."""
    try:
        # 1) Discard any text before the final </think> tag if it exists
        text_after_think = text.split("</think>", 1)[-1] if "</think>" in text else text

        # 2) Find fenced code blocks (```json ... ```)
        code_blocks = re.findall(r"```(?:json)?\s*([\s\S]*?)\s*```", text_after_think, re.DOTALL)
        if code_blocks:
            data = json.loads(code_blocks[0])
            return data if isinstance(data, list) else [data]

        # 3) Find the first valid JSON object/array in plain text
        match = re.search(r"(\[[\s\S]*\]|\{[\s\S]*\})", text_after_think.strip())
        if match:
            data = json.loads(match.group(1))
            return data if isinstance(data, list) else [data]

        logger.warning("No JSON found in LLM response.")
        return []

    except Exception as e:
        logger.error("Failed to parse JSON from LLM response: %s", e, exc_info=True)
        return []

# --- LM Studio Wrapper ---
class LMStudioConnection:
    """A wrapper for interacting with the LM Studio API."""
    def __init__(self, host: str, port: int, model: str, temperature: float) -> None:
        self.host = host
        self.port = port
        self.model = model
        self.temperature = temperature
        self.base_url = f"http://{host}:{port}"

    async def test_connection(self) -> bool:
        """Tests the connection to the LM Studio server."""
        try:
            async with aiohttp.ClientSession() as s:
                async with s.get(f"{self.base_url}/v1/models", timeout=5) as r:
                    return r.status == 200
        except Exception:
            return False

    async def generate(self, prompt: str, system_prompt: str | None = None) -> str:
        """Generates a completion using the LM Studio chat API."""
        url = f"{self.base_url}/v1/chat/completions"
        messages = (
            [{"role": "system", "content": system_prompt}] if system_prompt else []
        ) + [{"role": "user", "content": prompt}]

        payload = {
            "model": self.model,
            "messages": messages,
            "temperature": self.temperature,
            "max_tokens": 8096,
        }

        async with aiohttp.ClientSession() as s:
            async with s.post(url, json=payload, timeout=120) as r:
                r.raise_for_status()
                data = await r.json()
                return data["choices"][0]["message"]["content"]

# --- FastAPI Application ---
app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Allow all origins
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

_lmstudio_connection: LMStudioConnection | None = None


async def get_lmstudio_connection() -> LMStudioConnection:
    """Factory function to get the LM Studio connection instance."""
    return LMStudioConnection(
        host=config["lmstudio_host"],
        port=config["lmstudio_port"],
        model=config["model"],
        temperature=config["temperature"],
    )


@app.on_event("startup")
async def startup_event() -> None:
    """Initializes the connection to LM Studio on server startup."""
    global _lmstudio_connection
    _lmstudio_connection = await get_lmstudio_connection()
    if await _lmstudio_connection.test_connection():
        logger.info(
            "Connected to LM Studio at %s:%s",
            config["lmstudio_host"],
            config["lmstudio_port"],
        )
    else:
        logger.warning("Could not connect to LM Studio — please check if the API server is running.")


# --- API Endpoints ---
@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return {"status": "ok", "service": "unity-mcp-lmstudio"}


@app.post("/api/process")
async def process_request(request: Dict[str, Any]):
    """
    Processes a prompt, handles regular commands, and intercepts 'execute_cli'
    commands to run them locally and summarize the output.
    """
    prompt = request.get("prompt") or request.get("content")
    if not prompt:
        raise HTTPException(status_code=400, detail="Missing 'prompt' or 'content' in request.")

    if _lmstudio_connection is None:
        raise HTTPException(status_code=503, detail="Not connected to LM Studio.")

    # Initial call to the LLM
    llm_resp = await _lmstudio_connection.generate(prompt, config["system_prompt"])
    commands = extract_json_from_response(llm_resp)

    # --- CLI Command Handling ---
    # Check if the response contains a CLI command
    cli_command_to_run = None
    for command in commands:
        if command.get("function") == "execute_cli":
            cli_command_to_run = command
            break

    if cli_command_to_run:
        logger.info("Intercepted execute_cli command: %s", cli_command_to_run)

        cli_command_str = cli_command_to_run.get("args", {}).get("command")
        if not cli_command_str:
            return {"status": "error", "llm_response": "LLM generated an execute_cli command with no command to run.", "commands": []}

        try:
            # Execute the command
            result = subprocess.run(
                cli_command_str,
                shell=True,
                capture_output=True,
                text=True,
                timeout=120  # 2-minute timeout for safety
            )

            stdout = result.stdout.strip()
            stderr = result.stderr.strip()

            logger.info("CLI command stdout: %s", stdout)
            if stderr:
                logger.error("CLI command stderr: %s", stderr)

            # Create a new prompt to summarize the result
            output_summary = f"STDOUT:\n{stdout}\n\nSTDERR:\n{stderr}"
            summarization_prompt = (
                f"I just ran a command-line tool for the user based on their request. "
                f"The command was: '{cli_command_str}'.\n"
                f"The tool produced the following output:\n\n---\n{output_summary}\n---\n\n"
                f"Please provide a concise, user-friendly summary of what happened. "
                f"Describe the outcome, and if there was an error, explain it simply. "
                f"Do not output JSON or any other code. Just provide a natural language response."
            )

            # Second call to the LLM for summarization
            final_llm_response = await _lmstudio_connection.generate(summarization_prompt)

            # Return the summarized response to Unity, with no commands to execute
            return {"status": "success", "llm_response": final_llm_response, "commands": []}

        except subprocess.TimeoutExpired:
            logger.error("CLI command timed out: %s", cli_command_str)
            return {"status": "error", "llm_response": f"The command '{cli_command_str}' timed out and was cancelled.", "commands": []}
        except Exception as e:
            logger.error("Failed to execute or summarize CLI command: %s", e, exc_info=True)
            return {"status": "error", "llm_response": f"An unexpected error occurred while running the command: {e}", "commands": []}

    # If no CLI command, return the original response
    return {"status": "success", "llm_response": llm_resp, "commands": commands}


@app.get("/api/status")
async def get_status():
    """Returns the current connection status to LM Studio."""
    if _lmstudio_connection is None:
        return {"status": "disconnected"}

    connected = await _lmstudio_connection.test_connection()
    return {
        "status": "connected" if connected else "disconnected",
        "model": config["model"],
        "host": config["lmstudio_host"],
        "port": config["lmstudio_port"],
    }


@app.get("/api/models")
async def get_available_models():
    """Fetches the list of available models from the LM Studio backend."""
    if _lmstudio_connection is None:
        raise HTTPException(status_code=503, detail="Not connected to LM Studio.")

    try:
        url = f"{_lmstudio_connection.base_url}/v1/models"
        async with aiohttp.ClientSession() as session:
            async with session.get(url, timeout=10) as response:
                response.raise_for_status()
                models_data = await response.json()
                if "data" not in models_data or not isinstance(models_data["data"], list):
                    logger.error("Invalid format from LM Studio /v1/models: %s", models_data)
                    raise HTTPException(status_code=500, detail="Invalid format from LM Studio /v1/models endpoint.")

                model_ids = [model.get("id") for model in models_data["data"] if model.get("id")]
                return model_ids
    except aiohttp.ClientError as e:
        logger.error("Could not connect to LM Studio to get models: %s", e)
        raise HTTPException(status_code=502, detail=f"Could not connect to LM Studio: {e}")
    except Exception as e:
        logger.error("Failed to get available models: %s", e, exc_info=True)
        raise HTTPException(status_code=500, detail=f"An unexpected error occurred: {str(e)}")


@app.post("/api/configure")
async def configure(request: Dict[str, Any]):
    """Updates LM Studio settings without restarting the server."""
    # List of valid keys that can be updated in the config
    updatable_keys = [
        "host", "port", "lmstudio_host", "lmstudio_port",
        "model", "temperature", "system_prompt"
    ]

    for key in updatable_keys:
        if key in request:
            config[key] = request[key]
            logger.info("Updated config: %s = %s", key, request[key])

    global _lmstudio_connection
    _lmstudio_connection = await get_lmstudio_connection()
    return await get_status()


# --- Server Start ---
if __name__ == "__main__":
    logger.info(
        "Starting LM Studio MCP server on http://%s:%s", config["host"], config["port"]
    )
    uvicorn.run(
        "lmstudio_mcp_server:app",
        host=config["host"],
        port=config["port"],
        reload=True,
    )
