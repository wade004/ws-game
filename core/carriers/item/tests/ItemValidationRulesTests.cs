using System;
using System.Linq;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>四条 <see cref="Core.Foundation.DataRegistry.IValidationRule"/> 的直接调用测试：
    /// 构造一个已 <see cref="IDataRegistry.LoadAll"/> 过的（未注册这些规则，避免 <see
    /// cref="TestSupport.BuildRegistry"/> 因阻断而在夹具阶段抛异常）<see cref="IDataRegistryView"/>，
    /// 再直接调用规则的 <see cref="IValidationRule.Validate"/> 断言产出的 <see cref="ValidationIssue"/>
    /// 列表，不经过"注册规则 → LoadAll → 检查报告"这条会在负例场景下于夹具阶段就失败的路径。</summary>
    public class ItemValidationRulesTests
    {
        private const string SlotJson =
            "[{\"id\": \"item.slot.main_hand\", \"name_key\": \"l10n.item.slot.main_hand\", \"is_weapon\": true}," +
            "{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.item.slot.consumable\", \"is_equipment\": false}]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\", \"budget_multiplier\": 1}]";

        private const string BudgetCurveJson =
            "[{\"id\": \"item.budget.default\", \"entries\": [" +
            "{\"item_level\": 1, \"budget\": 20}, {\"item_level\": 10, \"budget\": 200}]}]";

        // ADR-0019 F1c：stats[].stat/grants.auras 现登记为 Reference(stat.definition)/
        // Reference(skill.aura_def)，本文件全部用例共用同一批假 id，补一份最小合法数据满足引用
        // 完整性（各用例只关心特定规则的 Validate 输出，不关心这两张表的具体内容）。
        private const string StatDefJson =
            "[{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"group\": \"primary\"}]";

        private const string AuraDefJson =
            "[{\"id\": \"skill.aura_def.sample_regen\", \"effects\": []}," +
            "{\"id\": \"skill.aura_def.sample_ward\", \"effects\": []}]";

        private static IDataRegistryView BuildView(string templateJson, string? setJson = null)
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("item.template", TestSupport.Table("item.template", templateJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("skill.aura_def", TestSupport.Table("skill.aura_def", AuraDefJson));
                if (setJson != null)
                {
                    source.Add("item.set", TestSupport.Table("item.set", setJson));
                }
            });
        }

        [Fact]
        public void BudgetRule_WithinBudget_NoIssue()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_ok\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_ok\", \"stack_size\": 5," +
                " \"name_key\": \"l10n.item.sample_ok\"," +
                " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 15}]}]");

            var issues = new ItemBudgetValidationRule(new Id("item.budget.default")).Validate(view).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void BudgetRule_ExceedsBudget_ReportsIssue()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_over\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_over\", \"stack_size\": 5," +
                " \"name_key\": \"l10n.item.sample_over\"," +
                " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 25}]}]");

            var issues = new ItemBudgetValidationRule(new Id("item.budget.default")).Validate(view).ToList();

            Assert.Single(issues);
            Assert.Equal(ItemBudgetValidationRule.Check, issues[0].Check);
            Assert.Equal(ValidationSeverity.Error, issues[0].Severity);
        }

        [Fact]
        public void BudgetRule_PctOp_FoldedByHundred_ExceedsBudget()
        {
            // item_level=1 → budget=20；value=0.25 的 pct 折算为 25，超出预算。
            var view = BuildView(
                "[{\"id\": \"item.sample_pct\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_pct\", \"stack_size\": 5," +
                " \"name_key\": \"l10n.item.sample_pct\"," +
                " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"pct\", \"value\": 0.25}]}]");

            var issues = new ItemBudgetValidationRule(new Id("item.budget.default")).Validate(view).ToList();

            Assert.Single(issues);
        }

        /// <summary>加固J3：数据集完全没有 item 域（item.template 一行都没有，例如单独校验
        /// data/_framework——该数据根只登记 found.* 两张框架级纯登记表）时，预算规则应容错跳过，
        /// 不因为"预算曲线也不存在"而报错。</summary>
        [Fact]
        public void BudgetRule_NoItemDomain_NoIssue()
        {
            var view = TestSupport.BuildRegistry(source =>
            {
                // 故意不往 source 里塞任何 item.* 表数据（对应"数据集完全没有 item 域"），
                // 惯例同 BuildView 但省去全部四个 source.Add 调用。
            });

            var issues = new ItemBudgetValidationRule(new Id("item.budget.default")).Validate(view).ToList();

            Assert.Empty(issues);
        }

        /// <summary>加固J3：数据集确实登记了 item.template（哪怕只有一条）但预算曲线缺失时，
        /// 仍然报错——"有 item 域"与"无 item 域"的容错边界，不能因为容错就连真正缺配置的情况
        /// 也放过。</summary>
        [Fact]
        public void BudgetRule_ItemTemplatePresent_MissingBudgetCurve_ReportsIssue()
        {
            var view = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                // 故意不注册 item.budget_curve 任何行（曲线 id "item.budget.default" 不存在）。
                source.Add("item.template", TestSupport.Table("item.template",
                    "[{\"id\": \"item.sample_no_curve\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                    " \"item_level\": 1, \"display_ref\": \"display.item.sample_no_curve\", \"stack_size\": 5," +
                    " \"name_key\": \"l10n.item.sample_no_curve\"," +
                    " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 5}]}]"));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
            });

            var issues = new ItemBudgetValidationRule(new Id("item.budget.default")).Validate(view).ToList();

            Assert.Single(issues);
            Assert.Equal(ItemBudgetValidationRule.Check, issues[0].Check);
            Assert.Equal(ValidationSeverity.Error, issues[0].Severity);
        }

        [Fact]
        public void WeaponProfileRule_WeaponSlotMissingProfile_ReportsIssue()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_bad_weapon\", \"slot\": \"item.slot.main_hand\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_bad_weapon\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_bad_weapon\"}]");

            var issues = new ItemWeaponProfileRule().Validate(view).ToList();

            Assert.Contains(issues, i => i.Check == ItemWeaponProfileRule.CheckMissing);
        }

        [Fact]
        public void WeaponProfileRule_NonWeaponSlotWithProfile_ReportsIssue()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_odd\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_odd\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_odd\"," +
                " \"weapon_profile\": {\"damage_min\": 1, \"damage_max\": 2, \"speed\": 1, \"weapon_school\": \"skill.school.physical\"}}]");

            var issues = new ItemWeaponProfileRule().Validate(view).ToList();

            Assert.Contains(issues, i => i.Check == ItemWeaponProfileRule.CheckUnexpected);
        }

        [Fact]
        public void WeaponProfileRule_WeaponSlotWithProfile_NoIssue()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_good_weapon\", \"slot\": \"item.slot.main_hand\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_good_weapon\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_good_weapon\"," +
                " \"weapon_profile\": {\"damage_min\": 1, \"damage_max\": 2, \"speed\": 1, \"weapon_school\": \"skill.school.physical\"}}]");

            var issues = new ItemWeaponProfileRule().Validate(view).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void StackSizeRule_BelowOne_NowCaughtBySchemaAlone_AtBuildTime()
        {
            // ADR-0021 收口：stack_size < 1 此前结构上仍是合法 Int（0/负数不违反 field_type），数值
            // 范围只能靠 ItemStackSizeRule.CheckMin 拦下；ItemSchemas.Template 对 stack_size 登记
            // min:1 后（见 ItemSchemas.cs stack_size 字段旁判断记录），同一批坏数据现在在
            // TestSupport.BuildRegistry 内部的 registry.LoadAll() 就已经被 field_range 挡下
            // （IsBlocking），该辅助方法按设计在夹具不干净时直接抛 InvalidOperationException（见类型
            // 顶部判断记录），不再需要专门注册/调用 ItemStackSizeRule 才能观测到这条约束——
            // ItemStackSizeRule.CheckMin 本身未退役（同 LootSchemas/EconomySchemas 判断记录，字段级
            // Range 与业务规则并存），只是这个具体输入值现在两者会同时命中，无法用同一个输入值单独
            // 触发其中一个而不触发另一个。
            var ex = Assert.Throws<InvalidOperationException>(() => BuildView(
                "[{\"id\": \"item.sample_bad_stack\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_bad_stack\", \"stack_size\": 0," +
                " \"name_key\": \"l10n.item.sample_bad_stack\"}]"));

            Assert.Contains("field_range", ex.Message);
            Assert.Contains("stack_size", ex.Message);
        }

        [Fact]
        public void StackSizeRule_EquipmentSlotStackSizeNotOne_ReportsIssue()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_bad_equip_stack\", \"slot\": \"item.slot.main_hand\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_bad_equip_stack\", \"stack_size\": 5," +
                " \"name_key\": \"l10n.item.sample_bad_equip_stack\"," +
                " \"weapon_profile\": {\"damage_min\": 1, \"damage_max\": 2, \"speed\": 1, \"weapon_school\": \"skill.school.physical\"}}]");

            var issues = new ItemStackSizeRule().Validate(view).ToList();

            Assert.Contains(issues, i => i.Check == ItemStackSizeRule.CheckEquipmentUnique);
        }

        [Fact]
        public void StackSizeRule_NonEquipmentBucketSlot_MultiStack_NoIssue()
        {
            // 阶段 3 整理："item.slot.consumable" 在本文件的 SlotJson 里 is_equipment=false（分类
            // 桶），stack_size > 1 不应触发 CheckEquipmentUnique（见 ItemStackSizeRule 判断记录）。
            var view = BuildView(
                "[{\"id\": \"item.sample_potion\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_potion\", \"stack_size\": 20," +
                " \"name_key\": \"l10n.item.sample_potion\"}]");

            var issues = new ItemStackSizeRule().Validate(view).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void SetMembershipRule_MismatchedPieces_ReportsIssue()
        {
            // ADR-0022 决策 4：item.set.pieces 现登记为 IdList + ReferenceTable(item.template)，
            // reference_integrity 会先于本规则拦下指向不存在物品的 pieces 元素——本用例要测试的是
            // "item.template.set_id 与 item.set.pieces 互相不一致"这条更窄的业务判断（两者都指向
            // 真实存在的记录，只是彼此不认可对方），因此 pieces 改为指向另一件真实存在的物品
            // （item.sample_other），而不是一个不存在的 id；"不存在的 id"这类缺陷现由
            // reference_integrity 在加载期更早地报告，不再是本规则的职责。
            var setJson = "[{\"id\": \"item.set.sample_broken\", \"name_key\": \"l10n.item.set.sample_broken\"," +
                " \"pieces\": [\"item.sample_other\"], \"bonuses\": []}]";
            var view = BuildView(
                "[{\"id\": \"item.sample_ring\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_ring\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_ring\", \"set_id\": \"item.set.sample_broken\"}," +
                "{\"id\": \"item.sample_other\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_other\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_other\"}]",
                setJson);

            var issues = new ItemSetMembershipRule().Validate(view).ToList();

            Assert.Contains(issues, i => i.Check == ItemSetMembershipRule.Check);
        }

        [Fact]
        public void SetMembershipRule_ConsistentPieces_NoIssue()
        {
            var setJson = "[{\"id\": \"item.set.sample_ok\", \"name_key\": \"l10n.item.set.sample_ok\"," +
                " \"pieces\": [\"item.sample_ring\"], \"bonuses\": []}]";
            var view = BuildView(
                "[{\"id\": \"item.sample_ring\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_ring\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_ring\", \"set_id\": \"item.set.sample_ok\"}]",
                setJson);

            var issues = new ItemSetMembershipRule().Validate(view).ToList();

            Assert.Empty(issues);
        }

        // -----------------------------------------------------------------
        // 相邻缺口根治（第五轮外部审核 audit-5e779c6-20260907，WA 报告"需要说明的取舍"第 3 条）：
        // ItemGrantsAurasDuplicateRule——grants.auras 同一物品内重复登记同一个 aura_def。
        // -----------------------------------------------------------------

        [Fact]
        public void GrantsAurasDuplicateRule_SameAuraDefListedTwice_ReportsWarning()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_dup_aura\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_dup_aura\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_dup_aura\"," +
                " \"grants\": {\"auras\": [\"skill.aura_def.sample_regen\", \"skill.aura_def.sample_regen\"]}}]");

            var issues = new ItemGrantsAurasDuplicateRule().Validate(view).ToList();

            Assert.Single(issues);
            Assert.Equal(ItemGrantsAurasDuplicateRule.Check, issues[0].Check);
            Assert.Equal(ValidationSeverity.Warning, issues[0].Severity);
        }

        [Fact]
        public void GrantsAurasDuplicateRule_ThreeCopiesOfSameAuraDef_ReportsOnlyOneIssue()
        {
            // 重复 3 次也只应报一条问题（按去重后的 aura_def id 报告，不是按"重复出现的次数"报告）。
            var view = BuildView(
                "[{\"id\": \"item.sample_dup_aura3\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_dup_aura3\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_dup_aura3\"," +
                " \"grants\": {\"auras\": [\"skill.aura_def.sample_regen\", \"skill.aura_def.sample_regen\"," +
                " \"skill.aura_def.sample_regen\"]}}]");

            var issues = new ItemGrantsAurasDuplicateRule().Validate(view).ToList();

            Assert.Single(issues);
        }

        [Fact]
        public void GrantsAurasDuplicateRule_DistinctAuraDefs_NoIssue()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_distinct_auras\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_distinct_auras\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_distinct_auras\"," +
                " \"grants\": {\"auras\": [\"skill.aura_def.sample_regen\", \"skill.aura_def.sample_ward\"]}}]");

            var issues = new ItemGrantsAurasDuplicateRule().Validate(view).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void GrantsAurasDuplicateRule_NoGrantsField_NoIssue()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_no_grants\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_no_grants\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_no_grants\"}]");

            var issues = new ItemGrantsAurasDuplicateRule().Validate(view).ToList();

            Assert.Empty(issues);
        }

        // -----------------------------------------------------------------
        // T-N2-1（ADR-0032 决策 2）：品质倍率顺序须与 sort_weight 一致
        // -----------------------------------------------------------------

        private static IDataRegistryView BuildQualityView(string qualityJson)
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", qualityJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("skill.aura_def", TestSupport.Table("skill.aura_def", AuraDefJson));
            });
        }

        [Fact]
        public void QualityMultiplierOrderRule_IncreasingWithSortWeight_NoIssue()
        {
            var view = BuildQualityView(
                "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\", \"sort_weight\": 1," +
                " \"budget_multiplier\": 1.0, \"price_multiplier\": 1.0}," +
                "{\"id\": \"item.quality.rare\", \"name_key\": \"l10n.item.quality.rare\", \"sort_weight\": 2," +
                " \"budget_multiplier\": 1.5, \"price_multiplier\": 1.5}," +
                "{\"id\": \"item.quality.epic\", \"name_key\": \"l10n.item.quality.epic\", \"sort_weight\": 3," +
                " \"budget_multiplier\": 1.5, \"price_multiplier\": 2.0}]");

            var issues = new ItemQualityMultiplierOrderRule().Validate(view).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void QualityMultiplierOrderRule_DecreasingBudgetMultiplier_ReportsError()
        {
            // sort_weight 2 的品质 budget_multiplier（0.8）低于 sort_weight 1 已出现的最大值（1.0），
            // 顺序与排序权重不一致（ADR-0032 决策 2）。
            var view = BuildQualityView(
                "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\", \"sort_weight\": 1," +
                " \"budget_multiplier\": 1.0, \"price_multiplier\": 1.0}," +
                "{\"id\": \"item.quality.rare\", \"name_key\": \"l10n.item.quality.rare\", \"sort_weight\": 2," +
                " \"budget_multiplier\": 0.8, \"price_multiplier\": 1.5}]");

            var issues = new ItemQualityMultiplierOrderRule().Validate(view).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Equal(ItemQualityMultiplierOrderRule.Check, issue.Check);
            Assert.Equal("budget_multiplier", issue.Field);
            Assert.Equal("item.quality.rare", issue.RecordKey);
        }

        [Fact]
        public void QualityMultiplierOrderRule_SameSortWeightDifferentMultiplier_NoIssue()
        {
            // 并列 sort_weight 的两个品质彼此不比较（相对顺序未定义，见类型判断记录）。
            var view = BuildQualityView(
                "[{\"id\": \"item.quality.side_a\", \"name_key\": \"l10n.item.quality.side_a\", \"sort_weight\": 1," +
                " \"budget_multiplier\": 1.2, \"price_multiplier\": 1.0}," +
                "{\"id\": \"item.quality.side_b\", \"name_key\": \"l10n.item.quality.side_b\", \"sort_weight\": 1," +
                " \"budget_multiplier\": 0.8, \"price_multiplier\": 1.0}]");

            var issues = new ItemQualityMultiplierOrderRule().Validate(view).ToList();

            Assert.Empty(issues);
        }

        // -----------------------------------------------------------------
        // T-N2-2（ADR-0032 决策 7；04 第 5 节"词缀份额之和"）：单条 item.affix.stat_mix
        // 内部 ratio 之和不超过一
        // -----------------------------------------------------------------

        private static IDataRegistryView BuildAffixView(string affixJson)
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("item.affix", TestSupport.Table("item.affix", affixJson));
            });
        }

        [Fact]
        public void AffixStatMixRatioSumRule_SumExceedsOne_ReportsError()
        {
            // 0.6 + 0.6 = 1.2 > 1，超出 Epsilon 容差，报错（ADR-0032 决策 7）。
            var view = BuildAffixView(
                "[{\"id\": \"item.affix.sum_bad\", \"name_key\": \"l10n.item.affix.sum_bad\"," +
                " \"budget_share\": 0.5, \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 0.6}," +
                " {\"stat\": \"stat.strength\", \"ratio\": 0.6}]}]");

            var issues = new ItemAffixStatMixRatioSumRule().Validate(view).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Equal(ItemAffixStatMixRatioSumRule.Check, issue.Check);
            Assert.Equal("stat_mix", issue.Field);
            Assert.Equal("item.affix.sum_bad", issue.RecordKey);
        }

        [Fact]
        public void AffixStatMixRatioSumRule_SumWithinOne_NoIssue()
        {
            // 0.5 + 0.5 = 1（含浮点误差在 Epsilon 容差内），不报错。
            var view = BuildAffixView(
                "[{\"id\": \"item.affix.sum_ok\", \"name_key\": \"l10n.item.affix.sum_ok\"," +
                " \"budget_share\": 0.5, \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 0.5}," +
                " {\"stat\": \"stat.strength\", \"ratio\": 0.5}]}]");

            var issues = new ItemAffixStatMixRatioSumRule().Validate(view).ToList();

            Assert.Empty(issues);
        }

        // -----------------------------------------------------------------
        // T-N2-3（ADR-0032 决策 3/10）：预算消耗公式加权改造集成到 ItemBudgetValidationRule 后的
        // 槽位系数、预算利用率过低警告——手算数值见 ItemBudgetCurveComputeConsumedTests（公式本身的
        // 6+ 组 k=1/k=1.5 手算不在本文件重复，这里只覆盖 Validate() 这一集成层：曲线×品质倍率×槽位
        // 系数算预算上限、利用率阈值判定）。
        // -----------------------------------------------------------------

        private const string SlotWithCoefficientJson =
            "[{\"id\": \"item.slot.ring\", \"name_key\": \"l10n.item.slot.ring\", \"budget_coefficient\": 0.5}," +
            "{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.item.slot.consumable\", \"is_equipment\": false}]";

        private static IDataRegistryView BuildBudgetFormulaView(string templateJson)
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotWithCoefficientJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("item.template", TestSupport.Table("item.template", templateJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("skill.aura_def", TestSupport.Table("skill.aura_def", AuraDefJson));
                // 故意不注册 stat.weight 行：ComputeConsumed 对没有对应记录的属性按缺省权重 1 回退
                // （见 ItemBudgetCurve.BuildStatBudgetInfo 判断记录），本组用例只关心槽位系数/利用率
                // 阈值这一层，不需要额外权重变量。
            });
        }

        [Fact]
        public void BudgetRule_SlotBudgetCoefficient_ShrinksLimit_PreviouslyOkItemNowExceeds()
        {
            // 手算（"含槽位系数的一组"）：item_level=1 → 曲线预算 20，quality.common 倍率 1，
            // item.slot.ring 槽位系数 0.5 ⇒ 上限 = 20×1×0.5 = 10。stats 单一 flat 词条 value=15，
            // 权重缺省 1 ⇒ 消耗 = 15。15 > 10，超预算报错——同样的 15 点若槽位系数是 1（既有
            // BudgetRule_WithinBudget_NoIssue 用的 consumable 槽位）本应在 20 的上限内合法，槽位系数
            // 把上限砍半后才超标，验证槽位系数确实乘进了预算上限。
            var view = BuildBudgetFormulaView(
                "[{\"id\": \"item.sample_ring\", \"slot\": \"item.slot.ring\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_ring\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_ring\"," +
                " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 15}]}]");

            var issues = new ItemBudgetValidationRule(new Id("item.budget.default")).Validate(view).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Equal(ItemBudgetValidationRule.Check, issue.Check);
        }

        [Fact]
        public void BudgetRule_UtilizationBelowThreshold_ReportsNonEscalatableWarning()
        {
            // 正例（利用率警告）：item_level=1 → 上限 20（quality/槽位系数均 1）。stats 消耗 8，
            // 利用率 8/20 = 0.4 < 默认阈值 0.7 ⇒ 警告，检查名 item_budget_utilization_low，且不产生
            // 超预算 Error（8 < 20）。
            var view = BuildBudgetFormulaView(
                "[{\"id\": \"item.sample_underfilled\", \"slot\": \"item.slot.consumable\", " +
                " \"quality\": \"item.quality.common\", \"item_level\": 1, " +
                " \"display_ref\": \"display.item.sample_underfilled\", \"stack_size\": 5," +
                " \"name_key\": \"l10n.item.sample_underfilled\"," +
                " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 8}]}]");

            var rule = new ItemBudgetValidationRule(new Id("item.budget.default"));
            var issues = rule.Validate(view).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal(ItemBudgetValidationRule.CheckUtilizationLow, issue.Check);
            Assert.True(rule.NonEscalatable);
        }

        /// <summary>分阶段落地计划 T-N5-3（数值规则核对表 W2）：不可提升警告在
        /// <see cref="DataRegistryStrictness.WarningsBlock"/> 下不阻断——同数据、同规则，走完整的
        /// <see cref="DataRegistry.LoadAll()"/> + <see cref="ValidationReport"/> 路径（不是像上面
        /// <c>BudgetRule_UtilizationBelowThreshold_ReportsNonEscalatableWarning</c> 那样只调用
        /// <c>rule.Validate(view)</c> 拿裸问题列表——那条路径永远看不到 <see
        /// cref="ValidationReport.IsBlocking"/>，证明不了"不阻断"这件事本身）。判断记录（不经
        /// <see cref="TestSupport.BuildRegistry"/>）：本用例需要 <c>report.IsBlocking</c> 只反映本
        /// 规则自己的 Warning，而 <see cref="TestSupport.BuildRegistry"/> 不注册
        /// <c>l10n.locale</c>/<c>l10n.text</c>——name_key 字段会各自降级出一条"l10n.text 表未加载，
        /// 跳过"的 Warning（可提升，会在 WarningsBlock 下一并阻断，掩盖本规则本身"不可提升"的行为），
        /// 因此改为手工建 registry 并显式补齐 l10n 两张表覆盖全部 name_key（同
        /// <c>T_N4_6_EconomyPriceFormulaTests.DeviationRule_NonEscalatable_UnderWarningsBlock_DoesNotBlock</c>/
        /// <c>StatDefinitionConsumerValidationRuleTests.NoConsumerWarning_UnderWarningsBlock_DoesNotBlock</c>
        /// 同一处理惯例）。</summary>
        [Fact]
        public void BudgetRule_UtilizationBelowThreshold_UnderWarningsBlock_DoesNotBlock()
        {
            const string slot = "[{\"id\": \"item.slot.n5_3_w2\", \"name_key\": \"l10n.n5_3_w2_slot\"}]";
            const string quality = "[{\"id\": \"item.quality.n5_3_w2\", \"name_key\": \"l10n.n5_3_w2_quality\", \"budget_multiplier\": 1}]";
            const string budgetCurve = "[{\"id\": \"item.budget.n5_3_w2\", \"entries\": [{\"item_level\": 1, \"budget\": 20}]}]";
            const string statDef = "[{\"id\": \"stat.n5_3_w2\", \"name_key\": \"l10n.n5_3_w2_stat\", \"group\": \"primary\"}]";
            const string template =
                "[{\"id\": \"item.n5_3_w2\", \"slot\": \"item.slot.n5_3_w2\", \"quality\": \"item.quality.n5_3_w2\", " +
                "\"item_level\": 1, \"display_ref\": \"display.n5_3_w2\", \"stack_size\": 1, \"name_key\": \"l10n.n5_3_w2_item\", " +
                "\"stats\": [{\"stat\": \"stat.n5_3_w2\", \"op\": \"flat\", \"value\": 8}]}]";
            const string locales = "[{\"id\": \"l10n.locale.zh_cn\", \"is_default\": true}]";
            const string texts =
                "[{\"key\": \"l10n.n5_3_w2_slot\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}," +
                "{\"key\": \"l10n.n5_3_w2_quality\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}," +
                "{\"key\": \"l10n.n5_3_w2_stat\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}," +
                "{\"key\": \"l10n.n5_3_w2_item\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}]";

            var source = new InMemoryDataSource()
                .Add(ItemSchemas.SlotDefinition.Name, TestSupport.Table(ItemSchemas.SlotDefinition.Name, slot))
                .Add(ItemSchemas.QualityDefinition.Name, TestSupport.Table(ItemSchemas.QualityDefinition.Name, quality))
                .Add(ItemSchemas.BudgetCurve.Name, TestSupport.Table(ItemSchemas.BudgetCurve.Name, budgetCurve))
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name, TestSupport.Table(Core.Numbers.StatBlock.StatSchemas.Definition.Name, statDef))
                .Add(ItemSchemas.Template.Name, TestSupport.Table(ItemSchemas.Template.Name, template))
                .Add("l10n.locale", TestSupport.Table("l10n.locale", locales))
                .Add("l10n.text", TestSupport.Table("l10n.text", texts));

            var registry = new DataRegistry(source, TestSupport.CreateBus(),
                new DataRegistryOptions { FailOnUnknownTable = false, Strictness = DataRegistryStrictness.WarningsBlock });
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(ItemSchemas.BudgetCurve);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(Core.Foundation.Localization.L10nSchemas.Locale);
            registry.RegisterSchema(Core.Foundation.Localization.L10nSchemas.Text);
            registry.RegisterValidationRule(new ItemBudgetValidationRule(new Id("item.budget.n5_3_w2")));

            var report = registry.LoadAll();

            var issue = Assert.Single(report.Issues, i => i.Check == ItemBudgetValidationRule.CheckUtilizationLow);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal(1, report.NonEscalatableWarningCount);
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void BudgetRule_UtilizationAtOrAboveThreshold_NoIssue()
        {
            // 负例：同一预算上限 20，stats 消耗 15，利用率 15/20 = 0.75 >= 默认阈值 0.7，不报警告
            // （也不超预算）。
            var view = BuildBudgetFormulaView(
                "[{\"id\": \"item.sample_wellfilled\", \"slot\": \"item.slot.consumable\", " +
                " \"quality\": \"item.quality.common\", \"item_level\": 1, " +
                " \"display_ref\": \"display.item.sample_wellfilled\", \"stack_size\": 5," +
                " \"name_key\": \"l10n.item.sample_wellfilled\"," +
                " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 15}]}]");

            var issues = new ItemBudgetValidationRule(new Id("item.budget.default")).Validate(view).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void BudgetRule_CustomUtilizationThreshold_OverloadOverridesDefault()
        {
            // T-N2-3 新增构造重载：同 BudgetRule_UtilizationAtOrAboveThreshold_NoIssue 的 0.75 利用率，
            // 默认阈值 0.7 下不报警告；显式传入阈值 0.9 后 0.75 < 0.9，应报警告——验证新重载确实生效
            // （不是默认值的静默复制）。
            var view = BuildBudgetFormulaView(
                "[{\"id\": \"item.sample_customthreshold\", \"slot\": \"item.slot.consumable\", " +
                " \"quality\": \"item.quality.common\", \"item_level\": 1, " +
                " \"display_ref\": \"display.item.sample_customthreshold\", \"stack_size\": 5," +
                " \"name_key\": \"l10n.item.sample_customthreshold\"," +
                " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 15}]}]");

            var issues = new ItemBudgetValidationRule(new Id("item.budget.default"), 0.9).Validate(view).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal(ItemBudgetValidationRule.CheckUtilizationLow, issue.Check);
        }

        // -----------------------------------------------------------------
        // T-N2-11（ADR-0032 决策 7/10；04 第 5 节"模板加词缀最大份额超预算"）：模板自身消耗 +
        // 可抽词缀池最大份额 × 预算不得超过预算上限。全部用例复用 item_level=1 → 曲线预算 20
        // （BudgetCurveJson）、quality.common 倍率 1、item.slot.consumable 槽位系数缺省 1 ⇒
        // 预算上限 B = 20，与既有 BudgetRule_* 用例同一组手算基线。
        // -----------------------------------------------------------------

        private const string QualityWithAffixCountOneJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\"," +
            " \"budget_multiplier\": 1, \"affix_count\": 1}]";

        private static IDataRegistryView BuildTemplateAffixShareView(
            string templateJson, string affixJson, string? qualityJson = null)
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition",
                    TestSupport.Table("item.quality_definition", qualityJson ?? QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("item.template", TestSupport.Table("item.template", templateJson));
                source.Add("item.affix", TestSupport.Table("item.affix", affixJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("skill.aura_def", TestSupport.Table("skill.aura_def", AuraDefJson));
            });
        }

        private static string TemplateWithStatsAndOptionalAffixes(string id, double statValue, string? affixesJson = null)
        {
            var affixesField = affixesJson != null ? $", \"affixes\": {affixesJson}" : string.Empty;
            return "[{\"id\": \"" + id + "\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display." + id + "\", \"stack_size\": 5," +
                " \"name_key\": \"l10n." + id + "\"," +
                " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": " + statValue + "}]" +
                affixesField + "}]";
        }

        [Fact]
        public void AffixShareRule_ConsumedPlusMaxShareExceedsBudget_ReportsError()
        {
            // consumed=15、候选词缀唯一一条 budget_share=0.5（affix_count=1 全取），
            // total = 15 + 0.5×20 = 25 > 20（B）⇒ 报错。
            var templateJson = TemplateWithStatsAndOptionalAffixes("item.sample_share_over", 15);
            var affixJson =
                "[{\"id\": \"item.affix.a\", \"name_key\": \"l10n.item.affix.a\", \"budget_share\": 0.5," +
                " \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1}]}]";
            var view = BuildTemplateAffixShareView(templateJson, affixJson, QualityWithAffixCountOneJson);

            var issues = new ItemTemplateAffixShareExceedsBudgetRule(new Id("item.budget.default")).Validate(view).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Equal(ItemTemplateAffixShareExceedsBudgetRule.Check, issue.Check);
            Assert.Equal("item.sample_share_over", issue.RecordKey);
            Assert.Equal("affixes", issue.Field);
        }

        [Fact]
        public void AffixShareRule_ConsumedPlusMaxShareWithinBudget_NoIssue()
        {
            // consumed=5，同一条候选词缀 budget_share=0.5，total = 5 + 10 = 15 <= 20（B）⇒ 不报错。
            var templateJson = TemplateWithStatsAndOptionalAffixes("item.sample_share_ok", 5);
            var affixJson =
                "[{\"id\": \"item.affix.a\", \"name_key\": \"l10n.item.affix.a\", \"budget_share\": 0.5," +
                " \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1}]}]";
            var view = BuildTemplateAffixShareView(templateJson, affixJson, QualityWithAffixCountOneJson);

            var issues = new ItemTemplateAffixShareExceedsBudgetRule(new Id("item.budget.default")).Validate(view).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void AffixShareRule_TwoCandidatesNoWhitelist_TopShareByDescendingBudgetShareExceedsBudget()
        {
            // 两条候选词缀（budget_share 0.5/0.1），affix_count=1 只取降序最高的一条（0.5）；
            // consumed=15，total = 15 + 0.5×20 = 25 > 20 ⇒ 报错——验证"按 budget_share 降序取前
            // affix_count 条"确实取到了份额更大的那条，不是任意/插入顺序。
            var templateJson = TemplateWithStatsAndOptionalAffixes("item.sample_share_two_candidates", 15);
            var affixJson =
                "[{\"id\": \"item.affix.a\", \"name_key\": \"l10n.item.affix.a\", \"budget_share\": 0.5," +
                " \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1}]}," +
                "{\"id\": \"item.affix.b\", \"name_key\": \"l10n.item.affix.b\", \"budget_share\": 0.1," +
                " \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1}]}]";
            var view = BuildTemplateAffixShareView(templateJson, affixJson, QualityWithAffixCountOneJson);

            var issues = new ItemTemplateAffixShareExceedsBudgetRule(new Id("item.budget.default")).Validate(view).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal(ItemTemplateAffixShareExceedsBudgetRule.Check, issue.Check);
        }

        [Fact]
        public void AffixShareRule_TemplateAffixesWhitelistNarrowsToLowerShareCandidate_NoIssue()
        {
            // 同上一用例的两条候选词缀与 consumed=15，但模板 affixes 白名单只收窄到份额更小的
            // item.affix.b（0.1）：total = 15 + 0.1×20 = 17 <= 20 ⇒ 不报错——白名单收窄候选池后
            // 不再超预算（若不收窄，同上一用例会取到 0.5 那条而报错）。
            var templateJson = TemplateWithStatsAndOptionalAffixes(
                "item.sample_share_whitelisted", 15, "[\"item.affix.b\"]");
            var affixJson =
                "[{\"id\": \"item.affix.a\", \"name_key\": \"l10n.item.affix.a\", \"budget_share\": 0.5," +
                " \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1}]}," +
                "{\"id\": \"item.affix.b\", \"name_key\": \"l10n.item.affix.b\", \"budget_share\": 0.1," +
                " \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1}]}]";
            var view = BuildTemplateAffixShareView(templateJson, affixJson, QualityWithAffixCountOneJson);

            var issues = new ItemTemplateAffixShareExceedsBudgetRule(new Id("item.budget.default")).Validate(view).ToList();

            Assert.Empty(issues);
        }

        [Fact]
        public void AffixShareRule_QualityAffixCountUnregistered_TreatedAsUnlimited_SumsAllCandidates()
        {
            // quality.common 不登记 affix_count（用既有 QualityJson，不是 QualityWithAffixCountOneJson）
            // ⇒ 视为不限，取全部候选之和：两条候选各 budget_share=0.3，maxShare = 0.6；
            // consumed=10，total = 10 + 0.6×20 = 22 > 20 ⇒ 报错——验证"未登记 affix_count"不等于
            // "affix_count=0（不取任何候选）"，是保守上界口径（同类型判断记录）。
            var templateJson = TemplateWithStatsAndOptionalAffixes("item.sample_share_unlimited", 10);
            var affixJson =
                "[{\"id\": \"item.affix.a\", \"name_key\": \"l10n.item.affix.a\", \"budget_share\": 0.3," +
                " \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1}]}," +
                "{\"id\": \"item.affix.b\", \"name_key\": \"l10n.item.affix.b\", \"budget_share\": 0.3," +
                " \"quality_pool\": \"item.quality.common\", \"weight\": 1," +
                " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1}]}]";
            var view = BuildTemplateAffixShareView(templateJson, affixJson, QualityJson);

            var issues = new ItemTemplateAffixShareExceedsBudgetRule(new Id("item.budget.default")).Validate(view).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal(ItemTemplateAffixShareExceedsBudgetRule.Check, issue.Check);
        }
    }
}
