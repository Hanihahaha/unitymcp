#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        private static object InvokeComponentMethod(string requestJson)
        {
            InvokeComponentMethodRequestDto request;
            try
            {
                request = JsonUtility.FromJson<InvokeComponentMethodRequestDto>(requestJson);
            }
            catch (Exception ex)
            {
                return new ErrorDto("bad_request", "POST 请求体不是有效的调用 JSON：" + ex.Message);
            }

            if (request == null || request.id == 0 || string.IsNullOrWhiteSpace(request.component) || string.IsNullOrWhiteSpace(request.method))
            {
                return new ErrorDto("bad_request", "请求体必须包含 id、component 和 method。");
            }

            var gameObject = EditorUtility.InstanceIDToObject(request.id) as GameObject;
            if (gameObject == null || !IsSceneObject(gameObject))
            {
                return new ErrorDto("not_found", "没有找到实例 ID 为 " + request.id + " 的场景 GameObject。");
            }

            var components = gameObject.GetComponents<MonoBehaviour>()
                .Where(component => component != null)
                .Where(component => ComponentNameMatches(component.GetType(), request.component))
                .Where(component => request.componentInstanceId == 0 || component.GetInstanceID() == request.componentInstanceId)
                .ToArray();

            if (components.Length == 0)
            {
                return new ErrorDto("component_not_found", "GameObject 上没有找到组件 " + request.component + "。");
            }

            if (components.Length > 1)
            {
                return new ErrorDto("ambiguous_component", "GameObject 上有多个匹配组件，请传 componentInstanceId 明确指定。");
            }

            var component = components[0];
            var methods = component.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => string.Equals(method.Name, request.method, StringComparison.Ordinal))
                .Select(method => new
                {
                    method,
                    attribute = method.GetCustomAttributes(typeof(UnityMcpCallableAttribute), true)
                        .OfType<UnityMcpCallableAttribute>()
                        .FirstOrDefault()
                })
                .Where(item => item.attribute != null)
                .ToArray();

            if (methods.Length == 0)
            {
                return new ErrorDto("method_not_exposed", "没有找到名为 " + request.method + " 且标记 [UnityMcpCallable] 的 public 实例方法。");
            }

            if (methods.Length > 1)
            {
                return new ErrorDto("ambiguous_method", "不支持调用已暴露的重载方法，请为 MCP 调用提供唯一的方法名。");
            }

            var selected = methods[0];
            var methodInfo = selected.method;
            if (!EditorApplication.isPlaying && !selected.attribute.AllowInEditMode)
            {
                return new ErrorDto("play_mode_required", "该方法只允许在 Play Mode 调用。需要编辑模式调用时，请设置 [UnityMcpCallable(AllowInEditMode = true)]。");
            }

            if (methodInfo.IsGenericMethodDefinition || methodInfo.ContainsGenericParameters || methodInfo.IsSpecialName)
            {
                return new ErrorDto("unsupported_method", "不支持泛型方法或特殊方法。");
            }

            if (methodInfo.GetCustomAttributes(typeof(AsyncStateMachineAttribute), false).Length > 0)
            {
                return new ErrorDto("unsupported_method", "不支持 async 方法，请暴露可同步完成的包装方法。");
            }

            var parameters = methodInfo.GetParameters();
            var arguments = request.arguments ?? new InvokeArgumentDto[0];
            if (parameters.Length != arguments.Length)
            {
                return new ErrorDto("argument_count_mismatch", "方法需要 " + parameters.Length + " 个参数，但请求提供了 " + arguments.Length + " 个。");
            }

            var convertedArguments = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType.IsByRef || parameters[i].IsOut || parameters[i].ParameterType.IsPointer)
                {
                    return new ErrorDto("unsupported_parameter", "参数 " + parameters[i].Name + " 使用了不支持的 ref、out 或指针类型。");
                }

                if (!TryConvertArgument(arguments[i] == null ? null : arguments[i].json, parameters[i].ParameterType, out convertedArguments[i], out var conversionError))
                {
                    return new ErrorDto("argument_conversion_failed", "参数 " + parameters[i].Name + " 转换失败：" + conversionError);
                }
            }

            if (!IsSupportedReturnType(methodInfo.ReturnType))
            {
                return new ErrorDto("unsupported_return_type", "不支持返回类型 " + FriendlyTypeName(methodInfo.ReturnType) + "。请使用 void、基础类型、枚举、UnityEngine.Object 或可序列化对象。");
            }

            object returnValue;
            try
            {
                returnValue = methodInfo.Invoke(component, convertedArguments);
            }
            catch (TargetInvocationException ex)
            {
                var cause = ex.InnerException ?? ex;
                return new ErrorDto("invocation_failed", cause.GetType().Name + ": " + cause.Message + "\n" + cause.StackTrace);
            }
            catch (Exception ex)
            {
                return new ErrorDto("invocation_failed", ex.GetType().Name + ": " + ex.Message);
            }

            return new InvokeComponentMethodResultDto
            {
                ok = true,
                gameObjectInstanceId = gameObject.GetInstanceID(),
                componentInstanceId = component.GetInstanceID(),
                componentType = component.GetType().FullName ?? component.GetType().Name,
                method = methodInfo.Name,
                returnType = FriendlyTypeName(methodInfo.ReturnType),
                returnValueJson = SerializeReturnValue(returnValue, methodInfo.ReturnType)
            };
        }

        private static bool ComponentNameMatches(Type componentType, string requestedName)
        {
            return string.Equals(componentType.Name, requestedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(componentType.FullName, requestedName, StringComparison.OrdinalIgnoreCase);
        }

        private static CallableMethodDto[] BuildCallableMethods(Type componentType)
        {
            var methods = componentType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttributes(typeof(UnityMcpCallableAttribute), true).Length > 0)
                .ToArray();

            return methods
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .Select(method =>
                {
                    var attribute = method.GetCustomAttributes(typeof(UnityMcpCallableAttribute), true)
                        .OfType<UnityMcpCallableAttribute>()
                        .First();
                    return new CallableMethodDto
                    {
                        name = method.Name,
                        returnType = FriendlyTypeName(method.ReturnType),
                        allowInEditMode = attribute.AllowInEditMode,
                        hasExposedOverloads = methods.Count(other => string.Equals(other.Name, method.Name, StringComparison.Ordinal)) > 1,
                        parameters = method.GetParameters()
                            .Select(parameter => new CallableParameterDto
                            {
                                name = parameter.Name,
                                type = FriendlyTypeName(parameter.ParameterType)
                            })
                            .ToArray()
                    };
                })
                .ToArray();
        }

        private static bool TryConvertArgument(string json, Type declaredType, out object value, out string error)
        {
            value = null;
            error = null;
            var text = string.IsNullOrWhiteSpace(json) ? "null" : json.Trim();
            var nullableType = Nullable.GetUnderlyingType(declaredType);
            var targetType = nullableType ?? declaredType;

            if (string.Equals(text, "null", StringComparison.Ordinal))
            {
                if (!declaredType.IsValueType || nullableType != null)
                {
                    return true;
                }

                error = "null 不能赋给 " + FriendlyTypeName(declaredType) + "。";
                return false;
            }

            try
            {
                if (targetType == typeof(string))
                {
                    value = ParseJsonString(text);
                    return true;
                }

                if (targetType == typeof(char))
                {
                    var parsed = ParseJsonString(text);
                    if (parsed == null || parsed.Length != 1)
                    {
                        error = "char 参数必须是单个字符的 JSON 字符串。";
                        return false;
                    }

                    value = parsed[0];
                    return true;
                }

                if (targetType == typeof(bool))
                {
                    if (!bool.TryParse(text, out var boolean))
                    {
                        error = "需要 JSON boolean。";
                        return false;
                    }

                    value = boolean;
                    return true;
                }

                if (IsNumericType(targetType))
                {
                    value = Convert.ChangeType(text, targetType, CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType.IsEnum)
                {
                    if (text.StartsWith("\"", StringComparison.Ordinal))
                    {
                        value = Enum.Parse(targetType, ParseJsonString(text), true);
                    }
                    else
                    {
                        var enumValue = Convert.ChangeType(text, Enum.GetUnderlyingType(targetType), CultureInfo.InvariantCulture);
                        value = Enum.ToObject(targetType, enumValue);
                    }
                    return true;
                }

                if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
                {
                    var instanceId = ParseObjectReferenceInstanceId(text);
                    var referencedObject = EditorUtility.InstanceIDToObject(instanceId);
                    if (referencedObject == null || !targetType.IsInstanceOfType(referencedObject))
                    {
                        error = "实例 ID " + instanceId + " 不是有效的 " + FriendlyTypeName(targetType) + "。";
                        return false;
                    }

                    value = referencedObject;
                    return true;
                }

                if (targetType == typeof(object) || targetType.IsInterface || targetType.IsAbstract || targetType.IsArray)
                {
                    error = "不支持动态 object、接口、抽象类型或数组参数。";
                    return false;
                }

                if (!targetType.IsSerializable)
                {
                    error = FriendlyTypeName(targetType) + " 未标记为可序列化。";
                    return false;
                }

                value = JsonUtility.FromJson(text, targetType);
                if (value == null && targetType.IsValueType)
                {
                    error = "JsonUtility 无法创建目标值。";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ParseJsonString(string json)
        {
            if (json.Length < 2 || json[0] != '"' || json[json.Length - 1] != '"')
            {
                throw new FormatException("需要 JSON 字符串。");
            }

            var wrapper = JsonUtility.FromJson<JsonStringWrapperDto>("{\"value\":" + json + "}");
            if (wrapper == null)
            {
                throw new FormatException("需要 JSON 字符串。");
            }
            return wrapper.value;
        }

        private static int ParseObjectReferenceInstanceId(string json)
        {
            if (int.TryParse(json, NumberStyles.Integer, CultureInfo.InvariantCulture, out var directId))
            {
                return directId;
            }

            var reference = JsonUtility.FromJson<UnityObjectReferenceArgumentDto>(json);
            if (reference == null || reference.instanceId == 0)
            {
                throw new FormatException("UnityEngine.Object 参数必须是实例 ID，或 {\"instanceId\": 123}。");
            }
            return reference.instanceId;
        }

        private static bool IsSupportedReturnType(Type returnType)
        {
            if (returnType == typeof(void))
            {
                return true;
            }

            if (typeof(IEnumerator).IsAssignableFrom(returnType) || typeof(Task).IsAssignableFrom(returnType))
            {
                return false;
            }

            returnType = Nullable.GetUnderlyingType(returnType) ?? returnType;

            if (returnType == typeof(string) || returnType == typeof(char) || returnType == typeof(bool)
                || IsNumericType(returnType) || returnType.IsEnum
                || typeof(UnityEngine.Object).IsAssignableFrom(returnType))
            {
                return true;
            }

            return returnType != typeof(object)
                && !returnType.IsInterface
                && !returnType.IsAbstract
                && !returnType.IsArray
                && returnType.IsSerializable;
        }

        private static string SerializeReturnValue(object value, Type returnType)
        {
            if (returnType == typeof(void) || value == null)
            {
                return "null";
            }

            returnType = Nullable.GetUnderlyingType(returnType) ?? returnType;

            if (returnType == typeof(string) || returnType == typeof(char))
            {
                return SimpleJson.Serialize(value.ToString());
            }

            if (returnType == typeof(bool))
            {
                return (bool)value ? "true" : "false";
            }

            if (IsNumericType(returnType))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            if (returnType.IsEnum)
            {
                return SimpleJson.Serialize(value.ToString());
            }

            if (value is UnityEngine.Object unityObject)
            {
                return SimpleJson.Serialize(new UnityObjectReferenceResultDto
                {
                    instanceId = unityObject.GetInstanceID(),
                    name = unityObject.name,
                    type = unityObject.GetType().FullName ?? unityObject.GetType().Name
                });
            }

            return JsonUtility.ToJson(value);
        }

        private static bool IsNumericType(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte)
                || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint)
                || type == typeof(long) || type == typeof(ulong)
                || type == typeof(float) || type == typeof(double)
                || type == typeof(decimal);
        }

        private static string FriendlyTypeName(Type type)
        {
            return type.FullName ?? type.Name;
        }
    }
}
#endif
