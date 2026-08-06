# Unity MCP Setup and Troubleshooting

Read this file only when tools are unavailable, the bridge is offline, project resolution fails, or setup is part of the request.

## Architecture

Unity MCP has two local processes:

1. `UnityMcpBridge`, a Unity Editor package that serves local HTTP endpoints on `127.0.0.1`.
2. `UnityMcpServer`, a stdio MCP server that discovers bridges, selects one by `projectPath`, and translates MCP calls.

Both are required for live Editor access. Compile tools can launch Unity in batch mode when no matching bridge is online and a Unity executable is configured.

## Install and start the bridge

Install `unity/UnityMcpBridge` from the `unitymcp` repository through Unity Package Manager, either from disk or from the repository's Git URL/package path. In the target Unity Editor, use **MCP > 启动桥接服务**.

The default discovery range is `127.0.0.1:8765` through the next 19 ports, allowing multiple Editors. The bridge binds only to localhost.

## Configure the MCP server

Register the server under the name `unity`. A Windows stdio configuration has this shape:

```json
{
  "mcpServers": {
    "unity": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<repository-root>/server/UnityMcpServer/UnityMcpServer.csproj"
      ],
      "env": {
        "UNITY_MCP_BRIDGE_URL": "http://127.0.0.1:8765",
        "UNITY_MCP_UNITY_PATH": "<unity-editor-installation>/Editor/<version>/Editor/Unity.exe"
      }
    }
  }
}
```

Adjust paths to the local checkout and installed Unity version. `UNITY_MCP_UNITY_PATH` is optional for live calls but required for offline batch compilation unless `unityPath` is passed to a compile tool.

## Environment options

| Variable | Purpose | Default |
| --- | --- | --- |
| `UNITY_MCP_BRIDGE_URL` | Preferred/default bridge URL | `http://127.0.0.1:8765` |
| `UNITY_MCP_BRIDGE_HOST` | Host scanned for bridge instances | `127.0.0.1` |
| `UNITY_MCP_BRIDGE_PORT_START` | First discovery port | `8765` |
| `UNITY_MCP_BRIDGE_PORT_COUNT` | Number of ports to scan, max 100 | `20` |
| `UNITY_MCP_BRIDGE_SCAN_CACHE_MS` | Discovery cache duration | `2000` |
| `UNITY_MCP_UNITY_PATH` | Unity executable for batch compilation | unset |
| `UNITY_MCP_UNITY_COMPILE_TIMEOUT_MS` | Batch compilation timeout | `600000` |
| `UNITY_MCP_UNITY_LOG_PATH` | Batch compile log path | `<project>/Logs/unity-mcp-batch-compile.log` |

## Troubleshoot

- **No Unity tools in the client:** Confirm the MCP server entry is enabled and `dotnet` can run the server project. Restart or reload the MCP client after configuration changes.
- **`reachable: false`:** Open the target Unity project, verify the bridge package compiled, and start it from the MCP menu.
- **Wrong project or `bridge_not_resolved`:** Supply the exact absolute Unity root. Use the `bridges` list from `unity_bridge_status`; do not substitute a different project.
- **Live calls fail after script compilation:** The bridge may be reloading its AppDomain. Use compile-and-wait rather than issuing immediate calls or sleeping.
- **Batch compile is skipped:** Configure `UNITY_MCP_UNITY_PATH` or pass `unityPath`, and confirm `projectPath` is a valid Unity root.
- **Capture fails:** Confirm the required mode, Play Mode requirement, fresh IDs, Camera/provider presence, PNG format, dimensions at most 4096, and timeout at most 60000 ms.
- **Method invocation fails:** Re-query scripts, use the exact exposed method/component, disambiguate duplicate components, enter Play Mode unless allowed in Edit Mode, and avoid stale IDs.
