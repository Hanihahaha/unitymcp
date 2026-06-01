#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        private static object EnterPlayMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return new ErrorDto("unity_busy", "Unity is entering or exiting Play mode. Try again later.");
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return new ErrorDto("unity_busy", "Unity is compiling or updating assets. Try again later.");
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return new ErrorDto("save_cancelled", "Saving current modified scenes was cancelled.");
            }

            EditorApplication.isPlaying = true;

            return new EnterPlayModeDto
            {
                ok = true,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode
            };
        }

        private static object StopPlayMode()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return new ErrorDto("unity_busy", "Unity is compiling or updating assets. Try again later.");
            }

            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return new StopPlayModeDto
                {
                    ok = true,
                    wasPlaying = false,
                    isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                    message = "Unity is not in Play mode."
                };
            }

            EditorApplication.isPlaying = false;
            return new StopPlayModeDto
            {
                ok = true,
                wasPlaying = true,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                message = "Requested Unity to exit Play mode."
            };
        }
    }
}
#endif
