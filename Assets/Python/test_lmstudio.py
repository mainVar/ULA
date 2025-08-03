import asyncio
import json
from lmstudio_connection import get_lmstudio_connection
from config import config

async def main():
    config.lmstudio_host = "localhost"
    config.lmstudio_port = 1234
    config.lmstudio_model = "qwen/qwen3-14b"
    config.save_to_file()

    conn = await get_lmstudio_connection()   # створить / оновить
    ok = await conn.test_connection()
    print("Connection:", ok)

if __name__ == "__main__":
    asyncio.run(main())
