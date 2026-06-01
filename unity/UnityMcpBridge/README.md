# Unity MCP 桥接包

这个包会在 Unity Editor 内启动一个本机 HTTP 桥接服务，让外部 MCP 服务读取 Unity 运行时数据。

## 安装

在 Unity 中打开 **Window > Package Manager > + > Add package from disk...**，然后选择当前文件夹里的 `package.json`。

安装后，通过 **MCP > 启动桥接服务** 启动。

桥接服务默认监听 `http://127.0.0.1:8765/`。

## 查询当前项目

使用 `/project-info` 可以查询当前 Unity Editor 打开的项目名称、项目路径、`Assets` 路径、Unity 版本和当前激活场景。

## 查询编译状态

使用 `/compile-state` 可以查询 Unity 是否正在编译、刷新资源、播放或切换播放状态。

使用 `/request-script-compile` 可以请求 Unity 刷新资源并触发脚本编译，适合外部 IDE 或工具修改 `.cs` 文件后调用。

使用 `/wait-compile-complete?timeoutMs=120000` 可以等待 Unity 编译或资源刷新结束。

## 查询日志

使用 `/logs?limit=100` 可以查询最近捕获的 Unity 日志。

使用 `/logs?types=Error,Assert,Exception&limit=100` 可以只查询错误、断言和异常日志。

使用 `/logs?types=Warning&limit=100` 可以只查询警告日志。

## 查询脚本字段

使用 `/object-scripts?id=12345` 可以查询某个 GameObject 上挂载的脚本，以及这些脚本暴露在 Inspector 中的序列化字段。

可选参数：

- `script`：按脚本名过滤，例如 `PlayerController`。
- `limit`：每个脚本最多返回的字段数量，默认 200，最大 1000。

## 场景与播放模式

使用 `/find-scenes?name=Sample&limit=20` 可以按名称或路径查找项目中的场景资产。

使用 `/select-scene?path=Assets/Scenes/SampleScene.unity` 可以打开指定场景并设为当前场景。

使用 `/enter-play-mode` 可以让 Unity 使用当前激活场景进入播放模式。

使用 `/stop-play-mode` 可以请求退出 Unity 播放模式。
