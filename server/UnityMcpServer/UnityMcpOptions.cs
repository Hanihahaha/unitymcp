internal sealed class UnityMcpOptions
{
    public string BridgeBaseUrl { get; init; } = "http://127.0.0.1:8765";
    public string BridgeHost { get; init; } = "127.0.0.1";
    public int BridgePortStart { get; init; } = 8765;
    public int BridgePortCount { get; init; } = 20;
    public int BridgeScanCacheMs { get; init; } = 2000;
    public string? UnityExecutablePath { get; init; }
    public int ExternalCompileTimeoutMs { get; init; } = 600000;
    public string? ExternalCompileLogPath { get; init; }

    public static UnityMcpOptions FromEnvironment()
    {
        var unityExecutablePath = ReadFirst(
            "UNITY_MCP_UNITY_PATH",
            "UNITY_MCP_UNITY_EXE",
            "UNITY_EDITOR_PATH");

        return new UnityMcpOptions
        {
            BridgeBaseUrl = Environment.GetEnvironmentVariable("UNITY_MCP_BRIDGE_URL") ?? "http://127.0.0.1:8765",
            BridgeHost = Environment.GetEnvironmentVariable("UNITY_MCP_BRIDGE_HOST") ?? "127.0.0.1",
            BridgePortStart = ReadInt("UNITY_MCP_BRIDGE_PORT_START", 8765),
            BridgePortCount = Math.Max(1, Math.Min(ReadInt("UNITY_MCP_BRIDGE_PORT_COUNT", 20), 100)),
            BridgeScanCacheMs = Math.Max(0, ReadInt("UNITY_MCP_BRIDGE_SCAN_CACHE_MS", 2000)),
            UnityExecutablePath = unityExecutablePath,
            ExternalCompileTimeoutMs = ReadInt("UNITY_MCP_UNITY_COMPILE_TIMEOUT_MS", 600000),
            ExternalCompileLogPath = Environment.GetEnvironmentVariable("UNITY_MCP_UNITY_LOG_PATH")
        };
    }

    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string? ReadFirst(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static int ReadInt(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
