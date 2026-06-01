#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        private const int MaxDefaultObjects = 100;

        private static HealthDto BuildHealth()
        {
            return new HealthDto
            {
                ok = true,
                bridgeUrl = Prefix.TrimEnd('/'),
                bridgePort = activePort,
                unityVersion = Application.unityVersion,
                projectName = Application.productName,
                projectPath = GetProjectPath(),
                activeScene = SceneManager.GetActiveScene().name,
                isPlaying = EditorApplication.isPlaying,
                timestampUtc = DateTime.UtcNow.ToString("O")
            };
        }

        private static ProjectInfoDto BuildProjectInfo()
        {
            var assetsPath = Application.dataPath;
            var projectPath = GetProjectPath();
            return new ProjectInfoDto
            {
                projectName = string.IsNullOrEmpty(projectPath) ? Application.productName : new DirectoryInfo(projectPath).Name,
                productName = Application.productName,
                projectPath = projectPath,
                assetsPath = assetsPath,
                bridgeUrl = Prefix.TrimEnd('/'),
                bridgePort = activePort,
                unityVersion = Application.unityVersion,
                activeScene = SceneManager.GetActiveScene().name,
                activeScenePath = SceneManager.GetActiveScene().path,
                isPlaying = EditorApplication.isPlaying,
                timestampUtc = DateTime.UtcNow.ToString("O")
            };
        }

        private static PlayStateDto BuildPlayState()
        {
            return new PlayStateDto
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                timeSinceStartup = EditorApplication.timeSinceStartup,
                timeScale = Time.timeScale,
                frameCount = Time.frameCount
            };
        }

        private static CompileStateDto BuildCompileState()
        {
            return new CompileStateDto
            {
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isPlaying = EditorApplication.isPlaying,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                isBusy = EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode,
                timestampUtc = DateTime.UtcNow.ToString("O")
            };
        }

        private static object RequestScriptCompile()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return new ErrorDto("unity_busy", "Unity 正在编译或刷新资源，请稍后再试。");
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return new ErrorDto("unity_busy", "Unity 正在播放或切换播放状态，请先退出播放模式。");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            return new CompileRequestDto
            {
                ok = true,
                requested = true,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                message = "已请求 Unity 刷新资源并触发脚本编译。"
            };
        }

        private static ScenesDto BuildScenes()
        {
            var scenes = new List<SceneDto>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                scenes.Add(ToSceneDto(scene));
            }

            return new ScenesDto
            {
                activeScene = SceneManager.GetActiveScene().name,
                scenes = scenes.ToArray()
            };
        }

        private static ObjectsDto QueryObjects(System.Collections.Specialized.NameValueCollection query)
        {
            var name = query["name"];
            var component = query["component"];
            var activeOnly = ParseBool(query["activeOnly"], true);
            var limit = Clamp(ParseInt(query["limit"], MaxDefaultObjects), 1, 1000);

            var objects = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(go => IsSceneObject(go))
                .Where(go => !activeOnly || go.activeInHierarchy);

            if (!string.IsNullOrWhiteSpace(name))
            {
                objects = objects.Where(go => go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (!string.IsNullOrWhiteSpace(component))
            {
                objects = objects.Where(go => go.GetComponent(component) != null);
            }

            var results = objects
                .OrderBy(go => go.scene.name)
                .ThenBy(GetHierarchyPath)
                .Take(limit)
                .Select(ToSummary)
                .ToArray();

            return new ObjectsDto
            {
                count = results.Length,
                limit = limit,
                objects = results
            };
        }

        private static object GetObject(System.Collections.Specialized.NameValueCollection query)
        {
            var id = ParseInt(query["id"], 0);
            if (id == 0)
            {
                return new ErrorDto("bad_request", "缺少必需的查询参数 id。");
            }

            var obj = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (obj == null || !IsSceneObject(obj))
            {
                return new ErrorDto("not_found", "没有找到实例 ID 为 " + id + " 的场景 GameObject。");
            }

            return ToDetail(obj);
        }

        private static GameObjectSummaryDto ToSummary(GameObject go)
        {
            return new GameObjectSummaryDto
            {
                instanceId = go.GetInstanceID(),
                name = go.name,
                path = GetHierarchyPath(go),
                scene = go.scene.name,
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,
                tag = go.tag,
                layer = LayerMask.LayerToName(go.layer),
                components = go.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToArray()
            };
        }

        private static GameObjectDetailDto ToDetail(GameObject go)
        {
            var summary = ToSummary(go);
            return new GameObjectDetailDto
            {
                instanceId = summary.instanceId,
                name = summary.name,
                path = summary.path,
                scene = summary.scene,
                activeSelf = summary.activeSelf,
                activeInHierarchy = summary.activeInHierarchy,
                tag = summary.tag,
                layer = summary.layer,
                components = summary.components,
                transform = new TransformDto
                {
                    position = ToVector(go.transform.position),
                    localPosition = ToVector(go.transform.localPosition),
                    rotationEuler = ToVector(go.transform.rotation.eulerAngles),
                    localRotationEuler = ToVector(go.transform.localRotation.eulerAngles),
                    lossyScale = ToVector(go.transform.lossyScale),
                    localScale = ToVector(go.transform.localScale)
                },
                childCount = go.transform.childCount
            };
        }

        private static bool IsSceneObject(GameObject go)
        {
            if (go == null || string.IsNullOrEmpty(go.scene.name))
            {
                return false;
            }

            return !EditorUtility.IsPersistent(go) && go.hideFlags == HideFlags.None;
        }

        private static string GetHierarchyPath(GameObject go)
        {
            var names = new Stack<string>();
            var current = go.transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static float[] ToVector(Vector3 vector)
        {
            return new[] { vector.x, vector.y, vector.z };
        }
    }
}
#endif
