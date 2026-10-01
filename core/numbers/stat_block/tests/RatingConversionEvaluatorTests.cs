using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    /// <summary>
    /// <see cref="RatingConversionEvaluator"/> 纯函数的直接用例，以及 <see cref="StatModifier.DefaultMultGroup"/>
    /// 与 stat_block 各校验规则元数据（<c>RuleId</c>/<c>DefaultSeverity</c>/<c>NonEscalatable</c>）的直接断言
    /// （T-L4，2026-10-01 测试覆盖第四批）。此前这些只经 <c>StatHost</c>/<c>ItemBudgetCurve</c> 间接触及，
    /// 求值器自身的退化分支（空曲线、除数为零、饱和封顶、反函数的无穷大）没有任何直接保证。
    /// 期望值全部由公式在用例里算出，不写裸数。
    /// </summary>
    public sealed class RatingConversionEvaluatorTests
    {
        private static PiecewiseCurve Curve(params (double Level, double PointsPerPercent)[] points)
        {
            var list = new CurvePoint[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                list[i] = new CurvePoint(points[i].Level, points[i].PointsPerPercent);
            }

            return new PiecewiseCurve(list);
        }

        // -----------------------------------------------------------------
        // ToPercent
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public void ToPercent_Breakpoints_DividesRawByInterpolatedPointsPerPercent(int level)
        {
            var curve = Curve((1, 10), (10, 100));
            const double raw = 45.0;

            var percent = RatingConversionEvaluator.ToPercent(RatingConversionShape.Breakpoints, curve, 0, 0, raw, level);

            Assert.Equal(raw / curve.Evaluate(level), percent, 12);
        }

        [Fact]
        public void ToPercent_Breakpoints_NullOrEmptyCurve_IsPassThrough()
        {
            Assert.Equal(7.5, RatingConversionEvaluator.ToPercent(RatingConversionShape.Breakpoints, null, 0, 0, 7.5, 3));
            Assert.Equal(7.5, RatingConversionEvaluator.ToPercent(RatingConversionShape.Breakpoints, PiecewiseCurve.Empty, 0, 0, 7.5, 3));
        }

        [Fact]
        public void ToPercent_Breakpoints_ZeroPointsPerPercent_DegradesToZeroInsteadOfThrowing()
        {
            var curve = Curve((1, 0), (10, 0));

            Assert.Equal(0.0, RatingConversionEvaluator.ToPercent(RatingConversionShape.Breakpoints, curve, 0, 0, 50, 5));
        }

        [Fact]
        public void ToPercent_Breakpoints_IgnoresKAndCap()
        {
            var curve = Curve((1, 20), (10, 20));

            var a = RatingConversionEvaluator.ToPercent(RatingConversionShape.Breakpoints, curve, 1, 0.1, 40, 4);
            var b = RatingConversionEvaluator.ToPercent(RatingConversionShape.Breakpoints, curve, 999, 999, 40, 4);

            Assert.Equal(a, b);
            Assert.Equal(40.0 / 20.0, a, 12);
        }

        [Fact]
        public void ToPercent_Saturation_IgnoresCurve_AndUsesFormula()
        {
            const double k = 5.0;
            const double raw = 30.0;
            const int level = 4;
            const double cap = 0.9;
            var junkCurve = Curve((1, 1), (10, 1));

            var withCurve = RatingConversionEvaluator.ToPercent(RatingConversionShape.Saturation, junkCurve, k, cap, raw, level);
            var withoutCurve = RatingConversionEvaluator.ToPercent(RatingConversionShape.Saturation, null, k, cap, raw, level);

            Assert.Equal(withoutCurve, withCurve);
            Assert.Equal(raw / (raw + k * level), withCurve, 12);
        }

        // -----------------------------------------------------------------
        // EvaluateSaturation
        // -----------------------------------------------------------------

        [Fact]
        public void EvaluateSaturation_ResultAboveCap_IsClampedToCap()
        {
            const double k = 1.0;
            const int level = 1;
            const double raw = 1000.0;
            var uncapped = raw / (raw + k * level);
            var cap = uncapped / 2.0;

            Assert.Equal(cap, RatingConversionEvaluator.EvaluateSaturation(raw, level, k, cap));
        }

        [Fact]
        public void EvaluateSaturation_ZeroRaw_IsZero()
        {
            Assert.Equal(0.0, RatingConversionEvaluator.EvaluateSaturation(0.0, 5, 2.0, 1.0));
        }

        [Fact]
        public void EvaluateSaturation_NonPositiveDenominator_DegradesToZero()
        {
            // rawValue + k * level == 0（此时直接除会得到 NaN / 无穷）
            Assert.Equal(0.0, RatingConversionEvaluator.EvaluateSaturation(-10.0, 5, 2.0, 1.0));
            // rawValue + k * level < 0
            Assert.Equal(0.0, RatingConversionEvaluator.EvaluateSaturation(-100.0, 5, 2.0, 1.0));
            // level 0 且 raw 0
            Assert.Equal(0.0, RatingConversionEvaluator.EvaluateSaturation(0.0, 0, 2.0, 1.0));
        }

        [Fact]
        public void EvaluateSaturation_NegativeRawWithPositiveDenominator_ClampsNegativeResultToZero()
        {
            const double raw = -1.0;
            const double k = 5.0;
            const int level = 4;
            Assert.True(raw + k * level > 0.0);
            Assert.True(raw / (raw + k * level) < 0.0);

            Assert.Equal(0.0, RatingConversionEvaluator.EvaluateSaturation(raw, level, k, 1.0));
        }

        [Fact]
        public void EvaluateSaturation_IsMonotonicInRaw_UntilCap()
        {
            const double k = 3.0;
            const int level = 7;
            var previous = -1.0;
            foreach (var raw in new[] { 0.0, 1.0, 10.0, 100.0, 1000.0 })
            {
                var value = RatingConversionEvaluator.EvaluateSaturation(raw, level, k, 1.0);
                Assert.True(value > previous, $"raw={raw}");
                Assert.True(value < 1.0);
                previous = value;
            }
        }

        // -----------------------------------------------------------------
        // ToPoints（反函数）
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(1)]
        [InlineData(6)]
        [InlineData(10)]
        public void ToPoints_Breakpoints_IsPercentTimesPointsPerPercent(int level)
        {
            var curve = Curve((1, 10), (10, 100));
            const double percent = 2.5;

            var points = RatingConversionEvaluator.ToPoints(RatingConversionShape.Breakpoints, curve, 0, 0, percent, level);

            Assert.Equal(percent * curve.Evaluate(level), points, 12);
        }

        [Fact]
        public void ToPoints_Breakpoints_NullOrEmptyCurve_IsPassThrough()
        {
            Assert.Equal(3.25, RatingConversionEvaluator.ToPoints(RatingConversionShape.Breakpoints, null, 0, 0, 3.25, 2));
            Assert.Equal(3.25, RatingConversionEvaluator.ToPoints(RatingConversionShape.Breakpoints, PiecewiseCurve.Empty, 0, 0, 3.25, 2));
        }

        [Fact]
        public void ToPoints_Breakpoints_ZeroPointsPerPercent_ReturnsZero()
        {
            var curve = Curve((1, 0), (10, 0));

            Assert.Equal(0.0, RatingConversionEvaluator.ToPoints(RatingConversionShape.Breakpoints, curve, 0, 0, 12.0, 5));
        }

        [Theory]
        [InlineData(1, 2.0)]
        [InlineData(5, 8.0)]
        [InlineData(20, 0.5)]
        public void RoundTrip_Breakpoints_ToPointsThenToPercent_RestoresPercent(int level, double percent)
        {
            var curve = Curve((1, 10), (20, 200));

            var points = RatingConversionEvaluator.ToPoints(RatingConversionShape.Breakpoints, curve, 0, 0, percent, level);
            var back = RatingConversionEvaluator.ToPercent(RatingConversionShape.Breakpoints, curve, 0, 0, points, level);

            Assert.Equal(percent, back, 9);
        }

        [Theory]
        [InlineData(1, 0.1)]
        [InlineData(5, 0.5)]
        [InlineData(20, 0.75)]
        public void RoundTrip_Saturation_ToPointsThenToPercent_RestoresPercent(int level, double percent)
        {
            const double k = 4.0;
            const double cap = 1.0;

            var points = RatingConversionEvaluator.ToPoints(RatingConversionShape.Saturation, null, k, cap, percent, level);
            var back = RatingConversionEvaluator.ToPercent(RatingConversionShape.Saturation, null, k, cap, points, level);

            Assert.Equal(percent, back, 9);
        }

        [Fact]
        public void ToPoints_Saturation_Formula_IsPercentTimesKLevelOverOneMinusPercent()
        {
            const double percent = 0.4;
            const double k = 5.0;
            const int level = 3;

            var points = RatingConversionEvaluator.ToPoints(RatingConversionShape.Saturation, null, k, 1.0, percent, level);

            Assert.Equal(percent * k * level / (1.0 - percent), points, 12);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.5)]
        [InlineData(-1000.0)]
        public void ToPoints_Saturation_NonPositivePercent_IsZero(double percent)
        {
            Assert.Equal(0.0, RatingConversionEvaluator.ToPoints(RatingConversionShape.Saturation, null, 5.0, 1.0, percent, 3));
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        [InlineData(1000.0)]
        public void ToPoints_Saturation_PercentAtOrAboveOne_IsPositiveInfinity(double percent)
        {
            // 曲线永远达不到的百分比：需要无穷多点数（调用方据此判定超预算，不抛异常）。
            Assert.True(double.IsPositiveInfinity(
                RatingConversionEvaluator.ToPoints(RatingConversionShape.Saturation, null, 5.0, 1.0, percent, 3)));
        }

        [Fact]
        public void ToPoints_Saturation_JustBelowOne_IsLargeButFinite()
        {
            var points = RatingConversionEvaluator.ToPoints(RatingConversionShape.Saturation, null, 5.0, 1.0, 0.999999, 3);

            Assert.False(double.IsInfinity(points));
            Assert.True(points > 1000.0);
        }

        [Fact]
        public void ToPoints_Saturation_ZeroKOrZeroLevel_IsZero_ForAnyFinitePercent()
        {
            Assert.Equal(0.0, RatingConversionEvaluator.ToPoints(RatingConversionShape.Saturation, null, 0.0, 1.0, 0.5, 3));
            Assert.Equal(0.0, RatingConversionEvaluator.ToPoints(RatingConversionShape.Saturation, null, 5.0, 1.0, 0.5, 0));
        }

        [Fact]
        public void RatingConversionShape_HasExactlyTheTwoDocumentedMembers()
        {
            Assert.Equal(
                new[] { RatingConversionShape.Breakpoints, RatingConversionShape.Saturation },
                (RatingConversionShape[])Enum.GetValues(typeof(RatingConversionShape)));
        }

        // -----------------------------------------------------------------
        // StatModifier.DefaultMultGroup
        // -----------------------------------------------------------------

        [Fact]
        public void StatModifier_DefaultMultGroup_ConstantIsDefault()
        {
            Assert.Equal("default", StatModifier.DefaultMultGroup);
        }

        [Fact]
        public void StatModifier_NullOrEmptyMultGroup_FallsBackToDefaultMultGroup()
        {
            var stat = new Id("stat.cov_l4");
            var source = new Id("src.cov_l4");

            Assert.Equal(StatModifier.DefaultMultGroup, new StatModifier(stat, StatModifierOp.Mult, 1.5, source).MultGroup);
            Assert.Equal(StatModifier.DefaultMultGroup, new StatModifier(stat, StatModifierOp.Mult, 1.5, source, null).MultGroup);
            Assert.Equal(StatModifier.DefaultMultGroup, new StatModifier(stat, StatModifierOp.Mult, 1.5, source, "").MultGroup);
        }

        [Fact]
        public void StatModifier_ExplicitMultGroup_IsKeptVerbatim_AndOtherFieldsRoundTrip()
        {
            var stat = new Id("stat.cov_l4");
            var source = new Id("src.cov_l4");

            var modifier = new StatModifier(stat, StatModifierOp.Pct, -0.25, source, "buff_group");

            Assert.Equal("buff_group", modifier.MultGroup);
            Assert.Equal(stat, modifier.Stat);
            Assert.Equal(StatModifierOp.Pct, modifier.Op);
            Assert.Equal(-0.25, modifier.Value);
            Assert.Equal(source, modifier.SourceId);
        }

        [Fact]
        public void StatModifier_MultGroup_IsCaseSensitive_NotNormalized()
        {
            var modifier = new StatModifier(new Id("stat.cov_l4"), StatModifierOp.Mult, 1.0, new Id("src.cov_l4"), "DEFAULT");

            Assert.Equal("DEFAULT", modifier.MultGroup);
            Assert.NotEqual(StatModifier.DefaultMultGroup, modifier.MultGroup);
        }

        // -----------------------------------------------------------------
        // stat_block 校验规则元数据
        // -----------------------------------------------------------------

        [Fact]
        public void StatRules_RuleId_IsTheConcreteTypeName()
        {
            Assert.Equal(nameof(StatDefinitionConsumerValidationRule), ((IValidationRule)new StatDefinitionConsumerValidationRule()).RuleId);
            Assert.Equal(nameof(StatDefinitionDerivationCycleValidationRule), ((IValidationRule)new StatDefinitionDerivationCycleValidationRule()).RuleId);
            Assert.Equal(nameof(StatRatingConversionValidationRule), ((IValidationRule)new StatRatingConversionValidationRule()).RuleId);
            Assert.Equal(nameof(StatDefinitionValidationRule), ((IValidationRule)new StatDefinitionValidationRule()).RuleId);
        }

        [Fact]
        public void StatRules_DefaultSeverityAndEscalation_MatchTheirDocumentedGrade()
        {
            // 警告级且不可提升：抓"意图"的消费方规则；其余是阻断级、可被严格度提升。
            IValidationRule consumer = new StatDefinitionConsumerValidationRule();
            Assert.Equal(ValidationSeverity.Warning, consumer.DefaultSeverity);
            Assert.True(consumer.NonEscalatable);

            foreach (var rule in new IValidationRule[]
            {
                new StatDefinitionDerivationCycleValidationRule(),
                new StatRatingConversionValidationRule(),
                new StatDefinitionValidationRule(),
            })
            {
                Assert.Equal(ValidationSeverity.Error, rule.DefaultSeverity);
                Assert.False(rule.NonEscalatable);
            }
        }
    }
}
