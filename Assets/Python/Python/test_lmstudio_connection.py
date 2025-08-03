import aiohttp
import asyncio
import json

async def test_lmstudio_connection():
    url = "http://localhost:1234/v1/models"
    print(f"Testing connection to {url}...")
    
    try:
        async with aiohttp.ClientSession() as session:
            async with session.get(url, timeout=5) as response:
                print(f"Response status: {response.status}")
                if response.status == 200:
                    data = await response.json()
                    print("Connection successful!")
                    print("Available models:")
                    for model in data.get('data', []):
                        print(f"- {model.get('id', 'Unknown')}")
                else:
                    print(f"Error: {await response.text()}")
    except aiohttp.ClientConnectorError as e:
        print(f"Connection error: {e}")
        print("Please make sure LM Studio is running with the API server enabled on port 1234")
    except Exception as e:
        print(f"An error occurred: {e}")

if __name__ == "__main__":
    asyncio.run(test_lmstudio_connection())
