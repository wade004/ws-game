using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Foundation.Common.Json
{
    public class JsonWriterTests
    {
        [Fact]
        public void Write_Null()
        {
            Assert.Equal("null", JsonWriter.Write(JsonNull.Instance));
        }

        [Fact]
        public void Write_Bool()
        {
            Assert.Equal("true", JsonWriter.Write(JsonBool.True));
            Assert.Equal("false", JsonWriter.Write(JsonBool.False));
        }

        [Fact]
        public void Write_IntegerNumberWithoutRawText()
        {
            Assert.Equal("5", JsonWriter.Write(new JsonNumber(5.0)));
        }

        [Fact]
        public void Write_PreservesRawNumberText()
        {
            var n = new JsonNumber(1.5, "1.50");
            Assert.Equal("1.50", JsonWriter.Write(n));
        }

        [Fact]
        public void Write_StringEscapesQuotesAndBackslashes()
        {
            var s = new JsonString("a\"b\\c\nd");
            Assert.Equal("\"a\\\"b\\\\c\\nd\"", JsonWriter.Write(s));
        }

        [Fact]
        public void Write_NonAsciiKeptByDefault()
        {
            var s = new JsonString("中文");
            Assert.Equal("\"中文\"", JsonWriter.Write(s));
        }

        [Fact]
        public void Write_NonAsciiEscapedWhenRequested()
        {
            var s = new JsonString("中");
            var options = new JsonWriterOptions { EscapeNonAscii = true };
            Assert.Equal("\"\\u4e2d\"", JsonWriter.Write(s, options));
        }

        [Fact]
        public void Write_EmptyArrayAndObjectAreCompact()
        {
            Assert.Equal("[]", JsonWriter.Write(new JsonArray()));

            var builder = new JsonObjectBuilder();
            Assert.Equal("{}", JsonWriter.Write(builder.Build()));
        }

        [Fact]
        public void Write_ObjectPreservesInsertionOrderAndUsesTwoSpaceIndent()
        {
            var builder = new JsonObjectBuilder();
            builder.Add("z", new JsonNumber(1.0));
            builder.Add("a", new JsonNumber(2.0));
            var obj = builder.Build();

            var expected = "{\n  \"z\": 1,\n  \"a\": 2\n}";
            Assert.Equal(expected, JsonWriter.Write(obj));
        }

        [Fact]
        public void Write_ArrayUsesConfiguredNewLine()
        {
            var arr = new JsonArray(new JsonValue[] { new JsonNumber(1.0), new JsonNumber(2.0) });
            var expected = "[\r\n  1,\r\n  2\r\n]";
            var options = new JsonWriterOptions { NewLine = "\r\n" };
            Assert.Equal(expected, JsonWriter.Write(arr, options));
        }

        [Fact]
        public void Write_NaNThrows()
        {
            var n = new JsonNumber(double.NaN);
            Assert.Throws<System.InvalidOperationException>(() => JsonWriter.Write(n));
        }

        [Fact]
        public void Write_ThenParse_RoundTripsDeterministically()
        {
            var builder = new JsonObjectBuilder();
            builder.Add("table", new JsonString("stat.definition"));
            builder.Add("schema_version", new JsonNumber(1.0));
            var rowsBuilder = new JsonObjectBuilder();
            rowsBuilder.Add("id", new JsonString("stat.strength"));
            var rows = new JsonArray(new JsonValue[] { rowsBuilder.Build() });
            builder.Add("rows", rows);
            var root = builder.Build();

            var text1 = JsonWriter.Write(root);
            var reparsed = JsonReader.Parse(text1);
            var text2 = JsonWriter.Write(reparsed);

            Assert.Equal(text1, text2);
        }
    }
}
