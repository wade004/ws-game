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
