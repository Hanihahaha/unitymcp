#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        private static readonly List<CompileWaiter> CompileWaiters = new List<CompileWaiter>();
        private static bool compileObservedSinceStartup;
        private static int compileSequence;

        static void InitializeCompilationWatcher()
        {
            CompilationPipeline.compilationStarted += _ =>
            {
                compileObservedSinceStartup = true;
                compileSequence++;
                CompleteCompileWaiters(false, "Unity script compilation started.");
            };

            CompilationPipeline.compilationFinished += _ =>
            {
                compileSequence++;
                CompleteCompileWaiters(true, "Unity script compilation finished.");
            };
        }

        private static Task<object> WaitForCompileComplete(System.Collections.Specialized.NameValueCollection query)
        {
            var timeoutMs = Clamp(ParseInt(query["timeoutMs"], 120000), 1000, 600000);
            var requireObservedCompile = ParseBool(query["requireObservedCompile"], false);
            var startedAt = DateTime.UtcNow;

            var tcs = new TaskCompletionSource<object>();
            MainThreadActions.Enqueue(() =>
            {
                var isBusy = IsUnityCompileBusy();
                if (!isBusy && (!requireObservedCompile || compileObservedSinceStartup))
                {
                    tcs.SetResult(BuildCompileWaitResult(true, false, false, startedAt, "Unity is already idle."));
                    return;
                }

                var waiter = new CompileWaiter
                {
                    startedAt = startedAt,
                    timeoutAt = startedAt.AddMilliseconds(timeoutMs),
                    requireObservedCompile = requireObservedCompile,
                    sequenceAtStart = compileSequence,
                    result = tcs
                };

                CompileWaiters.Add(waiter);
                CheckCompileWaiters();
            });

            return tcs.Task;
        }

        private static bool IsUnityCompileBusy()
        {
            return EditorApplication.isCompiling || EditorApplication.isUpdating;
        }

        private static void CheckCompileWaiters()
        {
            if (CompileWaiters.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var isBusy = IsUnityCompileBusy();

            for (var i = CompileWaiters.Count - 1; i >= 0; i--)
            {
                var waiter = CompileWaiters[i];
                if (now >= waiter.timeoutAt)
                {
                    CompileWaiters.RemoveAt(i);
                    waiter.result.TrySetResult(BuildCompileWaitResult(false, true, isBusy, waiter.startedAt, "Timed out waiting for Unity compilation to complete."));
                    continue;
                }

                var observedAfterStart = compileSequence > waiter.sequenceAtStart || compileObservedSinceStartup;
                if (!isBusy && (!waiter.requireObservedCompile || observedAfterStart))
                {
                    CompileWaiters.RemoveAt(i);
                    waiter.result.TrySetResult(BuildCompileWaitResult(true, false, false, waiter.startedAt, "Unity compilation is complete."));
                }
            }
        }

        private static void CompleteCompileWaiters(bool onlyIfIdle, string message)
        {
            if (onlyIfIdle && IsUnityCompileBusy())
            {
                return;
            }

            CheckCompileWaiters();
        }

        private static CompileWaitResultDto BuildCompileWaitResult(bool completed, bool timedOut, bool isBusy, DateTime startedAt, string message)
        {
            return new CompileWaitResultDto
            {
                completed = completed,
                timedOut = timedOut,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isBusy = isBusy,
                observedCompile = compileObservedSinceStartup,
                compileSequence = compileSequence,
                elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                message = message
            };
        }

        private class CompileWaiter
        {
            public DateTime startedAt;
            public DateTime timeoutAt;
            public bool requireObservedCompile;
            public int sequenceAtStart;
            public TaskCompletionSource<object> result;
        }
    }
}
#endif
