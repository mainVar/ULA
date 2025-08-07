"""
This script provides a FastAPI-based server to proxy requests from Unity to an
LM Studio backend. It exposes endpoints to process natural language prompts,
generate JSON commands for the Unity Editor, and manage the connection to the
LM Studio server.

Enhanced with:
- ReAct pattern support (Thought -> Action -> Observation)
- MCP server compatibility endpoint
- ChromaDB integration for memory
- Project indexing placeholder (for Roslyn integration)
"""

import logging
import asyncio
import aiohttp
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from typing import Dict, Any, List, Optional
import uvicorn
import json
import re
import os

# --- Local Imports ---
try:
    from memory import MemoryManager
except ImportError:
    # This is a fallback for environments where the script isn't run as a module
    MemoryManager = None

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
    "chromadb_host": "localhost",
    "chromadb_port": 8000,
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
2. manage_shader
   - Description: Manages shader files in the Unity project.
   - args:
     - action (string, required): The operation to perform. One of: 'create', 'read', 'update', 'delete'.
     - name (string, required): The name of the shader (without the .shader extension).
     - path (string, optional): The folder path to the shader. Defaults to "Assets/Shaders/".
     - contents (string, optional): The shader code for 'create' or 'update' actions.
--------------------------------------------------------------------------------
-- GAME OBJECTS & HIERARCHY
--------------------------------------------------------------------------------
3. manage_gameobject
   - Description: The main tool for creating, finding, modifying, and deleting GameObjects and their components.
   - args:
     - action (string, required): The operation. One of: 'create', 'find', 'modify', 'delete', 'add_component', 'remove_component', 'get_components'.
     - target (string, optional): The name or path of the GameObject to act upon.
     - name (string, optional): The name for 'create' or 'modify' actions.
     - primitive_type (string, optional): If creating a primitive, specify its type. e.g., 'Cube', 'Sphere'.
     - position (array of floats, optional): The world position [x, y, z].
     - rotation (array of floats, optional): The world rotation as Euler angles [x, y, z].
     - scale (array of floats, optional): The local scale [x, y, z].
     - parent (string, optional): The name or path of the parent GameObject.
     - components_to_add (array of strings, optional): List of component names to add.
     - component_properties (object, optional): Dictionary to set component properties.
--------------------------------------------------------------------------------
-- ASSETS & PREFABS
--------------------------------------------------------------------------------
4. manage_asset
   - Description: Manages project assets like Materials, Prefabs, and Folders.
   - args:
     - action (string, required): The operation. e.g., 'create', 'delete', 'create_folder'.
     - path (string, required): The asset path relative to the Assets folder.
     - asset_type (string, optional): The type of asset to create, e.g., 'Material'.
     - properties (object, optional): Dictionary of properties for the asset.
--------------------------------------------------------------------------------
-- CODE ANALYSIS & MANAGEMENT (New Tools for ReAct/MCP)
--------------------------------------------------------------------------------
5. read_script
   - Description: Reads the content of a C# script file.
   - args:
     - path (string, required): The full path to the script file (e.g., "Assets/Scripts/PlayerController.cs").
6. analyze_script
    - Description: Analyzes a C# script for potential issues or improvements (placeholder).
    - args:
     - path (string, required): The full path to the script file.
7. modify_script
    - Description: Modifies a C# script based on provided instructions (placeholder).
    - args:
     - path (string, required): The full path to the script file.
     - instructions (string, required): Instructions on what changes to make.
8. find_class
    - Description: Finds a class definition in the project (placeholder).
    - args:
     - name (string, required): The name of the class to find.
9. find_method
    - Description: Finds a method definition in the project (placeholder).
    - args:
     - name (string, required): The name of the method to find.
10. project_indexer
    - Description: Indexes the entire Unity C# project to understand its structure (placeholder).
    - args:
      - action (string, required): 'index' or 'status'.
""",
    "react_system_prompt": """You are a Unity Editor assistant. You must use the ReAct pattern: Thought -> Action -> Observation.

Your goal is to complete the user's request by thinking, choosing an action, and observing the result.
- **Thought**: First, think about the user's request and your plan. Describe your reasoning inside <thought> tags.
- **Action**: Based on your thought, select ONE of the available functions and specify its arguments. The action format is strict:
Action: function_name
Action Input: {"arg1": "value1", "arg2": "value2"}
- **Observation**: After you provide an action, the system will execute it and return an observation.

You will repeat this cycle (Thought -> Action -> Observation) until the task is complete.
If you have finished the task, use the 'finish_task' action.

