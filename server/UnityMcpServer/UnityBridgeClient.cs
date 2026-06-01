using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed partial class UnityBridgeClient
{
    private readonly HttpClient http;
    private readonly UnityMcpOptions options;
    private readonly UnityBridgeDirectory bridgeDirectory;
    private readonly UnityEditorCompiler editorCompiler;

    public UnityBridgeClient(HttpClient http, UnityMcpOptions options, UnityBridgeDirectory bridgeDirectory)
    {
        this.http = http;
        this.options = options;
        this.bridgeDirectory = bridgeDirectory;
        editorCompiler = new UnityEditorCompiler(options);
    }

    public async Task<object?> CallToolAsync(string name, JsonObject args)
    {
        if (name == "unity_bridge_status")
        {
            return await GetBridgeStatusAsync(args);
        }

        var path = name switch
        {
            "unity_health" => "health",
            "unity_get_project_info" => "project-info",
            "unity_get_play_state" => "play-state",
            "unity_get_compile_state" => "compile-state",
            "unity_request_script_compile" => null,
            "unity_request_script_compile_and_wait" => null,
            "unity_wait_for_compile_complete" => null,
            "unity_list_scenes" => "scenes",
            "unity_find_scenes" => BuildFindScenesPath(args),
            "unity_select_scene" => BuildSelectScenePath(args),
            "unity_query_objects" => BuildObjectsPath(args),
            "unity_get_object" => BuildObjectPath(args),
            "unity_get_object_scripts" => BuildObjectScriptsPath(args),
            "unity_get_logs" => BuildLogsPath(args),
            "unity_get_error_logs" => BuildTypedLogsPath(args, "Error,Assert,Exception"),
            "unity_get_warning_logs" => BuildTypedLogsPath(args, "Warning"),
            "unity_enter_play_mode" => "enter-play-mode",
            "unity_stop_play_mode" => "stop-play-mode",
            _ => null
        };

        return name switch
        {
            "unity_request_script_compile" => await RequestScriptCompileAsync(args),
            "unity_request_script_compile_and_wait" => await RequestScriptCompileAndWaitAsync(args),
            "unity_wait_for_compile_complete" => await WaitForCompileCompleteAsync(args),
            _ => path == null ? null : await CallSelectedBridgeAsync(path, args)
        };
    }

    public Task<JsonDocument?> ReadResourceAsync(string? uri)
    {
        var path = uri switch
        {
            "unity://health" => "health",
            "unity://play-state" => "play-state",
            "unity://scenes" => "scenes",
            "unity://logs/recent" => "logs?limit=100",
            _ => null
        };

        return path == null ? Task.FromResult<JsonDocument?>(null) : ReadResourceFromSelectedBridgeAsync(path);
    }

    private async Task<JsonDocument?> CallSelectedBridgeAsync(string path, JsonObject args, CancellationToken cancellationToken = default)
    {
        var endpoint = await ResolveRequiredEndpointAsync(JsonArgs.TryGetString(args, "projectPath"));
        return await GetBridgeJsonAsync(endpoint, path, cancellationToken);
    }

    private async Task<JsonDocument?> ReadResourceFromSelectedBridgeAsync(string path, CancellationToken cancellationToken = default)
    {
        var endpoint = await ResolveRequiredEndpointAsync(null);
        return await GetBridgeJsonAsync(endpoint, path, cancellationToken);
    }

    private async Task<BridgeEndpoint> ResolveRequiredEndpointAsync(string? projectPath)
    {
        var resolution = await bridgeDirectory.ResolveAsync(projectPath);
        if (resolution.Endpoint != null)
        {
            return resolution.Endpoint;
        }

        throw new InvalidOperationException(BuildBridgeResolutionError(resolution));
    }

    private static string BuildBridgeResolutionError(BridgeEndpointResolution resolution)
    {
        var endpoints = resolution.Endpoints.Select(endpoint => new
        {
            endpoint.ProjectPath,
            endpoint.BaseUrl,
            endpoint.ProjectName,
            endpoint.UnityVersion
        });
        return JsonSerializer.Serialize(new
        {
            error = "bridge_not_resolved",
            message = resolution.Error,
            requestedProjectPath = resolution.RequestedProjectPath,
            bridges = endpoints
        });
    }

    private async Task<JsonDocument?> GetBridgeJsonAsync(BridgeEndpoint endpoint, string path, CancellationToken cancellationToken = default)
    {
        return await GetBridgeJsonAsync(endpoint.BaseUrl, path, cancellationToken);
    }

    private async Task<JsonDocument?> GetBridgeJsonAsync(string baseUrl, string path, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(BuildBridgeUri(baseUrl, path), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Unity bridge returned {(int)response.StatusCode}: {body}");
        }

        return JsonDocument.Parse(body);
    }

    private static string BuildBridgeUri(string baseUrl, string path)
    {
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private async Task<object> GetBridgeStatusAsync(JsonObject args)
    {
        var startedAt = DateTime.UtcNow;
        var endpoints = await bridgeDirectory.GetEndpointsAsync(forceRefresh: true);
        var projectPath = JsonArgs.TryGetString(args, "projectPath");
        var resolution = string.IsNullOrWhiteSpace(projectPath)
            ? null
            : await bridgeDirectory.ResolveAsync(projectPath);
        return new
        {
            reachable = endpoints.Count > 0,
            bridgeUrl = options.BridgeBaseUrl,
            bridgeHost = options.BridgeHost,
            portStart = options.BridgePortStart,
            portCount = options.BridgePortCount,
            elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
            message = endpoints.Count > 0
                ? "Unity Bridge discovery found reachable bridge endpoints."
                : "No Unity Bridge is reachable in the configured port range. Start it in Unity via MCP/启动桥接服务.",
            selected = resolution == null || resolution.Endpoint == null
                ? null
                : new
                {
                    bridgeUrl = resolution.Endpoint.BaseUrl,
                    resolution.Endpoint.Port,
                    resolution.Endpoint.ProjectPath,
                    resolution.Endpoint.ProjectName,
                    resolution.Endpoint.UnityVersion
                },
            selectionError = resolution?.Error,
            bridges = endpoints.Select(endpoint => new
            {
                bridgeUrl = endpoint.BaseUrl,
                endpoint.Port,
                endpoint.ProjectPath,
                endpoint.ProjectName,
                endpoint.UnityVersion,
                projectInfo = endpoint.ProjectInfo
            }).ToArray()
        };
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

    private static string? TryReadString(JsonNode? node, string key)
    {
        return node is JsonObject obj
            && obj.TryGetPropertyValue(key, out var value)
            && value != null
            ? value.GetValue<string>()
            : null;
    }

    private static string BuildObjectsPath(JsonObject args)
    {
        var query = new QueryStringBuilder();
        query.Add("name", JsonArgs.TryGetString(args, "name"));
        query.Add("component", JsonArgs.TryGetString(args, "component"));
        query.Add("activeOnly", JsonArgs.TryGetBool(args, "activeOnly"));
        query.Add("limit", JsonArgs.TryGetInt(args, "limit"));
        return "objects" + query;
    }

    private static string BuildFindScenesPath(JsonObject args)
    {
        var query = new QueryStringBuilder();
        query.Add("name", JsonArgs.TryGetString(args, "name"));
        query.Add("path", JsonArgs.TryGetString(args, "path"));
        query.Add("limit", JsonArgs.TryGetInt(args, "limit"));
        return "find-scenes" + query;
    }

    private static string BuildSelectScenePath(JsonObject args)
    {
        var path = JsonArgs.TryGetString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("unity_select_scene requires 'path'.");
        }

        var query = new QueryStringBuilder();
        query.Add("path", path);
        return "select-scene" + query;
    }

    private static string BuildObjectPath(JsonObject args)
    {
        var id = JsonArgs.TryGetInt(args, "id") ?? JsonArgs.TryGetInt(args, "instanceId");
        if (id == null)
        {
            throw new ArgumentException("unity_get_object requires 'id' or 'instanceId'.");
        }

        return "object?id=" + Uri.EscapeDataString(id.Value.ToString());
    }

    private static string BuildObjectScriptsPath(JsonObject args)
    {
        var id = JsonArgs.TryGetInt(args, "id") ?? JsonArgs.TryGetInt(args, "instanceId");
        if (id == null)
        {
            throw new ArgumentException("unity_get_object_scripts requires 'id' or 'instanceId'.");
        }

        var query = new QueryStringBuilder();
        query.Add("id", id);
        query.Add("script", JsonArgs.TryGetString(args, "script"));
        query.Add("limit", JsonArgs.TryGetInt(args, "limit"));
        return "object-scripts" + query;
    }

    private static string BuildLogsPath(JsonObject args)
    {
        var query = new QueryStringBuilder();
        query.Add("limit", JsonArgs.TryGetInt(args, "limit"));
        query.Add("types", JsonArgs.TryGetString(args, "types"));
        return "logs" + query;
    }

    private static string BuildTypedLogsPath(JsonObject args, string types)
    {
        var query = new QueryStringBuilder();
        query.Add("limit", JsonArgs.TryGetInt(args, "limit"));
        query.Add("types", types);
        return "logs" + query;
    }
}
