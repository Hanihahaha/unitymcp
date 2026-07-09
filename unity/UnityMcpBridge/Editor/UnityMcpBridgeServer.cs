#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor
{
    [InitializeOnLoad]
    public static partial class UnityMcpBridgeServer
    {
        private const string Host = "127.0.0.1";
        private const int PortStart = 8765;
        private const int PortCount = 20;
        private const int MaxLogs = 500;

        private static readonly ConcurrentQueue<Action> MainThreadActions = new ConcurrentQueue<Action>();
        private static readonly List<LogEntryDto> Logs = new List<LogEntryDto>();
        private static readonly object LogsLock = new object();

        private static HttpListener listener;
        private static CancellationTokenSource cancellation;
        private static Task listenTask;
        private static int nextRequestId;
        private static int activePort;

        private static string Prefix
        {
            get { return "http://" + Host + ":" + activePort + "/"; }
        }

        static UnityMcpBridgeServer()
        {
            EditorApplication.update += DrainMainThreadActions;
            Application.logMessageReceived += CaptureLog;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            InitializeCompilationWatcher();
            EditorApplication.delayCall += Start;
        }

        [MenuItem("MCP/启动桥接服务")]
        public static void Start()
        {
            if (listener != null)
            {
                Debug.Log("[Unity MCP] 桥接服务已经在运行：" + Prefix);
                return;
            }

            cancellation = new CancellationTokenSource();

            try
            {
                listener = StartListenerOnAvailablePort();
            }
            catch (Exception ex)
            {
                cancellation.Dispose();
                cancellation = null;
                Debug.LogError("[Unity MCP] Failed to start bridge service in port range " + PortStart + "-" + (PortStart + PortCount - 1) + ": " + ex.Message);
                return;
            }

            listenTask = Task.Run(() => ListenLoop(cancellation.Token));
            Debug.Log("[Unity MCP] 桥接服务已启动：" + Prefix);
        }

        [MenuItem("MCP/停止桥接服务")]
        public static void Stop()
        {
            var oldListener = listener;
            listener = null;

            if (oldListener == null)
            {
                return;
            }

            try
            {
                cancellation?.Cancel();
                oldListener.Stop();
                oldListener.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Unity MCP] 停止桥接服务时出错：" + ex.Message);
            }
            finally
            {
                cancellation?.Dispose();
                cancellation = null;
                listenTask = null;
                activePort = 0;
            }

            Debug.Log("[Unity MCP] 桥接服务已停止");
        }

        [MenuItem("MCP/查看桥接服务状态")]
        public static void ShowStatus()
        {
            Debug.Log(listener == null
                ? "[Unity MCP] 桥接服务未运行"
                : "[Unity MCP] 桥接服务正在运行：" + Prefix);
        }

        private static HttpListener StartListenerOnAvailablePort()
        {
            var firstOffset = StablePortOffset(GetProjectPath());
            Exception lastError = null;

            for (var i = 0; i < PortCount; i++)
            {
                var port = PortStart + ((firstOffset + i) % PortCount);
                var prefix = "http://" + Host + ":" + port + "/";
                var candidate = new HttpListener();
                candidate.Prefixes.Add(prefix);

                try
                {
                    candidate.Start();
                    activePort = port;
                    return candidate;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try
                    {
                        candidate.Close();
                    }
                    catch
                    {
                    }
                }
            }

            throw new InvalidOperationException("No available bridge port. Last error: " + (lastError == null ? "<none>" : lastError.Message));
        }

        private static int StablePortOffset(string value)
        {
            unchecked
            {
                const uint fnvOffset = 2166136261;
                const uint fnvPrime = 16777619;
                var hash = fnvOffset;

                if (!string.IsNullOrEmpty(value))
                {
                    for (var i = 0; i < value.Length; i++)
                    {
                        hash ^= char.ToUpperInvariant(value[i]);
                        hash *= fnvPrime;
                    }
                }

                return (int)(hash % PortCount);
            }
        }

        private static string GetProjectPath()
        {
            var assetsPath = Application.dataPath;
            return System.IO.Directory.GetParent(assetsPath)?.FullName ?? string.Empty;
        }

        private static async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    if (!token.IsCancellationRequested)
                    {
                        EnqueueLogWarning("[Unity MCP] 监听器意外停止");
                    }
                    break;
                }

                _ = Task.Run(() => HandleContext(context));
            }
        }

        private static async Task HandleContext(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod == "OPTIONS")
                {
                    WriteCorsHeaders(context.Response);
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }

                if (context.Request.HttpMethod != "GET")
                {
                    await WriteJson(context, 405, new ErrorDto("method_not_allowed", "只支持 GET 请求。"));
                    return;
                }

                var path = context.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant();
                switch (path)
                {
                    case "":
                    case "health":
                        await WriteJson(context, 200, await RunOnMainThread(BuildHealth));
                        break;
                    case "project-info":
                        await WriteJson(context, 200, await RunOnMainThread(BuildProjectInfo));
                        break;
                    case "play-state":
                        await WriteJson(context, 200, await RunOnMainThread(BuildPlayState));
                        break;
                    case "compile-state":
                        await WriteJson(context, 200, await RunOnMainThread(BuildCompileState));
                        break;
                    case "request-script-compile":
                        await WriteJson(context, 200, await RunOnMainThread(RequestScriptCompile));
                        break;
                    case "wait-compile-complete":
                        await WriteJson(context, 200, await WaitForCompileComplete(context.Request.QueryString));
                        break;
                    case "scenes":
                        await WriteJson(context, 200, await RunOnMainThread(BuildScenes));
                        break;
                    case "find-scenes":
                        await WriteJson(context, 200, await RunOnMainThread(() => FindScenes(context.Request.QueryString)));
                        break;
                    case "select-scene":
                        await WriteJson(context, 200, await RunOnMainThread(() => SelectScene(context.Request.QueryString)));
                        break;
                    case "objects":
                        await WriteJson(context, 200, await RunOnMainThread(() => QueryObjects(context.Request.QueryString)));
                        break;
                    case "object":
                        await WriteJson(context, 200, await RunOnMainThread(() => GetObject(context.Request.QueryString)));
                        break;
                    case "object-scripts":
                        await WriteJson(context, 200, await RunOnMainThread(() => GetObjectScripts(context.Request.QueryString)));
                        break;
                    case "logs":
                        await WriteJson(context, 200, BuildLogs(context.Request.QueryString));
                        break;
                    case "enter-play-mode":
                        await WriteJson(context, 200, await RunOnMainThread(EnterPlayMode));
                        break;
                    case "stop-play-mode":
                        await WriteJson(context, 200, await RunOnMainThread(StopPlayMode));
                        break;
                    case "execute-menu-item":
                        await WriteJson(context, 200, await RunOnMainThread(() => ExecuteMenuItem(context.Request.QueryString)));
                        break;
                    default:
                        await WriteJson(context, 404, new ErrorDto("not_found", "未知接口：" + path));
                        return;
                }
            }
            catch (Exception ex)
            {
                await WriteJson(context, 500, new ErrorDto("bridge_error", ex.ToString()));
            }
        }

        private static Task<T> RunOnMainThread<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();
            MainThreadActions.Enqueue(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        private static void DrainMainThreadActions()
        {
            while (MainThreadActions.TryDequeue(out var action))
            {
                action();
            }

            CheckCompileWaiters();
        }

        private static void CaptureLog(string condition, string stackTrace, LogType type)
        {
            lock (LogsLock)
            {
                Logs.Add(new LogEntryDto
                {
                    index = ++nextRequestId,
                    type = type.ToString(),
                    message = condition,
                    stackTrace = stackTrace,
                    timestampUtc = DateTime.UtcNow.ToString("O")
                });

                if (Logs.Count > MaxLogs)
                {
                    Logs.RemoveRange(0, Logs.Count - MaxLogs);
                }
            }
        }

        private static LogsDto BuildLogs(System.Collections.Specialized.NameValueCollection query)
        {
            var limit = Clamp(ParseInt(query["limit"], 100), 1, MaxLogs);
            var types = ParseLogTypes(query["types"]);
            var sinceUtc = ParseUtcDateTime(query["sinceUtc"]);

            lock (LogsLock)
            {
                var filtered = Logs.FindAll(entry =>
                    (types.Count == 0 || types.Contains(entry.type))
                    && (sinceUtc == null || IsOnOrAfter(entry.timestampUtc, sinceUtc.Value)));

                var start = Math.Max(0, filtered.Count - limit);
                return new LogsDto
                {
                    count = filtered.Count - start,
                    limit = limit,
                    entries = filtered.GetRange(start, filtered.Count - start).ToArray()
                };
            }
        }

        private static HashSet<string> ParseLogTypes(string value)
        {
            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value))
            {
                return types;
            }

            var parts = value.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    types.Add(trimmed);
                }
            }

            return types;
        }

        private static DateTime? ParseUtcDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            DateTime parsed;
            if (!DateTime.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out parsed))
            {
                return null;
            }

            return parsed.ToUniversalTime();
        }

        private static bool IsOnOrAfter(string timestampUtc, DateTime sinceUtc)
        {
            var timestamp = ParseUtcDateTime(timestampUtc);
            return timestamp != null && timestamp.Value >= sinceUtc;
        }

        private static void EnqueueLogWarning(string message)
        {
            MainThreadActions.Enqueue(() => Debug.LogWarning(message));
        }

        private static async Task WriteJson(HttpListenerContext context, int statusCode, object payload)
        {
            var json = SimpleJson.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);

            WriteCorsHeaders(context.Response);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        private static void WriteCorsHeaders(HttpListenerResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = "http://127.0.0.1";
            response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            return bool.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
#endif
