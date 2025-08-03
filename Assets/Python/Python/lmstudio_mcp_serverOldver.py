import logging
import asyncio
import aiohttp
from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import Dict, Any, Optional, List
import uvicorn
import json
import re # <-- Важливо: додано імпорт для регулярних виразів

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger("LMStudioMCP")

# --- Configuration ---
# Конфігурація сервера. Особливу увагу зверніть на системний промпт.
config = {
    "host": "localhost",
    "port": 6500,
    "lmstudio_host": "localhost",
    "lmstudio_port": 1234,
    "model": "qwen/qwen3-14b", # Переконайтесь, що ця модель завантажена в LM Studio
    "temperature": 0.2,
    # --- КЛЮЧОВА ЗМІНА: Покращений системний промпт ---
    "system_prompt": """You are a tool-calling agent for Unity. Your goal is to translate user requests into JSON commands that the Unity Editor can execute.

When the user asks for an action, you MUST respond ONLY with a JSON object or an array of JSON objects representing the function calls. Do NOT add any extra text, explanations, or markdown formatting like ```json. Your response should be pure, valid JSON.

Available functions:
- `create_object({"name": "object_name", "type": "CUBE" | "SPHERE" | "EMPTY", "position": [x, y, z]})`
- `set_object_transform({"name": "object_name", "position": [x, y, z], "rotation": [x, y, z], "scale": [x, y, z]})`
- `delete_object({"name": "object_name"})`
- `find_objects_by_name({"name": "search_term"})`
- `editor_action({"action": "PLAY" | "STOP" | "SAVE"})`

Example user request: "Create a sphere named Ball at position 1, 2, 3"
Example JSON response:
```json
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