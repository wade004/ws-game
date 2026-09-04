using System;
using System.Globalization;
using System.Text;

namespace Core.Foundation.Common.Json
{
    /// <summary>
    /// 零依赖、纯手写的 JSON 读取器（RFC 8259 子集，不用任何 JSON 库、不使用反射）。
    /// 支持完整的对象/数组/字符串（含 <c>\uXXXX</c> 转义与代理对）/数字（含指数、<c>-0</c>）/
    /// 布尔/<c>null</c>；拒绝尾随逗号、注释、<c>NaN</c>/<c>Infinity</c>、结尾多余内容；
    /// 允许并跳过开头的 UTF-8 BOM；递归深度上限 <see cref="MaxDepth"/>（防止恶意/畸形输入
    /// 导致栈溢出）。错误统一抛 <see cref="JsonParseException"/>，携带 1 起计数的行号/列号。
    /// </summary>
    public static class JsonReader
    {
        /// <summary>对象/数组嵌套深度上限，超出即报错，不递归到栈溢出。</summary>
        public const int MaxDepth = 256;

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var state = new ReaderState(text);
            state.SkipBom();
            state.SkipWhitespace();
            var value = state.ParseValue(0);
            state.SkipWhitespace();
            if (!state.AtEnd)
            {
                throw state.Error("JSON 文本结尾存在多余内容");
            }
            return value;
        }

        private sealed class ReaderState
        {
            private readonly string _text;
            private int _pos;
            private int _line = 1;
            private int _column = 1;

            public ReaderState(string text)
            {
                _text = text;
            }

            public bool AtEnd => _pos >= _text.Length;

            public void SkipBom()
            {
                if (_text.Length > 0 && _text[0] == BomChar)
                {
                    Advance();
                }
            }

            /// <summary>UTF-8 BOM 解码为文本后对应的 U+FEFF ZERO WIDTH NO-BREAK SPACE。
            /// 用数值转义而非字面量字符书写，避免源文件编码差异导致误判。</summary>
            private const char BomChar = (char)0xFEFF;

            private char Current => _text[_pos];

            private char Advance()
            {
                var c = _text[_pos];
                _pos++;
                if (c == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }
                return c;
            }

            public JsonParseException Error(string message) => new JsonParseException(_line, _column, message);

