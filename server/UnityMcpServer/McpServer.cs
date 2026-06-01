using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class McpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly UnityBridgeClient bridgeClient;

    public McpServer(UnityBridgeClient bridgeClient)
    {
        this.bridgeClient = bridgeClient;
    }

    public async Task RunAsync()
    {
        string? line;
        while ((line = await Console.In.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonNode? request = null;
            try
            {
                request = JsonNode.Parse(line);
                if (request is not JsonObject requestObject)
                {
                    await WriteErrorAsync(null, -32600, "Invalid request");
                    continue;
                }

                await HandleRequestAsync(requestObject);
            }
            catch (JsonException ex)
            {
                await WriteErrorAsync(null, -32700, "Parse error", ex.Message);
            }
            catch (Exception ex)
            {
                await WriteErrorAsync(request?["id"], -32603, "Internal error", ex.Message);
            }
        }
    }

    private async Task HandleRequestAsync(JsonObject request)
    {
        var id = request["id"];
        var method = request["method"]?.GetValue<string>();

        switch (method)
        {
            case "initialize":
                await WriteResultAsync(id, new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        tools = new { },
                        resources = new { }
                    },
                    serverInfo = new
                    {
                        name = "unity-mcp",
                        version = "0.1.0"
                    }
                });
                break;

            case "notifications/initialized":
                break;

            case "ping":
                await WriteResultAsync(id, new { });
                break;

            case "tools/list":
                await WriteResultAsync(id, new { tools = ToolCatalog.All });
                break;

            case "tools/call":
                await HandleToolCallAsync(id, request["params"] as JsonObject);
                break;

            case "resources/list":
                await WriteResultAsync(id, new { resources = ResourceCatalog.All });
                break;

            case "resources/read":
                await HandleResourceReadAsync(id, request["params"] as JsonObject);
                break;

            default:
                await WriteErrorAsync(id, -32601, "Method not found", method ?? "<missing>");
                break;
        }
    }

    private async Task HandleToolCallAsync(JsonNode? id, JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>();
        var args = parameters?["arguments"] as JsonObject ?? new JsonObject();

        if (string.IsNullOrWhiteSpace(name))
        {
            await WriteErrorAsync(id, -32602, "Invalid params", "Missing tool name.");
            return;
        }

        var result = await bridgeClient.CallToolAsync(name, args);
        if (result == null)
        {
            await WriteErrorAsync(id, -32602, "Unknown tool", name);
            return;
        }

        var rawJson = result is JsonDocument document
            ? document.RootElement.GetRawText()
            : JsonSerializer.Serialize(result, JsonOptions);

        await WriteResultAsync(id, new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = rawJson
                }
            },
            structuredContent = JsonNode.Parse(rawJson),
            isError = false
        });
    }

    private async Task HandleResourceReadAsync(JsonNode? id, JsonObject? parameters)
    {
        var uri = parameters?["uri"]?.GetValue<string>();
        var result = await bridgeClient.ReadResourceAsync(uri);
        if (result == null)
        {
            await WriteErrorAsync(id, -32602, "Unknown resource", uri ?? "<missing>");
            return;
        }

        await WriteResultAsync(id, new
        {
            contents = new[]
            {
                new
                {
                    uri,
                    mimeType = "application/json",
                    text = result.RootElement.GetRawText()
                }
            }
        });
    }

    private static async Task WriteResultAsync(JsonNode? id, object result)
    {
        if (id == null)
        {
            return;
        }

        await WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            id,
            result
        });
    }

    private static async Task WriteErrorAsync(JsonNode? id, int code, string message, string? data = null)
    {
        await WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            id,
            error = new
            {
                code,
                message,
                data
            }
        });
    }

    private static async Task WriteMessageAsync(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await Console.Out.WriteLineAsync(json);
        await Console.Out.FlushAsync();
    }
}
