import logging
import asyncio
import aiohttp
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from typing import Dict, Any, List
import uvicorn
import json
import re

# ─── Логування ────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger("LMStudioMCP")

# ─── Конфігурація ─────────────────────────────────────────────────────────────
config: Dict[str, Any] = {
    "host": "localhost",
    "port": 6500,
    "lmstudio_host": "localhost",
    "lmstudio_port": 1234,
    "model": "qwen/qwen3-14b",
    "temperature": 0.2,
    "system_prompt": """You are a tool‑calling agent for Unity. Your goal is to translate
user requests into JSON commands that the Unity Editor can execute.

When the user asks for an action, you MUST respond ONLY with a JSON object or an
array of JSON objects representing the function calls. Do NOT add any extra
text, explanations, or markdown formatting like ```json```. Your response must
be pure, valid JSON.

Available functions:
- create_object({"name": "object_name", "type": "CUBE" | "SPHERE" | "EMPTY",
                 "position": [x, y, z]})
- set_object_transform({"name": "object_name",
                        "position": [x, y, z],
                        "rotation": [x, y, z],
                        "scale": [x, y, z]})
- delete_object({"name": "object_name"})
- find_objects_by_name({"name": "search_term"})
- editor_action({"action": "PLAY" | "STOP" | "SAVE"})

Example user request: "Create a sphere named Ball at position 1, 2, 3"
Example JSON response:
[
  {
    "function": "create_object",
    "arguments": {
      "name": "Ball",
      "type": "SPHERE",
      "position": [1, 2, 3]
    }
  }
]
Use code with caution.

Python
Now, process the user's request.""",
}

# ─── Парсер JSON‑команд ───────────────────────────────────────────────────────
def extract_json_from_response(text: str) -> List[Dict[str, Any]]:
    """Витягує JSON‑команди з відповіді LLM, ігноруючи <think> … </think>."""
    try:
        # 1) Обрізаємо усе до </think>, якщо такий блок є
        text_after_think = text.split("</think>", 1)[-1] if "</think>" in text else text

        # 2) Шукаємо fenced‑блок ```json … ```
        code_blocks = re.findall(r"```(?:json)?\s*([\s\S]*?)\s*```", text_after_think, re.DOTALL)
        if code_blocks:
            data = json.loads(code_blocks[0])
            return data if isinstance(data, list) else [data]

        # 3) Шукаємо перший валідний JSON‑об’єкт/масив у plain‑тексті
        match = re.search(r"(\[[\s\S]*\]|\{[\s\S]*\})", text_after_think.strip())
        if match:
            data = json.loads(match.group(1))
            return data if isinstance(data, list) else [data]

        logger.warning("No JSON found in LLM response.")
        return []

    except Exception as e:
        logger.error("Failed to parse JSON from LLM response: %s", e, exc_info=True)
        return []

# ─── Обгортка над LM Studio ───────────────────────────────────────────────────
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

    async def generate(self, prompt: str, system_prompt: str | None = None) -> str:
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

# ─── FastAPI ──────────────────────────────────────────────────────────────────
app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],          # відкрито всюди
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

_lmstudio_connection: LMStudioConnection | None = None


async def get_lmstudio_connection() -> LMStudioConnection:
    return LMStudioConnection(
        host=config["lmstudio_host"],
        port=config["lmstudio_port"],
        model=config["model"],
        temperature=config["temperature"],
    )


@app.on_event("startup")
async def startup_event() -> None:
    global _lmstudio_connection
    _lmstudio_connection = await get_lmstudio_connection()
    if await _lmstudio_connection.test_connection():
        logger.info(
            "Connected to LM Studio at %s:%s",
            config["lmstudio_host"],
            config["lmstudio_port"],
        )
    else:
        logger.warning("Could not connect to LM Studio — перевірте, чи увімкнено API.")


# ─── Ендпоїнти ────────────────────────────────────────────────────────────────
@app.get("/health")
async def health_check():
    return {"status": "ok", "service": "unity-mcp-lmstudio"}


@app.post("/api/process")
async def process_request(request: Dict[str, Any]):
    prompt = request.get("prompt") or request.get("content")
    if not prompt:
        raise HTTPException(status_code=400, detail="Missing 'prompt' or 'content'")

    if _lmstudio_connection is None:
        raise HTTPException(status_code=503, detail="Not connected to LM Studio.")

    llm_resp = await _lmstudio_connection.generate(prompt, config["system_prompt"])
    commands = extract_json_from_response(llm_resp)

    return {"status": "success", "llm_response": llm_resp, "commands": commands}


@app.get("/api/status")
async def get_status():
    if _lmstudio_connection is None:
        return {"status": "disconnected"}

    connected = await _lmstudio_connection.test_connection()
    return {
        "status": "connected" if connected else "disconnected",
        "model": config["model"],
        "host": config["lmstudio_host"],
        "port": config["lmstudio_port"],
    }


@app.post("/api/configure")
async def configure(request: Dict[str, Any]):
    """Оновити налаштування LM Studio без перезапуску сервера."""
    for key in ["host", "port", "model", "temperature", "system_prompt"]:
        req_key = f"lmstudio_{key}"
        if req_key in request:
            config[req_key] = request[req_key]

    global _lmstudio_connection
    _lmstudio_connection = await get_lmstudio_connection()
    return await get_status()


# ─── Запуск ────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    logger.info(
        "Starting LM Studio MCP server on http://%s:%s", config["host"], config["port"]
    )
    uvicorn.run(
        "lmstudio_mcp_server:app",
        host=config["host"],
        port=config["port"],
        reload=True,
    )
