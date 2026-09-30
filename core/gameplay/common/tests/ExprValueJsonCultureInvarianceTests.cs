using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Core.Gameplay.Common;
using Tests.Gameplay.Culture;
using Xunit;

namespace Tests.Gameplay.Common
{
    /// <summary>
    /// 测试覆盖梳理 T-H13：<see cref="ExprValueJson"/> 的 ToJson/Parse 往返（RewardBundle/dialog 的 world_flag
    /// 值、存档段值）在非不变文化（de-DE 小数逗号、tr-TR、sv-SE 负号 U+2212）下必须与不变文化下逐字节相同，
    /// 且读回的 <see cref="ExprValue"/> 与原值相等（Int/Number 的区分不因文化丢失）。
    /// </summary>
    public sealed class ExprValueJsonCultureInvarianceTests
    {
        private static IEnumerable<ExprValue> Samples()
        {
            yield return ExprValue.OfBool(true);
            yield return ExprValue.OfInt(0);
            yield return ExprValue.OfInt(-42);
            yield return ExprValue.OfInt(9007199254740993L); // 2^53 + 1
            yield return ExprValue.OfInt(long.MinValue + 1);
            yield return ExprValue.OfNumber(0.1);
            yield return ExprValue.OfNumber(-1234.5);
            yield return ExprValue.OfNumber(3.0); // 必须带 ".0" 才能与 Int 区分
            yield return ExprValue.OfNumber(1e-7);
            yield return ExprValue.OfNumber(1.5e21);
            yield return ExprValue.OfNumber(1.0 / 3.0);
            yield return ExprValue.OfString("Iiİı, 1.5");
            yield return ExprValue.OfId(new Id("quest.sample_culture_id"));
        }

        private static string WriteAll()
        {
            var parts = new List<string>();
            foreach (var value in Samples())
            {
                parts.Add(JsonWriter.Write(ExprValueJson.ToJson(value)));
            }

            return string.Join("\n", parts);
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void ToJson_Text_IsByteIdenticalUnderNonInvariantCulture(string culture)
        {
            var baseline = CultureScope.Run("", WriteAll);
            var actual = CultureScope.Run(culture, WriteAll);

            Assert.Equal(baseline, actual);
            Assert.DoesNotContain("0,1", actual);
            Assert.Contains("3.0", baseline);
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void RoundTrip_ValuesEqualOriginalUnderNonInvariantCulture(string culture)
        {
            using (CultureScope.Enter(culture))
            {
                foreach (var value in Samples())
                {
                    var text = JsonWriter.Write(ExprValueJson.ToJson(value));
                    var roundTripped = ExprValueJson.Parse(JsonReader.Parse(text));

                    Assert.Equal(value, roundTripped);
                    Assert.Equal(value.Kind, roundTripped.Kind);
                    Assert.True(ExprValueJson.IsValid(JsonReader.Parse(text)));
                }
            }
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void Parse_DecimalTextFromJson_IsReadWithInvariantSyntaxUnderNonInvariantCulture(string culture)
        {
            using (CultureScope.Enter(culture))
            {
                Assert.Equal(ExprValue.OfNumber(2.5), ExprValueJson.Parse(JsonReader.Parse("2.5")));
                Assert.Equal(ExprValue.OfNumber(-0.125), ExprValueJson.Parse(JsonReader.Parse("-0.125")));
                Assert.Equal(ExprValue.OfInt(-7), ExprValueJson.Parse(JsonReader.Parse("-7")));
            }
        }
    }
}
