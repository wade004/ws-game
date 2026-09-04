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
            "{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.item.slot.consumable\"}]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\", \"budget_multiplier\": 1}]";

        private const string BudgetCurveJson =
            "[{\"id\": \"item.budget.default\", \"entries\": [" +
            "{\"item_level\": 1, \"budget\": 20}, {\"item_level\": 10, \"budget\": 200}]}]";

        private static IDataRegistryView BuildView(string templateJson, string? setJson = null)
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("item.template", TestSupport.Table("item.template", templateJson));
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
    }
}
