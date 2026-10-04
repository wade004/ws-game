using System;
using System.Collections.Generic;
using System.Text;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>
    /// JSON 文本的"外科式"编辑与行差异（调参面板"写回数据表"用，ADR-0150）：只改要改的那个值（或在对象/数组末尾追加一项），
    /// 其余字节（缩进、注释式空行、字段顺序、数字写法）原样不动，所以生成的差异只含这次调参真正改的几行，可以直接走正常评审与提交。
    /// <para>
    /// 判断记录（为什么不整体解析再重写）：整体重写会重排格式、改写数字的书写（<c>0.0</c> → <c>0</c>），让一行调参变成整文件差异，没法评审；
    /// 数据表是人维护的文本，写回工具必须保守。这里的扫描器只认标准 JSON（对象、数组、字符串、标量），不认注释与尾逗号——数据表本来就是标准 JSON。
    /// </para>
    /// </summary>
    internal sealed class JsonSpan
    {
        /// <summary>'o' 对象、'a' 数组、'v' 标量（字符串、数字、布尔、null）。</summary>
        public char Kind { get; }

        public int Start { get; }

        /// <summary>结束位置（不含）。</summary>
        public int End { get; internal set; }

        public List<JsonMember> Members { get; } = new List<JsonMember>();

        public List<JsonSpan> Items { get; } = new List<JsonSpan>();

        internal JsonSpan(char kind, int start)
        {
            Kind = kind;
            Start = start;
        }

        public JsonSpan? Get(string key)
        {
            foreach (var m in Members)
            {
                if (string.Equals(m.Key, key, StringComparison.Ordinal))
                {
                    return m.Value;
                }
            }

            return null;
        }

        public string Text(string source) => source.Substring(Start, End - Start);

        /// <summary>标量字符串的值（去引号；含转义的字符串按 JSON 规则还原）；非字符串标量返回 null。</summary>
        public string? StringValue(string source)
        {
            if (Kind != 'v' || End - Start < 2 || source[Start] != '"')
            {
                return null;
            }

            return ((JsonString)JsonReader.Parse(Text(source))).Value;
        }
    }

    internal sealed class JsonMember
    {
        public string Key { get; }

        public int KeyStart { get; }

        public JsonSpan Value { get; }

        public JsonMember(string key, int keyStart, JsonSpan value)
        {
            Key = key;
            KeyStart = keyStart;
            Value = value;
        }
    }

    internal static class TextPatch
    {
        public static JsonSpan Parse(string text)
        {
            var pos = 0;
            var root = ParseValue(text, ref pos);
            SkipWs(text, ref pos);
            if (pos != text.Length)
            {
                throw new LabFormatException("JSON 文本末尾有多余内容，位置 " + pos);
            }

            return root;
        }

        private static void SkipWs(string t, ref int p)
        {
            while (p < t.Length && (t[p] == ' ' || t[p] == '\t' || t[p] == '\r' || t[p] == '\n'))
            {
                p++;
            }
        }

        private static JsonSpan ParseValue(string t, ref int p)
        {
            SkipWs(t, ref p);
            if (p >= t.Length)
            {
                throw new LabFormatException("JSON 文本意外结束");
            }

            var c = t[p];
            if (c == '{')
            {
                var span = new JsonSpan('o', p);
                p++;
                SkipWs(t, ref p);
                if (t[p] == '}')
                {
                    p++;
                    span.End = p;
                    return span;
                }

                while (true)
                {
                    SkipWs(t, ref p);
                    var keyStart = p;
                    var keySpan = ParseString(t, ref p);
                    var key = ((JsonString)JsonReader.Parse(t.Substring(keySpan.Item1, keySpan.Item2 - keySpan.Item1))).Value;
                    SkipWs(t, ref p);
                    if (t[p] != ':')
                    {
                        throw new LabFormatException("JSON 对象成员缺冒号，位置 " + p);
                    }

                    p++;
                    var value = ParseValue(t, ref p);
                    span.Members.Add(new JsonMember(key, keyStart, value));
                    SkipWs(t, ref p);
                    if (t[p] == ',')
                    {
                        p++;
                        continue;
                    }

                    if (t[p] == '}')
                    {
                        p++;
                        span.End = p;
                        return span;
                    }

                    throw new LabFormatException("JSON 对象格式错误，位置 " + p);
                }
            }

            if (c == '[')
            {
                var span = new JsonSpan('a', p);
                p++;
                SkipWs(t, ref p);
                if (t[p] == ']')
                {
                    p++;
                    span.End = p;
                    return span;
                }

                while (true)
                {
                    span.Items.Add(ParseValue(t, ref p));
                    SkipWs(t, ref p);
                    if (t[p] == ',')
                    {
                        p++;
                        continue;
                    }

                    if (t[p] == ']')
                    {
                        p++;
                        span.End = p;
                        return span;
                    }

                    throw new LabFormatException("JSON 数组格式错误，位置 " + p);
                }
            }

            if (c == '"')
            {
                var s = ParseString(t, ref p);
                var scalar = new JsonSpan('v', s.Item1) { End = s.Item2 };
                return scalar;
            }

            var start = p;
            while (p < t.Length && t[p] != ',' && t[p] != '}' && t[p] != ']' && t[p] != ' ' && t[p] != '\t' && t[p] != '\r' && t[p] != '\n')
            {
                p++;
            }

            if (p == start)
            {
                throw new LabFormatException("JSON 值为空，位置 " + p);
            }

            return new JsonSpan('v', start) { End = p };
        }

        private static Tuple<int, int> ParseString(string t, ref int p)
        {
            if (t[p] != '"')
            {
                throw new LabFormatException("JSON 字符串缺引号，位置 " + p);
            }

            var start = p;
            p++;
            while (p < t.Length && t[p] != '"')
            {
                p += t[p] == '\\' ? 2 : 1;
            }

            if (p >= t.Length)
            {
                throw new LabFormatException("JSON 字符串没有结束引号");
            }

            p++;
            return Tuple.Create(start, p);
        }

        // ---------- 编辑（均返回新文本，原文本不变）----------

        public static string Replace(string text, JsonSpan span, string newValueText) =>
            text.Substring(0, span.Start) + newValueText + text.Substring(span.End);

        public static string NewLine(string text) => text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";

        /// <summary>某位置所在行的行首缩进（仅当该位置之前整行都是空白时返回缩进，否则返回 null 表示"同行内联"）。</summary>
        private static string? LineIndent(string text, int position)
        {
            var lineStart = position;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
            {
                lineStart--;
            }

            var indent = text.Substring(lineStart, position - lineStart);
            foreach (var ch in indent)
            {
                if (ch != ' ' && ch != '\t')
                {
                    return null;
                }
            }

            return indent;
        }

        /// <summary>在对象末尾追加成员；<paramref name="valueText"/> 若跨多行，其续行缩进由调用方按 <see cref="MemberIndent"/> 给出。</summary>
        public static string InsertMember(string text, JsonSpan obj, string key, string valueText)
        {
            var keyText = JsonWriter.Write(new JsonString(key), JsonWriterOptions.Default);
            if (obj.Members.Count == 0)
            {
                var closeIndent = LineIndent(text, obj.End - 1) ?? string.Empty;
                var nl = NewLine(text);
                var inner = closeIndent + "  ";
                return text.Substring(0, obj.Start) + "{" + nl + inner + keyText + ": " + valueText + nl + closeIndent + "}" + text.Substring(obj.End);
            }

            var last = obj.Members[obj.Members.Count - 1];
            var indent = LineIndent(text, last.KeyStart);
            var insertAt = last.Value.End;
            if (indent == null)
            {
                return text.Substring(0, insertAt) + ", " + keyText + ": " + valueText + text.Substring(insertAt);
            }

            return text.Substring(0, insertAt) + "," + NewLine(text) + indent + keyText + ": " + valueText + text.Substring(insertAt);
        }

        /// <summary>对象成员的行缩进（没有成员时按右花括号缩进 + 2 空格）；供调用方生成多行值文本。</summary>
        public static string MemberIndent(string text, JsonSpan obj)
        {
            if (obj.Members.Count > 0)
            {
                return LineIndent(text, obj.Members[obj.Members.Count - 1].KeyStart) ?? string.Empty;
            }

            return (LineIndent(text, obj.End - 1) ?? string.Empty) + "  ";
        }

        /// <summary>数组元素的行缩进（没有元素时按右方括号缩进 + 2 空格）。</summary>
        public static string ItemIndent(string text, JsonSpan array)
        {
            if (array.Items.Count > 0)
            {
                return LineIndent(text, array.Items[array.Items.Count - 1].Start) ?? string.Empty;
            }

            return (LineIndent(text, array.End - 1) ?? string.Empty) + "  ";
        }

        /// <summary>在数组末尾追加元素；<paramref name="itemText"/> 的续行缩进由调用方按 <see cref="ItemIndent"/> 给出。</summary>
        public static string InsertItem(string text, JsonSpan array, string itemText)
        {
            if (array.Items.Count == 0)
            {
                var closeIndent = LineIndent(text, array.End - 1) ?? string.Empty;
                var nl = NewLine(text);
                return text.Substring(0, array.Start) + "[" + nl + closeIndent + "  " + itemText + nl + closeIndent + "]" + text.Substring(array.End);
            }

            var last = array.Items[array.Items.Count - 1];
            var indent = LineIndent(text, last.Start);
            if (indent == null)
            {
                return text.Substring(0, last.End) + ", " + itemText + text.Substring(last.End);
            }

            return text.Substring(0, last.End) + "," + NewLine(text) + indent + itemText + text.Substring(last.End);
        }

        // ---------- 行差异 ----------

        /// <summary>
        /// 标准统一差异（<c>--- a/路径</c> / <c>+++ b/路径</c>、三行上下文），文本相同返回空串。
        /// 行以 <c>\n</c> 切分（<c>\r</c> 留在行尾，不影响比较）；用最长公共子序列求差，数据表一次调参只改几行，规模不成问题。
        /// </summary>
        public static string UnifiedDiff(string path, string oldText, string newText, int context = 3)
        {
            if (string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var a = oldText.Split('\n');
            var b = newText.Split('\n');
            var n = a.Length;
            var m = b.Length;
            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            {
                for (var j = m - 1; j >= 0; j--)
                {
                    lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            // 编辑脚本：' ' 相同、'-' 删除、'+' 新增。
            var ops = new List<KeyValuePair<char, string>>();
            {
                int i = 0, j = 0;
                while (i < n && j < m)
                {
                    if (string.Equals(a[i], b[j], StringComparison.Ordinal))
                    {
                        ops.Add(new KeyValuePair<char, string>(' ', a[i]));
                        i++;
                        j++;
                    }
                    else if (lcs[i + 1, j] >= lcs[i, j + 1])
                    {
                        ops.Add(new KeyValuePair<char, string>('-', a[i]));
                        i++;
                    }
                    else
                    {
                        ops.Add(new KeyValuePair<char, string>('+', b[j]));
                        j++;
                    }
                }

                while (i < n)
                {
                    ops.Add(new KeyValuePair<char, string>('-', a[i++]));
                }

                while (j < m)
                {
                    ops.Add(new KeyValuePair<char, string>('+', b[j++]));
                }
            }

            var sb = new StringBuilder();
            sb.Append("--- a/").Append(path).Append('\n');
            sb.Append("+++ b/").Append(path).Append('\n');

            // 按"改动行 ± 上下文"切 hunk。
            var changed = new List<int>();
            for (var k = 0; k < ops.Count; k++)
            {
                if (ops[k].Key != ' ')
                {
                    changed.Add(k);
                }
            }

            var idx = 0;
            while (idx < changed.Count)
            {
                var first = Math.Max(0, changed[idx] - context);
                var lastChanged = changed[idx];
                var next = idx + 1;
                while (next < changed.Count && changed[next] - lastChanged <= 2 * context)
                {
                    lastChanged = changed[next];
                    next++;
                }

                var end = Math.Min(ops.Count - 1, lastChanged + context);
                int oldStart = 1, newStart = 1;
                for (var k = 0; k < first; k++)
                {
                    if (ops[k].Key != '+') oldStart++;
                    if (ops[k].Key != '-') newStart++;
                }

                int oldCount = 0, newCount = 0;
                var body = new StringBuilder();
                for (var k = first; k <= end; k++)
                {
                    body.Append(ops[k].Key).Append(ops[k].Value).Append('\n');
                    if (ops[k].Key != '+') oldCount++;
                    if (ops[k].Key != '-') newCount++;
                }

                sb.Append("@@ -").Append(oldStart).Append(',').Append(oldCount).Append(" +").Append(newStart).Append(',').Append(newCount).Append(" @@\n");
                sb.Append(body);
                idx = next;
            }

            return sb.ToString();
        }
    }
}