Available Actions are listed in the initial system message. You must only use the functions provided.
Do not ask for clarification. Use your tools to find the information you need.
Start by thinking about the user's request.
""",
}

# --- Global Instances ---
_lmstudio_connection: Optional[LMStudioConnection] = None
_memory_manager: Optional[MemoryManager] = None

# --- JSON Command Parser (Enhanced for ReAct) ---
def extract_json_from_response(text: str) -> List[Dict[str, Any]]:
    """Extracts JSON command objects from a string."""
    # This regex is designed to find JSON objects, which are the format for our commands.
    # It looks for patterns that start with { and end with }, and are properly balanced.
    # It's a common pattern for extracting JSON from LLM text responses.
    json_pattern = re.compile(r"(\{.*?\})(?=\s*\{|\s*$)", re.DOTALL)
    matches = json_pattern.findall(text)

    commands = []
    for match in matches:
        try:
            # Clean up the match by removing potential newlines and backticks
            clean_match = match.strip().replace('\n', '').replace('`', '')
            data = json.loads(clean_match)
            commands.append(data)
        except json.JSONDecodeError:
            logger.warning(f"Could not decode JSON from match: {match}")
            continue

    if not commands:
        logger.warning("No JSON objects found in LLM response.")

    return commands


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

    async def generate(self, prompt: str, system_prompt: Optional[str] = None) -> str:
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
        try:
            async with aiohttp.ClientSession() as s:
                async with s.post(url, json=payload, timeout=120) as r:
                    r.raise_for_status()
                    data = await r.json()
                    return data["choices"][0]["message"]["content"]
        except aiohttp.ClientError as e:
            logger.error(f"Error connecting to LM Studio: {e}")
            raise HTTPException(status_code=502, detail=f"Could not connect to LM Studio: {e}")
        except Exception as e:
            logger.error(f"An unexpected error occurred during LLM generation: {e}", exc_info=True)
            raise HTTPException(status_code=500, detail="Error during LLM generation.")


# --- FastAPI Application ---
app = FastAPI(
    title="Unity MCP LM Studio Agent",
    description="A server that uses a local LLM to control the Unity Editor via the ReAct pattern.",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# --- Server Lifecycle Events ---
@app.on_event("startup")
async def startup_event() -> None:
    """Initializes connections on server startup."""
    global _lmstudio_connection, _memory_manager

    # Connect to LM Studio
    _lmstudio_connection = LMStudioConnection(
        host=config["lmstudio_host"],
        port=config["lmstudio_port"],
        model=config["model"],
        temperature=config["temperature"],
    )
    if await _lmstudio_connection.test_connection():
        logger.info("Connected to LM Studio at %s:%s", config["lmstudio_host"], config["lmstudio_port"])
    else:
        logger.warning("Could not connect to LM Studio. Please ensure the API server is running.")

    # Connect to ChromaDB
    if MemoryManager:
        try:
            _memory_manager = MemoryManager(
                host=config["chromadb_host"],
                port=config["chromadb_port"]
            )
            logger.info("Connected to ChromaDB at %s:%s", config["chromadb_host"], config["chromadb_port"])
        except Exception as e:
            logger.error(f"Could not connect to ChromaDB. Is it running? - {e}", exc_info=True)
            _memory_manager = None
    else:
        logger.warning("MemoryManager not available. Skipping ChromaDB connection.")


# --- API Endpoints ---

@app.get("/health", summary="Health Check")
async def health_check():
    """Check if the server is running."""
    return {"status": "ok", "service": "unity-mcp-lmstudio"}

@app.get("/api/status", summary="Get Connection Status")
async def get_status():
    """Returns the current connection status to backend services."""
    lm_studio_connected = False
    if _lmstudio_connection:
        lm_studio_connected = await _lmstudio_connection.test_connection()

    return {
        "lm_studio_status": "connected" if lm_studio_connected else "disconnected",
        "chromadb_status": "connected" if _memory_manager and _memory_manager.client else "disconnected",
        "model": config["model"],
    }

@app.post("/api/step", summary="Process a ReAct Step")
async def step(request: Dict[str, Any]):
    """
    Processes a single step in the ReAct loop.
    Receives the user's prompt and history, returns the next thought and action.
    """
    prompt = request.get("prompt")
    history = request.get("history", [])

    if not prompt:
        raise HTTPException(status_code=400, detail="Missing 'prompt' in request.")
    if _lmstudio_connection is None:
        raise HTTPException(status_code=503, detail="Not connected to LM Studio.")

    # Build the context for the LLM
    # 1. Start with the base system prompt
    # 2. Add memories from ChromaDB
    # 3. Add the history of the current interaction
    # 4. Add the final user prompt

    context = config["react_system_prompt"] + "\n" + config["system_prompt"]

    # Add relevant memories
    if _memory_manager:
        memories = _memory_manager.search_memory(prompt, n_results=3)
        if memories:
            context += "\n--- Relevant Memories ---\n"
            for mem in memories:
                context += f"- {mem['document']}\n"
            context += "-------------------------\n"

    # Add history
    for item in history:
        context += f"\n<thought>{item.get('thought', '')}</thought>\n"
        action = item.get('action', {})
        if action and action.get('function'):
            context += f"Action: {action['function']}\n"
            context += f"Action Input: {json.dumps(action.get('args', {}))}\n"
            context += f"Observation: {item.get('observation', 'No observation provided.')}\n"

    context += f"\nUser Request: {prompt}\n<thought>"

    llm_response = await _lmstudio_connection.generate(context, system_prompt=None) # System prompt is already in the context

    # Parse Thought and Action from the response
    thought_match = re.search(r"<thought>([\s\S]*?)</thought>", llm_response, re.DOTALL)
    action_match = re.search(r"Action:\s*(\w+)", llm_response)
    input_match = re.search(r"Action Input:\s*(\{.*\})", llm_response, re.DOTALL)

    thought = thought_match.group(1).strip() if thought_match else ""
    action_name = action_match.group(1).strip() if action_match else None
    action_args_str = input_match.group(1).strip() if input_match else "{}"

    try:
        action_args = json.loads(action_args_str)
    except json.JSONDecodeError:
        logger.error(f"Could not parse Action Input JSON: {action_args_str}")
        action_args = {}
        # If parsing fails, we might want to tell the LLM it made a mistake.
        # For now, we'll just return an empty action.
        action_name = "error"
        thought += "\n(System: Failed to parse Action Input. Please provide valid JSON.)"


    return {
        "thought": thought,
        "action": {
            "function": action_name,
            "args": action_args
        } if action_name else None,
        "raw_response": llm_response
    }


@app.get("/mcp/server", summary="MCP Server Information")
async def mcp_server():
    """Provides MCP-compatible server information, including available tools."""
    # This endpoint describes what the agent can do.
    # It's useful for clients that want to auto-configure themselves.
    tool_descriptions = config["system_prompt"] # The tool descriptions are in the system prompt

    # A simple parser to extract tool info from the prompt string.
    # This is not robust, but works for the current format.
    tools = []
    tool_sections = re.findall(r"(\d+\.\s*\w+)\s*-\s*Description:\s*(.*?)\s*-\s*args:\s*(.*?)(?=\n\d+\.|\Z)", tool_descriptions, re.DOTALL)
    for section in tool_sections:
        name = section[0].split('.')[1].strip()
        description = section[1].strip()
        # For now, inputSchema is a placeholder. A more robust system would parse the args properly.
        tools.append({
            "name": name,
            "description": description,
            "inputSchema": {"type": "object", "properties": {}}
        })

    return {
        "version": "0.2.0",
        "name": "unity-mcp-react-agent",
        "description": "Unity Editor Control Agent with ReAct pattern and Memory.",
        "capabilities": {
            "resources": True,
            "prompts": False,
            "tools": True
        },
        "tools": tools
    }

@app.post("/api/memory/clear", summary="Clear Agent Memory")
async def clear_agent_memory():
    """Clears all data from the ChromaDB collection."""
    if not _memory_manager:
        raise HTTPException(status_code=503, detail="MemoryManager is not available.")

    try:
        _memory_manager.clear_memory()
        return {"status": "success", "message": "Agent memory cleared."}
    except Exception as e:
        logger.error(f"Error clearing memory: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail="Failed to clear agent memory.")

# --- Placeholder Tool Implementations ---
# These functions simulate the new tools. In a real scenario, they would
# interact with a Roslyn-based C# tool or use other methods for code analysis.

@app.post("/api/tools/read_script", summary="Read a Script File")
async def read_script(request: Dict[str, str]):
    """Reads the content of a specified script file."""
    path = request.get("path")
    if not path or not isinstance(path, str):
        raise HTTPException(status_code=400, detail="Invalid 'path' provided.")

    # Basic security check to prevent directory traversal
    if ".." in path or not path.startswith("Assets/"):
        raise HTTPException(status_code=400, detail="Invalid or insecure file path.")

    try:
        with open(path, 'r', encoding='utf-8') as f:
            content = f.read()
        return {"status": "success", "content": content}
    except FileNotFoundError:
        return {"status": "error", "message": f"File not found: {path}"}
    except Exception as e:
        return {"status": "error", "message": str(e)}

@app.post("/api/tools/project_indexer", summary="Index the C# Project")
async def project_indexer(request: Dict[str, str]):
    """Placeholder for the Roslyn-based project indexer."""
    # In a real implementation, this would trigger a C# CLI tool.
    # The C# tool would scan the project, analyze scripts, and maybe
    # populate the ChromaDB memory with findings.
    logger.info("Project indexer called (placeholder).")
    return {
        "status": "success",
        "message": "Project indexing initiated (simulation). In a real system, this would take time.",
        "details": "This tool is a placeholder for a C# Roslyn-based executable that would analyze the project."
    }


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
        log_level="info"
    )
