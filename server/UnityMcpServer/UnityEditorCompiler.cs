using System.Diagnostics;

internal sealed class UnityEditorCompiler
{
    private readonly UnityMcpOptions options;

    public UnityEditorCompiler(UnityMcpOptions options)
    {
        this.options = options;
    }

    public async Task<object> CompileProjectAsync(string? projectPath, string? unityPath = null, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var resolvedUnityPath = UnityMcpOptions.NormalizePath(string.IsNullOrWhiteSpace(unityPath)
            ? options.UnityExecutablePath
            : unityPath);
        var resolvedProjectPath = UnityMcpOptions.NormalizePath(projectPath);

        if (string.IsNullOrWhiteSpace(resolvedUnityPath))
        {
            return BuildSkipped(startedAt, resolvedUnityPath, resolvedProjectPath, "Unity executable path is not configured. Set UNITY_MCP_UNITY_PATH or pass unityPath.");
        }

        if (string.IsNullOrWhiteSpace(resolvedProjectPath))
        {
            return BuildSkipped(startedAt, resolvedUnityPath, resolvedProjectPath, "projectPath is required.");
        }

        if (!File.Exists(resolvedUnityPath))
        {
            return BuildSkipped(startedAt, resolvedUnityPath, resolvedProjectPath, "Configured Unity executable does not exist: " + resolvedUnityPath);
        }

        if (!Directory.Exists(resolvedProjectPath))
        {
            return BuildSkipped(startedAt, resolvedUnityPath, resolvedProjectPath, "Requested Unity project path does not exist: " + resolvedProjectPath);
        }

        var logPath = ResolveLogPath(resolvedProjectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(1000, options.ExternalCompileTimeoutMs));

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedUnityPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-batchmode");
        startInfo.ArgumentList.Add("-quit");
        startInfo.ArgumentList.Add("-projectPath");
        startInfo.ArgumentList.Add(resolvedProjectPath);
        startInfo.ArgumentList.Add("-logFile");
        startInfo.ArgumentList.Add(logPath);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process == null)
            {
                return BuildSkipped(startedAt, resolvedUnityPath, resolvedProjectPath, "Failed to start Unity process.");
            }

            await process.WaitForExitAsync(timeout.Token);
            var exitCode = process.ExitCode;
            var logTail = ReadLogTail(logPath);

            return new
            {
                ran = true,
                completed = exitCode == 0,
                timedOut = false,
                exitCode,
                unityPath = resolvedUnityPath,
                projectPath = resolvedProjectPath,
                logPath,
                elapsedMs = ElapsedMs(startedAt),
                logTail,
                message = exitCode == 0
                    ? "Unity batchmode compile completed successfully."
                    : "Unity batchmode compile failed."
            };
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new
            {
                ran = true,
                completed = false,
                timedOut = true,
                exitCode = (int?)null,
                unityPath = resolvedUnityPath,
                projectPath = resolvedProjectPath,
                logPath,
                elapsedMs = ElapsedMs(startedAt),
                logTail = ReadLogTail(logPath),
                message = "Timed out waiting for Unity batchmode compile."
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ran = false,
                completed = false,
                timedOut = false,
                exitCode = (int?)null,
                unityPath = resolvedUnityPath,
                projectPath = resolvedProjectPath,
                logPath,
                elapsedMs = ElapsedMs(startedAt),
                error = ex.Message,
                message = "Failed to run Unity batchmode compile."
            };
        }
        finally
        {
            process?.Dispose();
        }
    }

    private object BuildSkipped(DateTime startedAt, string? unityPath, string? projectPath, string message)
    {
        return new
        {
            ran = false,
            completed = false,
            timedOut = false,
            exitCode = (int?)null,
            unityPath,
            projectPath,
            logPath = (string?)null,
            elapsedMs = ElapsedMs(startedAt),
            message
        };
    }

    private string ResolveLogPath(string projectPath)
    {
        if (!string.IsNullOrWhiteSpace(options.ExternalCompileLogPath))
        {
            return Path.GetFullPath(options.ExternalCompileLogPath);
        }

        return Path.Combine(projectPath, "Logs", "unity-mcp-batch-compile.log");
    }

    private static string[] ReadLogTail(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            return File.ReadLines(path).TakeLast(80).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static int ElapsedMs(DateTime startedAt)
    {
        return (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
    }
}
