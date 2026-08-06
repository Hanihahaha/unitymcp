---
name: unity-mcp
description: Inspect, control, and validate Unity projects through the Unity MCP bridge. Use whenever a task depends on live Unity Editor state or runtime behavior, including checking the connected project, scene, play or compile state; finding scenes or GameObjects; reading components, transforms, Inspector fields, or Unity logs; entering or stopping Play Mode; invoking explicitly exposed UnityMcpCallable methods; capturing Game View, Camera, or provider images; executing a known Unity menu item; and compiling Unity scripts after code edits. Prefer filesystem and code tools for static source or asset edits; use this skill for Unity-observed facts, Editor actions, runtime interaction, and post-edit validation.
---

# Unity MCP

Use the Unity MCP server as the source of truth for Editor and runtime state. Keep ordinary source inspection and file editing in filesystem tools.

## Establish the target

1. Resolve the absolute Unity project root, identified by `Assets/` and `ProjectSettings/`.
2. Pass that exact root as `projectPath` to every project-specific Unity tool. Do not pass `Assets/`, the repository parent, or a relative path.
3. Call `unity_bridge_status` with `projectPath` before a workflow that needs the live Editor. This call reports offline state without failing and lists discovered projects.
4. Call `unity_get_project_info` before an Editor action to verify that the selected bridge belongs to the intended project.

Tool names may be exposed with a client-specific namespace such as `mcp__unity__unity_bridge_status`. Match by the final tool name documented here.

## Decide whether to call MCP

Call Unity MCP when the answer or action depends on Unity itself:

- Observe the active scene, loaded scenes, Play Mode, compilation, GameObjects, components, serialized Inspector fields, Console logs, frame/time state, or rendered output.
- Validate `.cs` changes with Unity compilation. Prefer `unity_request_script_compile_and_wait` after completing a coherent edit batch.
- Reproduce or inspect runtime behavior by entering Play Mode, querying fresh objects, invoking an exposed method, capturing an image, or reading logs.
- Open a requested scene or execute a known Editor menu command.
- Distinguish a code defect from an Editor/import/runtime problem.

Do not call Unity MCP merely to:

- Read, search, create, or edit source code, text assets, project settings, scenes, or prefabs as files.
- Answer a static architecture or code question that does not require current Editor state.
- Guess at a method or menu command. Discover callable methods first and use a menu path only when it is known from code, documentation, or the user.
- Poll with sleeps. Use the compile-and-wait tools.

For tasks that include both code edits and validation, edit with filesystem tools first, then use Unity MCP to compile, inspect errors, and exercise the affected behavior.

## Follow the operating loop

1. **Observe:** Check bridge/project state and read the smallest relevant state surface.
2. **Narrow:** Find scenes or objects with filters; fetch detail only for selected results.
3. **Act:** Perform only the requested Editor transition, callable method, capture, or menu action.
4. **Verify:** Re-read state, errors, relevant logs, or pixels. Do not treat a successful tool response as proof of correct behavior.
5. **Clean up:** Stop Play Mode after temporary testing unless the user asked to leave it running. Report any deliberate scene or Editor state change.

Use the smallest useful `limit` for object, field, and log queries. Start with errors; fetch warnings or broader logs only when needed.

## Respect state and side effects

- Treat `unity_select_scene`, `unity_enter_play_mode`, `unity_stop_play_mode`, `unity_invoke_component_method`, and `unity_execute_menu_item` as state-changing calls.
- Do not switch scenes, invoke gameplay/editor actions, or run menu items unless the request authorizes that effect. Ask before a potentially destructive or ambiguous command.
- Expect GameObject and component instance IDs to become stale after entering Play Mode, stopping it, compiling scripts, or a domain reload. Query the object again after every such boundary.
- Invoke only methods returned by `unity_get_object_scripts` under `callableMethods`. Most require Play Mode; Edit Mode is allowed only when `allowInEditMode` is true.
- Remember that compile tools may stop Play Mode automatically. Use `unity_request_script_compile_and_wait` when initiating compilation; use `unity_wait_for_compile_complete` only when compilation was already triggered elsewhere.
- When the bridge is offline, live inspection and Editor actions are unavailable. Compilation can still fall back to Unity batch mode when a Unity executable is configured.

## Load references as needed

- Read [references/tools.md](references/tools.md) to select tools and arguments.
- Read [references/workflows.md](references/workflows.md) for compilation, runtime inspection, visual QA, scene, method, and menu workflows.
- Read [references/setup.md](references/setup.md) only when MCP tools are missing, the bridge is offline, project selection fails, or setup must be explained.

## Report evidence

Name the project and scene observed, the action performed, and the evidence used to verify it. For compilation, report completion/timeout plus error logs. For runtime or visual checks, report the relevant state, method result, logs, or captured image finding. Clearly state when validation could not run because the bridge or Unity executable was unavailable.
