using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Foundation.Common.Json
{
    public class JsonReaderTests
    {
        [Fact]
        public void Parse_Null()
        {
            var v = JsonReader.Parse("null");
            Assert.Equal(JsonKind.Null, v.Kind);
        }

        [Fact]
        public void Parse_BoolTrueAndFalse()
        {
            Assert.True(((JsonBool)JsonReader.Parse("true")).Value);
            Assert.False(((JsonBool)JsonReader.Parse("false")).Value);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("-0", 0)]
        [InlineData("123", 123)]
        [InlineData("-42", -42)]
        public void Parse_IntegerNumbers(string text, long expected)
        {
            var n = (JsonNumber)JsonReader.Parse(text);
            Assert.True(n.TryGetInt64(out var value));
            Assert.Equal(expected, value);
        }

        [Fact]
        public void Parse_FractionAndExponentNumbers()
        {
            var n1 = (JsonNumber)JsonReader.Parse("1.5");
            Assert.Equal(1.5, n1.Value);
            Assert.False(n1.TryGetInt64(out _));

            var n2 = (JsonNumber)JsonReader.Parse("1e3");
            Assert.Equal(1000.0, n2.Value);

            var n3 = (JsonNumber)JsonReader.Parse("-2.5E-2");
            Assert.Equal(-0.025, n3.Value, 10);
        }

        [Fact]
        public void Parse_NumberRawTextPreserved()
        {
            var n = (JsonNumber)JsonReader.Parse("1.50");
            Assert.Equal("1.50", n.RawNumberText);
        }

        [Fact]
        public void Parse_LeadingZeroRejected()
        {
            Assert.Throws<JsonParseException>(() => JsonReader.Parse("01"));
        }

        // -----------------------------------------------------------------
        // F-01 根治（2026-09-10 codex 第十六轮 schema 审计，schema-findings.md）：JSON 语法合法但
        // double.Parse 求值结果非有限（Infinity/-Infinity）时拒绝，指数写法本身继续正常支持。
        // -----------------------------------------------------------------

        /// <summary>F-01 复现原文：<c>JsonReader.Parse("1e309")</c> 此前成功返回
        /// <c>JsonNumber</c>，<c>Value</c> 是 <c>+Infinity</c>；<c>"-1e309"</c> 对称得到
        /// <c>-Infinity</c>。两者语法都合法（数字 + 指数），JSON 语义里没有 Infinity，必须拒绝。</summary>
        [Theory]
        [InlineData("1e309")]
        [InlineData("-1e309")]
        public void Parse_ExponentOverflowToInfinity_Throws(string text)
        {
            var ex = Assert.Throws<JsonParseException>(() => JsonReader.Parse(text));
            Assert.Equal(1, ex.Line);
            Assert.Equal(1, ex.Column); // 起始列：错误定位在数字 token 开头，不是解析完之后的位置
        }

        [Fact]
        public void Parse_LargeDecimalLiteralOverflowToInfinity_Throws()
        {
            var text = new string('9', 500) + ".1";
            Assert.Throws<JsonParseException>(() => JsonReader.Parse(text));
        }

        [Fact]
        public void Parse_ExponentOverflow_ErrorPositionAfterLeadingContent()
        {
            var ex = Assert.Throws<JsonParseException>(() => JsonReader.Parse("{\"value\": 1e309}"));
            Assert.Equal(11, ex.Column); // "value": 后数字开头列号
        }

        /// <summary>普通指数写法（含负指数、正常量级）继续被接受，不受有限性检查误伤。</summary>
        [Theory]
        [InlineData("1e5", 100000.0)]
        [InlineData("1.5e-3", 0.0015)]
        [InlineData("-0", 0.0)]
        public void Parse_FiniteExponentAndSignedZero_StillAccepted(string text, double expected)
        {
            var n = (JsonNumber)JsonReader.Parse(text);
            Assert.Equal(expected, n.Value, 12);
        }

        /// <summary>2^53 + 1，超出 double 精确表示整数的边界，但仍是有限值——不应被有限性检查误伤；
        /// <see cref="JsonNumber.TryGetInt64"/> 走 <see cref="JsonNumber.RawNumberText"/> 分支精确
        /// 还原，不经过 <c>Value</c> 的精度损失。</summary>
        [Fact]
        public void Parse_LargeSafeIntegerBeyondDoublePrecision_StillAccepted()
        {
            var n = (JsonNumber)JsonReader.Parse("9007199254740993");
            Assert.True(n.TryGetInt64(out var value));
            Assert.Equal(9007199254740993L, value);
        }

        [Fact]
        public void Parse_SimpleString()
        {
            var s = (JsonString)JsonReader.Parse("\"hello\"");
            Assert.Equal("hello", s.Value);
        }

        [Fact]
        public void Parse_StringEscapes()
        {
            var s = (JsonString)JsonReader.Parse("\"a\\\"b\\\\c\\/d\\n\\t\\r\\b\\f\"");
            Assert.Equal("a\"b\\c/d\n\t\r\b\f", s.Value);
        }

        [Fact]
        public void Parse_UnicodeEscape()
        {
            var s = (JsonString)JsonReader.Parse("\"\\u4e2d\\u6587\"");
            Assert.Equal("中文", s.Value);
        }

        [Fact]
        public void Parse_SurrogatePairEscape()
        {
            // U+1F600 GRINNING FACE, encoded as surrogate pair 😀.
            var s = (JsonString)JsonReader.Parse("\"\\ud83d\\ude00\"");
            Assert.Equal("😀", s.Value);
        }

        [Fact]
        public void Parse_EmptyArrayAndObject()
        {
            var arr = (JsonArray)JsonReader.Parse("[]");
            Assert.Empty(arr);

            var obj = (JsonObject)JsonReader.Parse("{}");
            Assert.Empty(obj);
        }

        [Fact]
        public void Parse_NestedArrayAndObject()
        {
            var root = (JsonObject)JsonReader.Parse("{\"a\": [1, 2, {\"b\": true}], \"c\": null}");
            var a = (JsonArray)root["a"];
            Assert.Equal(3, a.Count);
            var inner = (JsonObject)a[2];
            Assert.True(((JsonBool)inner["b"]).Value);
            Assert.Equal(JsonKind.Null, root["c"].Kind);
        }

        [Fact]
        public void Parse_ObjectPreservesKeyInsertionOrder()
        {
            var obj = (JsonObject)JsonReader.Parse("{\"z\": 1, \"a\": 2, \"m\": 3}");
            var keys = new System.Collections.Generic.List<string>(obj.Keys);
            Assert.Equal(new[] { "z", "a", "m" }, keys);
        }

        [Fact]
        public void Parse_DuplicateKeyThrows()
        {
            var ex = Assert.Throws<JsonParseException>(() => JsonReader.Parse("{\"a\": 1, \"a\": 2}"));
            Assert.Contains("重复", ex.Message);
        }

        [Fact]
        public void Parse_TrailingCommaInArrayRejected()
        {
            Assert.Throws<JsonParseException>(() => JsonReader.Parse("[1, 2, ]"));
        }

        [Fact]
        public void Parse_TrailingCommaInObjectRejected()
        {
            Assert.Throws<JsonParseException>(() => JsonReader.Parse("{\"a\": 1, }"));
        }

        [Fact]
        public void Parse_CommentsRejected()
        {
            Assert.Throws<JsonParseException>(() => JsonReader.Parse("// comment\n{}"));
        }

        [Fact]
        public void Parse_NaNAndInfinityRejected()
        {
            Assert.Throws<JsonParseException>(() => JsonReader.Parse("NaN"));
            Assert.Throws<JsonParseException>(() => JsonReader.Parse("Infinity"));
        }

        [Fact]
        public void Parse_TrailingExtraContentRejected()
        {
            Assert.Throws<JsonParseException>(() => JsonReader.Parse("{} {}"));
        }

        [Fact]
        public void Parse_TruncatedInputThrows()
        {
            Assert.Throws<JsonParseException>(() => JsonReader.Parse("{\"a\": "));
            Assert.Throws<JsonParseException>(() => JsonReader.Parse("\"unterminated"));
        }

        [Fact]
        public void Parse_DepthLimitExceededThrows()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < JsonReader.MaxDepth + 10; i++) sb.Append('[');
            for (int i = 0; i < JsonReader.MaxDepth + 10; i++) sb.Append(']');
            Assert.Throws<JsonParseException>(() => JsonReader.Parse(sb.ToString()));
        }

        [Fact]
        public void Parse_BomIsSkipped()
        {
            var text = "﻿{\"a\": 1}";
            var obj = (JsonObject)JsonReader.Parse(text);
            Assert.True(obj.ContainsKey("a"));
        }

        [Fact]
        public void Parse_ErrorReportsLineAndColumn()
        {
            var ex = Assert.Throws<JsonParseException>(() => JsonReader.Parse("{\n  \"a\": ,\n}"));
            Assert.Equal(2, ex.Line);
        }

        [Fact]
        public void Parse_RoundTripThroughWriterProducesEquivalentTree()
        {
            var text = "{\"a\": 1, \"b\": [true, false, null, \"x\"], \"c\": {\"d\": 2.5}}";
            var value = JsonReader.Parse(text);
            var written = JsonWriter.Write(value);
            var reparsed = JsonReader.Parse(written);

            var obj1 = (JsonObject)value;
            var obj2 = (JsonObject)reparsed;
            Assert.Equal(obj1.Count, obj2.Count);
            Assert.True(((JsonNumber)obj2["a"]).TryGetInt64(out var aVal));
            Assert.Equal(1, aVal);
        }
    }
}
