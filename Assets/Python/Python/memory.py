import chromadb
from chromadb.utils import embedding_functions
import logging
from typing import List, Dict, Any

logger = logging.getLogger("MemoryManager")

class MemoryManager:
    def __init__(self, host: str = "localhost", port: int = 8000, model_name: str = "nomic-ai/nomic-embed-text-v1.5"):
        self.host = host
        self.port = port
        self.client = chromadb.HttpClient(host=self.host, port=self.port)

        # Using a sentence-transformer model that runs locally
        self.embedding_function = embedding_functions.SentenceTransformerEmbeddingFunction(
            model_name=model_name,
            device="cpu" # Or "cuda" if you have a GPU
        )

        self.collection = self.client.get_or_create_collection(
            name="unity_agent_memory",
            embedding_function=self.embedding_function,
            metadata={"hnsw:space": "cosine"}
        )
        logger.info("ChromaDB collection 'unity_agent_memory' loaded/created.")

    def add_memory(self, text: str, metadata: Dict[str, Any] = None):
        """Adds a single text entry to the memory."""
        # ChromaDB requires a list for documents, metadatas, and ids
        # We'll use the text itself as the ID for simplicity, assuming it's unique enough for this context
        if self.collection.get(ids=[text])['ids']:
             logger.info("Memory already exists, skipping addition.")
             return
        self.collection.add(
            documents=[text],
            metadatas=[metadata] if metadata else [{}],
            ids=[text]
        )
        logger.info(f"Added memory: {text}")

    def add_memories(self, texts: List[str], metadatas: List[Dict[str, Any]] = None):
        """Adds multiple text entries to the memory."""
        # Use texts as IDs
        ids = texts
        if not metadatas:
            metadatas = [{}] * len(texts)

        self.collection.add(
            documents=texts,
            metadatas=metadatas,
            ids=ids
        )
        logger.info(f"Added {len(texts)} memories.")

    def search_memory(self, query: str, n_results: int = 5) -> List[Dict[str, Any]]:
        """Searches the memory for relevant entries."""
        results = self.collection.query(
            query_texts=[query],
            n_results=n_results
        )
        # The query result is a dict with lists for each key.
        # We want to return a list of result objects.
        if not results or not results['ids'][0]:
            return []

        # Re-structure the data for easier use
        output = []
        ids = results['ids'][0]
        documents = results['documents'][0]
        metadatas = results['metadatas'][0]
        distances = results['distances'][0]

        for i in range(len(ids)):
            output.append({
                "id": ids[i],
                "document": documents[i],
                "metadata": metadatas[i],
                "distance": distances[i]
            })

        return output

    def clear_memory(self):
        """Clears all entries from the collection."""
        try:
            # Delete the collection and recreate it to clear all data
            self.client.delete_collection(name=self.collection.name)
            self.collection = self.client.get_or_create_collection(
                name=self.collection.name,
                embedding_function=self.embedding_function,
                metadata={"hnsw:space": "cosine"}
            )
            logger.info("Memory collection cleared and recreated.")
        except Exception as e:
            logger.error(f"Failed to clear memory: {e}", exc_info=True)
            # This might happen if the collection doesn't exist, which is fine.
            # We can just recreate it.
            self.collection = self.client.get_or_create_collection(
                name="unity_agent_memory",
                embedding_function=self.embedding_function,
                metadata={"hnsw:space": "cosine"}
            )

    def get_all_memories(self) -> List[Dict[str, Any]]:
        """Retrieves all documents from the collection."""
        results = self.collection.get()
        return results

# Example Usage (for testing)
if __name__ == '__main__':
    logging.basicConfig(level=logging.INFO)

    # This assumes the ChromaDB docker container is running
    try:
        memory_manager = MemoryManager()

        print("Clearing any existing memory...")
        memory_manager.clear_memory()

        print("\nAdding memories...")
        memory_manager.add_memory("PlayerController script manages player movement.", {"type": "script_info"})
        memory_manager.add_memory("The main camera is tagged as 'MainCamera'.", {"type": "scene_info"})
        memory_manager.add_memory("Create a new C# script using manage_script('create', ...)", {"type": "tool_usage"})

        print("\nSearching for 'player script'...")
        search_results = memory_manager.search_memory("player script")
        for result in search_results:
            print(f"  - Found: '{result['document']}' (Distance: {result['distance']:.4f})")

        print("\nTotal memories stored:", memory_manager.collection.count())

    except Exception as e:
        print(f"\nError: Could not connect to ChromaDB. Please ensure the docker-compose service is running.")
        print(f"  - Run 'docker-compose up -d' in the terminal.")
        print(f"  - Details: {e}")
