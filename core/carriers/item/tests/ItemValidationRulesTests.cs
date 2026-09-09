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
        public void StackSizeRule_BelowOne_ReportsIssue()
        {
            var view = BuildView(
                "[{\"id\": \"item.sample_bad_stack\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_bad_stack\", \"stack_size\": 0," +
                " \"name_key\": \"l10n.item.sample_bad_stack\"}]");

            var issues = new ItemStackSizeRule().Validate(view).ToList();

            Assert.Contains(issues, i => i.Check == ItemStackSizeRule.CheckMin);
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
            var setJson = "[{\"id\": \"item.set.sample_broken\", \"name_key\": \"l10n.item.set.sample_broken\"," +
                " \"pieces\": [\"item.sample_other\"], \"bonuses\": []}]";
            var view = BuildView(
                "[{\"id\": \"item.sample_ring\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display.item.sample_ring\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.sample_ring\", \"set_id\": \"item.set.sample_broken\"}]",
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
    }
}
