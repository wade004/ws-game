using System;
using System.Globalization;

namespace Core.Foundation.Common.Json
{
    /// <summary>
    /// JSON 值的种类判别（见 <see cref="JsonValue"/>）。数字统一为 <see cref="JsonKind.Number"/>，
    /// 不区分整数/浮点——JSON 语法本身不区分，整数取用见 <see cref="JsonNumber.TryGetInt64"/>。
    /// </summary>
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// JSON 值不可变树的抽象基类（零依赖自写实现，不借助任何 JSON 库、不使用反射）。
    /// 具体形态：<see cref="JsonNull"/>、<see cref="JsonBool"/>、<see cref="JsonNumber"/>、
    /// <see cref="JsonString"/>、<see cref="JsonArray"/>、<see cref="JsonObject"/>。
    /// 由 <see cref="JsonReader.Parse"/> 产出，供 <see cref="JsonWriter.Write"/> 序列化回文本。
    /// </summary>
    public abstract class JsonValue
    {
        public abstract JsonKind Kind { get; }
    }

    /// <summary>JSON <c>null</c>。单例，比较用引用相等或 <see cref="JsonValue.Kind"/>。</summary>
    public sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();

        private JsonNull()
        {
        }

        public override JsonKind Kind => JsonKind.Null;
    }

    /// <summary>JSON <c>true</c>/<c>false</c>。</summary>
    public sealed class JsonBool : JsonValue
    {
        public static readonly JsonBool True = new JsonBool(true);
        public static readonly JsonBool False = new JsonBool(false);

        public bool Value { get; }

        private JsonBool(bool value)
        {
            Value = value;
        }

        public static JsonBool Of(bool value) => value ? True : False;

        public override JsonKind Kind => JsonKind.Bool;
    }

    /// <summary>
    /// JSON 数字。<see cref="Value"/> 统一存为 <see cref="double"/>；<see cref="RawNumberText"/>
    /// 保留原始文本（见 <see cref="JsonReader"/>"数字统一 double，另提供... 原始文本保留...便于
    /// 无损回写"），供 <see cref="JsonWriter"/> 优先原样回写、避免 double 往返丢精度或改变格式
    /// （如 <c>1.50</c> 被规整成 <c>1.5</c>）。程序构造（非解析出的）数字没有原始文本，
    /// <see cref="RawNumberText"/> 为 null，写出时按 <see cref="Value"/> 用规范格式格式化。
    /// </summary>
    public sealed class JsonNumber : JsonValue
    {
        public double Value { get; }

        /// <summary>解析出该数字时的原始文本（如 <c>"1.50"</c>、<c>"-0"</c>、<c>"1e3"</c>）；
        /// 程序直接构造的数字没有原始文本，此时为 null。</summary>
        public string? RawNumberText { get; }

        public JsonNumber(double value)
        {
            Value = value;
            RawNumberText = null;
        }

        public JsonNumber(double value, string rawNumberText)
        {
            Value = value;
            RawNumberText = rawNumberText ?? throw new ArgumentNullException(nameof(rawNumberText));
        }

        public override JsonKind Kind => JsonKind.Number;

        /// <summary>
        /// 要求 <see cref="RawNumberText"/>（若有）或 <see cref="Value"/> 表示一个不带小数点/指数的
        /// 整数，且落在 <see cref="long"/> 范围内；否则返回 false。优先按原始文本判断（避免 double
        /// 精度损失误判大整数），没有原始文本时退化为按 <see cref="Value"/> 是否为整数值判断。
        /// </summary>
        public bool TryGetInt64(out long value)
        {
            if (RawNumberText != null)
            {
                if (RawNumberText.IndexOf('.') < 0 && RawNumberText.IndexOf('e') < 0 && RawNumberText.IndexOf('E') < 0)
                {
                    return long.TryParse(RawNumberText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
                }

                value = 0;
                return false;
            }

            if (Value >= long.MinValue && Value <= long.MaxValue && Math.Floor(Value) == Value && !double.IsInfinity(Value))
            {
                value = (long)Value;
                return true;
            }

            value = 0;
            return false;
        }
    }

    /// <summary>JSON 字符串（已完成转义解码后的文本）。</summary>
    public sealed class JsonString : JsonValue
    {
        public string Value { get; }

        public JsonString(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public override JsonKind Kind => JsonKind.String;
    }
}
