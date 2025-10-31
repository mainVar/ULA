namespace UnityLocalAi
{
    public static class Constants
    {
        public const int MCP_PORT = 6500;
        public const float CONNECTION_CHECK_INTERVAL = 5f;
        public const string CONFIG_DIRECTORY = "Temp/LMStudioConfig";
        public const string CONFIG_FILE = "lmstudio_config.json";

        public static class API
        {
            public static string Health => $"http://localhost:{MCP_PORT}/health";
            public static string Process => $"http://localhost:{MCP_PORT}/api/process";
            public static string Status => $"http://localhost:{MCP_PORT}/api/status";
            public static string Models => $"http://localhost:{MCP_PORT}/api/models";
            public static string Configure => $"http://localhost:{MCP_PORT}/api/configure";
        }
    }
}
