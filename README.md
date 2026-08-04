# Unity MCP

这个项目让 AI Agent 可以通过 MCP 读取 Unity Editor 和 Play Mode 的实时数据。

项目分成两部分：

- `unity/UnityMcpBridge`：Unity Editor 包，负责在本机启动 HTTP 桥接服务。
- `server/UnityMcpServer`：stdio MCP 服务，负责把 MCP 工具调用转发给 Unity 桥接服务。

## 文案约定

- 暴露给 AI 读取的 MCP 工具描述、资源描述、参数说明使用英文。
- 暴露给用户看的 Unity 菜单、Unity 日志、README 和必要代码注释使用中文。
- JSON 字段名、工具名、资源 URI 保持英文，便于模型稳定调用。

## 当前能力

当前提供这些 MCP 工具：

- `unity_health`
- `unity_bridge_status`
- `unity_get_project_info`
- `unity_get_play_state`
- `unity_get_compile_state`
- `unity_request_script_compile`
- `unity_request_script_compile_and_wait`
- `unity_wait_for_compile_complete`
- `unity_list_scenes`
- `unity_find_scenes`
- `unity_select_scene`
- `unity_query_objects`
- `unity_get_object`
- `unity_get_object_scripts`
- `unity_invoke_component_method`
- `unity_capture_image`
- `unity_get_logs`
- `unity_get_error_logs`
- `unity_get_warning_logs`
- `unity_enter_play_mode`
- `unity_stop_play_mode`
- `unity_execute_menu_item`

`unity_get_object_scripts` 用于查询某个 GameObject 上挂载的脚本，以及脚本暴露在 Inspector 中的序列化字段。参数：

- `id`：Unity GameObject 实例 ID。
- `script`：可选，按脚本名过滤，例如 `PlayerController`。
- `limit`：可选，每个脚本最多返回的字段数量，默认 200，最大 1000。

它也会返回脚本中用 `[UnityMcpCallable]` 显式开放给 Agent 的方法签名。调用方法使用 `unity_invoke_component_method`：

```json
{
  "projectPath": "E:/Project/MyGame",
  "id": 12345,
  "component": "PlayerController",
  "method": "Respawn",
  "arguments": [3, true]
}
```

脚本方法必须是 `public` 实例方法，并显式添加 Attribute：

```csharp
using UnityMcpBridge;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [UnityMcpCallable]
    public bool Respawn(int checkpoint, bool restoreHealth)
    {
        return true;
    }

    [UnityMcpCallable(AllowInEditMode = true)]
    public void MoveForAuthoring(Vector3 position)
    {
        transform.position = position;
    }
}
```

默认只允许 Play Mode 调用。`AllowInEditMode` 必须由方法作者显式开启。当前支持同步方法、基础类型、枚举、Unity 可序列化对象/结构体，以及通过实例 ID 传入的 `UnityEngine.Object`；不支持重载、泛型、`ref`/`out`、数组、协程和 `Task`。

## 截图

`unity_capture_image` 会直接返回 MCP `image` content。它不依赖具体 UI 框架，支持这些模式：

- `provider`：调用目标 GameObject 上实现 `IUnityMcpImageProvider` 的项目适配器，需要 Play Mode。
- `camera`：渲染目标 GameObject 上的 Camera，可在 Edit Mode 使用。
- `game_view`：截取当前 Game View，需要 Play Mode。

Provider 调用示例：

```json
{
  "projectPath": "E:/Project/MyGame",
  "mode": "provider",
  "id": 12345,
  "providerComponentInstanceId": 23456,
  "captureName": "default",
  "width": 1024,
  "height": 1024,
  "format": "png",
  "options": {}
}
```

`IUnityMcpImageProvider` 定义在 `UnityMcpBridge.Runtime`。项目适配器返回 PNG 字节、MIME 类型和尺寸；Bridge 负责主线程调度、逐 Editor update 推进协程、超时和大小校验，MCP Server 负责生成标准图片内容。第三方框架代码应只存在于项目适配器中，不应加入 Bridge 核心。

## 代码结构

MCP Server：

- `Program.cs`：启动入口。
- `McpServer.cs`：stdio JSON-RPC/MCP 协议处理。
- `UnityBridgeClient.cs`：转发 MCP 工具和资源读取到 Unity Bridge。
- `UnityBridgeClient.Compilation.cs`：编译触发、跨域重载等待和日志读取。
- `Catalogs.cs`：MCP tools/resources 元数据。
- `Schema.cs`：MCP input schema 构造。
- `JsonArgs.cs`：读取工具参数。
- `QueryStringBuilder.cs`：构造 Unity Bridge 查询参数。

Unity Bridge：

- `UnityMcpBridgeServer.cs`：HTTP 服务、菜单、日志捕获、请求路由。
- `UnityMcpBridgeQueries.cs`：基础状态、场景、GameObject 查询。
- `UnityMcpBridgeScripts.cs`：脚本和 Inspector 字段查询。
- `UnityMcpBridgeScenes.cs`：场景资产查找、场景选择。
- `UnityMcpBridgePlay.cs`：进入和退出播放模式。
- `UnityMcpBridgeDtos.cs`：返回 DTO。
- `SimpleJson.cs`：无外部依赖 JSON 序列化。

## 安装 Unity 桥接包

1. 打开你的 Unity 项目。
2. 打开 **Window > Package Manager**。
3. 点击 **+ > Add package from git URL...**。
4. 输入`https://github.com/Hanihahaha/unitymcp.git?path=/unity/UnityMcpBridge`。
5. 通过 **MCP > 启动桥接服务** 启动。

桥接服务默认监听：

```text
http://127.0.0.1:8765/
```

可用的本地接口：

