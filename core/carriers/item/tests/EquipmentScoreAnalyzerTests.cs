using System;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 分阶段落地计划 T-N2-4（ADR-0032 决策 9；07 第 1.2 节修订段"装备评分"）验收：<see
    /// cref="EquipmentScoreAnalyzer.Score"/> 评分对预算单调 &gt;= 2 组（同槽位同职业，预算上限更高的
    /// 物品评分更高；职业权重覆盖改变评分排序），外加基础计算/输入校验用例。
    /// </summary>
    public sealed class EquipmentScoreAnalyzerTests
    {
        private static readonly Id StrengthId = new Id("stat.strength");
        private static readonly Id StaminaId = new Id("stat.stamina");
        private static readonly Id WarriorId = new Id("arch.class.warrior");

        private const string SlotJson =
            "[{\"id\": \"item.slot.chest\", \"name_key\": \"l10n.item.slot.chest\", \"budget_coefficient\": 1.0}]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\", \"budget_multiplier\": 1.0}]";

        private const string BudgetCurveJson =
            "[{\"id\": \"item.budget.default\", \"entries\": [{\"x\": 1, \"y\": 100}, {\"x\": 10, \"y\": 1000}]," +
            " \"exponent\": 1.5}]";

        private const string StatDefJson =
            "[{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"category\": \"primary\"}," +
            " {\"id\": \"stat.stamina\", \"name_key\": \"l10n.stat.stamina\", \"category\": \"primary\"}]";

        // 战士（arch.class.warrior）对耐力有职业覆盖权重 2.0，力量沿用基础权重 1.0（不登记覆盖）——
        // 用于"职业权重覆盖改变评分排序"一组。
        private const string StatWeightJson =
            "[{\"id\": \"stat.weight.strength\", \"stat\": \"stat.strength\", \"weight\": 1.0}," +
            " {\"id\": \"stat.weight.stamina\", \"stat\": \"stat.stamina\", \"weight\": 0.8," +
            " \"class_overrides\": [{\"class\": \"arch.class.warrior\", \"weight\": 2.0}]}]";

        // 同 core/numbers/stat_block/tests/StatWeightSchemaTests 手法：arch.class 走
        // TestSupport.BuildRegistry 的 FailOnUnknownTable=false 占位表，只需一条同 id 记录满足
        // class_overrides[].class 的引用完整性，不需要构造真实 arch.class schema 的其余必填字段。
        private const string ArchClassJson = "[{\"id\": \"arch.class.warrior\"}]";

        private static IDataRegistryView BuildView(string templateJson)
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("stat.weight", TestSupport.Table("stat.weight", StatWeightJson));
                source.Add("arch.class", TestSupport.Table("arch.class", ArchClassJson));
                source.Add("item.template", TestSupport.Table("item.template", templateJson));
            });
        }

        private static string Template(string id, int itemLevel, string statsJson) =>
            "{\"id\": \"" + id + "\", \"slot\": \"item.slot.chest\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": " + itemLevel + ", \"display_ref\": \"display." + id + "\", \"stack_size\": 1," +
            " \"name_key\": \"l10n." + id + "\", \"stats\": " + statsJson + "}";

        [Fact]
        public void Score_SingleFlatStat_MatchesManualCalc()
        {
            // 单一属性、无职业覆盖：评分 = value × weight（单项 k 次方根开完仍是自身，与 k 取值无关）。
            var view = BuildView(
                "[" + Template("item.tpl.single", 1,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 10}]") + "]");

            var result = EquipmentScoreAnalyzer.Score(new Id("item.tpl.single"), classId: null, view: view);

            Assert.Equal(10.0 * 1.0, result.Score, 9);
            Assert.Equal(1, result.ItemLevel);
            Assert.Null(result.ClassId);
        }

        [Fact]
        public void Score_ClassOverride_UsesOverriddenWeight()
        {
            // 同一模板，战士覆盖 stamina 权重为 2.0（基础 0.8），评分应反映覆盖后的权重而非基础权重。
            var view = BuildView(
                "[" + Template("item.tpl.stam", 1,
                    "[{\"stat\": \"stat.stamina\", \"op\": \"flat\", \"value\": 10}]") + "]");

            var baseResult = EquipmentScoreAnalyzer.Score(new Id("item.tpl.stam"), classId: null, view: view);
            var warriorResult = EquipmentScoreAnalyzer.Score(new Id("item.tpl.stam"), WarriorId, view);

            Assert.Equal(8.0, baseResult.Score, 9); // 10 × 0.8
            Assert.Equal(20.0, warriorResult.Score, 9); // 10 × 2.0（战士覆盖）
            Assert.Equal(0.8, baseResult.Weights[StaminaId], 9);
            Assert.Equal(2.0, warriorResult.Weights[StaminaId], 9);
        }

        [Fact]
        public void Score_MonotonicVsBudget_SameSlotSameClass_HigherBudgetItemScoresHigher()
        {
            // 同槽位（item.slot.chest）同职业（均为 null，不按职业覆盖）：两件物品分别按各自预算上限
            // 全额反解出 stats（BudgetSolver 反解），预算上限更高的物品评分应更高。
            IBudgetSolver solver = new BudgetSolver();

            var view1 = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("stat.weight", TestSupport.Table("stat.weight", StatWeightJson));
                source.Add("arch.class", TestSupport.Table("arch.class", ArchClassJson));
            });

            var low = solver.Solve(
                itemLevel: 1, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) }, budgetCurveId: new Id("item.budget.default"), view: view1);
            var high = solver.Solve(
                itemLevel: 10, qualityId: new Id("item.quality.common"), slotId: new Id("item.slot.chest"),
                statMix: new[] { (StrengthId, 1.0) }, budgetCurveId: new Id("item.budget.default"), view: view1);

            Assert.True(high.ItemBudgetLimit > low.ItemBudgetLimit);

            var lowValue = low.Values[StrengthId].ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var highValue = high.Values[StrengthId].ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            var view = BuildView(
                "[" + Template("item.tpl.low", 1,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": " + lowValue + "}]") + "," +
                Template("item.tpl.high", 10,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": " + highValue + "}]") + "]");

            var scoreLow = EquipmentScoreAnalyzer.Score(new Id("item.tpl.low"), classId: null, view: view);
            var scoreHigh = EquipmentScoreAnalyzer.Score(new Id("item.tpl.high"), classId: null, view: view);

            Assert.True(scoreHigh.Score > scoreLow.Score);
        }

        [Fact]
        public void Score_ClassWeightOverride_FlipsRanking()
        {
            // 力量模板与耐力模板同样填 10 点：不按职业时力量权重(1.0) > 耐力基础权重(0.8)，力量模板分更高；
            // 按战士职业覆盖后耐力权重变 2.0 > 力量权重 1.0（战士对力量无覆盖，沿用基础 1.0），
            // 耐力模板反而分更高——验证"职业权重覆盖改变评分排序"。
            var view = BuildView(
                "[" + Template("item.tpl.str", 1,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 10}]") + "," +
                Template("item.tpl.stam2", 1,
                    "[{\"stat\": \"stat.stamina\", \"op\": \"flat\", \"value\": 10}]") + "]");

            var strNoClass = EquipmentScoreAnalyzer.Score(new Id("item.tpl.str"), classId: null, view: view);
            var stamNoClass = EquipmentScoreAnalyzer.Score(new Id("item.tpl.stam2"), classId: null, view: view);
            Assert.True(strNoClass.Score > stamNoClass.Score);

            var strWarrior = EquipmentScoreAnalyzer.Score(new Id("item.tpl.str"), WarriorId, view);
            var stamWarrior = EquipmentScoreAnalyzer.Score(new Id("item.tpl.stam2"), WarriorId, view);
            Assert.True(stamWarrior.Score > strWarrior.Score);
        }

        [Fact]
        public void Compare_ReturnsSignConsistentWithScoreDifference()
        {
            var view = BuildView(
                "[" + Template("item.tpl.a", 1,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 5}]") + "," +
                Template("item.tpl.b", 1,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 20}]") + "]");

            var a = EquipmentScoreAnalyzer.Score(new Id("item.tpl.a"), classId: null, view: view);
            var b = EquipmentScoreAnalyzer.Score(new Id("item.tpl.b"), classId: null, view: view);

            Assert.True(EquipmentScoreAnalyzer.Compare(a, b) < 0);
            Assert.True(EquipmentScoreAnalyzer.Compare(b, a) > 0);
            Assert.Equal(0, EquipmentScoreAnalyzer.Compare(a, a));
        }

        [Fact]
        public void Score_AdditionalStats_ReservedParameter_IncreasesScore()
        {
            // 预留"附加属性列表"入参（供后续任务接词缀反解值）：追加一条 stamina 应让评分高于只算
            // 模板自身 stats 的结果。
            var view = BuildView(
                "[" + Template("item.tpl.base", 1,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 10}]") + "]");

            var withoutExtra = EquipmentScoreAnalyzer.Score(new Id("item.tpl.base"), classId: null, view: view);
            var withExtra = EquipmentScoreAnalyzer.Score(
                new Id("item.tpl.base"), classId: null, view: view,
                additionalStats: new[] { (StaminaId, 5.0) });

            Assert.True(withExtra.Score > withoutExtra.Score);
        }

        [Fact]
        public void Score_UnknownTemplate_ThrowsArgumentException()
        {
            var view = BuildView("[]");

            Assert.Throws<ArgumentException>(() =>
                EquipmentScoreAnalyzer.Score(new Id("item.tpl.does_not_exist"), classId: null, view: view));
        }

        /// <summary>
        /// 2026-09-16 深度复审 B-S1：新增的"接受预构建 statBudgetInfo"重载在多次调用间复用同一份
        /// 信息，结果必须与既有"每次都自建"重载逐位一致——覆盖 <paramref name="classId"/> 有值（职业
        /// 权重覆盖）与无值两种口径，验证"复用同一份信息"不悄悄改变评分结果。
        /// </summary>
        [Fact]
        public void Score_ReusingPrebuiltStatBudgetInfoAcrossMultipleCalls_MatchesAutoBuildingOverload()
        {
            var view = BuildView(
                "[" + Template("item.tpl.reuse_a", 1,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 10}," +
                    "{\"stat\": \"stat.stamina\", \"op\": \"flat\", \"value\": 6}]") + "," +
                Template("item.tpl.reuse_b", 2,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 4}]") + "]");

            // 无职业覆盖口径：statBudgetInfo 与 classId=null 的既有重载共用同一份。
            var noClassInfo = ItemBudgetCurve.BuildStatBudgetInfo(view);
            foreach (var templateId in new[] { new Id("item.tpl.reuse_a"), new Id("item.tpl.reuse_b") })
            {
                var viaAutoBuild = EquipmentScoreAnalyzer.Score(templateId, classId: null, view: view);
                var viaPrebuilt = EquipmentScoreAnalyzer.Score(templateId, classId: null, view: view, statBudgetInfo: noClassInfo);

                Assert.Equal(viaAutoBuild.Score, viaPrebuilt.Score, 9);
                Assert.Equal(viaAutoBuild.Weights.Count, viaPrebuilt.Weights.Count);
            }

            // 职业权重覆盖口径：statBudgetInfo 与 classId=WarriorId 的既有重载共用同一份。
            var warriorInfo = ItemBudgetCurve.BuildStatBudgetInfo(view, WarriorId);
            foreach (var templateId in new[] { new Id("item.tpl.reuse_a"), new Id("item.tpl.reuse_b") })
            {
                var viaAutoBuild = EquipmentScoreAnalyzer.Score(templateId, classId: WarriorId, view: view);
                var viaPrebuilt = EquipmentScoreAnalyzer.Score(templateId, classId: WarriorId, view: view, statBudgetInfo: warriorInfo);

                Assert.Equal(viaAutoBuild.Score, viaPrebuilt.Score, 9);
            }
        }

        // -----------------------------------------------------------------
        // 2026-09-16 深度复审 B 测试覆盖缺口 3：class_overrides 命中的覆盖权重为显式 0 时，
        // EquipmentScoreAnalyzer.Score 端到端应返回有限值（不产生 NaN）。ItemBudgetCurve
        // .ComputeConsumed 内部已有 weight==0.0 短路防御（见该方法），此前只被
        // ItemBudgetCurveComputeConsumedTests 这一层单测覆盖——本用例经完整的 Score 调用链路
        // （BuildStatBudgetInfo 按职业覆盖解出 weight=0 → ComputeConsumed）复现同一条防御，
        // 防止未来重构在中间层悄悄绕开它。
        // -----------------------------------------------------------------

        /// <summary>词条是 percent 分类、经 Saturation 换算曲线、<c>op=pct</c> 且取值 1.0（100%，
        /// 曲线永远达不到的百分比）——<c>RatingConversionEvaluator.InverseSaturation</c> 对此返回
        /// <see cref="double.PositiveInfinity"/>（见该方法判断记录）。该属性同时经
        /// <c>stat.weight.class_overrides</c> 对 <c>WarriorId</c> 显式登记权重 0——如果
        /// <c>ComputeConsumed</c> 的 <c>weight==0.0</c> 短路防御被绕开，`Infinity × 0` 会产生
        /// <c>NaN</c> 并沿 <c>Math.Pow</c> 传播到最终 <see cref="EquipmentScoreResult.Score"/>；
        /// 短路防御生效时该词条贡献恒为 0，最终评分应为有限值 0（本模板只有这一条 stats）。</summary>
        [Fact]
        public void Score_ClassOverrideWeightIsExplicitZero_OnInfinitePointsStat_ReturnsFiniteZero_NotNaN()
        {
            const string statDefWithPercent =
                "[{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"category\": \"primary\"}," +
                " {\"id\": \"stat.crit_pct\", \"name_key\": \"l10n.stat.crit_pct\", \"category\": \"percent\"," +
                " \"conversion_ref\": \"stat.rating.n_b_gap3\"}]";

            const string ratingConversionJson =
                "[{\"id\": \"stat.rating.n_b_gap3\", \"saturation\": {\"k\": 10, \"cap\": 0.5}}]";

            // 战士对 crit_pct 显式登记覆盖权重 0（class_overrides 命中分支）。
            const string statWeightWithZeroOverride =
                "[{\"id\": \"stat.weight.crit_pct\", \"stat\": \"stat.crit_pct\", \"weight\": 0.5," +
                " \"class_overrides\": [{\"class\": \"arch.class.warrior\", \"weight\": 0}]}]";

            var view = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", statDefWithPercent));
                source.Add("stat.rating_conversion", TestSupport.Table("stat.rating_conversion", ratingConversionJson));
                source.Add("stat.weight", TestSupport.Table("stat.weight", statWeightWithZeroOverride));
                source.Add("arch.class", TestSupport.Table("arch.class", ArchClassJson));
                source.Add("item.template", TestSupport.Table("item.template", "[" + Template("item.tpl.n_b_gap3", 1,
                    "[{\"stat\": \"stat.crit_pct\", \"op\": \"pct\", \"value\": 1.0}]") + "]"));
            });

            var result = EquipmentScoreAnalyzer.Score(new Id("item.tpl.n_b_gap3"), classId: WarriorId, view: view);

            Assert.False(double.IsNaN(result.Score), "class_overrides 权重为 0 时不应产生 NaN");
            Assert.Equal(0.0, result.Score, 9);
        }

        // -----------------------------------------------------------------
        // 消费方反馈第 45 条（2026-09-17）：阻断态下不抛异常 + 降级标记如实反映
        // -----------------------------------------------------------------

        [Fact]
        public void Score_NonBlockingState_MatchesPreChangeResult_NotDegraded()
        {
            var view = BuildView(
                "[" + Template("item.tpl.n45_ok", 1,
                    "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 10}]") + "]");

            var result = EquipmentScoreAnalyzer.Score(new Id("item.tpl.n45_ok"), classId: null, view: view);

            Assert.Equal(10.0, result.Score, 9);
            Assert.False(result.IsDegraded);
            Assert.Empty(result.MissingTables);
        }

        [Fact]
        public void Score_BlockingState_UnrelatedReferenceIntegrityError_DoesNotThrow_AndNotDegraded()
        {
            // 反馈原文复现：registry 因与本次评分用到的 item.template/stat.* 完全无关的坏引用
            // （test.widget.owner 指向不存在的 test.owner.ghost）整体阻断。
            var registry = TestSupport.BuildRegistryAllowBlocking(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("stat.weight", TestSupport.Table("stat.weight", StatWeightJson));
                source.Add("arch.class", TestSupport.Table("arch.class", ArchClassJson));
                source.Add("item.template", TestSupport.Table("item.template",
                    "[" + Template("item.tpl.n45_blocked", 1,
                        "[{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 10}]") + "]"));
                source.Add("test.widget", TestSupport.Table("test.widget",
                    "[{\"id\": \"test.widget.a\", \"owner\": \"test.owner.ghost\"}]"));
            }, out var report);
            Assert.True(report.IsBlocking);
            Assert.Throws<InvalidOperationException>(() => registry.GetAll("item.template"));

            // 修复前：下面这行会抛 InvalidOperationException——即使被评分的物品模板/三张 stat.*
            // 支持表与触发阻断的 test.widget.owner 毫无关系。修复后：不抛异常，且结果与非阻断态
            // 完全一致（不真的降级——具体 DataRegistry 场景下这些表本身没受影响）。
            var result = EquipmentScoreAnalyzer.Score(new Id("item.tpl.n45_blocked"), classId: null, view: registry);

            Assert.Equal(10.0, result.Score, 9);
            Assert.False(result.IsDegraded);
            Assert.Empty(result.MissingTables);
        }
    }
}
