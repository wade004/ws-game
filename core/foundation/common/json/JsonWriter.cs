using System;
using System.Globalization;
using System.Text;

namespace Core.Foundation.Common.Json
{
    /// <summary>
    /// 零依赖、纯手写的 JSON 写出器：把 <see cref="JsonValue"/> 树序列化为文本，输出确定性
    /// （对象按键的插入顺序、数组按元素顺序，不做任何重排序），供数据表往返（读入 → 迁移/
    /// 编辑 → 写回）与调试打印使用。
    /// </summary>
    public static class JsonWriter
    {
        public static string Write(JsonValue value, JsonWriterOptions? options = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            var opts = options ?? JsonWriterOptions.Default;
            var sb = new StringBuilder();
            WriteValue(sb, value, opts, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, JsonValue value, JsonWriterOptions opts, int depth)
        {
            switch (value)
            {
                case JsonNull _:
                    sb.Append("null");
                    break;

                case JsonBool b:
                    sb.Append(b.Value ? "true" : "false");
                    break;

                case JsonNumber n:
                    sb.Append(FormatNumber(n));
                    break;

                case JsonString s:
                    WriteString(sb, s.Value, opts);
                    break;

                case JsonArray arr:
                    WriteArray(sb, arr, opts, depth);
                    break;

                case JsonObject obj:
                    WriteObject(sb, obj, opts, depth);
                    break;

                default:
                    throw new InvalidOperationException($"未知的 JsonValue 具体类型：{value.GetType().Name}");
            }
        }

        private static void WriteArray(StringBuilder sb, JsonArray arr, JsonWriterOptions opts, int depth)
        {
            if (arr.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            var pretty = opts.Indent > 0;
            sb.Append('[');
            for (int i = 0; i < arr.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (pretty)
                {
                    sb.Append(opts.NewLine);
                    AppendIndent(sb, opts, depth + 1);
                }
                WriteValue(sb, arr[i], opts, depth + 1);
            }
            if (pretty)
            {
                sb.Append(opts.NewLine);
                AppendIndent(sb, opts, depth);
            }
            sb.Append(']');
        }

        private static void WriteObject(StringBuilder sb, JsonObject obj, JsonWriterOptions opts, int depth)
        {
            if (obj.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            var pretty = opts.Indent > 0;
            sb.Append('{');
            var isFirst = true;
            foreach (var entry in obj)
            {
                if (!isFirst) sb.Append(',');
                isFirst = false;
                if (pretty)
                {
                    sb.Append(opts.NewLine);
                    AppendIndent(sb, opts, depth + 1);
                }
                WriteString(sb, entry.Key, opts);
                sb.Append(':');
                if (pretty) sb.Append(' ');
                WriteValue(sb, entry.Value, opts, depth + 1);
            }
            if (pretty)
            {
                sb.Append(opts.NewLine);
                AppendIndent(sb, opts, depth);
            }
            sb.Append('}');
        }

        private static void AppendIndent(StringBuilder sb, JsonWriterOptions opts, int depth)
        {
            sb.Append(' ', opts.Indent * depth);
        }

        private static string FormatNumber(JsonNumber number)
        {
            if (number.RawNumberText != null)
            {
                return number.RawNumberText;
            }

            var v = number.Value;
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                throw new InvalidOperationException("JSON 不支持写出 NaN/Infinity 数值");
            }

            if (v == Math.Floor(v) && Math.Abs(v) < 1e15)
            {
                return ((long)v).ToString(CultureInfo.InvariantCulture);
            }

            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void WriteString(StringBuilder sb, string text, JsonWriterOptions opts)
        {
            sb.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || (opts.EscapeNonAscii && c > 0x7E))
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
