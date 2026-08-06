# Unity MCP Workflows

Use these sequences as patterns, not as mandatory full checklists. Omit calls that do not contribute evidence for the current request.

## Validate script edits

1. Make all related filesystem edits.
2. Call `unity_bridge_status({ projectPath })` if connection state is not already known.
3. Call `unity_request_script_compile_and_wait({ projectPath, logLimit: 100 })`.
4. Inspect `completed`, `timedOut`, `message`, and returned logs. A completed request can still contain compiler errors.
5. Call `unity_get_error_logs({ projectPath, limit: 100 })` when the returned log excerpt is unclear or runtime errors matter.
6. Fix relevant errors and repeat once per coherent edit batch.

Do not use a fixed sleep after editing scripts. The compile-and-wait tool handles asset refresh, Play Mode exit, domain reload reconnection, and configured batch-mode fallback.

## Inspect a runtime object

1. Verify the project and active scene with `unity_get_project_info` and `unity_list_scenes`.
2. Enter Play Mode only if runtime state is required.
3. After the transition, call `unity_query_objects` with a name or component filter.
4. Call `unity_get_object` for the chosen GameObject.
5. Call `unity_get_object_scripts` only when Inspector fields or exposed behavior are relevant.
6. Read errors or targeted logs after reproducing the behavior.
7. Stop Play Mode after temporary testing.

Never reuse an ID obtained before entering Play Mode.

## Invoke project behavior

1. Query a fresh GameObject ID in the current mode.
2. Call `unity_get_object_scripts` with an optional script filter.
3. Select a method exactly as returned under `callableMethods`; verify `allowInEditMode` when not playing.
4. Use `componentInstanceId` if multiple matching components are attached.
5. Call `unity_invoke_component_method` with arguments in declared parameter order.
6. Verify the returned result, affected object state, logs, or image.

Do not invoke a public method merely because it exists in source. Only `[UnityMcpCallable]` methods returned by the bridge are permitted.

## Perform visual QA

1. Choose the evidence surface:
   - Use `game_view` for the player's current view.
   - Use `camera` for a known Camera without entering Play Mode.
   - Use `provider` for project-specific UI, board, map, or other purpose-built captures.
2. Enter Play Mode for `game_view` or `provider`, then reacquire any required IDs.
3. Capture at a task-appropriate stable size; use PNG.
4. Inspect the image itself, not only metadata. Check framing, blank output, missing assets, overlays, and the specific requested behavior.
5. Pair visual evidence with error logs when rendering may have failed.
6. Stop Play Mode after temporary testing.

## Select and test a scene

1. Call `unity_find_scenes` with a narrow name/path filter.
2. Confirm the returned project-relative path and that switching scenes is within the request.
3. Call `unity_select_scene` with the exact path.
4. Verify the active scene with `unity_get_project_info` or `unity_list_scenes`.
5. Enter Play Mode only when the test requires it.

Do not choose among ambiguous scene matches without more evidence. Scene selection changes the user's Editor context.

## Run an Editor menu command

1. Obtain the exact menu path from the user, project source (for example a `MenuItem` attribute), or trusted documentation.
2. Verify the connected project.
3. Call `unity_execute_menu_item` once.
4. Verify its expected effect through state, object data, logs, generated files, or a capture.

Do not probe guessed menu paths. Do not use `MCP/启动桥接服务` to recover an offline bridge because the MCP request itself cannot reach that Editor.

## Diagnose an offline or wrong-project result

1. Call `unity_bridge_status({ projectPath })`.
2. If `reachable` is false, follow [setup.md](setup.md) and ask the user to start the bridge in Unity when live interaction is required.
3. If bridges are listed but `selected` is null, compare their `projectPath` values with the intended absolute path.
4. Correct the supplied project root; never silently use another open project.
5. For compile-only validation, call `unity_request_script_compile_and_wait` if `UNITY_MCP_UNITY_PATH` or `unityPath` is available, allowing batch fallback.
