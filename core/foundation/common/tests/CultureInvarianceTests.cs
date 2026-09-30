using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Xunit;

namespace Tests.Foundation.Culture
{
    /// <summary>
    /// 测试覆盖梳理 T-H13 / ADR-0125 D20：foundation 各文本化路径在非不变文化（de-DE 小数逗号、tr-TR
    /// i 大小写、sv-SE 负号 U+2212）下必须与不变文化下逐字节相同（AGENTS 第 3 节"浮点格式化固定用不变文化"）。
    /// 每条用例：基准在 <c>CultureScope.Run("", …)</c>（不变文化）下算出，再与目标文化下的结果逐字节比较；
    /// 能由规则直接拼出的期望（如 Vec2 文本）同时用规则校验，不写裸数。
    /// </summary>
    public sealed class CultureInvarianceTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------------------------------------------------------------- D20：Vec2 / Rect

        /// <summary>D20 复现：修复前 <c>Vec2.ToString</c> 用 <c>$"({X}, {Y})"</c> 随当前文化变（de-DE 下
        /// 得到 "(-1234,5, 0,25)"），本用例在 de-DE/tr-TR/sv-SE 下均应失败；固定 InvariantCulture 后通过。</summary>
        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void Vec2_ToString_IsByteIdenticalUnderNonInvariantCulture(string culture)
        {
            var v = new Vec2(-1234.5, 0.25);
            var expected = "(" + v.X.ToString(Inv) + ", " + v.Y.ToString(Inv) + ")";

            var baseline = CultureScope.Run("", () => v.ToString());
            var actual = CultureScope.Run(culture, () => v.ToString());

            Assert.Equal(expected, baseline);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void Rect_ToString_IsByteIdenticalUnderNonInvariantCulture(string culture)
        {
            var r = new Rect(new Vec2(-1.5, -2.75), new Vec2(1000.125, 0.001));
            var expected =
                "[(" + r.Min.X.ToString(Inv) + ", " + r.Min.Y.ToString(Inv) + ") .. (" +
                r.Max.X.ToString(Inv) + ", " + r.Max.Y.ToString(Inv) + ")]";

            var baseline = CultureScope.Run("", () => r.ToString());
            var actual = CultureScope.Run(culture, () => r.ToString());

            Assert.Equal(expected, baseline);
            Assert.Equal(expected, actual);
        }

        // ---------------------------------------------------------------- 其它 ToString 覆写（T-H13 顺带发现的同类缺陷）

        /// <summary>本次排查 <c>override ToString</c> 时发现 <c>SimStep</c>/<c>AnimClipEventSpec</c> 与 D20 同类：
        /// 用默认插值拼 double，随当前文化变（de-DE 下 "Continuous(dt=0,0166667)"）。已一并固定 InvariantCulture。</summary>
        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void SimStepAndAnimClipEventSpec_ToString_AreByteIdenticalUnderNonInvariantCulture(string culture)
        {
            const double dt = 0.0166667;
            const double timePct = 0.375;
            var step = Core.Foundation.SimLoop.SimStep.Continuous(dt);
            var evt = new Core.Foundation.DisplayInfo.AnimClipEventSpec("hit_frame", timePct);

            var expectedStep = "Continuous(dt=" + dt.ToString(Inv) + ")";
            var expectedEvt = "hit_frame@" + timePct.ToString("0.###", Inv);

            Assert.Equal(expectedStep, CultureScope.Run("", () => step.ToString()));
            Assert.Equal(expectedStep, CultureScope.Run(culture, () => step.ToString()));
            Assert.Equal(expectedEvt, CultureScope.Run("", () => evt.ToString()));
            Assert.Equal(expectedEvt, CultureScope.Run(culture, () => evt.ToString()));
        }

        // ---------------------------------------------------------------- JsonWriter / JsonReader

        private static JsonValue BuildSampleJson() =>
            new JsonObjectBuilder()
                .Add("half", new JsonNumber(0.5))
                .Add("neg", new JsonNumber(-1234.5))
                .Add("tiny", new JsonNumber(1e-7))
                .Add("huge", new JsonNumber(1.5e21))
                .Add("third", new JsonNumber(1.0 / 3.0))
                .Add("big_int", JsonNumber.FromInt64(9007199254740993L))
                .Add("neg_int", JsonNumber.FromInt64(-42))
                .Add("raw", new JsonNumber(1.5, "1.50"))
                .Add("text", new JsonString("Iiİı é\u0001"))
                .Add("arr", new JsonArray(new List<JsonValue> { new JsonNumber(2.25), JsonBool.True, JsonNull.Instance }))
                .Build();

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void JsonWriter_Write_IsByteIdenticalUnderNonInvariantCulture(string culture)
        {
            var value = BuildSampleJson();
            var options = new JsonWriterOptions { EscapeNonAscii = true };

            var baseline = CultureScope.Run("", () => JsonWriter.Write(value));
            var baselineEscaped = CultureScope.Run("", () => JsonWriter.Write(value, options));
            var actual = CultureScope.Run(culture, () => JsonWriter.Write(value));

            Assert.Equal(baseline, actual);
            Assert.Equal(baselineEscaped, CultureScope.Run(culture, () => JsonWriter.Write(value, options)));
            // 小数点必须是 '.'，不能出现小数逗号形式
            Assert.Contains("\"half\": 0.5", baseline);
            Assert.DoesNotContain("0,5", actual);
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void JsonReader_Parse_SameValuesAndRoundTripUnderNonInvariantCulture(string culture)
        {
            const string text = "{\"a\":1.5,\"b\":-0.001,\"c\":1e3,\"d\":[2.25,-7],\"e\":123456789012345678}";

            var baseline = CultureScope.Run("", () => JsonReader.Parse(text));
            var parsed = CultureScope.Run(culture, () => JsonReader.Parse(text));

            var obj = Assert.IsType<JsonObject>(parsed);
            Assert.Equal(1.5, ((JsonNumber)obj["a"]).Value);
            Assert.Equal(-0.001, ((JsonNumber)obj["b"]).Value);
            Assert.Equal(1000.0, ((JsonNumber)obj["c"]).Value);
            Assert.True(((JsonNumber)obj["e"]).TryGetInt64(out var e));
            Assert.Equal(123456789012345678L, e);

            // 两种文化下解析出的树写出后逐字节相同，且再解析再写出是不动点
            var baselineText = CultureScope.Run("", () => JsonWriter.Write(baseline));
            Assert.Equal(baselineText, CultureScope.Run(culture, () => JsonWriter.Write(parsed)));
            Assert.Equal(
                baselineText,
                CultureScope.Run(culture, () => JsonWriter.Write(JsonReader.Parse(JsonWriter.Write(parsed)))));
        }

        // ---------------------------------------------------------------- ExprLexer / ExprParser / ExprValue

        private static string DescribeTokens(string text)
        {
            var sb = new StringBuilder();
            foreach (var t in ExprLexer.Tokenize(text))
            {
                sb.Append(t.Kind).Append('|').Append(t.Text).Append('|').Append(t.Start).Append('|')
                    .Append(t.Length).Append('|').Append(t.IntValue.ToString(Inv)).Append('|')
                    .Append(t.NumberValue.ToString("R", Inv)).Append(';');
            }

            return sb.ToString();
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void ExprLexer_Tokenize_IsIdenticalUnderNonInvariantCulture(string culture)
        {
            const string text =
                "self.hp_pct < 0.3 and player.level >= 10 or not (target.range == 7.125) and \"Iiİı\" != \"x\"";

            var baseline = CultureScope.Run("", () => DescribeTokens(text));
            var actual = CultureScope.Run(culture, () => DescribeTokens(text));

            Assert.Equal(baseline, actual);

            // 数字字面量按不变文化语法解析："0.3" 的 NumberValue 必须是 0.3，不是 3 或解析失败
            var tokens = CultureScope.Run(culture, () => ExprLexer.Tokenize("0.3"));
            Assert.Equal(0.3, tokens[0].NumberValue);
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void ExprEvaluate_AndExprValueToString_AreIdenticalUnderNonInvariantCulture(string culture)
        {
            var schema = new ExprSchema()
                .Register("self", "hp_pct", ExprValueKind.Number)
                .Register("self", "range", ExprValueKind.Number);

            string Produce()
            {
                var node = ExprParser.Parse("self.hp_pct < 0.3 and self.range >= 2.25", schema);
                var host = new CultureHost(0.25, 2.5);
                var diagnostics = new ExprDiagnosticsRecorder();
                var result = ExprEvaluator.Evaluate(node, host, diagnostics);
                var errors = new List<string>();
                foreach (var error in diagnostics.Errors)
                {
                    errors.Add(error.Message);
                }

                return result + "|" + ExprValue.OfNumber(0.3) + "|" + ExprValue.OfNumber(-1234.5) + "|" +
                       ExprValue.OfInt(-1234567) + "|" + ExprValue.OfNumber(3.0) + "|" + string.Join(";", errors);
            }

            var baseline = CultureScope.Run("", Produce);
            var actual = CultureScope.Run(culture, Produce);

            Assert.Equal(baseline, actual);
            Assert.StartsWith("true|0.3|-1234.5|-1234567|", baseline);
        }

        private sealed class CultureHost : IExprHost
        {
            private readonly double _hp;
            private readonly double _range;

            public CultureHost(double hp, double range)
            {
                _hp = hp;
                _range = range;
            }

            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) =>
                key == "hp_pct" ? ExprValue.OfNumber(_hp) : ExprValue.OfNumber(_range);
        }

        // ---------------------------------------------------------------- RngStreamState

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void RngStreamState_ToStringAndParse_AreIdenticalUnderNonInvariantCulture(string culture)
        {
            var state = new RngStreamState(0xFEDCBA9876543210UL, 1UL, 0xABCDEFUL, ulong.MaxValue);
            var expected = string.Join("-", new[]
            {
                state.S0.ToString("x16", Inv), state.S1.ToString("x16", Inv),
                state.S2.ToString("x16", Inv), state.S3.ToString("x16", Inv),
            });

            var baseline = CultureScope.Run("", () => state.ToString());
            var actual = CultureScope.Run(culture, () => state.ToString());

            Assert.Equal(expected, baseline);
            Assert.Equal(expected, actual);
            Assert.Equal(state, CultureScope.Run(culture, () => RngStreamState.Parse(baseline)));
            Assert.True(CultureScope.Run(culture, () => RngStreamState.TryParse(baseline, out var parsed) && parsed == state));
        }
    }
}
