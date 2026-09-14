using System.Collections.Generic;
using Core.Foundation.Common;
using Xunit;

namespace Tests.Foundation.Common
{
    /// <summary>
    /// 分阶段落地计划 T-N0-1 验收：<see cref="PiecewiseCurve"/> 插值（两点线性、三点分段、左右越界
    /// 夹取、空表为 0、无序输入自动排序）、重复 x 语义、单点常值、单调/有限查询、输入快照。
    /// </summary>
    public class PiecewiseCurveTests
    {
        private static PiecewiseCurve Curve(params (double X, double Y)[] points)
        {
            var list = new List<CurvePoint>(points.Length);
            foreach (var p in points)
            {
                list.Add(new CurvePoint(p.X, p.Y));
            }
            return new PiecewiseCurve(list);
        }

        [Fact]
        public void TwoPoints_MidpointIsLinearInterpolation()
        {
            var curve = Curve((10, 100), (20, 300));

            Assert.Equal(200, curve.Evaluate(15));
            Assert.Equal(120, curve.Evaluate(11));
        }

        [Fact]
        public void ThreePoints_EachSegmentInterpolatesOnItsOwnSlope()
        {
            var curve = Curve((0, 0), (10, 100), (20, 400));

            Assert.Equal(50, curve.Evaluate(5));
            Assert.Equal(250, curve.Evaluate(15));
            Assert.Equal(100, curve.Evaluate(10));
        }

        [Fact]
        public void BelowFirstBreakpoint_ClampsToFirstY()
        {
            var curve = Curve((10, 100), (20, 300));

            Assert.Equal(100, curve.Evaluate(-5));
            Assert.Equal(100, curve.Evaluate(10));
        }

        [Fact]
        public void AboveLastBreakpoint_ClampsToLastY()
        {
            var curve = Curve((10, 100), (20, 300));

            Assert.Equal(300, curve.Evaluate(20));
            Assert.Equal(300, curve.Evaluate(1000));
        }

        [Fact]
        public void EmptyCurve_EvaluatesToZero()
        {
            Assert.Equal(0, PiecewiseCurve.Empty.Evaluate(42));
            Assert.Equal(0, Curve().Evaluate(-1));
            Assert.Equal(0, PiecewiseCurve.Empty.Count);
        }

        [Fact]
        public void UnsortedInput_IsSortedByXAndEvaluatesLikeSortedInput()
        {
            var unsorted = Curve((20, 400), (0, 0), (10, 100));
            var sorted = Curve((0, 0), (10, 100), (20, 400));

            Assert.Equal(new[] { 0.0, 10.0, 20.0 }, new[] { unsorted.Points[0].X, unsorted.Points[1].X, unsorted.Points[2].X });
            Assert.Equal(sorted.Evaluate(5), unsorted.Evaluate(5));
            Assert.Equal(sorted.Evaluate(15), unsorted.Evaluate(15));
        }

        [Fact]
        public void DuplicateX_KeepsInputOrderAndQueryOnDuplicateTakesFirst()
        {
            var curve = Curve((9, 40), (5, 30), (1, 10), (5, 20));

            // 稳定排序：两个 x=5 的断点保持输入顺序 (5,30) 在前、(5,20) 在后。
            Assert.Equal(new CurvePoint(5, 30), curve.Points[1]);
            Assert.Equal(new CurvePoint(5, 20), curve.Points[2]);
            // 落在重复 x 上：取以该 x 为右端的第一段的右端点。
            Assert.Equal(30, curve.Evaluate(5));
            // 落在重复 x 之后的段上：以后一个 (5,20) 为左端。
            Assert.Equal(30, curve.Evaluate(7));
        }

        [Fact]
        public void SinglePoint_IsConstantEverywhere()
        {
            var curve = Curve((7, 3.5));

            Assert.Equal(3.5, curve.Evaluate(-100));
            Assert.Equal(3.5, curve.Evaluate(7));
            Assert.Equal(3.5, curve.Evaluate(100));
        }

        [Fact]
        public void IsNonDecreasing_DetectsDecreaseAndAllowsPlateau()
        {
            Assert.True(Curve((1, 1), (2, 1), (3, 5)).IsNonDecreasing());
            Assert.False(Curve((1, 5), (2, 4)).IsNonDecreasing());
            Assert.False(Curve((1, 1), (2, double.NaN)).IsNonDecreasing());
            Assert.True(PiecewiseCurve.Empty.IsNonDecreasing());
        }

        [Fact]
        public void IsFinite_RejectsNaNAndInfinityOnEitherAxis()
        {
            Assert.True(Curve((1, 1), (2, 2)).IsFinite());
            Assert.False(Curve((1, double.NaN)).IsFinite());
            Assert.False(Curve((double.PositiveInfinity, 1)).IsFinite());
            Assert.False(Curve((1, double.NegativeInfinity)).IsFinite());
            Assert.True(PiecewiseCurve.Empty.IsFinite());
        }

        [Fact]
        public void Constructor_CopiesInput_LaterMutationDoesNotAffectCurve()
        {
            var source = new List<CurvePoint> { new CurvePoint(0, 0), new CurvePoint(10, 10) };
            var curve = new PiecewiseCurve(source);
            source[1] = new CurvePoint(10, 999);
            source.Add(new CurvePoint(20, 20));

            Assert.Equal(2, curve.Count);
            Assert.Equal(5, curve.Evaluate(5));
        }

        [Fact]
        public void IntegerLevelInputs_MatchLegacyItemBudgetFormulaBitForBit()
        {
            // 迁移前 ItemBudgetCurve.Interpolate 的式子：lo + t * (hi - lo)，t = (x - x0) / (x1 - x0)。
            var curve = Curve((1, 12.5), (60, 833.3), (80, 1580.75));
            for (var level = 1; level <= 80; level++)
            {
                double expected;
                if (level <= 60)
                {
                    var t = (double)(level - 1) / (60 - 1);
                    expected = 12.5 + t * (833.3 - 12.5);
                }
                else
                {
                    var t = (double)(level - 60) / (80 - 60);
                    expected = 833.3 + t * (1580.75 - 833.3);
                }
                Assert.Equal(expected, curve.Evaluate(level));
            }
        }
    }
}
