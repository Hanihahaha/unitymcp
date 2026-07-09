#if UNITY_EDITOR
using System;
using System.Collections.Specialized;
using UnityEditor;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        [Serializable]
        private class ExecuteMenuItemDto
        {
            public bool ok;
            public bool executed;
            public string menuPath;
            public string timestampUtc;
            public string message;
        }

        private static ExecuteMenuItemDto ExecuteMenuItem(NameValueCollection query)
        {
            var menuPath = query["path"] ?? query["menuPath"];
            if (string.IsNullOrWhiteSpace(menuPath))
            {
                return new ExecuteMenuItemDto
                {
                    ok = false,
                    executed = false,
                    menuPath = string.Empty,
                    timestampUtc = DateTime.UtcNow.ToString("O"),
                    message = "Missing required query parameter: path."
                };
            }

            var executed = EditorApplication.ExecuteMenuItem(menuPath);
            return new ExecuteMenuItemDto
            {
                ok = executed,
                executed = executed,
                menuPath = menuPath,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                message = executed
                    ? "Menu item executed."
                    : "Menu item was not found or could not be executed."
            };
        }
    }
}
#endif
