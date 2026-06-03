using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed partial class UnityBridgeClient
{
    private const int DefaultCompileTimeoutMs = 120000;
    private const int MaxCompileTimeoutMs = 600000;
    private const int DefaultCompilePollIntervalMs = 500;
    private const int MaxCompilePollIntervalMs = 5000;
    private const int DefaultCompileSettleMs = 1500;
    private const int MaxCompileSettleMs = 10000;
    private const int DefaultStopPlayModeTimeoutMs = 30000;
    private const int DefaultBridgeReconnectTimeoutMs = 120000;

    private async Task<object> RequestScriptCompileAsync(JsonObject args)
    {
        var startedAt = DateTime.UtcNow;
        var target = ReadCompileTarget(args);
        var readiness = await PrepareRequestedProjectForCompileAsync(startedAt, target, args);
        if (readiness.externalCompile != null)
        {
            return readiness.externalCompile;
        }
        if (!readiness.isReady)
        {
            return BuildReadinessFailure(startedAt, readiness);
        }

        JsonNode? request = null;
        string? requestError = null;

        try
        {
            if (!readiness.bridgeReconnected)
            {
                using var requestDocument = await GetBridgeJsonAsync(readiness.endpoint!, "request-script-compile");
                request = requestDocument == null ? null : JsonNode.Parse(requestDocument.RootElement.GetRawText());
            }
        }
        catch (Exception ex)
        {
            requestError = ex.Message;
        }

        return new
        {
            completed = requestError == null && !ReadError(request),
            timedOut = false,
            bridgeReconnected = readiness.bridgeReconnected,
            elapsedMs = ElapsedMs(startedAt),
            requested = request,
            requestError,
            finalState = readiness.postPlayState ?? readiness.projectInfo,
            logs = (JsonNode?)null,
            stoppedPlayMode = readiness.stoppedPlayMode,
            stopPlayMode = readiness.stopPlayMode,
            message = requestError == null
                ? readiness.bridgeReconnected
                    ? "Unity Bridge reconnected after an existing compile or domain reload. No additional compile request was sent."
                    : readiness.stoppedPlayMode
                    ? "Unity exited Play mode and script compilation was requested."
                    : "Unity script compilation was requested."
                : "Failed to request Unity script compilation: " + requestError
        };
    }

    private async Task<object> RequestScriptCompileAndWaitAsync(JsonObject args)
    {
        var startedAt = DateTime.UtcNow;
        var target = ReadCompileTarget(args);
        var readiness = await PrepareRequestedProjectForCompileAsync(startedAt, target, args);
        if (readiness.externalCompile != null)
        {
            return readiness.externalCompile;
        }
        if (!readiness.isReady)
        {
            return BuildReadinessFailure(startedAt, readiness);
        }

        JsonNode? request = null;
        string? requestError = null;

        try
        {
            if (!readiness.bridgeReconnected)
            {
                using var requestDocument = await GetBridgeJsonAsync(readiness.endpoint!, "request-script-compile");
                request = requestDocument == null ? null : JsonNode.Parse(requestDocument.RootElement.GetRawText());
            }
        }
        catch (Exception ex)
        {
            requestError = ex.Message;
        }

        var wait = await WaitForCompileCompleteAsync(readiness.endpoint!, args, startedAt, DefaultCompileSettleMs);
        var logs = await TryReadLogsAsync(readiness.endpoint!, JsonArgs.TryGetInt(args, "logLimit") ?? 100);

        return new
        {
            completed = wait.completed,
            timedOut = wait.timedOut,
            bridgeReconnected = readiness.bridgeReconnected || wait.bridgeReconnected,
            elapsedMs = wait.elapsedMs,
            requested = request,
            requestError,
            finalState = wait.finalState,
            logs,
            stoppedPlayMode = readiness.stoppedPlayMode,
            stopPlayMode = readiness.stopPlayMode,
            message = wait.message
        };
    }

    private async Task<object> WaitForCompileCompleteAsync(JsonObject args)
    {
        var startedAt = DateTime.UtcNow;
        var target = ReadCompileTarget(args);
        var readiness = await PrepareRequestedProjectForCompileAsync(startedAt, target, args);
        if (readiness.externalCompile != null)
        {
            return readiness.externalCompile;
        }
        if (!readiness.isReady)
        {
            return BuildReadinessFailure(startedAt, readiness);
        }

        var wait = await WaitForCompileCompleteAsync(readiness.endpoint!, args, DateTime.UtcNow, 0);
        return new
        {
            completed = wait.completed,
            timedOut = wait.timedOut,
            bridgeReconnected = readiness.bridgeReconnected || wait.bridgeReconnected,
            elapsedMs = wait.elapsedMs,
            finalState = wait.finalState,
            stoppedPlayMode = readiness.stoppedPlayMode,
            stopPlayMode = readiness.stopPlayMode,
            message = wait.message
        };
    }

    private async Task<BridgeReadiness> PrepareRequestedProjectForCompileAsync(DateTime startedAt, CompileTarget target, JsonObject args)
    {
        var readiness = await EnsureRequestedProjectOrCompileExternallyAsync(startedAt, target, args);
        if (!readiness.isReady || readiness.externalCompile != null)
        {
            return readiness;
        }

        return await StopPlayModeBeforeCompileAsync(readiness, args);
    }

    private async Task<BridgeReadiness> EnsureRequestedProjectOrCompileExternallyAsync(DateTime startedAt, CompileTarget target, JsonObject args)
    {
        if (string.IsNullOrWhiteSpace(target.ProjectPath))
        {
            return new BridgeReadiness(false, "projectPath is required for this compile request.", null, null, null);
        }

        var readiness = await GetBridgeReadinessAsync(target.ProjectPath, args);
        if (readiness.isReady)
        {
            return readiness;
        }

        var externalCompile = await editorCompiler.CompileProjectAsync(target.ProjectPath, target.UnityPath);
        return readiness with
        {
            externalCompile = new
            {
                completed = ReadBoolFromObject(externalCompile, "completed"),
                timedOut = ReadBoolFromObject(externalCompile, "timedOut"),
                bridgeReconnected = false,
                elapsedMs = ElapsedMs(startedAt),
                requested = (JsonNode?)null,
                requestError = readiness.reason,
                finalState = (JsonNode?)null,
                logs = (JsonNode?)null,
                externalCompile,
                message = readiness.reason + " Ran Unity batchmode compile for the requested project."
            }
        };
    }

    private async Task<BridgeReadiness> GetBridgeReadinessAsync(string projectPath, JsonObject args)
    {
        var requestedProjectPath = UnityMcpOptions.NormalizePath(projectPath);
        var timeoutMs = Clamp(JsonArgs.TryGetInt(args, "bridgeReconnectTimeoutMs") ?? DefaultBridgeReconnectTimeoutMs, 0, MaxCompileTimeoutMs);
        var pollIntervalMs = Clamp(JsonArgs.TryGetInt(args, "pollIntervalMs") ?? DefaultCompilePollIntervalMs, 100, MaxCompilePollIntervalMs);
        var resolution = timeoutMs == 0
            ? await bridgeDirectory.ResolveAsync(requestedProjectPath)
            : await bridgeDirectory.WaitForProjectAsync(requestedProjectPath!, timeoutMs, pollIntervalMs);
        if (resolution.Endpoint != null)
        {
            return new BridgeReadiness(true, "Unity Bridge is connected to the requested project.", resolution.Endpoint.ProjectInfo, resolution.Endpoint.ProjectPath, requestedProjectPath, endpoint: resolution.Endpoint);
        }

        return new BridgeReadiness(
            false,
            (resolution.Error ?? $"No Unity Bridge is connected to projectPath '{requestedProjectPath}'.") + $" Waited {timeoutMs}ms for the bridge to reconnect before falling back.",
            null,
            null,
            requestedProjectPath);
    }

    private async Task<BridgeReadiness> StopPlayModeBeforeCompileAsync(BridgeReadiness readiness, JsonObject args)
    {
        JsonNode? playState = null;
        try
        {
            playState = await TryGetBridgeJsonNodeAsync(readiness.endpoint!, "compile-state");
        }
        catch (Exception ex)
        {
            return readiness with
            {
                bridgeReconnected = true,
                postPlayState = null,
                reason = "Unity Bridge disconnected while checking compile state: " + ex.Message
            };
        }

        var shouldStopPlayMode = ReadBool(playState, "isPlaying") || ReadBool(playState, "isPlayingOrWillChangePlaymode");
        if (!shouldStopPlayMode)
        {
            return readiness with { postPlayState = playState };
        }

        JsonNode? stopResult = null;
        try
        {
            using var stopDocument = await GetBridgeJsonAsync(readiness.endpoint!, "stop-play-mode");
            stopResult = stopDocument == null ? null : JsonNode.Parse(stopDocument.RootElement.GetRawText());
        }
        catch (Exception ex)
        {
            return readiness with
            {
                isReady = false,
                reason = "Failed to request Unity exit Play mode before compiling: " + ex.Message,
                stopPlayMode = stopResult,
                postPlayState = playState
            };
        }

        if (ReadError(stopResult))
        {
            return readiness with
            {
                isReady = false,
                reason = "Unity refused to exit Play mode before compiling: " + (TryReadString(stopResult, "message") ?? TryReadString(stopResult, "error") ?? "<missing reason>"),
                stopPlayMode = stopResult,
                postPlayState = playState
            };
        }

        var stopTimeoutMs = Clamp(JsonArgs.TryGetInt(args, "stopPlayModeTimeoutMs") ?? DefaultStopPlayModeTimeoutMs, 1000, MaxCompileTimeoutMs);
        var pollIntervalMs = Clamp(JsonArgs.TryGetInt(args, "pollIntervalMs") ?? DefaultCompilePollIntervalMs, 100, MaxCompilePollIntervalMs);
        var deadline = DateTime.UtcNow.AddMilliseconds(stopTimeoutMs);
        JsonNode? finalState = playState;
        string? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                finalState = await TryGetBridgeJsonNodeAsync(readiness.endpoint!, "compile-state");
                lastError = null;

                if (!ReadBool(finalState, "isPlayingOrWillChangePlaymode"))
                {
                    return readiness with
                    {
                        stoppedPlayMode = true,
                        stopPlayMode = stopResult,
                        postPlayState = finalState
                    };
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            var remainingMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                break;
            }

            await Task.Delay(Math.Min(pollIntervalMs, remainingMs));
        }

        var reason = "Timed out waiting for Unity to exit Play mode before compiling.";
        if (!string.IsNullOrWhiteSpace(lastError))
        {
            reason += " Last bridge error: " + lastError;
        }

        return readiness with
        {
            isReady = false,
            reason = reason,
            stoppedPlayMode = true,
            stopPlayMode = stopResult,
            postPlayState = finalState
        };
    }

    private object BuildReadinessFailure(DateTime startedAt, BridgeReadiness readiness)
    {
        return new
        {
            completed = false,
            timedOut = false,
            bridgeReconnected = false,
            elapsedMs = ElapsedMs(startedAt),
            requested = (JsonNode?)null,
            requestError = readiness.reason,
            finalState = readiness.projectInfo,
            logs = (JsonNode?)null,
            requestedProjectPath = readiness.expectedProjectPath,
            actualProjectPath = readiness.actualProjectPath,
            stoppedPlayMode = readiness.stoppedPlayMode,
            stopPlayMode = readiness.stopPlayMode,
            postPlayState = readiness.postPlayState,
            message = readiness.reason
        };
    }

    private async Task<CompileWaitOutcome> WaitForCompileCompleteAsync(BridgeEndpoint endpoint, JsonObject args, DateTime startedAt, int defaultSettleMs)
    {
        var timeoutMs = Clamp(JsonArgs.TryGetInt(args, "timeoutMs") ?? DefaultCompileTimeoutMs, 1000, MaxCompileTimeoutMs);
        var pollIntervalMs = Clamp(JsonArgs.TryGetInt(args, "pollIntervalMs") ?? DefaultCompilePollIntervalMs, 100, MaxCompilePollIntervalMs);
        var settleMs = Clamp(JsonArgs.TryGetInt(args, "settleMs") ?? defaultSettleMs, 0, MaxCompileSettleMs);
        var requireObservedBusy = JsonArgs.TryGetBool(args, "requireObservedCompile") ?? false;
        var deadline = startedAt.AddMilliseconds(timeoutMs);
        var idleSince = DateTime.UtcNow;
        var sawBridgeOffline = false;
        var bridgeReconnected = false;
        var observedBusy = false;
        JsonNode? finalState = null;
        string? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            using var attemptTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Min(2000, pollIntervalMs + 1000)));

            try
            {
                using var document = await GetBridgeJsonAsync(endpoint, "compile-state", attemptTimeout.Token);
                var state = document == null ? null : JsonNode.Parse(document.RootElement.GetRawText());
                finalState = state;
                lastError = null;

                var isBusy = ReadBool(state, "isBusy")
                    || ReadBool(state, "isCompiling")
                    || ReadBool(state, "isUpdating");

                if (isBusy)
                {
                    observedBusy = true;
                    idleSince = DateTime.UtcNow;
                }

                if (sawBridgeOffline)
                {
                    bridgeReconnected = true;
                }

                var idleLongEnough = (DateTime.UtcNow - idleSince).TotalMilliseconds >= settleMs;
                var canCompleteWithoutBusy = !requireObservedBusy && idleLongEnough;
                var observedWork = observedBusy || sawBridgeOffline;

                if (!isBusy && (observedWork || canCompleteWithoutBusy))
                {
                    return new CompileWaitOutcome
                    {
                        completed = true,
                        timedOut = false,
                        bridgeReconnected = bridgeReconnected,
                        elapsedMs = ElapsedMs(startedAt),
                        finalState = finalState,
                        message = sawBridgeOffline
                            ? "Unity compilation completed after the bridge reconnected."
                            : "Unity compilation completed."
                    };
                }
            }
            catch (OperationCanceledException ex)
            {
                sawBridgeOffline = true;
                lastError = ex.Message;
            }
            catch (HttpRequestException ex)
            {
                sawBridgeOffline = true;
                lastError = ex.Message;
            }
            catch (Exception ex)
            {
                sawBridgeOffline = true;
                lastError = ex.Message;
            }

            var remainingMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0)
            {
                break;
            }

            await Task.Delay(Math.Min(pollIntervalMs, remainingMs));
        }

        return new CompileWaitOutcome
        {
            completed = false,
            timedOut = true,
            bridgeReconnected = bridgeReconnected,
            elapsedMs = ElapsedMs(startedAt),
            finalState = finalState,
            message = lastError == null
                ? "Timed out waiting for Unity compilation to complete."
                : "Timed out waiting for Unity compilation to complete. Last bridge error: " + lastError
        };
    }

    private async Task<JsonNode?> TryReadLogsAsync(BridgeEndpoint endpoint, int limit)
    {
        try
        {
            using var document = await GetBridgeJsonAsync(endpoint, "logs?limit=" + Clamp(limit, 1, 500));
            return document == null ? null : JsonNode.Parse(document.RootElement.GetRawText());
        }
        catch
        {
            return null;
        }
    }

    private async Task<JsonNode?> TryGetBridgeJsonNodeAsync(BridgeEndpoint endpoint, string path)
    {
        using var document = await GetBridgeJsonAsync(endpoint, path);
        return document == null ? null : JsonNode.Parse(document.RootElement.GetRawText());
    }

    private static bool ReadBool(JsonNode? node, string key)
    {
        return node is JsonObject obj
            && obj.TryGetPropertyValue(key, out var value)
            && value != null
            && value.GetValue<bool>();
    }

    private static bool ReadError(JsonNode? node)
    {
        return node is JsonObject obj
            && obj.TryGetPropertyValue("error", out var value)
            && value != null
            && !string.IsNullOrWhiteSpace(value.GetValue<string>());
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    private static int ElapsedMs(DateTime startedAt)
    {
        return (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
    }

    private static CompileTarget ReadCompileTarget(JsonObject args)
    {
        return new CompileTarget(
            UnityMcpOptions.NormalizePath(JsonArgs.TryGetString(args, "projectPath")),
            UnityMcpOptions.NormalizePath(JsonArgs.TryGetString(args, "unityPath")));
    }

    private static bool ReadBoolFromObject(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        return property?.GetValue(value) is bool boolValue && boolValue;
    }

    private sealed record CompileTarget(string? ProjectPath, string? UnityPath);

    private sealed record BridgeReadiness(
        bool isReady,
        string reason,
        JsonNode? projectInfo,
        string? actualProjectPath,
        string? expectedProjectPath,
        object? externalCompile = null,
        BridgeEndpoint? endpoint = null,
        bool bridgeReconnected = false,
        bool stoppedPlayMode = false,
        JsonNode? stopPlayMode = null,
        JsonNode? postPlayState = null);

    private sealed class CompileWaitOutcome
    {
        public bool completed { get; init; }
        public bool timedOut { get; init; }
        public bool bridgeReconnected { get; init; }
        public int elapsedMs { get; init; }
        public JsonNode? finalState { get; init; }
        public string message { get; init; } = string.Empty;
    }
}
