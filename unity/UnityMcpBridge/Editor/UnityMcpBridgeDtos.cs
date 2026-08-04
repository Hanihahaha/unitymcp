#if UNITY_EDITOR
using System;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        [Serializable]
        private class ErrorDto
        {
            public string error;
            public string message;

            public ErrorDto(string error, string message)
            {
                this.error = error;
                this.message = message;
            }
        }

        [Serializable]
        private class HealthDto
        {
            public bool ok;
            public string bridgeUrl;
            public int bridgePort;
            public string unityVersion;
            public string projectName;
            public string projectPath;
            public string activeScene;
            public bool isPlaying;
            public string timestampUtc;
        }

        [Serializable]
        private class ProjectInfoDto
        {
            public string projectName;
            public string productName;
            public string projectPath;
            public string assetsPath;
            public string bridgeUrl;
            public int bridgePort;
            public string unityVersion;
            public string activeScene;
            public string activeScenePath;
            public bool isPlaying;
            public string timestampUtc;
        }

        [Serializable]
        private class PlayStateDto
        {
            public bool isPlaying;
            public bool isPaused;
            public bool isCompiling;
            public bool isUpdating;
            public double timeSinceStartup;
            public float timeScale;
            public int frameCount;
        }

        [Serializable]
        private class CompileStateDto
        {
            public bool isCompiling;
            public bool isUpdating;
            public bool isPlaying;
            public bool isPlayingOrWillChangePlaymode;
            public bool isBusy;
            public string timestampUtc;
        }

        [Serializable]
        private class CompileRequestDto
        {
            public bool ok;
            public bool requested;
            public bool isCompiling;
            public bool isUpdating;
            public string timestampUtc;
            public string message;
        }

        [Serializable]
        private class CompileWaitResultDto
        {
            public bool completed;
            public bool timedOut;
            public bool isCompiling;
            public bool isUpdating;
            public bool isBusy;
            public bool observedCompile;
            public int compileSequence;
            public int elapsedMs;
            public string timestampUtc;
            public string message;
        }

        [Serializable]
        private class ScenesDto
        {
            public string activeScene;
            public SceneDto[] scenes;
        }

        [Serializable]
        private class SceneAssetsDto
        {
            public int count;
            public int limit;
            public SceneAssetDto[] scenes;
        }

        [Serializable]
        private class SceneAssetDto
        {
            public string name;
            public string path;
            public string guid;
            public bool isLoaded;
            public bool isActive;
        }

        [Serializable]
        private class SceneDto
        {
            public int handle;
            public string name;
            public string path;
            public bool isLoaded;
            public bool isDirty;
            public int rootCount;
            public bool isActive;
        }

        [Serializable]
        private class ObjectsDto
        {
            public int count;
            public int limit;
            public GameObjectSummaryDto[] objects;
        }

        [Serializable]
        private class GameObjectSummaryDto
        {
            public int instanceId;
            public string name;
            public string path;
            public string scene;
            public bool activeSelf;
            public bool activeInHierarchy;
            public string tag;
            public string layer;
            public string[] components;
        }

        [Serializable]
        private class GameObjectDetailDto : GameObjectSummaryDto
        {
            public TransformDto transform;
            public int childCount;
        }

        [Serializable]
        private class TransformDto
        {
            public float[] position;
            public float[] localPosition;
            public float[] rotationEuler;
            public float[] localRotationEuler;
            public float[] lossyScale;
            public float[] localScale;
        }

        [Serializable]
        private class GameObjectScriptsDto
        {
            public int instanceId;
            public string name;
            public string path;
            public string scene;
            public int scriptCount;
            public int fieldLimitPerScript;
            public ScriptDto[] scripts;
        }

        [Serializable]
        private class ScriptDto
        {
            public int instanceId;
            public string scriptName;
            public string typeName;
            public string assemblyName;
            public bool enabled;
            public int fieldCount;
            public bool truncated;
            public InspectorFieldDto[] fields;
            public bool isImageProvider;
            public int callableMethodCount;
            public CallableMethodDto[] callableMethods;
        }

        [Serializable]
        private class InspectorFieldDto
        {
            public string path;
            public string displayName;
            public string type;
            public int depth;
            public bool isArray;
            public int arraySize;
            public string value;
        }

        [Serializable]
        private class CallableMethodDto
        {
            public string name;
            public string returnType;
            public bool allowInEditMode;
            public bool hasExposedOverloads;
            public CallableParameterDto[] parameters;
        }

        [Serializable]
        private class CallableParameterDto
        {
            public string name;
            public string type;
        }

        [Serializable]
        private class InvokeComponentMethodRequestDto
        {
            public int id = 0;
            public string component = string.Empty;
            public int componentInstanceId = 0;
            public string method = string.Empty;
            public InvokeArgumentDto[] arguments = new InvokeArgumentDto[0];
        }

        [Serializable]
        private class InvokeArgumentDto
        {
            public string json = "null";
        }

        [Serializable]
        private class JsonStringWrapperDto
        {
            public string value = string.Empty;
        }

        [Serializable]
        private class UnityObjectReferenceArgumentDto
        {
            public int instanceId = 0;
        }

        [Serializable]
        private class InvokeComponentMethodResultDto
        {
            public bool ok;
            public int gameObjectInstanceId;
            public int componentInstanceId;
            public string componentType;
            public string method;
            public string returnType;
            public string returnValueJson;
        }

        [Serializable]
        private class UnityObjectReferenceResultDto
        {
            public int instanceId;
            public string name;
            public string type;
        }

        [Serializable]
        private class CaptureImageRequestDto
        {
            public string mode = "provider";
            public int id = 0;
            public int providerComponentInstanceId = 0;
            public string captureName = "default";
            public int width = 0;
            public int height = 0;
            public string format = "png";
            public string optionsJson = "{}";
            public int timeoutMs = 10000;
        }

        [Serializable]
        private class CaptureImageResultDto
        {
            public bool ok;
            public string mode;
            public string mimeType;
            public string data;
            public int width;
            public int height;
            public int byteCount;
            public int gameObjectInstanceId;
            public int sourceComponentInstanceId;
        }

        [Serializable]
        private class LogsDto
        {
            public int count;
            public int limit;
            public LogEntryDto[] entries;
        }

        [Serializable]
        private class LogEntryDto
        {
            public int index;
            public string type;
            public string message;
            public string stackTrace;
            public string timestampUtc;
        }

        [Serializable]
        private class SelectSceneDto
        {
            public bool ok;
            public SceneDto scene;
            public string selectedAsset;
            public string message;
        }

        [Serializable]
        private class EnterPlayModeDto
        {
            public bool ok;
            public bool isPlayingOrWillChangePlaymode;
        }

        [Serializable]
        private class StopPlayModeDto
        {
            public bool ok;
            public bool wasPlaying;
            public bool isPlayingOrWillChangePlaymode;
            public string message;
        }
    }
}
#endif
