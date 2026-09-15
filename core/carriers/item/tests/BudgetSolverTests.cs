using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 分阶段落地计划 T-N2-4（ADR-0032 决策 3；07 第 1.2 节修订段"预算反解"）验收：<see
    /// cref="BudgetSolver.Solve"/> 反解可逆 &gt;= 4 组（至少一组含 <c>percent</c> 类别属性经非恒等
    /// 换算曲线折点、一组 <c>k=1</c>、一组 <c>k=1.5</c>、一组多属性不同比例），外加输入校验负例。
    /// 反解出的属性值经 <see cref="ItemBudgetCurve.ComputeConsumed"/> 正向复算须等于目标预算
    /// （误差 &lt; 1e-9）。
    /// </summary>
    public sealed class BudgetSolverTests
    {
        private static readonly Id StrengthId = new Id("stat.strength");
        private static readonly Id StaminaId = new Id("stat.stamina");
        private static readonly Id CritRatingId = new Id("stat.crit_rating");

        private const string SlotJson =
            "[{\"id\": \"item.slot.chest\", \"name_key\": \"l10n.item.slot.chest\", \"budget_coefficient\": 1.0}," +
            " {\"id\": \"item.slot.ring\", \"name_key\": \"l10n.item.slot.ring\", \"budget_coefficient\": 0.5}]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\", \"budget_multiplier\": 1.0}," +
            " {\"id\": \"item.quality.rare\", \"name_key\": \"l10n.item.quality.rare\", \"budget_multiplier\": 2.0}]";

        private const string BudgetCurveJson =
            "[{\"id\": \"item.budget.default\", \"entries\": [{\"x\": 1, \"y\": 100}, {\"x\": 10, \"y\": 1000}]," +
            " \"exponent\": 1.5}," +
            " {\"id\": \"item.budget.linear\", \"entries\": [{\"x\": 1, \"y\": 100}], \"exponent\": 1.0}]";

        // 同 ItemBudgetCurveComputeConsumedTests 的换算曲线夹具（stat.rating.crit 断点表：level=1 时
        // 每 1% 需要 50 点）。
        private const string StatDefJson =
            "[{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"category\": \"primary\"}," +
            " {\"id\": \"stat.stamina\", \"name_key\": \"l10n.stat.stamina\", \"category\": \"primary\"}," +
            " {\"id\": \"stat.crit_rating\", \"name_key\": \"l10n.stat.crit_rating\", \"category\": \"percent\"," +
            " \"conversion_ref\": \"stat.rating.crit\"}]";

        private const string RatingConversionJson =
            "[{\"id\": \"stat.rating.crit\", \"entries\": [{\"x\": 1, \"y\": 50}, {\"x\": 3, \"y\": 80}]}]";

        private const string StatWeightJson =
            "[{\"id\": \"stat.weight.strength\", \"stat\": \"stat.strength\", \"weight\": 1.0}," +
            " {\"id\": \"stat.weight.stamina\", \"stat\": \"stat.stamina\", \"weight\": 0.8}," +
            " {\"id\": \"stat.weight.crit_rating\", \"stat\": \"stat.crit_rating\", \"weight\": 0.3}]";

        private static IDataRegistryView BuildView()
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("stat.rating_conversion", TestSupport.Table("stat.rating_conversion", RatingConversionJson));
                source.Add("stat.weight", TestSupport.Table("stat.weight", StatWeightJson));
            });
        }

        private static void AssertReversible(BudgetSolverResult result)
        {
            Assert.Equal(result.TargetBudget, result.ActualConsumed, 9);
        }

        [Fact]
        public void G1_K1_SingleStat_FullShare_Reversible()
        {
            // k=1、单属性、shareOfBudget 缺省 1.0：budget = 100（quality.common ×1、slot.chest ×1）。
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            var result = solver.Solve(
                itemLevel: 1,
                qualityId: new Id("item.quality.common"),
                slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.linear"),
                view: view);

            Assert.Equal(1.0, result.Exponent, 9);
            Assert.Equal(100.0, result.ItemBudgetLimit, 9);
            Assert.Equal(1.0, result.ShareOfBudget, 9);
            Assert.Equal(100.0, result.TargetBudget, 9);
            // k=1 时 S=B，term=ratio×S=100，权重 1.0 ⇒ value=100。
            Assert.Equal(100.0, result.Values[StrengthId], 9);
            AssertReversible(result);
        }

        [Fact]
        public void G2_K15_TwoStats_DifferentRatios_Reversible()
        {
            // k=1.5、两个属性不同比例（0.6/0.4）、quality.rare（预算×2）：budget = 100×2×1 = 200。
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            var result = solver.Solve(
                itemLevel: 1,
                qualityId: new Id("item.quality.rare"),
                slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 0.6), (StaminaId, 0.4) },
                budgetCurveId: new Id("item.budget.default"),
                view: view);

            Assert.Equal(1.5, result.Exponent, 9);
            Assert.Equal(200.0, result.TargetBudget, 9);
            AssertReversible(result);

            // 手算交叉验证：S = B / (0.6^1.5 + 0.4^1.5)^(1/1.5)；term_strength = 0.6×S，
            // value_strength = term_strength / 1.0（权重）；term_stamina = 0.4×S，
            // value_stamina = term_stamina / 0.8。
            var expectedScale = 200.0 / Math.Pow(Math.Pow(0.6, 1.5) + Math.Pow(0.4, 1.5), 1.0 / 1.5);
            Assert.Equal(0.6 * expectedScale / 1.0, result.Values[StrengthId], 9);
            Assert.Equal(0.4 * expectedScale / 0.8, result.Values[StaminaId], 9);
        }

        [Fact]
        public void G3_K15_PercentStat_BreakpointsConversion_MixedWithFlatStat_Reversible()
        {
            // 含 percent 类别属性（非恒等断点表换算曲线）的一组：crit_rating(0.5) + strength(0.5)。
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            var result = solver.Solve(
                itemLevel: 1,
                qualityId: new Id("item.quality.common"),
                slotId: new Id("item.slot.chest"),
                statMix: new[] { (CritRatingId, 0.5), (StrengthId, 0.5) },
                budgetCurveId: new Id("item.budget.default"),
                view: view);

            AssertReversible(result);

            // 反解出的 crit_rating 值是"点数"（op=flat 语义）。换算成 op=pct 的填写值（ToPercent 是
            // ToPoints 的反函数），与 strength 的 flat 值一起重新构造 stats，经 ComputeConsumed 正向
            // 复算应仍等于同一目标预算——验证换算折点在反解与正向公式之间是双向一致的。
            var critPoints = result.Values[CritRatingId];
            var critCurve = new PiecewiseCurve(new[] { new CurvePoint(1, 50), new CurvePoint(3, 80) });
            var critPercent = RatingConversionEvaluator.ToPercent(
                RatingConversionShape.Breakpoints, critCurve, k: 0, cap: 0, rawValue: critPoints, level: 1);

            var statInfo = new Dictionary<Id, ItemBudgetCurve.StatBudgetInfo>
            {
                [CritRatingId] = new ItemBudgetCurve.StatBudgetInfo(
                    0.3, isPercentCategory: true, RatingConversionShape.Breakpoints, critCurve, 0, 0),
                [StrengthId] = new ItemBudgetCurve.StatBudgetInfo(1.0, false, null, null, 0, 0),
            };
            var reconstructed = (JsonArray)JsonReader.Parse(
                "[{\"stat\": \"stat.crit_rating\", \"op\": \"pct\", \"value\": " +
                critPercent.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}," +
                " {\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": " +
                result.Values[StrengthId].ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}]");

            var recomputed = ItemBudgetCurve.ComputeConsumed(reconstructed, statInfo, level: 1, exponent: result.Exponent);
            Assert.Equal(result.TargetBudget, recomputed, 6);
        }

        [Fact]
        public void G4_K15_ShareOfBudget_LessThanOne_AffixScenario_Reversible()
        {
            // 词缀落值场景（ADR-0032 决策 7）：shareOfBudget=0.3，目标预算 = 该件预算 × 0.3。
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            var result = solver.Solve(
                itemLevel: 1,
                qualityId: new Id("item.quality.common"),
                slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.default"),
                shareOfBudget: 0.3,
                view: view);

            Assert.Equal(100.0, result.ItemBudgetLimit, 9);
            Assert.Equal(0.3, result.ShareOfBudget, 9);
            Assert.Equal(30.0, result.TargetBudget, 9);
            AssertReversible(result);
        }

        [Fact]
        public void G5_K15_SlotBudgetCoefficient_Reversible()
        {
            // 额外一组：槽位系数 0.5（item.slot.ring）接入目标预算，budget = 100×1×0.5 = 50。
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            var result = solver.Solve(
                itemLevel: 1,
                qualityId: new Id("item.quality.common"),
                slotId: new Id("item.slot.ring"),
                statMix: new[] { (StrengthId, 0.7), (StaminaId, 0.3) },
                budgetCurveId: new Id("item.budget.default"),
                view: view);

            Assert.Equal(50.0, result.ItemBudgetLimit, 9);
            Assert.Equal(50.0, result.TargetBudget, 9);
            AssertReversible(result);
        }

        // -----------------------------------------------------------------
        // 输入校验负例
        // -----------------------------------------------------------------

        [Fact]
        public void Solve_RatioSumNotOne_ThrowsArgumentException()
        {
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            Assert.Throws<ArgumentException>(() => solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 0.5), (StaminaId, 0.3) },
                budgetCurveId: new Id("item.budget.default"), view: view));
        }

        [Fact]
        public void Solve_RatioNotPositive_ThrowsArgumentException()
        {
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            Assert.Throws<ArgumentException>(() => solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.2), (StaminaId, -0.2) },
                budgetCurveId: new Id("item.budget.default"), view: view));
        }

        [Fact]
        public void Solve_UnknownQuality_ThrowsArgumentException()
        {
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            Assert.Throws<ArgumentException>(() => solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.does_not_exist"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.default"), view: view));
        }

        [Fact]
        public void Solve_UnknownSlot_ThrowsArgumentException()
        {
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            Assert.Throws<ArgumentException>(() => solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.does_not_exist"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.default"), view: view));
        }

        [Fact]
        public void Solve_UnknownBudgetCurve_ThrowsArgumentException()
        {
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            Assert.Throws<ArgumentException>(() => solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.does_not_exist"), view: view));
        }

        [Fact]
        public void Solve_ZeroWeightStat_ThrowsArgumentException()
        {
            var view = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                // 只登记 strength（不含 crit_rating），避免额外拖入 stat.rating_conversion 引用完整性
                // 要求——本用例只关心"权重登记为 0"这一分支，不需要百分比换算属性。
                source.Add("stat.definition", TestSupport.Table("stat.definition",
                    "[{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"category\": \"primary\"}]"));
                source.Add("stat.weight", TestSupport.Table("stat.weight",
                    "[{\"id\": \"stat.weight.strength\", \"stat\": \"stat.strength\", \"weight\": 0}]"));
            });
            IBudgetSolver solver = new BudgetSolver();

            Assert.Throws<ArgumentException>(() => solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.default"), view: view));
        }

        [Fact]
        public void Solve_ShareOfBudgetOutOfRange_ThrowsArgumentException()
        {
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            Assert.Throws<ArgumentException>(() => solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.default"), shareOfBudget: 0.0, view: view));

            Assert.Throws<ArgumentException>(() => solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.default"), shareOfBudget: 1.5, view: view));
        }

        [Fact]
        public void Solve_EmptyStatMix_ThrowsArgumentException()
        {
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            Assert.Throws<ArgumentException>(() => solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: Array.Empty<(Id, double)>(),
                budgetCurveId: new Id("item.budget.default"), view: view));
        }

        [Fact]
        public void Solve_DefaultInterfaceOverload_ShareOfBudgetDefaultsToOne()
        {
            // IBudgetSolver 的便捷重载（缺省 shareOfBudget=1.0）：经接口类型调用，验证默认接口方法
            // 生效且与显式传 1.0 结果一致。
            var view = BuildView();
            IBudgetSolver solver = new BudgetSolver();

            var viaDefault = solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.default"), view: view);

            var viaExplicit = solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) },
                budgetCurveId: new Id("item.budget.default"), shareOfBudget: 1.0, view: view);

            Assert.Equal(viaExplicit.TargetBudget, viaDefault.TargetBudget, 9);
            Assert.Equal(viaExplicit.Values[StrengthId], viaDefault.Values[StrengthId], 9);
        }
    }
}
