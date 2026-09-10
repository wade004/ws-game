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
        /// 精确整数工厂（V-02 根治，见第十七方深度审核 V-02/V-03）：为 <paramref name="value"/> 构造一个
        /// 带原始文本的 <see cref="JsonNumber"/>，<see cref="RawNumberText"/> 用不变文化的十进制文本
        /// （<c>value.ToString(CultureInfo.InvariantCulture)</c>），使 <see cref="JsonWriter"/> 按原文
        /// 写出、<see cref="TryGetInt64"/> 按原文解析——覆盖 <see cref="long"/> 全值域的精确往返（包括
        /// <see cref="double"/> 无法精确表示的 2^53 以上整数，如 <c>9007199254740993</c>）；不像
        /// <see cref="JsonNumber(double)"/> 单参数构造那样退化为按 <see cref="double"/> 存值。
        /// 任何需要把 <see cref="long"/>/<see cref="int"/> 精确写入 JSON（存档、事件序列化等）的调用方
        /// 应优先用本工厂而不是 <c>new JsonNumber(longValue)</c>——后者经 <c>long</c> 到 <c>double</c>
        /// 的隐式转换即丢失 2^53 以上的精度，参见 <c>core/gameplay/economy/core/CurrencyPersistable.cs</c>
        /// 判断记录。
        /// </summary>
        public static JsonNumber FromInt64(long value)
        {
            return new JsonNumber(value, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 要求 <see cref="RawNumberText"/>（若有）或 <see cref="Value"/> 表示一个不带小数点/指数的
        /// 整数，且落在 <see cref="long"/> 范围内；否则返回 false。优先按原始文本判断（避免 double
        /// 精度损失误判大整数），没有原始文本时退化为按 <see cref="Value"/> 是否为整数值判断。
        /// <para>
        /// 判断记录（V-03 根治，第十七方深度审核）：没有原始文本时，<see cref="Value"/> 已经是一个
        /// <see cref="double"/>，其可精确表示、且落在 <see cref="long"/> 定义域内的整数上界是
        /// <c>2^63 - 1024</c>（<see cref="long.MaxValue"/> 本身不能被 double 精确表示，会先舍入成
        /// <c>2^63</c>）——用 <c>Value &lt;= long.MaxValue</c> 这种写法时，<c>long.MaxValue</c> 会被
        /// 隐式转换成 double 常量 <c>2^63</c>（double 有效位数不够表示 <c>2^63-1</c>，就近舍入到
        /// <c>2^63</c>），导致 <c>Value == 2^63</c> 这个本不在 long 范围内的值被误判为可转换，
        /// <c>(long)Value</c> 再把 <c>2^63</c> 转换为 long 时发生溢出回绕，实际拿到
        /// <c>long.MinValue</c>（未定义行为，.NET 的 unchecked 转换语义是回绕）。修复：改用严格小于
        /// 2^63 的双精度字面量上界 <c>9223372036854775808.0</c>（即数学意义的 2^63，恰好可被 double
        /// 精确表示，作为半开区间的排他上界）与下界 <c>-9223372036854775808.0</c>（即 long.MinValue，
        /// 同样可被 double 精确表示，是闭区间下界，因为 long 范围本身就是
        /// <c>[-2^63, 2^63-1]</c>，而 -2^63 可精确表示、可安全转换），构成
        /// <c>[-2^63, 2^63)</c> 半开区间；区间端点判断之后仍需 <c>Math.Floor(Value) == Value</c> 确认
        /// 是整数值、<c>double.IsFinite(Value)</c> 排除 NaN/±Infinity（Infinity 落在区间外自然被前面
        /// 的比较排除，NaN 与任何比较结果都是 false，同样自然被排除，这里显式调用只为可读性与两处判断
        /// 记录对齐）。带原始文本路径不受影响，继续按文本精确解析，不经过 double 中转。
        /// </para>
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

            if (double.IsFinite(Value) && Value >= -9223372036854775808.0 && Value < 9223372036854775808.0 && Math.Floor(Value) == Value)
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
