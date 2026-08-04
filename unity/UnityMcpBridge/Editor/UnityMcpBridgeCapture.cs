#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        private const int DefaultCaptureTimeoutMs = 10000;
        private const int MaxCaptureTimeoutMs = 60000;
        private const int MaxCaptureDimension = 4096;
        private const int MaxCapturePngBytes = 16 * 1024 * 1024;

        private static readonly List<CaptureRoutineState> ActiveCaptures = new List<CaptureRoutineState>();

        private static Task<object> CaptureImageAsync(string requestJson)
        {
            var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            MainThreadActions.Enqueue(() => BeginImageCapture(requestJson, completion));
            return completion.Task;
        }

        private static void BeginImageCapture(string requestJson, TaskCompletionSource<object> completion)
        {
            CaptureImageRequestDto request;
            try
            {
                request = JsonUtility.FromJson<CaptureImageRequestDto>(requestJson);
            }
            catch (Exception ex)
            {
                completion.TrySetResult(new ErrorDto("bad_request", "Capture request JSON is invalid: " + ex.Message));
                return;
            }

            if (request == null)
            {
                completion.TrySetResult(new ErrorDto("bad_request", "Capture request body is required."));
                return;
            }

            request.mode = string.IsNullOrWhiteSpace(request.mode) ? "provider" : request.mode.Trim().ToLowerInvariant();
            request.captureName = string.IsNullOrWhiteSpace(request.captureName) ? "default" : request.captureName;
            request.format = string.IsNullOrWhiteSpace(request.format) ? "png" : request.format.Trim().ToLowerInvariant();
            request.optionsJson = string.IsNullOrWhiteSpace(request.optionsJson) ? "{}" : request.optionsJson;
            request.timeoutMs = Clamp(request.timeoutMs <= 0 ? DefaultCaptureTimeoutMs : request.timeoutMs, 100, MaxCaptureTimeoutMs);

            if (request.format != "png")
            {
                completion.TrySetResult(new ErrorDto("unsupported_format", "Only PNG capture is currently supported."));
                return;
            }

            if (!IsValidRequestedDimension(request.width) || !IsValidRequestedDimension(request.height))
            {
                completion.TrySetResult(new ErrorDto("bad_request", "Capture width and height must be between 1 and 4096 when specified."));
                return;
            }

            switch (request.mode)
            {
                case "provider":
                    BeginProviderCapture(request, completion);
                    break;
                case "game_view":
                    BeginGameViewCapture(request, completion);
                    break;
                case "camera":
                    completion.TrySetResult(CaptureCamera(request));
                    break;
                default:
                    completion.TrySetResult(new ErrorDto("unsupported_capture_mode", "Capture mode must be provider, camera, or game_view."));
                    break;
            }
        }

        private static void BeginProviderCapture(CaptureImageRequestDto request, TaskCompletionSource<object> completion)
        {
            if (!EditorApplication.isPlaying)
            {
                completion.TrySetResult(new ErrorDto("play_mode_required", "Provider capture requires Play Mode."));
                return;
            }

            var gameObject = FindCaptureGameObject(request.id);
            if (gameObject == null)
            {
                completion.TrySetResult(new ErrorDto("not_found", "No scene GameObject was found for instance id " + request.id + "."));
                return;
            }

            var providers = gameObject.GetComponents<MonoBehaviour>()
                .Where(component => component != null && component is IUnityMcpImageProvider)
                .Where(component => request.providerComponentInstanceId == 0 || component.GetInstanceID() == request.providerComponentInstanceId)
                .ToArray();

            if (providers.Length == 0)
            {
                completion.TrySetResult(new ErrorDto("provider_not_found", "The GameObject has no matching IUnityMcpImageProvider component."));
                return;
            }

            if (providers.Length > 1)
            {
                completion.TrySetResult(new ErrorDto("ambiguous_provider", "Multiple image providers are attached. Pass providerComponentInstanceId to select one."));
                return;
            }

            var providerComponent = providers[0];
            var provider = (IUnityMcpImageProvider)providerComponent;
            var captureRequest = new UnityMcpCaptureRequest
            {
                CaptureName = request.captureName,
                Width = request.width,
                Height = request.height,
                Format = request.format,
                OptionsJson = request.optionsJson
            };

            CaptureRoutineState state = null;
            UnityMcpImageResult earlyResult = null;
            IEnumerator routine;
            try
            {
                routine = provider.CaptureMcpImage(captureRequest, result =>
                {
                    if (state == null)
                    {
                        earlyResult = result;
                    }
                    else if (state.ProviderResult == null)
                    {
                        state.ProviderResult = result;
                    }
                });
            }
            catch (Exception ex)
            {
                completion.TrySetResult(new ErrorDto("capture_failed", ex.GetType().Name + ": " + ex.Message));
                return;
            }

            if (routine == null)
            {
                completion.TrySetResult(new ErrorDto("capture_failed", "The image provider returned no capture routine."));
                return;
            }

            state = new CaptureRoutineState
            {
                routine = routine,
                completion = completion,
                deadline = EditorApplication.timeSinceStartup + request.timeoutMs / 1000.0,
                mode = request.mode,
                gameObjectInstanceId = gameObject.GetInstanceID(),
                providerComponentInstanceId = providerComponent.GetInstanceID()
            };
            state.ProviderResult = earlyResult;
            StartCaptureRoutine(state);
        }

        private static void BeginGameViewCapture(CaptureImageRequestDto request, TaskCompletionSource<object> completion)
        {
            if (!EditorApplication.isPlaying)
            {
                completion.TrySetResult(new ErrorDto("play_mode_required", "Game View capture requires Play Mode."));
                return;
            }

            CaptureRoutineState state = null;
            var routine = CaptureGameView(request, result =>
            {
                if (state != null && state.ProviderResult == null)
                {
                    state.ProviderResult = result;
                }
            });
            state = new CaptureRoutineState
            {
                routine = routine,
                completion = completion,
                deadline = EditorApplication.timeSinceStartup + request.timeoutMs / 1000.0,
                mode = request.mode
            };
            StartCaptureRoutine(state);
        }

        private static IEnumerator CaptureGameView(
            CaptureImageRequestDto request,
            Action<UnityMcpImageResult> completed)
        {
            yield return null;

            Texture2D screenshot = null;
            try
            {
                screenshot = ScreenCapture.CaptureScreenshotAsTexture(1);
                var result = EncodeTextureToPng(screenshot, request.width, request.height);
                completed(result);
            }
            finally
            {
                if (screenshot != null)
                {
                    UnityEngine.Object.Destroy(screenshot);
                }
            }
        }

        private static object CaptureCamera(CaptureImageRequestDto request)
        {
            var gameObject = FindCaptureGameObject(request.id);
            if (gameObject == null)
            {
                return new ErrorDto("not_found", "No scene GameObject was found for instance id " + request.id + ".");
            }

            var camera = gameObject.GetComponent<Camera>();
            if (camera == null)
            {
                return new ErrorDto("camera_not_found", "The selected GameObject has no Camera component.");
            }

            var sourceWidth = camera.pixelWidth > 0 ? camera.pixelWidth : 1024;
            var sourceHeight = camera.pixelHeight > 0 ? camera.pixelHeight : 1024;
            ResolveOutputSize(sourceWidth, sourceHeight, request.width, request.height, out var width, out var height);

            var renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                var result = EncodeRenderTextureToPng(renderTexture, width, height);
                return ToCaptureImageResultDto(result, request.mode, gameObject.GetInstanceID(), camera.GetInstanceID());
            }
            catch (Exception ex)
            {
                return new ErrorDto("capture_failed", ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static void StartCaptureRoutine(CaptureRoutineState state)
        {
            ActiveCaptures.Add(state);
        }

        private static void AdvanceImageCaptures()
        {
            for (var i = ActiveCaptures.Count - 1; i >= 0; i--)
            {
                if (!AdvanceCaptureRoutine(ActiveCaptures[i]))
                {
                    ActiveCaptures.RemoveAt(i);
                }
            }
        }

        private static bool AdvanceCaptureRoutine(CaptureRoutineState state)
        {
            if (EditorApplication.timeSinceStartup >= state.deadline)
            {
                DisposeCaptureRoutine(state);
                state.completion.TrySetResult(new ErrorDto("capture_timeout", "Image capture timed out."));
                return false;
            }

            bool hasNext;
            try
            {
                hasNext = state.routine.MoveNext();
            }
            catch (Exception ex)
            {
                DisposeCaptureRoutine(state);
                state.completion.TrySetResult(new ErrorDto("capture_failed", ex.GetType().Name + ": " + ex.Message));
                return false;
            }

            if (state.ProviderResult != null)
            {
                DisposeCaptureRoutine(state);
                state.completion.TrySetResult(ToCaptureImageResultDto(
                    state.ProviderResult,
                    state.mode,
                    state.gameObjectInstanceId,
                    state.providerComponentInstanceId));
                return false;
            }

            if (!hasNext)
            {
                DisposeCaptureRoutine(state);
                state.completion.TrySetResult(new ErrorDto("capture_failed", "The capture routine completed without returning an image."));
                return false;
            }

            return true;
        }

        private static void DisposeCaptureRoutine(CaptureRoutineState state)
        {
            if (state.routine is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private static object ToCaptureImageResultDto(
            UnityMcpImageResult result,
            string mode,
            int gameObjectInstanceId,
            int providerComponentInstanceId)
        {
            if (result == null)
            {
                return new ErrorDto("capture_failed", "The capture provider returned a null result.");
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return new ErrorDto("capture_failed", result.Error);
            }

            if (result.Data == null || result.Data.Length == 0)
            {
                return new ErrorDto("capture_failed", "The capture provider returned no image bytes.");
            }

            if (result.Data.Length > MaxCapturePngBytes)
            {
                return new ErrorDto("image_too_large", "The encoded image exceeds 16 MB.");
            }

            if (result.Width <= 0 || result.Height <= 0
                || result.Width > MaxCaptureDimension || result.Height > MaxCaptureDimension)
            {
                return new ErrorDto("invalid_image", "The capture provider returned invalid image dimensions.");
            }

            var mimeType = string.IsNullOrWhiteSpace(result.MimeType) ? "image/png" : result.MimeType;
            if (!mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return new ErrorDto("invalid_image", "The capture provider returned an invalid image MIME type.");
            }

            return new CaptureImageResultDto
            {
                ok = true,
                mode = mode,
                mimeType = mimeType,
                data = Convert.ToBase64String(result.Data),
                width = result.Width,
                height = result.Height,
                byteCount = result.Data.Length,
                gameObjectInstanceId = gameObjectInstanceId,
                sourceComponentInstanceId = providerComponentInstanceId
            };
        }

        private static UnityMcpImageResult EncodeTextureToPng(Texture source, int requestedWidth, int requestedHeight)
        {
            if (source == null)
            {
                return new UnityMcpImageResult { Error = "The capture source texture is null." };
            }

            ResolveOutputSize(source.width, source.height, requestedWidth, requestedHeight, out var width, out var height);
            var renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(source, renderTexture);
                return EncodeRenderTextureToPng(renderTexture, width, height);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static UnityMcpImageResult EncodeRenderTextureToPng(RenderTexture source, int width, int height)
        {
            var previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                RenderTexture.active = source;
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false, false);
                return new UnityMcpImageResult
                {
                    Data = texture.EncodeToPNG(),
                    MimeType = "image/png",
                    Width = width,
                    Height = height
                };
            }
            finally
            {
                RenderTexture.active = previous;
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private static GameObject FindCaptureGameObject(int instanceId)
        {
            if (instanceId == 0)
            {
                return null;
            }

            var gameObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            return gameObject != null && IsSceneObject(gameObject) ? gameObject : null;
        }

        private static bool IsValidRequestedDimension(int value)
        {
            return value == 0 || (value > 0 && value <= MaxCaptureDimension);
        }

        private static void ResolveOutputSize(
            int sourceWidth,
            int sourceHeight,
            int requestedWidth,
            int requestedHeight,
            out int width,
            out int height)
        {
            sourceWidth = Math.Max(1, sourceWidth);
            sourceHeight = Math.Max(1, sourceHeight);

            if (requestedWidth == 0 && requestedHeight == 0)
            {
                width = sourceWidth;
                height = sourceHeight;
                FitWithinMaxCaptureSize(ref width, ref height);
                return;
            }

            if (requestedWidth == 0)
            {
                height = requestedHeight;
                width = Math.Max(1, (int)Math.Round(sourceWidth * (double)height / sourceHeight));
            }
            else if (requestedHeight == 0)
            {
                width = requestedWidth;
                height = Math.Max(1, (int)Math.Round(sourceHeight * (double)width / sourceWidth));
            }
            else
            {
                width = requestedWidth;
                height = requestedHeight;
            }

            FitWithinMaxCaptureSize(ref width, ref height);
        }

        private static void FitWithinMaxCaptureSize(ref int width, ref int height)
        {
            if (width <= MaxCaptureDimension && height <= MaxCaptureDimension)
            {
                return;
            }

            var scale = Math.Min(
                MaxCaptureDimension / (double)width,
                MaxCaptureDimension / (double)height);
            width = Math.Max(1, (int)Math.Round(width * scale));
            height = Math.Max(1, (int)Math.Round(height * scale));
        }

        private sealed class CaptureRoutineState
        {
            public IEnumerator routine;
            public TaskCompletionSource<object> completion;
            public UnityMcpImageResult ProviderResult;
            public double deadline;
            public string mode;
            public int gameObjectInstanceId;
            public int providerComponentInstanceId;
        }
    }
}
#endif
