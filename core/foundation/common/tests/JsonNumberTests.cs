using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Foundation.Common.Json
{
    /// <summary>
    /// 第十七方深度审核 V-02/V-03 根治验收：<see cref="JsonNumber.TryGetInt64"/> 没有
    /// <see cref="JsonNumber.RawNumberText"/> 时的 double 边界判定、<see cref="JsonNumber.FromInt64"/>
    /// 精确整数工厂。<see cref="JsonReaderTests"/> 已覆盖"经 <see cref="JsonReader"/> 解析出的带原始
    /// 文本数字"路径，本文件专注直接构造（不经解析）的 <see cref="JsonNumber"/>。
    /// </summary>
    public class JsonNumberTests
    {
        // -----------------------------------------------------------------
        // V-03：TryGetInt64 没有 RawNumberText 时的 double 边界（[-2^63, 2^63)）
        // -----------------------------------------------------------------

        /// <summary>V-03 复现原文：修复前 <c>Value &lt;= long.MaxValue</c> 里 <c>long.MaxValue</c>
        /// 被隐式转换成 double 常量 2^63（double 精度不够表示 2^63-1），使得直接构造的
        /// <c>new JsonNumber(9223372036854775808d)</c>（数学意义的 2^63，double 能精确表示）被误判
        /// 为"可转换"，<c>(long)Value</c> 再溢出回绕成 <c>long.MinValue</c>。修复后 2^63 必须落在区间
        /// 外，返回 false。</summary>
        [Fact]
        public void TryGetInt64_NoRawText_ExactlyTwoPow63_ReturnsFalse()
        {
            var n = new JsonNumber(9223372036854775808d); // 2^63，double 精确表示，但超出 long 范围
            Assert.False(n.TryGetInt64(out var value));
            Assert.Equal(0L, value);
        }

        /// <summary>2^63 之前最近的、能被 double 精确表示的整数（2^63 - 1024，double 在该量级的
        /// 精度间隔是 1024）：应落在 long 范围内、可转换。</summary>
        [Fact]
        public void TryGetInt64_NoRawText_JustBelowTwoPow63_ReturnsTrueAndExact()
        {
            const long expected = 9223372036854774784L; // 2^63 - 1024
            var n = new JsonNumber((double)expected);
            Assert.True(n.TryGetInt64(out var value));
            Assert.Equal(expected, value);
        }

        /// <summary>下界：-2^63（long.MinValue）本身可被 double 精确表示，是闭区间下界，必须接受。</summary>
        [Fact]
        public void TryGetInt64_NoRawText_NegativeTwoPow63_ReturnsTrueAndEqualsLongMinValue()
        {
            var n = new JsonNumber(-9223372036854775808d);
            Assert.True(n.TryGetInt64(out var value));
            Assert.Equal(long.MinValue, value);
        }

        /// <summary>略低于下界（更负）的值必须拒绝，与上界对称。判断记录：double 在
        /// <c>[2^63, 2^64)</c> 量级的 ULP 是 2048（不是 <c>[2^62, 2^63)</c> 量级的 1024——同一测试
        /// 文件里"刚好低于 2^63 上界"用例的 ULP 与这里不对称，是浮点表示本身的性质，不是笔误），
        /// 所以这里用 -2^63 - 2048（而不是 -2^63 - 1024，后者在两个相邻可表示 double 之间的中点，
        /// 编译期字面量转换会按银行家舍入就近取整，可能意外落回 -2^63 本身，误让这条"应该拒绝"的
        /// 用例失去意义）。</summary>
        [Fact]
        public void TryGetInt64_NoRawText_BelowNegativeTwoPow63_ReturnsFalse()
        {
            var n = new JsonNumber(-9223372036854777856d); // -2^63 - 2048，该量级下精确可表示
            Assert.False(n.TryGetInt64(out _));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void TryGetInt64_NoRawText_NonFiniteValue_ReturnsFalse(double value)
        {
            var n = new JsonNumber(value);
            Assert.False(n.TryGetInt64(out _));
        }

        [Fact]
        public void TryGetInt64_NoRawText_FractionalValue_ReturnsFalse()
        {
            var n = new JsonNumber(1.5);
            Assert.False(n.TryGetInt64(out _));
        }

        /// <summary>带原始文本的 long.MaxValue 精确接受对照——不受 V-03 修复影响，continued 走
        /// 原始文本分支，不经过 double 中转（对照 audit 报告"raw text 的 long.MaxValue 精确接受
        /// 对照保留"）。</summary>
        [Fact]
        public void TryGetInt64_RawText_LongMaxValue_ReturnsTrueAndExact()
        {
            var n = (JsonNumber)JsonReader.Parse(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(n.TryGetInt64(out var value));
            Assert.Equal(long.MaxValue, value);
        }

        /// <summary>带原始文本的 2^63（超出 long.MaxValue 一）仍必须拒绝——原始文本分支本就按
        /// long.TryParse 判定，不受本轮修复影响，这里只是确认修复没有意外放宽这条已有边界。</summary>
        [Fact]
        public void TryGetInt64_RawText_TwoPow63_ReturnsFalse()
        {
            var n = (JsonNumber)JsonReader.Parse("9223372036854775808");
            Assert.False(n.TryGetInt64(out var value));
            Assert.Equal(0L, value);
        }

        // -----------------------------------------------------------------
        // V-02：JsonNumber.FromInt64 精确整数工厂
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(9007199254740993L)] // 2^53 + 1，超出 double 精确整数表示上界
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        public void FromInt64_RoundTripsThroughWriteAndParse_ExactValue(long expected)
        {
            var n = JsonNumber.FromInt64(expected);
            Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), n.RawNumberText);

            var text = JsonWriter.Write(n);
            Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), text);

            var parsed = (JsonNumber)JsonReader.Parse(text);
            Assert.True(parsed.TryGetInt64(out var roundtripped));
            Assert.Equal(expected, roundtripped);
        }

        [Fact]
        public void FromInt64_ProducesValueConsistentWithRawText()
        {
            // Value 本身仍是 double（可能对超大整数有精度损失），但 TryGetInt64 优先按 RawNumberText
            // 解析，不依赖 Value 的精度——这里确认两者仍然一致（同一个数转 double 再转回不产生偏差），
            // 只是文档化 FromInt64 的 Value 字段不是"权威值"，RawNumberText 才是。
            var n = JsonNumber.FromInt64(9007199254740993L);
            Assert.Equal((double)9007199254740993L, n.Value);
            Assert.True(n.TryGetInt64(out var value));
            Assert.Equal(9007199254740993L, value);
        }
    }
}
