using System.Text.Json.Nodes;

internal sealed class UnityBridgeDirectory
{
    private readonly HttpClient http;
    private readonly UnityMcpOptions options;
    private readonly List<BridgeEndpoint> cached = [];
    private DateTime cacheExpiresUtc;

    public UnityBridgeDirectory(HttpClient http, UnityMcpOptions options)
    {
        this.http = http;
        this.options = options;
    }

    public async Task<IReadOnlyList<BridgeEndpoint>> GetEndpointsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && DateTime.UtcNow < cacheExpiresUtc)
        {
            return cached.ToArray();
        }

        var discovered = await ScanAsync();
        cached.Clear();
        cached.AddRange(discovered);
        cacheExpiresUtc = DateTime.UtcNow.AddMilliseconds(options.BridgeScanCacheMs);
        return cached.ToArray();
    }

    public async Task<BridgeEndpointResolution> ResolveAsync(string? projectPath)
    {
        var endpoints = await GetEndpointsAsync();
        var resolution = ResolveFrom(projectPath, endpoints);
        if (resolution.Endpoint != null)
        {
            return resolution;
        }

        endpoints = await GetEndpointsAsync(forceRefresh: true);
        return ResolveFrom(projectPath, endpoints);
    }

    public async Task<BridgeEndpointResolution> WaitForProjectAsync(string projectPath, int timeoutMs, int pollIntervalMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        BridgeEndpointResolution? lastResolution = null;

        while (DateTime.UtcNow <= deadline)
        {
            lastResolution = await ResolveAsync(projectPath);
            if (lastResolution.Endpoint != null)
            {
                return lastResolution;
            }

            var remainingMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                break;
            }

            await Task.Delay(Math.Min(pollIntervalMs, remainingMs));
        }

        return lastResolution ?? await ResolveAsync(projectPath);
    }

    private static BridgeEndpointResolution ResolveFrom(string? projectPath, IReadOnlyList<BridgeEndpoint> endpoints)
    {
        var normalizedProjectPath = UnityMcpOptions.NormalizePath(projectPath);
        if (!string.IsNullOrWhiteSpace(normalizedProjectPath))
        {
            var match = endpoints.FirstOrDefault(endpoint => PathEquals(endpoint.ProjectPath, normalizedProjectPath));
            return match == null
                ? BridgeEndpointResolution.Failed(
                    $"No Unity Bridge is connected to projectPath '{normalizedProjectPath}'.",
                    normalizedProjectPath,
                    endpoints)
                : BridgeEndpointResolution.Resolved(match, normalizedProjectPath, endpoints);
        }

        return BridgeEndpointResolution.Failed("projectPath is required to select a Unity Bridge.", null, endpoints);
    }

    private async Task<List<BridgeEndpoint>> ScanAsync()
    {
        var tasks = Enumerable.Range(options.BridgePortStart, options.BridgePortCount)
            .Select(TryReadEndpointAsync)
            .ToArray();

        var results = await Task.WhenAll(tasks);
        return results
            .Where(endpoint => endpoint != null)
            .Cast<BridgeEndpoint>()
            .OrderBy(endpoint => endpoint.Port)
            .ToList();
    }

    private async Task<BridgeEndpoint?> TryReadEndpointAsync(int port)
    {
        var url = $"http://{options.BridgeHost}:{port}";

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
            using var response = await http.GetAsync(url + "/project-info", timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            var info = JsonNode.Parse(body) as JsonObject;
            if (info == null)
            {
                return null;
            }

            var projectPath = UnityMcpOptions.NormalizePath(TryReadString(info, "projectPath"));
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return null;
            }

            return new BridgeEndpoint(
                url,
                port,
                projectPath,
                TryReadString(info, "projectName"),
                TryReadString(info, "unityVersion"),
                info);
        }
        catch
        {
            return null;
        }
    }

    private static bool PathEquals(string? left, string? right)
    {
        var normalizedLeft = UnityMcpOptions.NormalizePath(left);
        var normalizedRight = UnityMcpOptions.NormalizePath(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft)
            && !string.IsNullOrWhiteSpace(normalizedRight)
            && string.Equals(normalizedLeft, normalizedRight, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static string? TryReadString(JsonObject? node, string key)
    {
        return node != null
            && node.TryGetPropertyValue(key, out var value)
            && value != null
            ? value.GetValue<string>()
            : null;
    }
}

internal sealed record BridgeEndpoint(
    string BaseUrl,
    int Port,
    string ProjectPath,
    string? ProjectName,
    string? UnityVersion,
    JsonObject ProjectInfo);

internal sealed record BridgeEndpointResolution(
    BridgeEndpoint? Endpoint,
    string? RequestedProjectPath,
    IReadOnlyList<BridgeEndpoint> Endpoints,
    string? Error)
{
    public static BridgeEndpointResolution Resolved(
        BridgeEndpoint endpoint,
        string? requestedProjectPath,
        IReadOnlyList<BridgeEndpoint> endpoints)
    {
        return new BridgeEndpointResolution(endpoint, requestedProjectPath, endpoints, null);
    }

    public static BridgeEndpointResolution Failed(
        string error,
        string? requestedProjectPath,
        IReadOnlyList<BridgeEndpoint> endpoints)
    {
        return new BridgeEndpointResolution(null, requestedProjectPath, endpoints, error);
    }
}
