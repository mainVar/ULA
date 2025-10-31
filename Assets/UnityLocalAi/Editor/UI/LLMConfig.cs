using System;
using System.Collections.Generic;

namespace UnityLocalAi.UI
{
    [Serializable]
    public class LLMConfig
    {
        public string lmstudioHost = "localhost";
        public int lmstudioPort = 1234;
        public string lmstudioModel = "LM Studio Community/Meta-Llama-3-8B-Instruct-GGUF";
        public float lmstudioTemperature = 0.7f;

        public List<string> availableModels = new List<string>();
        public int selectedModelIndex = -1;
        public bool fetchingModels = false;
    }
}
