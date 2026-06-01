#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;

namespace UnityMcpBridge.Editor
{
    public static partial class UnityMcpBridgeServer
    {
        private static class SimpleJson
        {
            public static string Serialize(object value)
            {
                var builder = new StringBuilder();
                WriteValue(builder, value);
                return builder.ToString();
            }

            private static void WriteValue(StringBuilder builder, object value)
            {
                if (value == null)
                {
                    builder.Append("null");
                    return;
                }

                switch (value)
                {
                    case string text:
                        WriteString(builder, text);
                        return;
                    case bool boolean:
                        builder.Append(boolean ? "true" : "false");
                        return;
                    case int:
                    case long:
                    case float:
                    case double:
                    case decimal:
                        builder.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                        return;
                    case System.Collections.IEnumerable enumerable:
                        WriteArray(builder, enumerable);
                        return;
                    default:
                        WriteObject(builder, value);
                        return;
                }
            }

            private static void WriteArray(StringBuilder builder, System.Collections.IEnumerable enumerable)
            {
                builder.Append('[');
                var first = true;
                foreach (var item in enumerable)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    WriteValue(builder, item);
                    first = false;
                }

                builder.Append(']');
            }

            private static void WriteObject(StringBuilder builder, object value)
            {
                builder.Append('{');
                var first = true;
                var fields = value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);

                foreach (var field in fields)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    WriteString(builder, field.Name);
                    builder.Append(':');
                    WriteValue(builder, field.GetValue(value));
                    first = false;
                }

                builder.Append('}');
            }

            private static void WriteString(StringBuilder builder, string value)
            {
                builder.Append('"');
                foreach (var ch in value)
                {
                    switch (ch)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (ch < ' ')
                            {
                                builder.Append("\\u");
                                builder.Append(((int)ch).ToString("x4"));
                            }
                            else
                            {
                                builder.Append(ch);
                            }
                            break;
                    }
                }

                builder.Append('"');
            }
        }
    }
}
#endif
