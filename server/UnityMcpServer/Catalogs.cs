internal static class ResourceCatalog
{
    public static readonly object[] All =
    [
        new
        {
            uri = "unity://health",
            name = "Unity bridge health",
            description = "Current Unity Editor and bridge status.",
            mimeType = "application/json"
        },
        new
        {
            uri = "unity://play-state",
            name = "Unity play state",
            description = "Current editor play mode, pause, frame, and time state.",
            mimeType = "application/json"
        },
        new
        {
            uri = "unity://scenes",
            name = "Unity scenes",
            description = "Loaded scenes and active scene metadata.",
            mimeType = "application/json"
        },
        new
        {
            uri = "unity://logs/recent",
            name = "Unity recent logs",
            description = "Recent logs captured by the bridge.",
            mimeType = "application/json"
        }
    ];
}

internal static class ToolCatalog
{
    private static readonly object ProjectPathProperty = Schema.String("Unity project path. Required for all project-specific Unity tools.");

    private static object ProjectSchema(Dictionary<string, object>? properties = null, string[]? required = null)
    {
        var merged = properties == null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(properties);
        merged["projectPath"] = ProjectPathProperty;
        var requiredList = new List<string> { "projectPath" };
        if (required != null)
        {
            requiredList.AddRange(required.Where(item => item != "projectPath"));
        }

        return Schema.Object(merged, requiredList.ToArray());
    }

