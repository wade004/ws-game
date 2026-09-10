using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Core.Gameplay.Common;
using Xunit;

namespace Tests.Gameplay.Common
{
    /// <summary>
    /// CORE114-04 根治验收（外部审计 audit-76d16a5-20260910，见 <see cref="ExprValueJson.Parse"/>
    /// <c>"$id"</c> 分支、<see cref="ExprValueJson.IsValid"/> 判断记录）：非法 <c>Id</c>（不满足
    /// <c>Id</c> 格式正则）必须让 <c>Parse</c> 抛 <see cref="FormatException"/>（不是 <see
    /// cref="ArgumentException"/>），<c>IsValid</c> 据此稳定返回 <c>false</c> 而不是把异常冒泡给
    /// 调用方。改造自审计探针 <c>CoreBoundaryProbe.ExprValueInvalidId</c>。
    /// </summary>
    public sealed class ExprValueJsonTests
    {
        [Fact]
        public void Parse_InvalidIdShape_ThrowsFormatExceptionNotArgumentException()
        {
            var value = JsonReader.Parse("{\"$id\":\"BAD\"}");

            // 此前 new Id(...) 直接抛 ArgumentException，与 Parse 对外承诺的"格式错误抛
            // FormatException"合同不一致（外部审计复现 parse=throws:ArgumentException）。
            var ex = Assert.Throws<FormatException>(() => ExprValueJson.Parse(value));
            Assert.Contains("BAD", ex.Message);
        }

        [Fact]
        public void IsValid_InvalidIdShape_ReturnsFalseWithoutThrowing()
        {
            var value = JsonReader.Parse("{\"$id\":\"BAD\"}");

            // 此前 IsValid 只捕获 FormatException，接不住 Id 构造期的 ArgumentException，异常原样
            // 冒泡（外部审计复现 is_valid_call=throws:ArgumentException）。
            var ex = Record.Exception(() => ExprValueJson.IsValid(value));
            Assert.Null(ex);
            Assert.False(ExprValueJson.IsValid(value));
        }

        [Fact]
        public void IsValid_ArrayShape_ReturnsFalse()
        {
            var value = JsonReader.Parse("[]");
            Assert.False(ExprValueJson.IsValid(value));
        }

        [Fact]
        public void IsValid_PlainObjectWithoutId_ReturnsFalse()
        {
            var value = JsonReader.Parse("{\"foo\": 1}");
            Assert.False(ExprValueJson.IsValid(value));
        }

        [Theory]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("1")]
        [InlineData("1.5")]
        [InlineData("\"some_text\"")]
        [InlineData("{\"$id\": \"world.cov_flag\"}")]
        public void IsValid_LegalShapes_ReturnTrueAndParseSucceeds(string json)
        {
            var value = JsonReader.Parse(json);

            Assert.True(ExprValueJson.IsValid(value));
            var parsed = ExprValueJson.Parse(value);
            Assert.True(parsed.Kind == ExprValueKind.Bool || parsed.Kind == ExprValueKind.Int ||
                        parsed.Kind == ExprValueKind.Number || parsed.Kind == ExprValueKind.String ||
                        parsed.Kind == ExprValueKind.Id);
        }

        [Fact]
        public void Parse_ValidId_RoundTripsThroughToJson()
        {
            var value = JsonReader.Parse("{\"$id\": \"world.cov_flag\"}");

            var parsed = ExprValueJson.Parse(value);

            Assert.Equal(ExprValueKind.Id, parsed.Kind);
            Assert.Equal("world.cov_flag", parsed.AsId.Value);
        }
    }
}
