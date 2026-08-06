# Unity MCP Tool Catalog

All tools except `unity_bridge_status` require an absolute `projectPath`. Use tools rather than MCP resources because tools can select the intended Unity project explicitly.

## Connection and state

| Tool | Call when | Key inputs and notes |
| --- | --- | --- |
| `unity_bridge_status` | Start a live workflow or diagnose connectivity | Optional `projectPath`; returns `reachable`, discovered `bridges`, `selected`, and `selectionError` without failing offline. |
| `unity_health` | Confirm the selected bridge and Editor respond | `projectPath`; returns Editor/bridge metadata. |
| `unity_get_project_info` | Verify project identity, Unity version, Assets path, or active scene | `projectPath`. Use before state-changing calls. |
| `unity_get_play_state` | Check play/pause/frame/time state | `projectPath`. |
| `unity_get_compile_state` | Check compilation, asset update, or play transition state | `projectPath`. Prefer wait tools over manual polling. |

## Compilation and logs

| Tool | Call when | Key inputs and notes |
| --- | --- | --- |
| `unity_request_script_compile_and_wait` | Validate a completed batch of external `.cs` edits | `projectPath`; optional `timeoutMs`, `logLimit`, `unityPath`. Preferred compile tool. Stops Play Mode if needed, survives domain reload, and falls back to batch mode if the target bridge is unavailable. |
| `unity_request_script_compile` | Trigger compilation only when another step will intentionally continue asynchronously | `projectPath`; returns after requesting refresh. Usually prefer compile-and-wait. |
| `unity_wait_for_compile_complete` | Compilation/import was already triggered elsewhere | `projectPath`; optional `timeoutMs`, `requireObservedCompile`. Do not use it to request a new compile. |
| `unity_get_error_logs` | Diagnose failures or verify no Unity errors | `projectPath`, optional `limit` (max 500). Includes Error, Assert, Exception. |
| `unity_get_warning_logs` | Investigate warnings relevant to the task | `projectPath`, optional `limit` (max 500). |
| `unity_get_logs` | Inspect broader or filtered recent Console output | `projectPath`, optional `limit`, `types` such as `Error,Assert,Exception`. Logs cover the period since the bridge package loaded. |

Compile options are normally unnecessary. Defaults: `timeoutMs=120000`, `pollIntervalMs=500`, `stopPlayModeTimeoutMs=30000`, and a reconnect wait before batch fallback. Raise timeouts only for a known slow project.

## Scenes and objects

| Tool | Call when | Key inputs and notes |
| --- | --- | --- |
| `unity_list_scenes` | Read loaded scenes and identify the active scene | `projectPath`. |
| `unity_find_scenes` | Locate scene assets before opening one | `projectPath`; optional case-insensitive `name`, `path`, `limit`. |
| `unity_select_scene` | Open a user-requested scene | `projectPath`, project-relative `path`, for example `Assets/Scenes/Main.unity`. Changes Editor state. |
| `unity_query_objects` | Find GameObjects by partial name or component type | `projectPath`; optional `name`, `component`, `activeOnly` (default true), `limit`. Use a narrow filter. |
| `unity_get_object` | Inspect a selected object's transform and components | `projectPath`, GameObject instance `id`. |
| `unity_get_object_scripts` | Read attached MonoBehaviours, Inspector-visible fields, callable methods, or image provider support | `projectPath`, GameObject `id`; optional `script`, `limit` per script. Does not call property getters. |

Instance IDs are session state, not durable identifiers. Re-query after Play Mode transitions, script compilation, or domain reload.

## Runtime actions and images

| Tool | Call when | Key inputs and notes |
| --- | --- | --- |
| `unity_enter_play_mode` | Runtime behavior must be exercised | `projectPath`; uses the active scene. Wait for state to settle, then re-query objects. |
| `unity_stop_play_mode` | Finish temporary runtime testing | `projectPath`; succeeds as a no-op if already stopped. |
| `unity_invoke_component_method` | Invoke a method explicitly exposed by the project | `projectPath`, fresh GameObject `id`, `component`, exact `method`; optional `componentInstanceId`, ordered JSON `arguments`. Discover it first with `unity_get_object_scripts`. |
| `unity_capture_image` | Inspect actual rendered or project-provided visual output | `projectPath`, `mode`; optional size (max 4096 each side), `timeoutMs` (max 60000). PNG only. |
| `unity_execute_menu_item` | Run a known Unity main-menu command | `projectPath`, exact `menuPath`, for example `Tools/My Action`. Do not use guessed paths. |

Capture modes:

- `game_view`: Capture the current Game View; requires Play Mode and no object ID.
- `camera`: Render a Camera on the GameObject identified by `id`; works in Edit Mode.
- `provider`: Call an `IUnityMcpImageProvider` on the GameObject identified by `id`; requires Play Mode. Use optional `providerComponentInstanceId`, `captureName`, and provider-specific `options` when discovered or documented.

Callable arguments support null, primitives, enums, Unity-serializable objects/structs, and `UnityEngine.Object` references by instance ID. They do not support overloaded exposed methods, generics, `ref`/`out`, arrays, coroutines, or `Task` methods.

## MCP resources

The server advertises `unity://health`, `unity://play-state`, `unity://scenes`, and `unity://logs/recent`, but do not use them in the current implementation. Resource reads cannot supply `projectPath`, while bridge resolution requires it, so they fail project selection. Use `unity_health`, `unity_get_play_state`, `unity_list_scenes`, and `unity_get_logs` instead.
