#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        private static object GetObjectScripts(System.Collections.Specialized.NameValueCollection query)
        {
            var id = ParseInt(query["id"], 0);
            var scriptFilter = query["script"];
            var limit = Clamp(ParseInt(query["limit"], 200), 1, 1000);

            if (id == 0)
            {
                return new ErrorDto("bad_request", "缺少必需的查询参数 id。");
            }

            var obj = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (obj == null || !IsSceneObject(obj))
            {
                return new ErrorDto("not_found", "没有找到实例 ID 为 " + id + " 的场景 GameObject。");
            }

            var scripts = obj.GetComponents<MonoBehaviour>()
                .Where(component => component != null)
                .Where(component => string.IsNullOrWhiteSpace(scriptFilter)
                    || component.GetType().Name.IndexOf(scriptFilter, StringComparison.OrdinalIgnoreCase) >= 0
                    || (component.GetType().FullName ?? component.GetType().Name).IndexOf(scriptFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(component => ToScriptDto(component, limit))
                .ToArray();

            return new GameObjectScriptsDto
            {
                instanceId = obj.GetInstanceID(),
                name = obj.name,
                path = GetHierarchyPath(obj),
                scene = obj.scene.name,
                scriptCount = scripts.Length,
                fieldLimitPerScript = limit,
                scripts = scripts
            };
        }

        private static ScriptDto ToScriptDto(MonoBehaviour component, int fieldLimit)
        {
            var type = component.GetType();
            var fields = new List<InspectorFieldDto>();
            var serializedObject = new SerializedObject(component);
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script")
                {
                    continue;
                }

                fields.Add(ToInspectorFieldDto(iterator));
                if (fields.Count >= fieldLimit)
                {
                    break;
                }
            }

            return new ScriptDto
            {
                instanceId = component.GetInstanceID(),
                scriptName = type.Name,
                typeName = type.FullName,
                assemblyName = type.Assembly.GetName().Name,
                enabled = component.enabled,
                fieldCount = fields.Count,
                truncated = fields.Count >= fieldLimit && iterator.NextVisible(false),
                fields = fields.ToArray()
            };
        }

        private static InspectorFieldDto ToInspectorFieldDto(SerializedProperty property)
        {
            return new InspectorFieldDto
            {
                path = property.propertyPath,
                displayName = property.displayName,
                type = property.propertyType.ToString(),
                depth = property.depth,
                isArray = property.isArray && property.propertyType != SerializedPropertyType.String,
                arraySize = property.isArray && property.propertyType != SerializedPropertyType.String ? property.arraySize : -1,
                value = ReadSerializedPropertyValue(property)
            };
        }

        private static string ReadSerializedPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue.ToString();
                case SerializedPropertyType.Boolean:
                    return property.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:
                    return property.floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    return property.colorValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null
                        ? "null"
                        : property.objectReferenceValue.name + " (" + property.objectReferenceValue.GetType().Name + ", " + property.objectReferenceInstanceIDValue + ")";
                case SerializedPropertyType.LayerMask:
                    return property.intValue.ToString();
                case SerializedPropertyType.Enum:
                    return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                        ? property.enumDisplayNames[property.enumValueIndex]
                        : property.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2:
                    return property.vector2Value.ToString();
                case SerializedPropertyType.Vector3:
                    return property.vector3Value.ToString();
                case SerializedPropertyType.Vector4:
                    return property.vector4Value.ToString();
                case SerializedPropertyType.Rect:
                    return property.rectValue.ToString();
                case SerializedPropertyType.ArraySize:
                    return property.intValue.ToString();
                case SerializedPropertyType.Character:
                    return ((char)property.intValue).ToString();
                case SerializedPropertyType.AnimationCurve:
                    return property.animationCurveValue == null ? "null" : "keys=" + property.animationCurveValue.length;
                case SerializedPropertyType.Bounds:
                    return property.boundsValue.ToString();
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue.eulerAngles.ToString();
                case SerializedPropertyType.ExposedReference:
                    return property.exposedReferenceValue == null ? "null" : property.exposedReferenceValue.name;
                case SerializedPropertyType.FixedBufferSize:
                    return property.fixedBufferSize.ToString();
                case SerializedPropertyType.Vector2Int:
                    return property.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int:
                    return property.vector3IntValue.ToString();
                case SerializedPropertyType.RectInt:
                    return property.rectIntValue.ToString();
                case SerializedPropertyType.BoundsInt:
                    return property.boundsIntValue.ToString();
                default:
                    return property.hasVisibleChildren ? "<object>" : string.Empty;
            }
        }
    }
}
#endif
