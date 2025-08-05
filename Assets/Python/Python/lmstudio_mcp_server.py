import logging
import asyncio
import aiohttp
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from typing import Dict, Any, List
import uvicorn
import json
import re
from new_prompts import PLANNER_SYSTEM_PROMPT, EXECUTOR_SYSTEM_PROMPT

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
}

# --- JSON Command Parser ---
def extract_json_from_response(text: str) -> Dict[str, Any]:
    """Extracts a single JSON command object from the LLM response."""
    try:
        # Find the first valid JSON object
        match = re.search(r"\{[\s\S]*\}", text.strip())
        if match:
            return json.loads(match.group(0))

        logger.warning("No JSON object found in LLM response.")
        return {}
    except Exception as e:
        logger.error("Failed to parse JSON from LLM response: %s", e, exc_info=True)
        return {}

# --- Plan Parser ---
def parse_plan_from_response(text: str) -> List[str]:
    """Extracts a numbered list of steps from the planner's response."""
    # Find all lines that start with a number and a period.
    steps = re.findall(r"^\s*\d+\.\s*(.*)", text, re.MULTILINE)
    if not steps:
        # As a fallback, if no numbered list is found, treat the whole response as a single step.
        logger.warning("Could not parse a numbered plan. Treating entire response as a single step.")
        return [text.strip()]
    return steps

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

    async def generate(self, prompt: str, system_prompt: str) -> str:
        """Generates a completion using the LM Studio chat API."""
        url = f"{self.base_url}/v1/chat/completions"
        messages = [{"role": "system", "content": system_prompt}, {"role": "user", "content": prompt}]
        payload = {"model": self.model, "messages": messages, "temperature": self.temperature, "max_tokens": 8192}

        async with aiohttp.ClientSession() as s:
            async with s.post(url, json=payload, timeout=120) as r:
                r.raise_for_status()
                data = await r.json()
                return data["choices"][0]["message"]["content"]

# --- FastAPI Application ---
app = FastAPI()
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_credentials=True, allow_methods=["*"], allow_headers=["*"])

_lmstudio_connection: LMStudioConnection | None = None

async def get_lmstudio_connection() -> LMStudioConnection:
    """Factory function to get the LM Studio connection instance."""
    return LMStudioConnection(
        host=config["lmstudio_host"], port=config["lmstudio_port"],
        model=config["model"], temperature=config["temperature"]
    )

@app.on_event("startup")
async def startup_event() -> None:
    """Initializes the connection to LM Studio on server startup."""
    global _lmstudio_connection
    _lmstudio_connection = await get_lmstudio_connection()
    if await _lmstudio_connection.test_connection():
        logger.info("Connected to LM Studio at %s:%s", config["lmstudio_host"], config["lmstudio_port"])
    else:
        logger.warning("Could not connect to LM Studio.")

# --- API Endpoints ---
@app.get("/health")
async def health_check():
    return {"status": "ok"}

@app.post("/api/process")
async def process_request(request: Dict[str, Any]):
    """Processes a prompt using the new Plan-and-Execute strategy."""
    user_prompt = request.get("prompt")
    if not user_prompt:
        raise HTTPException(status_code=400, detail="Missing 'prompt' in request.")

    if _lmstudio_connection is None:
        raise HTTPException(status_code=503, detail="Not connected to LM Studio.")

    # --- STEP 1: Create a plan ---
    logger.info("--- Step 1: Generating Plan ---")
    plan_text = await _lmstudio_connection.generate(user_prompt, PLANNER_SYSTEM_PROMPT)
    steps = parse_plan_from_response(plan_text)
    logger.info("Generated Plan:\n%s", "\n".join(f"{i+1}. {s}" for i, s in enumerate(steps)))

    # --- STEP 2: Execute the plan ---
    logger.info("--- Step 2: Executing Plan ---")
    final_commands = []
    for i, step in enumerate(steps):
        logger.info("Executing Step %d: %s", i + 1, step)
        # The executor prompt is a combination of the system prompt and the specific step
        executor_prompt = f"Execute this task: \"{step}\""
        json_command_text = await _lmstudio_connection.generate(executor_prompt, EXECUTOR_SYSTEM_PROMPT)

        command = extract_json_from_response(json_command_text)
        if command:
            final_commands.append(command)
        else:
            logger.error("Executor failed to produce a valid JSON command for step: %s", step)

    # The "llm_response" for the user will be the plan itself.
    return {"status": "success", "llm_response": plan_text, "commands": final_commands}

# (Other endpoints like /api/status, /api/models, /api/configure remain the same)
@app.get("/api/status")
async def get_status():
    if _lmstudio_connection is None: return {"status": "disconnected"}
    connected = await _lmstudio_connection.test_connection()
    return {"status": "connected" if connected else "disconnected", "model": config["model"], "host": config["lmstudio_host"], "port": config["lmstudio_port"]}

@app.get("/api/models")
async def get_available_models():
    if _lmstudio_connection is None: raise HTTPException(status_code=503, detail="Not connected to LM Studio.")
    try:
        url = f"{_lmstudio_connection.base_url}/v1/models"
        async with aiohttp.ClientSession() as session:
            async with session.get(url, timeout=10) as response:
                response.raise_for_status()
                models_data = await response.json()
                model_ids = [model.get("id") for model in models_data.get("data", []) if model.get("id")]
                return model_ids
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/configure")
async def configure(request: Dict[str, Any]):
    updatable_keys = ["host", "port", "lmstudio_host", "lmstudio_port", "model", "temperature"]
    for key in updatable_keys:
        if key in request: config[key] = request[key]
    global _lmstudio_connection
    _lmstudio_connection = await get_lmstudio_connection()
    return await get_status()

# --- Server Start ---
if __name__ == "__main__":
    logger.info("Starting LM Studio MCP server on http://%s:%s", config["host"], config["port"])
    uvicorn.run("lmstudio_mcp_server:app", host=config["host"], port=config["port"], reload=True)
