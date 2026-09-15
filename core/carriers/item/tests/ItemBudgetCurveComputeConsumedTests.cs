using System;
using System.Collections.Generic;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 分阶段落地计划 T-N2-3（ADR-0032 决策 3）验收：<see cref="ItemBudgetCurve.ComputeConsumed"/>
    /// 新消耗公式 <c>(Σ(属性值×权重)^k)^(1/k)</c> 的手算用例——k=1、k=1.5 各 ≥ 3 组，含百分比属性
    /// 经换算曲线折点的一组（断点表与饱和两种形态各一）。本文件直接构造 <see
    /// cref="ItemBudgetCurve.StatBudgetInfo"/> 字典调用公式本身，不经过 <see
    /// cref="Core.Foundation.DataRegistry.IDataRegistryView"/>——<see
    /// cref="ItemBudgetCurve.BuildStatBudgetInfo"/> 从 view 读表的正确性、槽位系数、预算利用率
    /// 警告经 <see cref="ItemBudgetValidationRule"/> 的 Validate 集成用例另行覆盖（见
    /// <see cref="ItemValidationRulesTests"/> 同批新增用例）。
    /// </summary>
    public sealed class ItemBudgetCurveComputeConsumedTests
    {
        private static readonly Id StrengthId = new Id("stat.strength");
        private static readonly Id StaminaId = new Id("stat.stamina");
        private static readonly Id CritRatingId = new Id("stat.crit_rating");
        private static readonly Id HasteRatingId = new Id("stat.haste_rating");

        private static JsonArray Stats(string json) => (JsonArray)JsonReader.Parse(json);

        /// <summary>断点表换算曲线：level=1 → 每 1% 需要 50 点（同 data/_sample/stat/
        /// stat.rating_conversion.json 的 stat.rating.crit 样例首个断点）。</summary>
        private static PiecewiseCurve CritCurve() =>
            new PiecewiseCurve(new[] { new CurvePoint(1, 50), new CurvePoint(3, 80) });

        [Fact]
        public void G1_K1_SingleFlatStat_WeightPassthrough()
        {
            // 手算：单一 flat 词条，权重 1.0 直接透传，term = 10 × 1.0 = 10；单项和开 k 次方根仍是自身，
            // k 对单项结果无影响（本组用 k=1 验证基线）。
            var stats = Stats("[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 10}]");
            var info = new Dictionary<Id, ItemBudgetCurve.StatBudgetInfo>
            {
                [StrengthId] = new ItemBudgetCurve.StatBudgetInfo(1.0, false, null, null, 0, 0),
            };

            var consumed = ItemBudgetCurve.ComputeConsumed(stats, info, level: 1, exponent: 1.0);

            Assert.Equal(10.0, consumed, 9);
        }

        [Fact]
        public void G2_K1_TwoFlatStats_WeightedLinearSum()
        {
            // 手算：k=1 时公式退化为线性加权和：10×1.0 + 10×0.8 = 10 + 8 = 18。
            var stats = Stats(
                "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 10}," +
                " {\"stat\": \"stat.stamina\", \"op\": \"flat\", \"value\": 10}]");
            var info = new Dictionary<Id, ItemBudgetCurve.StatBudgetInfo>
            {
                [StrengthId] = new ItemBudgetCurve.StatBudgetInfo(1.0, false, null, null, 0, 0),
                [StaminaId] = new ItemBudgetCurve.StatBudgetInfo(0.8, false, null, null, 0, 0),
            };

            var consumed = ItemBudgetCurve.ComputeConsumed(stats, info, level: 1, exponent: 1.0);

            Assert.Equal(18.0, consumed, 9);
        }

        [Fact]
        public void G6_K1_MissingStatWeightEntry_DefaultsToWeightOne()
        {
            // 第三组 k=1（验证"stat.weight 没有该属性对应记录时按缺省权重 1 回退"判断记录，见
            // ItemBudgetCurve.BuildStatBudgetInfo 判断记录；槽位系数本身乘在预算上限而非本方法计算
            // 范围内，"含槽位系数的一组"由 ItemValidationRulesTests 的 Validate 级用例覆盖）：单一
            // flat 词条，statInfo 字典里完全没有 stat.strength 这一条（模拟没有登记 stat.weight 的
            // 属性），ComputeConsumed 应按缺省权重 1 处理，consumed = 12。
            var stats = Stats("[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 12}]");
            var info = new Dictionary<Id, ItemBudgetCurve.StatBudgetInfo>();

            var consumed = ItemBudgetCurve.ComputeConsumed(stats, info, level: 1, exponent: 1.0);

            Assert.Equal(12.0, consumed, 9);
        }

        [Fact]
        public void G3_K15_PercentStat_PctOp_BreakpointsConversion_FoldedBackToPoints()
        {
            // 含"百分比属性经换算曲线折点"的一组（断点表形态）：stat.crit_rating 是 percent 分类，
            // op=pct，作者填的 0.10 是"最终 10% 暴击"的意图（运行时绕开换算层直接乘进百分比，见
            // ItemBudgetCurve.ComputeConsumed 判断记录）。折回点数：points = percent × pointsPerPercent
            // (level=1) = 0.10 × 50 = 5；乘权重 0.3 → term = 1.5。单项和在 k=1.5 下仍是自身（单项
            // Math.Pow(1.5,1.5)^(1/1.5) = 1.5），验证换算折点与 k 取值无关。
            var stats = Stats("[{\"stat\": \"stat.crit_rating\", \"op\": \"pct\", \"value\": 0.10}]");
            var info = new Dictionary<Id, ItemBudgetCurve.StatBudgetInfo>
            {
                [CritRatingId] = new ItemBudgetCurve.StatBudgetInfo(
                    0.3, isPercentCategory: true, RatingConversionShape.Breakpoints, CritCurve(), 0, 0),
            };

            var consumed = ItemBudgetCurve.ComputeConsumed(stats, info, level: 1, exponent: 1.5);

            Assert.Equal(1.5, consumed, 9);
        }

        [Fact]
        public void G3b_PercentStat_FlatOp_AlreadyPoints_NoConversionApplied()
        {
            // 同一属性换一种 op：flat 词条按判断记录已经是"点数"本身，不需要再折算——用同一条换算曲线，
            // 只要 op 改成 flat，5 点原始值直接乘权重 0.3 得到 1.5，与"先按 0.10 percent 折算成 5 点"
            // 殊途同归但路径不同，验证 flat 分支确实没有经过 ToPoints。
            var stats = Stats("[{\"stat\": \"stat.crit_rating\", \"op\": \"flat\", \"value\": 5}]");
            var info = new Dictionary<Id, ItemBudgetCurve.StatBudgetInfo>
            {
                [CritRatingId] = new ItemBudgetCurve.StatBudgetInfo(
                    0.3, isPercentCategory: true, RatingConversionShape.Breakpoints, CritCurve(), 0, 0),
            };

            var consumed = ItemBudgetCurve.ComputeConsumed(stats, info, level: 1, exponent: 1.0);

            Assert.Equal(1.5, consumed, 9);
        }

        [Fact]
        public void G4_K15_TwoFlatStats_ExponentPenalizesSingleStatStacking()
        {
            // 手算（k=1.5）：terms = 3, 4（权重均为 1.0）。consumed = (3^1.5 + 4^1.5)^(1/1.5)
            // = (5.196152422706632 + 8)^(1/1.5) = 13.196152422706632^0.6666... ≈ 5.584250376480029
            // （用独立于生产代码的 Math.Pow 表达式重算一遍作交叉验证，而不是直接照抄实现）。
            var stats = Stats(
                "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 3}," +
                " {\"stat\": \"stat.stamina\", \"op\": \"flat\", \"value\": 4}]");
            var info = new Dictionary<Id, ItemBudgetCurve.StatBudgetInfo>
            {
                [StrengthId] = new ItemBudgetCurve.StatBudgetInfo(1.0, false, null, null, 0, 0),
                [StaminaId] = new ItemBudgetCurve.StatBudgetInfo(1.0, false, null, null, 0, 0),
            };

            var expected = Math.Pow(Math.Pow(3, 1.5) + Math.Pow(4, 1.5), 1.0 / 1.5);
            var consumed = ItemBudgetCurve.ComputeConsumed(stats, info, level: 1, exponent: 1.5);

            Assert.Equal(expected, consumed, 9);
            // 同样两个原始值在 k=1 下的线性和是 7，k=1.5 结果 (~5.58) 明显小于 7——验证指数确实让
            // "分散到两个属性"比"想象中的线性叠加"更省预算，即"集中堆单一属性更贵"的惩罚方向正确
            // （数值设计 03 第 1.2 节"属性合并指数……堆单一属性会被惩罚"）。
            Assert.True(consumed < 7.0);
        }

        [Fact]
        public void G5_K15_PercentStat_SaturationConversion_MultOp_CombinedWithFlatStat()
        {
            // 含"百分比属性经换算曲线折点"的另一组（饱和曲线形态）+ k=1.5：stat.haste_rating 是
            // percent 分类、饱和换算 k=50、cap=0.5（同 data/_sample stat.rating.haste 样例），
            // op=mult，作者填 0.2（20% 急速）。level=10。
            // 折回点数：points = percent × k × level / (1 − percent) = 0.2×50×10/0.8 = 125，
            // 乘权重 0.3 → term_e = 37.5。另配一条 flat 词条 stat.strength value=20，weight=1.0 →
            // term_a = 20。consumed = (37.5^1.5 + 20^1.5)^(1/1.5)。
            var stats = Stats(
                "[{\"stat\": \"stat.haste_rating\", \"op\": \"mult\", \"value\": 0.2}," +
                " {\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 20}]");
            var info = new Dictionary<Id, ItemBudgetCurve.StatBudgetInfo>
            {
                [HasteRatingId] = new ItemBudgetCurve.StatBudgetInfo(
                    0.3, isPercentCategory: true, RatingConversionShape.Saturation, null, conversionK: 50, conversionCap: 0.5),
                [StrengthId] = new ItemBudgetCurve.StatBudgetInfo(1.0, false, null, null, 0, 0),
            };

            var expected = Math.Pow(Math.Pow(37.5, 1.5) + Math.Pow(20, 1.5), 1.0 / 1.5);
            var consumed = ItemBudgetCurve.ComputeConsumed(stats, info, level: 10, exponent: 1.5);

            Assert.Equal(expected, consumed, 6);
        }

        [Fact]
        public void ComputeConsumed_ZeroWeight_WithInfeasiblePercent_DoesNotProduceNaN()
        {
            // 判断记录回归用例（见 ItemBudgetCurve.ComputeConsumed 判断记录"权重为 0 且折算结果为
            // 无穷大时不产生 NaN"）：饱和曲线对 percent >= 1 的反函数按定义返回 +Infinity；若该属性
            // 权重恰好登记为 0，Infinity × 0 在 IEEE754 下是 NaN——本方法应提前把 weight==0 的项短路
            // 为 0，不产生 NaN。
            var stats = Stats("[{\"stat\": \"stat.haste_rating\", \"op\": \"mult\", \"value\": 1.0}]");
            var info = new Dictionary<Id, ItemBudgetCurve.StatBudgetInfo>
            {
                [HasteRatingId] = new ItemBudgetCurve.StatBudgetInfo(
                    0.0, isPercentCategory: true, RatingConversionShape.Saturation, null, conversionK: 50, conversionCap: 0.5),
            };

            var consumed = ItemBudgetCurve.ComputeConsumed(stats, info, level: 10, exponent: 1.5);

            Assert.Equal(0.0, consumed, 9);
            Assert.False(double.IsNaN(consumed));
        }
    }
}
