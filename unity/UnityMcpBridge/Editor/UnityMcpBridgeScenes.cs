#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        private const int MaxDefaultSceneAssets = 100;

        private static SceneAssetsDto FindScenes(System.Collections.Specialized.NameValueCollection query)
        {
            var name = query["name"];
            var path = query["path"];
            var limit = Clamp(ParseInt(query["limit"], MaxDefaultSceneAssets), 1, 1000);

            var scenes = AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(scenePath => !string.IsNullOrWhiteSpace(scenePath))
                .Where(scenePath => string.IsNullOrWhiteSpace(name)
                    || Path.GetFileNameWithoutExtension(scenePath).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(scenePath => string.IsNullOrWhiteSpace(path)
                    || scenePath.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(scenePath => scenePath, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(ToSceneAssetDto)
                .ToArray();

            return new SceneAssetsDto
            {
                count = scenes.Length,
                limit = limit,
                scenes = scenes
            };
        }

        private static object SelectScene(System.Collections.Specialized.NameValueCollection query)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return new ErrorDto("unity_busy", "Unity is entering, playing, or exiting Play mode. Exit Play mode before selecting a scene.");
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return new ErrorDto("unity_busy", "Unity is compiling or updating assets. Try again later.");
            }

            var scenePath = (query["path"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return new ErrorDto("bad_request", "Missing required query parameter: path.");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                return new ErrorDto("scene_not_found", "Could not find scene: " + scenePath);
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return new ErrorDto("save_cancelled", "Saving current modified scenes was cancelled.");
            }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            Selection.activeObject = sceneAsset;

            return new SelectSceneDto
            {
                ok = true,
                scene = ToSceneDto(scene),
                selectedAsset = scenePath,
                message = "Scene selected."
            };
        }

        private static SceneAssetDto ToSceneAssetDto(string scenePath)
        {
            return new SceneAssetDto
            {
                name = Path.GetFileNameWithoutExtension(scenePath),
                path = scenePath,
                guid = AssetDatabase.AssetPathToGUID(scenePath),
                isActive = string.Equals(SceneManager.GetActiveScene().path, scenePath, StringComparison.Ordinal),
                isLoaded = IsSceneLoaded(scenePath)
            };
        }

        private static SceneDto ToSceneDto(Scene scene)
        {
            return new SceneDto
            {
                handle = scene.handle,
                name = scene.name,
                path = scene.path,
                isLoaded = scene.isLoaded,
                isDirty = scene.isDirty,
                rootCount = scene.rootCount,
                isActive = scene == SceneManager.GetActiveScene()
            };
        }

        private static bool IsSceneLoaded(string scenePath)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (string.Equals(SceneManager.GetSceneAt(i).path, scenePath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