    public static readonly object[] All =
    [
        new
        {
            name = "unity_bridge_status",
            description = "Check whether the Unity Bridge HTTP endpoint is reachable. This tool returns a structured offline status instead of failing when Unity Bridge is not running.",
            inputSchema = Schema.Object(new Dictionary<string, object>
            {
                ["projectPath"] = Schema.String("Optional Unity project path to select and report a specific discovered bridge.")
            })
        },
        new
        {
            name = "unity_health",
            description = "Check whether the Unity bridge is reachable and return Unity Editor metadata.",
            inputSchema = ProjectSchema()
        },
        new
        {
            name = "unity_get_project_info",
            description = "Get the Unity project currently opened by the connected Editor, including project path, Assets path, Unity version, and active scene.",
            inputSchema = ProjectSchema()
        },
        new
        {
            name = "unity_get_play_state",
            description = "Get current Unity Editor play, pause, compile, frame, and time state.",
            inputSchema = ProjectSchema()
        },
        new
        {
            name = "unity_get_compile_state",
            description = "Get whether Unity is compiling, updating assets, playing, or changing play state.",
            inputSchema = ProjectSchema()
        },
        new
        {
            name = "unity_request_script_compile",
            description = "Ask Unity to refresh assets and trigger script compilation after external script edits. If the connected or requested Unity project is in Play mode, exit Play mode before requesting compilation.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["projectPath"] = Schema.String("Unity project path to compile. Required."),
                ["unityPath"] = Schema.String("Optional Unity executable path override. Defaults to UNITY_MCP_UNITY_PATH."),
                ["bridgeReconnectTimeoutMs"] = Schema.Integer("Maximum time to wait for the Unity Bridge to reconnect before falling back to batchmode compile. Defaults to 120000, max 600000. Use 0 to disable."),
                ["pollIntervalMs"] = Schema.Integer("Polling interval in milliseconds while waiting for Play mode to exit. Defaults to 500, max 5000."),
                ["stopPlayModeTimeoutMs"] = Schema.Integer("Maximum time to wait for Unity to exit Play mode before compiling. Defaults to 30000, max 600000.")
            })
        },
        new
        {
            name = "unity_request_script_compile_and_wait",
            description = "Compile the requested Unity project. If the bridge is online and connected to projectPath, exit Play mode if needed, then refresh assets through the running Editor and wait. If Unity is not open or the bridge is connected to a different project, run Unity in batchmode for projectPath.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["projectPath"] = Schema.String("Unity project path to compile. Required."),
                ["unityPath"] = Schema.String("Optional Unity executable path override. Defaults to UNITY_MCP_UNITY_PATH."),
                ["bridgeReconnectTimeoutMs"] = Schema.Integer("Maximum time to wait for the Unity Bridge to reconnect before falling back to batchmode compile. Defaults to 120000, max 600000. Use 0 to disable."),
                ["timeoutMs"] = Schema.Integer("Maximum time to wait in milliseconds. Defaults to 120000, max 600000."),
                ["pollIntervalMs"] = Schema.Integer("Polling interval in milliseconds while waiting. Defaults to 500, max 5000."),
                ["stopPlayModeTimeoutMs"] = Schema.Integer("Maximum time to wait for Unity to exit Play mode before compiling. Defaults to 30000, max 600000."),
                ["settleMs"] = Schema.Integer("Idle settle window in milliseconds before completing if no busy state is observed. Defaults to 1500 for this tool, max 10000."),
                ["requireObservedCompile"] = Schema.Boolean("When true, wait until a busy compile/update state has been observed before completing. Defaults to false."),
                ["logLimit"] = Schema.Integer("Maximum recent Unity log entries to return after waiting. Defaults to 100, max 500.")
            })
        },
        new
        {
            name = "unity_wait_for_compile_complete",
            description = "Wait for compilation of the requested Unity project. If the bridge is online and connected to projectPath, exit Play mode if needed, then wait through the running Editor. If Unity is not open or the bridge is connected to a different project, run Unity in batchmode for projectPath.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["projectPath"] = Schema.String("Unity project path to compile or wait for. Required."),
                ["unityPath"] = Schema.String("Optional Unity executable path override. Defaults to UNITY_MCP_UNITY_PATH."),
                ["bridgeReconnectTimeoutMs"] = Schema.Integer("Maximum time to wait for the Unity Bridge to reconnect before falling back to batchmode compile. Defaults to 120000, max 600000. Use 0 to disable."),
                ["timeoutMs"] = Schema.Integer("Maximum time to wait in milliseconds. Defaults to 120000, max 600000."),
                ["pollIntervalMs"] = Schema.Integer("Polling interval in milliseconds while waiting. Defaults to 500, max 5000."),
                ["stopPlayModeTimeoutMs"] = Schema.Integer("Maximum time to wait for Unity to exit Play mode before waiting. Defaults to 30000, max 600000."),
                ["settleMs"] = Schema.Integer("Idle settle window in milliseconds before completing if no busy state is observed. Defaults to 0 for this tool, max 10000."),
                ["requireObservedCompile"] = Schema.Boolean("When true, wait until a busy compile/update state has been observed before completing. Defaults to false.")
            })
        },
        new
        {
            name = "unity_list_scenes",
            description = "List loaded Unity scenes and identify the active scene.",
            inputSchema = ProjectSchema()
        },
        new
        {
            name = "unity_find_scenes",
            description = "Find scene assets in the Unity project by optional name or path substring.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["name"] = Schema.String("Optional case-insensitive scene name substring."),
                ["path"] = Schema.String("Optional case-insensitive asset path substring."),
                ["limit"] = Schema.Integer("Maximum number of scene assets to return. Defaults to 100, max 1000.")
            })
        },
        new
        {
            name = "unity_select_scene",
            description = "Open a Unity scene asset by project-relative path, making it the active scene.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["path"] = Schema.String("Unity scene asset path, such as Assets/Scenes/SampleScene.unity.")
            }, ["path"])
        },
        new
        {
            name = "unity_query_objects",
            description = "Query scene GameObjects by name or component type.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["name"] = Schema.String("Optional case-insensitive name substring."),
                ["component"] = Schema.String("Optional Unity component type name, such as Camera or Rigidbody."),
                ["activeOnly"] = Schema.Boolean("When true, only return active GameObjects. Defaults to true."),
                ["limit"] = Schema.Integer("Maximum number of objects to return. Defaults to 100, max 1000.")
            })
        },
        new
        {
            name = "unity_get_object",
            description = "Get detailed transform and component data for a scene GameObject by instance id.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["id"] = Schema.Integer("Unity GameObject instance id.")
            }, ["id"])
        },
        new
        {
            name = "unity_get_object_scripts",
            description = "Get MonoBehaviour scripts attached to a GameObject, their Inspector-visible serialized fields, methods exposed with [UnityMcpCallable], and whether each script implements IUnityMcpImageProvider.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["id"] = Schema.Integer("Unity GameObject instance id."),
                ["script"] = Schema.String("Optional script name filter, such as PlayerController."),
                ["limit"] = Schema.Integer("Maximum Inspector fields to return per script. Defaults to 200, max 1000.")
            }, ["id"])
        },
        new
        {
            name = "unity_invoke_component_method",
            description = "Invoke a public instance method marked with [UnityMcpCallable] on a MonoBehaviour attached to a scene GameObject. Methods are Play Mode only unless their attribute explicitly allows Edit Mode.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["id"] = Schema.Integer("Unity GameObject instance id. Query the object again after entering Play Mode because instance ids may change."),
                ["component"] = Schema.String("Component type name or fully qualified type name, such as PlayerController or Game.PlayerController."),
                ["componentInstanceId"] = Schema.Integer("Optional component instance id used to disambiguate multiple components of the same type on one GameObject."),
                ["method"] = Schema.String("Exact name of a public instance method marked with [UnityMcpCallable]. Exposed overloads are rejected."),
                ["arguments"] = Schema.Array(Schema.Any(), "Ordered JSON arguments. Supports null, primitives, enums, serializable objects and structs, and UnityEngine.Object references by instance id.")
            }, ["id", "component", "method"])
        },
        new
        {
            name = "unity_capture_image",
            description = "Capture an image from Unity and return it as MCP image content. Modes: provider uses a project-defined IUnityMcpImageProvider on a GameObject; camera renders a Camera component; game_view captures the current Game View. provider and game_view require Play Mode.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["mode"] = Schema.String("Capture mode: provider, camera, or game_view. Defaults to provider."),
                ["id"] = Schema.Integer("GameObject instance id. Required for provider and camera modes."),
                ["providerComponentInstanceId"] = Schema.Integer("Optional MonoBehaviour instance id used to select one IUnityMcpImageProvider when multiple providers are attached."),
                ["captureName"] = Schema.String("Optional provider-defined capture operation name. Defaults to default."),
                ["width"] = Schema.Integer("Optional output width in pixels. Defaults to the source width, max 4096."),
                ["height"] = Schema.Integer("Optional output height in pixels. Defaults to the source height, max 4096."),
                ["format"] = Schema.String("Image format. Currently only png is supported."),
                ["options"] = Schema.Any("Optional provider-specific JSON value. It is passed to IUnityMcpImageProvider as OptionsJson."),
                ["timeoutMs"] = Schema.Integer("Capture timeout in milliseconds. Defaults to 10000, max 60000.")
            })
        },
        new
        {
            name = "unity_get_logs",
            description = "Get recent Unity logs captured since the bridge package loaded.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["limit"] = Schema.Integer("Maximum number of log entries to return. Defaults to 100, max 500."),
                ["types"] = Schema.String("Optional comma-separated Unity LogType filter, such as Error,Assert,Exception or Warning.")
            })
        },
        new
        {
            name = "unity_get_error_logs",
            description = "Get recent Unity error logs, including Error, Assert, and Exception entries.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["limit"] = Schema.Integer("Maximum number of error log entries to return. Defaults to 100, max 500.")
            })
        },
        new
        {
            name = "unity_get_warning_logs",
            description = "Get recent Unity warning logs.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["limit"] = Schema.Integer("Maximum number of warning log entries to return. Defaults to 100, max 500.")
            })
        },
        new
        {
            name = "unity_enter_play_mode",
            description = "Enter Unity Play mode using the currently active scene.",
            inputSchema = ProjectSchema()
        },
        new
        {
            name = "unity_stop_play_mode",
            description = "Exit Unity Play mode if the Editor is currently playing or changing play state.",
            inputSchema = ProjectSchema()
        },
        new
        {
            name = "unity_execute_menu_item",
            description = "Execute a Unity Editor main menu item by its menu path, such as Tools/My Action or MCP/Start Bridge Service. The menu item is invoked on the Unity main thread.",
            inputSchema = ProjectSchema(new Dictionary<string, object>
            {
                ["menuPath"] = Schema.String("Unity Editor menu path to execute, such as Tools/My Action.")
            }, ["menuPath"])
        }
    ];
}
