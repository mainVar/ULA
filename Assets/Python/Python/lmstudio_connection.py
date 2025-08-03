# lmstudio_connection.py
import aiohttp
import asyncio
import json
import sys
from dataclasses import dataclass
from config import config

# Configure console for UTF-8 output on Windows
if sys.platform == 'win32':
    import os
    os.system('chcp 65001 >nul')  # Set console to UTF-8 mode

@dataclass
class LMStudioConnection:
    host: str
    port: int
    model: str

    async def test_connection(self) -> bool:
        url = f"http://{self.host}:{self.port}/v1/models"
        print("\n=== Testing LM Studio Connection ===")
        print(f"Attempting to connect to: {url}")
        print(f"Model: {self.model}")
        
        try:
            print("\n1. Creating aiohttp client session...")
            async with aiohttp.ClientSession() as session:
                print("2. Sending GET request to /v1/models...")
                async with session.get(url, timeout=10) as response:
                    print(f"3. Received response with status: {response.status}")
                    print(f"4. Response headers: {dict(response.headers)}")
                    
                    if response.status == 200:
                        print("\n[SUCCESS] Successfully connected to LM Studio!")
                        try:
                            print("5. Attempting to parse JSON response...")
                            data = await response.json()
                            models = [m['id'] for m in data.get('data', [])]
                            print(f"\nAvailable models: {models}")
                            
                            if self.model not in models:
                                print(f"\n[WARNING] Configured model '{self.model}' not found in available models!")
                                print("   Available models:")
                                for model in models:
                                    print(f"   - {model}")
                            else:
                                print(f"\n[SUCCESS] Configured model '{self.model}' is available!")
                                
                        except Exception as e:
                            print(f"\n[ERROR] Error parsing models response: {e}")
                            print(f"Response content: {await response.text()}")
                            return False
                        
                        return True
                    else:
                        print(f"\n[ERROR] Connection failed with status code: {response.status}")
                        print(f"Response body: {await response.text()}")
                        return False
                        
        except aiohttp.ClientConnectorError as e:
            print(f"\n[ERROR] Connection error: {e}")
            print("This usually means LM Studio is not running or the port is incorrect.")
            print("Please make sure LM Studio is running with the API server enabled.")
            return False
            
        except asyncio.TimeoutError:
            print("\n[ERROR] Connection timed out. The server took too long to respond.")
            print("Please check if LM Studio is running and the API server is enabled.")
            return False
            
        except Exception as e:
            print(f"\n[ERROR] Unexpected error: {type(e).__name__}: {str(e)}")
            import traceback
            traceback.print_exc()
            return False

    async def get_completion(self, prompt: str, system_prompt: str, temperature: float = 0.2):
        url = f"http://{self.host}:{self.port}/v1/chat/completions"
        payload = {
            "model": self.model,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user",   "content": prompt},
            ],
            "temperature": temperature,
            "stream": False,
        }
        async with aiohttp.ClientSession() as s:
            async with s.post(url, json=payload) as r:
                data = await r.json()
                txt = data["choices"][0]["message"]["content"]
                return txt, data

    async def extract_mcp_commands(self, text: str):
        # 👇 приклад: шукаємо перший JSON‑блок у відповіді
        try:
            start = text.index("{")
            end   = text.rindex("}") + 1
            return json.loads(text[start:end])
        except ValueError:
            return []

_connection: LMStudioConnection | None = None

async def get_lmstudio_connection() -> LMStudioConnection:
    global _connection
    if _connection is None:
        _connection = LMStudioConnection(
            host  = config.lmstudio_host,
            port  = config.lmstudio_port,
            model = config.lmstudio_model,
        )
    return _connection