```text
GET /health
GET /project-info
GET /play-state
GET /compile-state
GET /request-script-compile
GET /wait-compile-complete?timeoutMs=120000
GET /scenes
GET /find-scenes?name=Sample&limit=20
GET /select-scene?path=Assets/Scenes/SampleScene.unity
GET /objects?name=Player&component=Rigidbody&limit=20
GET /object?id=12345
GET /object-scripts?id=12345&script=PlayerController&limit=200
POST /invoke-component-method
POST /capture-image
GET /logs?limit=100
GET /logs?types=Error,Assert,Exception&limit=100
GET /logs?types=Warning&limit=100
GET /enter-play-mode
GET /stop-play-mode
GET /execute-menu-item?path=Tools/My%20Action
```

## 构建 MCP 服务

```powershell
dotnet build .\server\UnityMcpServer\UnityMcpServer.csproj
```

手动运行：

```powershell
dotnet run --project .\server\UnityMcpServer\UnityMcpServer.csproj
```

默认连接 `http://127.0.0.1:8765/`。可以用环境变量覆盖：

```powershell
$env:UNITY_MCP_BRIDGE_URL = "http://127.0.0.1:8765"
```

可选自动编译配置：

```powershell
$env:UNITY_MCP_UNITY_PATH = "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe"
```

编译类工具调用时需要传入 `projectPath`。MCP Server 会检查当前 Bridge 连接的 Unity 项目是否匹配该路径：匹配时使用运行中的 Editor 刷新并等待编译；Bridge 未启动或项目路径不匹配时，使用 `UNITY_MCP_UNITY_PATH` 指向的 Unity batchmode 编译本次传入的 `projectPath`。也可以在工具参数里传 `unityPath` 覆盖默认 Unity 路径。

额外可选项：

- `UNITY_MCP_UNITY_COMPILE_TIMEOUT_MS`：Unity batchmode 编译超时，默认 `600000`。
- `UNITY_MCP_UNITY_LOG_PATH`：batchmode 编译日志路径，默认写到目标项目的 `Logs/unity-mcp-batch-compile.log`。

## MCP 客户端配置示例

请把路径改成你机器上的绝对路径：

```json
{
  "mcpServers": {
    "unity": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "E:\\Project\\unitymcp\\server\\UnityMcpServer\\UnityMcpServer.csproj"
      ],
      "env": {
        "UNITY_MCP_BRIDGE_URL": "http://127.0.0.1:8765",
        "UNITY_MCP_UNITY_PATH": "C:\\Program Files\\Unity\\Hub\\Editor\\2022.3.62f1\\Editor\\Unity.exe"
      }
    }
  }
}
```

## Codex配置
```powershell

codex mcp add unity --env UNITY_MCP_BRIDGE_URL=http://127.0.0.1:8765 --env UNITY_MCP_UNITY_PATH="C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe" -- dotnet run --project "你的路径\UnityMcpServer\UnityMcpServer.csproj"

```

## 说明

- 桥接服务只绑定 `127.0.0.1`。
- `unity_bridge_status` 在 Unity Bridge 未启动时也会返回结构化结果，Agent 可用它第一时间判断桥接服务是否在线。
- Unity API 访问会调度到 Unity 主线程执行。
- `unity_get_project_info` 可以确认当前连接的 Unity Editor 打开的是哪个项目。
- `unity_request_script_compile` 会调用 Unity 的资源刷新流程，适合外部 IDE 或工具修改脚本后主动触发 Unity 扫描并自动编译。
- AI 触发编译时推荐使用 `unity_request_script_compile_and_wait`，它在 MCP Server 进程中轮询 Unity Bridge，可以容忍 Unity 域重载导致桥接服务短暂断开，并在 Unity 空闲后返回最近日志。
- `unity_request_script_compile_and_wait` 与 `unity_wait_for_compile_complete` 需要传入 `projectPath`。如果 Bridge 离线或连接到不同项目，会调用 Unity batchmode 编译该 `projectPath`，并返回 batchmode 日志尾部。
- `unity_request_script_compile_and_wait` 默认会等待一个短暂的稳定空闲窗口，避免 Unity 刚收到刷新请求但尚未进入编译状态时过早返回。
- `unity_wait_for_compile_complete` 会等待 Unity 编译/资源刷新结束，现在同样在 MCP Server 进程中轮询，避免 Agent 用固定 sleep 猜测完成时间。
- `unity_get_error_logs` 会返回 Error、Assert 和 Exception 日志；`unity_get_warning_logs` 会返回 Warning 日志。
- `unity_find_scenes` 可以按名称或路径查找项目中的场景资产；`unity_select_scene` 会按项目相对路径打开场景。
- 对象查询默认限制返回数量，避免一次返回过多内容。
- Inspector 字段查询只读取 Unity 序列化系统可见的字段，不会调用属性 getter。
- `unity_get_object_scripts` 会列出 `[UnityMcpCallable]` 方法，`unity_invoke_component_method` 会在 Unity 主线程调用其中的唯一 public 实例方法。
- `unity_get_object_scripts` 的 `isImageProvider` 表示脚本是否可作为 `unity_capture_image` 的 provider。
- `unity_capture_image` 当前输出 PNG，单边最大 4096 像素，编码后最大 16 MB；图片 Base64 只放在 MCP image content 中，不会复制到 structured content。
- 进入 Play Mode 或脚本域重载后 GameObject 实例 ID 可能变化，调用前应重新查询对象。
- `unity_enter_play_mode` 会使用当前激活场景进入播放模式。
- `unity_stop_play_mode` 会在 Unity 正在播放或切换播放状态时请求退出播放模式；如果当前没有播放，会返回成功并说明无需退出。
- `unity_execute_menu_item` 会按菜单路径调用 Unity Editor 主菜单项，例如 `Tools/My Action`。
