var options = UnityMcpOptions.FromEnvironment();
using var http = new HttpClient
{
    Timeout = TimeSpan.FromMinutes(10)
};

var bridgeDirectory = new UnityBridgeDirectory(http, options);
var bridgeClient = new UnityBridgeClient(http, options, bridgeDirectory);
var server = new McpServer(bridgeClient);
await server.RunAsync();
