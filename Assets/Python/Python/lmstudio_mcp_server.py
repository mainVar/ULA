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
import argparse
from config import SERVER_CONFIG, LMSTUDIO_CONFIG, SYSTEM_PROMPT

# --- Logging Setup ---
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger("LMStudioMCP")

# --- JSON Command Parser ---
def extract_json_from_response(text: str) -> List[Dict[str, Any]]:
    try:
        text_after_think = text.split("</think>", 1)[-1] if "</think>" in text else text
        code_blocks = re.findall(r"```(?:json)?\s*([\s\S]*?)\s*```", text_after_think, re.DOTALL)
        if code_blocks:
            data = json.loads(code_blocks[0])
            return data if isinstance(data, list) else [data]

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
    def __init__(self, host: str, port: int, model: str, temperature: float) -> None:
        self.host = host
        self.port = port
        self.model = model
        self.temperature = temperature
        self.base_url = f"http://{host}:{port}"

    async def test_connection(self) -> bool:
        try:
            async with aiohttp.ClientSession() as s:
                async with s.get(f"{self.base_url}/v1/models", timeout=5) as r:
                    return r.status == 200
        except Exception:
            return False

    async def generate(self, prompt: str, system_prompt: str | None = None) -> Dict[str, Any]:
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
                return data["choices"][0]["message"]

# --- FastAPI Application ---
app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

_lmstudio_connection: LMStudioConnection | None = None

async def get_lmstudio_connection() -> LMStudioConnection:
    return LMStudioConnection(
        host=LMSTUDIO_CONFIG["host"],
        port=LMSTUDIO_CONFIG["port"],
        model=LMSTUDIO_CONFIG["model"],
        temperature=LMSTUDIO_CONFIG["temperature"],
    )

@app.on_event("startup")
async def startup_event() -> None:
    global _lmstudio_connection
    _lmstudio_connection = await get_lmstudio_connection()
    if await _lmstudio_connection.test_connection():
        logger.info(
            "Connected to LM Studio at %s:%s",
            LMSTUDIO_CONFIG["host"],
            LMSTUDIO_CONFIG["port"],
        )
    else:
        logger.warning("Could not connect to LM Studio — please check if the API server is running.")

# --- API Endpoints ---
@app.get("/health")
async def health_check():
    return {"status": "ok", "service": "unity-mcp-lmstudio"}

@app.post("/api/process")
async def process_request(request: Dict[str, Any]):
    prompt = request.get("prompt") or request.get("content")
    if not prompt:
        raise HTTPException(status_code=400, detail="Missing 'prompt' or 'content' in request.")

    if _lmstudio_connection is None:
        raise HTTPException(status_code=503, detail="Not connected to LM Studio.")

    llm_message = await _lmstudio_connection.generate(prompt, SYSTEM_PROMPT)
    llm_content = llm_message.get("content", "")
    llm_reasoning = llm_message.get("reasoning")
    commands = extract_json_from_response(llm_content)

    return {"status": "success", "llm_response": llm_content, "reasoning": llm_reasoning, "commands": commands}

@app.get("/api/status")
async def get_status():
    if _lmstudio_connection is None:
        return {"status": "disconnected"}

    connected = await _lmstudio_connection.test_connection()
    return {
        "status": "connected" if connected else "disconnected",
        "model": LMSTUDIO_CONFIG["model"],
        "host": LMSTUDIO_CONFIG["host"],
        "port": LMSTUDIO_CONFIG["port"],
    }

@app.get("/api/models")
async def get_available_models():
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
    updatable_keys = [
        "host", "port", "lmstudio_host", "lmstudio_port",
        "model", "temperature", "system_prompt"
    ]

    for key in updatable_keys:
        if key in request:
            if key in SERVER_CONFIG:
                SERVER_CONFIG[key] = request[key]
            elif key in LMSTUDIO_CONFIG:
                LMSTUDIO_CONFIG[key] = request[key]
            logger.info("Updated config: %s = %s", key, request[key])

    global _lmstudio_connection
    _lmstudio_connection = await get_lmstudio_connection()
    return await get_status()

# --- Server Start ---
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Unity Local AI Server")
    parser.add_argument("--host", type=str, default=SERVER_CONFIG["host"], help="Host to run the server on")
    parser.add_argument("--port", type=int, default=SERVER_CONFIG["port"], help="Port to run the server on")
    args = parser.parse_args()

    logger.info(
        "Starting LM Studio MCP server on http://%s:%s", args.host, args.port
    )
    uvicorn.run(
        "lmstudio_mcp_server:app",
        host=args.host,
        port=args.port,
        reload=True,
    )
