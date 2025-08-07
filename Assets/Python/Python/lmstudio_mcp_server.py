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
import os
from pathlib import Path

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
    "temperature": 0.2,
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
-- FILE I/O (Primary Method for Scripts & Shaders)
--------------------------------------------------------------------------------

1. write_file
   - Description: Creates or overwrites a file with the given content. This is the PREFERRED method for creating and updating scripts, shaders, and other text-based assets.
   - args:
     - path (string, required): The full path of the file, relative to the project's root (e.g., "Assets/Scripts/PlayerController.cs").
     - contents (string, required): The full content of the file.

   - Example (Create a C# Script):
     [
       {
         "function": "write_file",
         "args": {
           "path": "Assets/Scripts/PlayerController.cs",
           "contents": "using UnityEngine;\\n\\npublic class PlayerController : MonoBehaviour {\\n    public float speed = 5.0f;\\n    void Update() {\\n        float horizontal = Input.GetAxis(\\"Horizontal\\");\\n        transform.Translate(new Vector3(horizontal, 0, 0) * speed * Time.deltaTime);\\n    }\\n}"
         }
       }
     ]

   - Example (Create a Shader):
     [
       {
         "function": "write_file",
         "args": {
           "path": "Assets/Shaders/SimpleUnlit.shader",
           "contents": "Shader \\"Unlit/SimpleUnlit\\" { Properties { _Color (\\"Color\\", Color) = (1,1,1,1) } SubShader { Pass { CGPROGRAM #pragma vertex vert #pragma fragment frag #include \\"UnityCG.cginc\\" struct appdata { float4 vertex : POSITION; }; struct v2f { float4 vertex : SV_POSITION; }; fixed4 _Color; v2f vert (appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); return o; } fixed4 frag (v2f i) : SV_Target { return _Color; } ENDCG } } }"
         }
       }
     ]

--------------------------------------------------------------------------------
-- SCRIPTING & SHADERS (Management)
--------------------------------------------------------------------------------

2. manage_script
   - Description: Manages C# script files (read, delete). For creating/updating, use `write_file`.
   - args:
     - action (string, required): The operation. One of: 'read', 'delete'.
     - name (string, required): The name of the script (without the .cs extension).
     - path (string, optional): The folder path to the script. Defaults to "Assets/Scripts/".

3. manage_shader
   - Description: Manages shader files (read, delete). For creating/updating, use `write_file`.
   - args:
     - action (string, required): The operation. One of: 'read', 'delete'.
     - name (string, required): The name of the shader (without the .shader extension).
     - path (string, optional): The folder path to the shader. Defaults to "Assets/Shaders/".

--------------------------------------------------------------------------------
-- GAME OBJECTS & HIERARCHY
--------------------------------------------------------------------------------

4. manage_gameobject
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

   - Example (Create a complex object and attach a script):
     [
       {
         "function": "write_file",
         "args": {
           "path": "Assets/Scripts/Player.cs",
           "contents": "using UnityEngine; public class Player : MonoBehaviour {}"
         }
       },
       {
         "function": "manage_gameobject",
         "args": {
           "action": "create",
           "name": "PlayerObject",
           "components_to_add": ["Player"]
         }
       }
     ]

--------------------------------------------------------------------------------
-- ASSETS & PREFABS
--------------------------------------------------------------------------------

5. manage_asset
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

--------------------------------------------------------------------------------
-- SCENE MANAGEMENT
--------------------------------------------------------------------------------

6. manage_scene
   - Description: Manages scenes (new, save, load).
   - args:
     - action (string, required): The operation. One of: 'new', 'save', 'load'.
     - name (string, optional): The name of the scene for 'load' or 'save' actions. Include the path from 'Assets/', e.g., "Scenes/Level1".

--------------------------------------------------------------------------------
-- EDITOR & CONSOLE
--------------------------------------------------------------------------------

7. manage_editor
   - Description: Controls the Unity Editor's state.
   - args:
     - action (string, required): The operation. One of: 'play', 'pause', 'stop', 'get_state'.

8. read_console
   - Description: Reads messages from the Unity Editor console.
   - args:
     - action (string, required): The operation. One of: 'get', 'clear'.
     - types (array of strings, optional): Message types to get. One or more of: 'error', 'warning', 'log'. Defaults to all.

9. execute_menu_item
   - Description: Executes a Unity Editor menu item by its path.
   - args:
     - menu_path (string, required): The full path of the menu item (e.g., "File/Save Project", "Window/AI/NavMesh").
""",
}

# --- JSON Command Parser ---
def extract_json_from_response(text: str) -> List[Dict[str, Any]]:
    """
    Extracts JSON commands from the LLM response. It tries multiple strategies
    to find the JSON, making it robust against conversational text.
    """
    try:
        # Strategy 1: Discard any text within <think>...</think> blocks
        text_after_think = re.sub(r"<think>[\s\S]*?</think>", "", text).strip()

        # Strategy 2: Find fenced code blocks (```json ... ```)
        code_blocks = re.findall(r"```(?:json)?\s*([\s\S]*?)\s*```", text_after_think, re.DOTALL)
        if code_blocks:
            try:
                data = json.loads(code_blocks[0])
                logger.info("Successfully parsed JSON from fenced code block.")
                return data if isinstance(data, list) else [data]
            except json.JSONDecodeError as e:
                logger.warning("Found fenced code block, but failed to parse JSON: %s", e)


        # Strategy 3: Find the last potential start of a JSON array or object.
        # This is robust against conversational text that might contain JSON-like strings.
        last_bracket_pos = text_after_think.rfind('[')
        last_brace_pos = text_after_think.rfind('{')

        start_pos = -1
        if last_bracket_pos != -1 and last_bracket_pos > last_brace_pos:
            start_pos = last_bracket_pos
        elif last_brace_pos != -1:
            start_pos = last_brace_pos

        if start_pos != -1:
            json_text = text_after_think[start_pos:]
            try:
                data = json.loads(json_text)
                logger.info("Successfully parsed JSON from last found bracket/brace.")
                return data if isinstance(data, list) else [data]
            except json.JSONDecodeError as e:
                logger.warning(
                    "Could not parse JSON from last found bracket/brace at pos %d: %s",
                    start_pos, e
                )

        # Strategy 4: Fallback to the original greedy regex search as a last resort.
        match = re.search(r"(\[[\s\S]*\]|\{[\s\S]*\})", text_after_think.strip())
        if match:
            try:
                data = json.loads(match.group(1))
                logger.info("Successfully parsed JSON using fallback regex.")
                return data if isinstance(data, list) else [data]
            except json.JSONDecodeError as e:
                logger.warning("Fallback regex found a match, but it was not valid JSON: %s", e)


        logger.warning("No valid JSON found in LLM response after all strategies.")
        return []

    except Exception as e:
        logger.error("An unexpected error occurred during JSON parsing: %s", e, exc_info=True)
        return []

# --- File I/O Operations ---
def execute_write_file(command: Dict[str, Any]) -> None:
    """Executes the write_file command, writing content to a specified path."""
    args = command.get("args", {})
    path_str = args.get("path")
    contents = args.get("contents")

    if not path_str or not contents:
        logger.error("`write_file` command is missing `path` or `contents`.")
        return

    try:
        # Security: Ensure the path is within the project's 'Assets' directory.
        # This prevents writing to arbitrary locations on the file system.
        project_root = Path(os.getcwd()).resolve()
        # Navigate up from Assets/Python/Python to the project root
        if "Assets" in project_root.parts:
            while project_root.name != "Assets":
                project_root = project_root.parent
            project_root = project_root.parent


        target_path = (project_root / path_str).resolve()

        # Double-check that the resolved path is still within the project root.
        if project_root not in target_path.parents and target_path != project_root:
            logger.error(
                "Security risk: Attempted to write to a path outside the project root: %s",
                target_path,
            )
            return

        # Create parent directories if they don't exist
        target_path.parent.mkdir(parents=True, exist_ok=True)

        # Write the file
        with open(target_path, "w", encoding="utf-8") as f:
            f.write(contents)

        logger.info("Successfully wrote file to %s", target_path)

    except Exception as e:
        logger.error("Failed to execute `write_file` command: %s", e, exc_info=True)


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
            "max_tokens": 4096,
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
    """Processes a prompt and returns the LLM response and extracted commands."""
    prompt = request.get("prompt") or request.get("content")
    if not prompt:
        raise HTTPException(status_code=400, detail="Missing 'prompt' or 'content' in request.")

    if _lmstudio_connection is None:
        raise HTTPException(status_code=503, detail="Not connected to LM Studio.")

    llm_resp = await _lmstudio_connection.generate(prompt, config["system_prompt"])
    all_commands = extract_json_from_response(llm_resp)

    # Separate write_file commands from others
    write_commands = [cmd for cmd in all_commands if cmd.get("function") == "write_file"]
    other_commands = [cmd for cmd in all_commands if cmd.get("function") != "write_file"]

    # Execute file writing commands on the server
    if write_commands:
        logger.info("Executing %d `write_file` command(s)...", len(write_commands))
        for command in write_commands:
            execute_write_file(command)

    # Return other commands to be executed by Unity
    return {"status": "success", "llm_response": llm_resp, "commands": other_commands}


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