            public void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    var c = Current;
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        Advance();
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public JsonValue ParseValue(int depth)
            {
                if (AtEnd) throw Error("表达式提前结束，期望一个 JSON 值");

                var c = Current;
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return new JsonString(ParseStringLiteral());
                    case 't': return ParseLiteral("true", JsonBool.True);
                    case 'f': return ParseLiteral("false", JsonBool.False);
                    case 'n': return ParseLiteral("null", JsonNull.Instance);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ParseNumber();
                        }
                        throw Error($"未预期的字符 '{c}'，期望一个 JSON 值（对象/数组/字符串/数字/true/false/null；不支持注释、NaN、Infinity）");
                }
            }

            private JsonValue ParseLiteral(string literal, JsonValue result)
            {
                for (int i = 0; i < literal.Length; i++)
                {
                    if (AtEnd || Current != literal[i])
                    {
                        throw Error($"非法记号，期望字面量 \"{literal}\"");
                    }
                    Advance();
                }
                return result;
            }

            private JsonObject ParseObject(int depth)
            {
                if (depth >= MaxDepth) throw Error($"JSON 嵌套深度超过上限 {MaxDepth}");

                Advance(); // consume '{'
                var entries = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, JsonValue>>();
                var index = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);

                SkipWhitespace();
                if (!AtEnd && Current == '}')
                {
                    Advance();
                    return new JsonObject(entries, index);
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || Current != '"')
                    {
                        throw Error("对象的键必须是双引号包裹的字符串");
                    }

                    var keyLine = _line;
                    var keyColumn = _column;
                    var key = ParseStringLiteral();

                    SkipWhitespace();
                    if (AtEnd || Current != ':')
                    {
                        throw Error("对象键后缺少 ':'");
                    }
                    Advance(); // consume ':'
                    SkipWhitespace();

                    var value = ParseValue(depth + 1);

                    if (index.ContainsKey(key))
                    {
                        throw new JsonParseException(keyLine, keyColumn, $"对象中键 \"{key}\" 重复");
                    }
                    index.Add(key, entries.Count);
                    entries.Add(new System.Collections.Generic.KeyValuePair<string, JsonValue>(key, value));

                    SkipWhitespace();
                    if (AtEnd) throw Error("对象未闭合，缺少 '}'");

                    if (Current == ',')
                    {
                        Advance();
                        SkipWhitespace();
                        if (!AtEnd && Current == '}')
                        {
                            throw Error("不支持尾随逗号（对象 '}' 前多余的 ','）");
                        }
                        continue;
                    }

                    if (Current == '}')
                    {
                        Advance();
                        return new JsonObject(entries, index);
                    }

                    throw Error("对象内缺少 ',' 或 '}'");
                }
            }

            private JsonArray ParseArray(int depth)
            {
                if (depth >= MaxDepth) throw Error($"JSON 嵌套深度超过上限 {MaxDepth}");

                Advance(); // consume '['
                var items = new System.Collections.Generic.List<JsonValue>();

                SkipWhitespace();
                if (!AtEnd && Current == ']')
                {
                    Advance();
                    return new JsonArray(items);
                }

                while (true)
                {
                    SkipWhitespace();
                    var value = ParseValue(depth + 1);
                    items.Add(value);

                    SkipWhitespace();
                    if (AtEnd) throw Error("数组未闭合，缺少 ']'");

                    if (Current == ',')
                    {
                        Advance();
                        SkipWhitespace();
                        if (!AtEnd && Current == ']')
                        {
                            throw Error("不支持尾随逗号（数组 ']' 前多余的 ','）");
                        }
                        continue;
                    }

                    if (Current == ']')
                    {
                        Advance();
                        return new JsonArray(items);
                    }

                    throw Error("数组内缺少 ',' 或 ']'");
                }
            }

            private string ParseStringLiteral()
            {
                var startLine = _line;
                var startColumn = _column;
                Advance(); // consume opening '"'

                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw new JsonParseException(startLine, startColumn, "字符串未闭合，缺少结尾的 '\"'");
                    }

                    var c = Advance();

                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c == '\\')
                    {
                        if (AtEnd) throw Error("字符串转义序列不完整");
                        var esc = Advance();
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                sb.Append(ParseUnicodeEscape());
                                break;
                            default:
                                throw Error($"非法转义序列 '\\{esc}'");
                        }
                        continue;
                    }

                    if (c < 0x20)
                    {
                        throw Error($"字符串中包含未转义的控制字符 (0x{(int)c:X2})，必须转义");
                    }

                    sb.Append(c);
                }
            }

            /// <summary>解析 <c>\uXXXX</c>：4 位十六进制码元；高代理项后紧跟合法的 <c>\uXXXX</c>
            /// 低代理项时按代理对拼接（两个 char 依次写入即为 .NET 字符串的合法表示），否则原样
            /// 保留该码元（RFC 8259 不强制校验代理对的合法性，本读取器不做额外拒绝）。</summary>
            private char ParseUnicodeEscape()
            {
                if (_pos + 4 > _text.Length) throw Error("\\u 转义序列后必须跟 4 位十六进制数字");

                int code = 0;
                for (int i = 0; i < 4; i++)
                {
                    var h = Advance();
                    int digit;
                    if (h >= '0' && h <= '9') digit = h - '0';
                    else if (h >= 'a' && h <= 'f') digit = h - 'a' + 10;
                    else if (h >= 'A' && h <= 'F') digit = h - 'A' + 10;
                    else throw Error("\\u 转义序列包含非法十六进制字符");
                    code = (code << 4) | digit;
                }

                return (char)code;
            }

            private JsonNumber ParseNumber()
            {
                var start = _pos;

                if (!AtEnd && Current == '-')
                {
                    Advance();
                }

                if (AtEnd || !IsDigit(Current))
                {
                    throw Error("非法数字：'-' 后必须跟数字");
                }

                if (Current == '0')
                {
                    Advance();
                    if (!AtEnd && IsDigit(Current))
                    {
                        throw Error("非法数字：不允许前导零（如 \"01\"）");
                    }
                }
                else
                {
                    while (!AtEnd && IsDigit(Current))
                    {
                        Advance();
                    }
                }

                if (!AtEnd && Current == '.')
                {
                    Advance();
                    if (AtEnd || !IsDigit(Current))
                    {
                        throw Error("非法数字：小数点后必须至少一位数字");
                    }
                    while (!AtEnd && IsDigit(Current))
                    {
                        Advance();
                    }
                }

                if (!AtEnd && (Current == 'e' || Current == 'E'))
                {
                    Advance();
                    if (!AtEnd && (Current == '+' || Current == '-'))
                    {
                        Advance();
                    }
                    if (AtEnd || !IsDigit(Current))
                    {
                        throw Error("非法数字：指数部分必须至少一位数字");
                    }
                    while (!AtEnd && IsDigit(Current))
                    {
                        Advance();
                    }
                }

                var raw = _text.Substring(start, _pos - start);
                var value = double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                return new JsonNumber(value, raw);
            }

            private static bool IsDigit(char c) => c >= '0' && c <= '9';
        }
    }
}
