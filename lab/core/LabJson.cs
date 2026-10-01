using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>
    /// 脚本、指纹、夹具文件共用的 JSON 小工具：基于框架自己的 <see cref="JsonValue"/> 树与
    /// <see cref="JsonWriter"/>（键保持插入顺序、数字按不变文化往返格式、换行固定 LF），因此同一份内存结构
    /// 写出的字节永远相同——"逻辑组逐字节一致"的字节级比较依赖这一点。内核不使用任何依赖系统文化或
    /// 字典枚举顺序的序列化器（<c>AGENTS.md</c> 第 3 节确定性要求）。
    /// </summary>
    public static class LabJson
    {
        public static JsonNumber Num(double value) => new JsonNumber(value);

        public static JsonNumber Num(int value) => JsonNumber.FromInt64(value);

        public static JsonNumber Num(long value) => JsonNumber.FromInt64(value);

        public static JsonString Str(string value) => new JsonString(value);

        public static JsonBool Bool(bool value) => JsonBool.Of(value);

        public static JsonObject Vec(Vec2 value) =>
            new JsonObjectBuilder().Add("x", Num(value.X)).Add("y", Num(value.Y)).Build();

        /// <summary>写出文本：两空格缩进、LF、末尾补一个换行。</summary>
        public static string Write(JsonValue value) => JsonWriter.Write(value, JsonWriterOptions.Default) + "\n";

        public static JsonObject ParseObject(string text, string what)
        {
            JsonValue parsed;
            try
            {
                parsed = JsonReader.Parse(text);
            }
            catch (JsonParseException ex)
            {
                throw new LabFormatException($"{what} 不是合法 JSON：{ex.Message}");
            }

            return parsed as JsonObject ?? throw new LabFormatException($"{what} 的顶层必须是对象");
        }

        public static string RequireString(JsonObject obj, string key, string what)
        {
            if (obj.TryGetValue(key, out var v) && v is JsonString s)
            {
                return s.Value;
            }

            throw new LabFormatException($"{what} 缺少字符串字段 \"{key}\"");
        }

        public static string? OptionalString(JsonObject obj, string key, string what)
        {
            if (!obj.TryGetValue(key, out var v) || v is JsonNull)
            {
                return null;
            }

            return v is JsonString s ? s.Value : throw new LabFormatException($"{what} 的字段 \"{key}\" 必须是字符串");
        }

        public static double RequireNumber(JsonObject obj, string key, string what)
        {
            if (obj.TryGetValue(key, out var v) && v is JsonNumber n)
            {
                return n.Value;
            }

            throw new LabFormatException($"{what} 缺少数值字段 \"{key}\"");
        }

        public static int RequireInt(JsonObject obj, string key, string what)
        {
            if (obj.TryGetValue(key, out var v) && v is JsonNumber n && n.TryGetInt64(out var l) &&
                l >= int.MinValue && l <= int.MaxValue)
            {
                return (int)l;
            }

            throw new LabFormatException($"{what} 缺少整数字段 \"{key}\"");
        }

        public static JsonObject RequireObject(JsonObject obj, string key, string what)
        {
            if (obj.TryGetValue(key, out var v) && v is JsonObject o)
            {
                return o;
            }

            throw new LabFormatException($"{what} 缺少对象字段 \"{key}\"");
        }

        public static JsonArray RequireArray(JsonObject obj, string key, string what)
        {
            if (obj.TryGetValue(key, out var v) && v is JsonArray a)
            {
                return a;
            }

            throw new LabFormatException($"{what} 缺少数组字段 \"{key}\"");
        }

        public static Vec2 ReadVec(JsonValue value, string what)
        {
            if (value is JsonObject o)
            {
                return new Vec2(RequireNumber(o, "x", what), RequireNumber(o, "y", what));
            }

            throw new LabFormatException($"{what} 必须是 {{x, y}} 对象");
        }

        /// <summary>不变文化的往返数值文本，用于人读的差异报告。</summary>
        public static string Fmt(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>实验室输入文件（脚本、指纹、基线）格式或内容非法。</summary>
    public sealed class LabFormatException : Exception
    {
        public LabFormatException(string message)
            : base(message)
        {
        }
    }
}
