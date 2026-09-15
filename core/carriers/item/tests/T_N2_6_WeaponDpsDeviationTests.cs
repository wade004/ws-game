using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 分阶段落地计划 T-N2-6（ADR-0032 决策 4；拍板 6；设计层裁定"护甲位显式字段"）验收测试：
    /// 武器秒伤查询 <see cref="EquipmentHost.GetWeaponDps"/>、<c>damage_min/max</c> 偏离秒伤曲线警告
    /// （<see cref="ItemWeaponDamageDeviatesDpsCurveRule"/>）、<c>item.slot_definition.has_armor</c>
    /// 显式护甲位字段（取代 T-N2-5 的"非武器位且真正装备位"推断，见 <c>core/carriers/item/README.md</c>
    /// 判断记录 20/21）。数据夹具独立于 <see cref="EquipmentHostTests"/>/<see
    /// cref="T_N2_5_ArmorAffixReqLevelTests"/>（同"自带最小夹具"既有惯例）。
    /// </summary>
    public class T_N2_6_WeaponDpsDeviationTests
    {
        // -----------------------------------------------------------------
        // 验收组 1：GetWeaponDps 查询（曲线(item_level) × 品质预算倍率 × 武器槽位系数）。
        // -----------------------------------------------------------------

        private const string DpsSlotJson =
            "[{\"id\": \"item.slot.t6_weapon\", \"name_key\": \"l10n.item.slot.t6_weapon\"," +
            " \"is_weapon\": true, \"budget_coefficient\": 2.0}]";

        private const string DpsQualityJson =
            "[{\"id\": \"item.quality.t6_rare\", \"name_key\": \"l10n.item.quality.t6_rare\"," +
            " \"budget_multiplier\": 1.5}]";

        // item_level=1 → 10（单点曲线，PiecewiseCurve 越界夹取到端点，等价于常数）。
        private const string DpsWeaponDpsCurveJson =
            "[{\"id\": \"item.weapon_dps.t6_default\", \"entries\": [{\"x\": 1, \"y\": 10}]}]";

        private const string DpsTemplateJson =
            "[{\"id\": \"item.t6_weapon_a\", \"slot\": \"item.slot.t6_weapon\"," +
            " \"quality\": \"item.quality.t6_rare\", \"item_level\": 1," +
            " \"display_ref\": \"display.item.t6_weapon_a\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.t6_weapon_a\"," +
            " \"weapon_profile\": {\"damage_min\": 5, \"damage_max\": 25, \"speed\": 2.0," +
            " \"weapon_school\": \"skill.school.physical\"}}]";

        private sealed class DpsFixture
        {
            public InventoryHost Inventory = null!;
            public StatHost StatHost = null!;
            public EquipmentHost Equipment = null!;
        }

        private static readonly Id DpsPlayer = new Id("player.t6_dps_hero");

        // StatHost 构造期无条件要求 stat.definition 表已加载（即便本组用例不关心任何具体属性值）。
        private const string DpsStatDefJson =
            "[{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"category\": \"primary\"}]";

        private static DpsFixture BuildDpsFixture(Id? weaponDpsCurveId = null)
        {
            var registry = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", DpsSlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", DpsQualityJson));
                source.Add("item.weapon_dps_curve", TestSupport.Table("item.weapon_dps_curve", DpsWeaponDpsCurveJson));
                source.Add("item.template", TestSupport.Table("item.template", DpsTemplateJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", DpsStatDefJson));
            });

            var bus = TestSupport.CreateBus();
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            var effectSink = new FakeEffectSink();
            var skillGranter = new RecordingSkillGranter();
            var unitAccess = new FakeUnitAccess().Add(DpsPlayer, 10);
            var options = new ItemOptions { WeaponDpsCurveId = weaponDpsCurveId ?? new Id("item.weapon_dps.t6_default") };

            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, effectSink, skillGranter.Grant, unitAccess, options);

            return new DpsFixture { Inventory = inventory, StatHost = statHost, Equipment = equipment };
        }

        private static Id GiveAndReturnInstance(DpsFixture f, string templateId)
        {
            f.Inventory.AddItem(DpsPlayer, new Id(templateId), 1);
            var items = f.Inventory.ListItems(DpsPlayer);
            return items[items.Count - 1].InstanceId;
        }

        [Fact]
        public void GetWeaponDps_EquippedWeapon_ReturnsCurveTimesQualityTimesSlotCoefficient()
        {
            var f = BuildDpsFixture();
            f.StatHost.RegisterUnit(DpsPlayer);
            var instanceId = GiveAndReturnInstance(f, "item.t6_weapon_a");
            f.Equipment.Equip(DpsPlayer, instanceId, new Id("item.slot.t6_weapon"));

            var dps = f.Equipment.GetWeaponDps(DpsPlayer);

            // 手算：曲线(1)=10 × 品质预算倍率 1.5 × 武器槽位系数 2.0 = 30。
            Assert.Equal(30.0, dps, 9);
        }

        [Fact]
        public void GetWeaponDps_NoWeaponEquipped_ReturnsZero()
        {
            var f = BuildDpsFixture();
            f.StatHost.RegisterUnit(DpsPlayer);

            Assert.Equal(0.0, f.Equipment.GetWeaponDps(DpsPlayer));
        }

        [Fact]
        public void GetWeaponDps_CurveIdNotRegistered_ReturnsZero()
        {
            // ItemOptions.WeaponDpsCurveId 指向一条数据里不存在的曲线 id——同 ApplyArmorValue"曲线
            // 缺失按不写/不算处理"既有口径，返回 0，不抛异常。
            var f = BuildDpsFixture(weaponDpsCurveId: new Id("item.weapon_dps.t6_missing"));
            f.StatHost.RegisterUnit(DpsPlayer);
            var instanceId = GiveAndReturnInstance(f, "item.t6_weapon_a");
            f.Equipment.Equip(DpsPlayer, instanceId, new Id("item.slot.t6_weapon"));

            Assert.Equal(0.0, f.Equipment.GetWeaponDps(DpsPlayer));
        }

        // -----------------------------------------------------------------
        // 验收组 2：damage_min/max 偏离秒伤曲线警告（ItemWeaponDamageDeviatesDpsCurveRule）。
        // -----------------------------------------------------------------

        private const string DevSlotJson =
            "[{\"id\": \"item.slot.t6_dev_weapon\", \"name_key\": \"l10n.item.slot.t6_dev_weapon\"," +
            " \"is_weapon\": true, \"budget_coefficient\": 1.0}]";

        private const string DevQualityJson =
            "[{\"id\": \"item.quality.t6_dev_common\", \"name_key\": \"l10n.item.quality.t6_dev_common\"," +
            " \"budget_multiplier\": 1.0}]";

        // item_level=1 → 4（单点曲线）；期望均值 = 秒伤(4) × speed(1) = 4。
        private const string DevWeaponDpsCurveJson =
            "[{\"id\": \"item.weapon_dps.t6_dev\", \"entries\": [{\"x\": 1, \"y\": 4}]}]";

        private static IDataRegistryView BuildDevView(string templateJson)
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", DevSlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", DevQualityJson));
                source.Add("item.weapon_dps_curve", TestSupport.Table("item.weapon_dps_curve", DevWeaponDpsCurveJson));
                source.Add("item.template", TestSupport.Table("item.template", templateJson));
            });
        }

        private static readonly Id DevCurveId = new Id("item.weapon_dps.t6_dev");

        [Fact]
        public void DeviationRule_MeanFarFromDpsTimesSpeed_ReportsWarning()
        {
            // 期望均值 4（见 DevWeaponDpsCurveJson 注释）；实际均值 (1+1)/2=1，偏差 75% > 默认阈值 20%。
            var view = BuildDevView(
                "[{\"id\": \"item.t6_dev_over\", \"slot\": \"item.slot.t6_dev_weapon\"," +
                " \"quality\": \"item.quality.t6_dev_common\", \"item_level\": 1," +
                " \"display_ref\": \"display.item.t6_dev_over\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.t6_dev_over\"," +
                " \"weapon_profile\": {\"damage_min\": 1, \"damage_max\": 1, \"speed\": 1," +
                " \"weapon_school\": \"skill.school.physical\"}}]");

            var rule = new ItemWeaponDamageDeviatesDpsCurveRule(DevCurveId);
            var issues = new System.Collections.Generic.List<ValidationIssue>(rule.Validate(view));

            var issue = Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal(ItemWeaponDamageDeviatesDpsCurveRule.Check, issue.Check);
        }

        [Fact]
        public void DeviationRule_MeanMatchesDpsTimesSpeed_NoIssue()
        {
            // 实际均值 (3.6+4.4)/2=4.0，与期望均值 4 完全一致（0% 偏差），阈值内不报。
            var view = BuildDevView(
                "[{\"id\": \"item.t6_dev_within\", \"slot\": \"item.slot.t6_dev_weapon\"," +
                " \"quality\": \"item.quality.t6_dev_common\", \"item_level\": 1," +
                " \"display_ref\": \"display.item.t6_dev_within\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.t6_dev_within\"," +
                " \"weapon_profile\": {\"damage_min\": 3.6, \"damage_max\": 4.4, \"speed\": 1," +
                " \"weapon_school\": \"skill.school.physical\"}}]");

            var rule = new ItemWeaponDamageDeviatesDpsCurveRule(DevCurveId);
            var issues = new System.Collections.Generic.List<ValidationIssue>(rule.Validate(view));

            Assert.Empty(issues);
        }

        [Fact]
        public void DeviationRule_DamageMinMaxNotFilled_NoIssue()
        {
            // weapon_profile 只填 speed，未填 damage_min/damage_max——"未填不报"，与"填了 0"区分。
            var view = BuildDevView(
                "[{\"id\": \"item.t6_dev_unfilled\", \"slot\": \"item.slot.t6_dev_weapon\"," +
                " \"quality\": \"item.quality.t6_dev_common\", \"item_level\": 1," +
                " \"display_ref\": \"display.item.t6_dev_unfilled\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.t6_dev_unfilled\"," +
                " \"weapon_profile\": {\"speed\": 1, \"weapon_school\": \"skill.school.physical\"}}]");

            var rule = new ItemWeaponDamageDeviatesDpsCurveRule(DevCurveId);
            var issues = new System.Collections.Generic.List<ValidationIssue>(rule.Validate(view));

            Assert.Empty(issues);
        }

        [Fact]
        public void DeviationRule_CustomThreshold_WidensAcceptedRange()
        {
            // 同 DeviationRule_MeanFarFromDpsTimesSpeed_ReportsWarning 的数据（偏差 75%），但构造时
            // 传入 0.8 阈值——验证"阈值可配置"构造重载生效。
            var view = BuildDevView(
                "[{\"id\": \"item.t6_dev_custom_threshold\", \"slot\": \"item.slot.t6_dev_weapon\"," +
                " \"quality\": \"item.quality.t6_dev_common\", \"item_level\": 1," +
                " \"display_ref\": \"display.item.t6_dev_custom_threshold\", \"stack_size\": 1," +
                " \"name_key\": \"l10n.item.t6_dev_custom_threshold\"," +
                " \"weapon_profile\": {\"damage_min\": 1, \"damage_max\": 1, \"speed\": 1," +
                " \"weapon_school\": \"skill.school.physical\"}}]");

            var rule = new ItemWeaponDamageDeviatesDpsCurveRule(DevCurveId, deviationThreshold: 0.8);
            var issues = new System.Collections.Generic.List<ValidationIssue>(rule.Validate(view));

            Assert.Empty(issues);
        }

        // -----------------------------------------------------------------
        // 验收组 3：item.slot_definition.has_armor 显式护甲位字段（设计层裁定，取代 T-N2-5 推断）。
        // -----------------------------------------------------------------

        private const string ArmorSlotJson =
            "[" +
            // 显式 has_armor: true——真正装备位、非武器，且是护甲位。
            "{\"id\": \"item.slot.t6_head\", \"name_key\": \"l10n.item.slot.t6_head\"," +
            " \"has_armor\": true, \"budget_coefficient\": 1.0}," +
            // 非武器、真正装备位，但未显式登记 has_armor（缺省 false）——T-N2-5 旧推断规则会误判成
            // 护甲位，T-N2-6 显式字段规则下不应写护甲。
            "{\"id\": \"item.slot.t6_ring\", \"name_key\": \"l10n.item.slot.t6_ring\"," +
            " \"budget_coefficient\": 1.0}" +
            "]";

        private const string ArmorQualityJson =
            "[{\"id\": \"item.quality.t6_armor_common\", \"name_key\": \"l10n.item.quality.t6_armor_common\"," +
            " \"budget_multiplier\": 1.0}]";

        private const string ArmorCurveJson =
            "[{\"id\": \"item.armor.t6_default\", \"entries\": [{\"x\": 1, \"y\": 20}]}]";

        private const string ArmorStatDefJson =
            "[{\"id\": \"stat.armor\", \"name_key\": \"l10n.stat.armor\", \"category\": \"defense\"}]";

        private const string ArmorTemplateJson =
            "[" +
            "{\"id\": \"item.t6_head_plain\", \"slot\": \"item.slot.t6_head\"," +
            " \"quality\": \"item.quality.t6_armor_common\", \"item_level\": 1," +
            " \"display_ref\": \"display.item.t6_head_plain\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.t6_head_plain\"}," +
            "{\"id\": \"item.t6_ring_plain\", \"slot\": \"item.slot.t6_ring\"," +
            " \"quality\": \"item.quality.t6_armor_common\", \"item_level\": 1," +
            " \"display_ref\": \"display.item.t6_ring_plain\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.t6_ring_plain\"}" +
            "]";

        private sealed class ArmorFixture
        {
            public InventoryHost Inventory = null!;
            public StatHost StatHost = null!;
            public EquipmentHost Equipment = null!;
        }

        private static readonly Id ArmorPlayer = new Id("player.t6_armor_hero");
        private static readonly Id StatArmor = new Id("stat.armor");

        private static ArmorFixture BuildArmorFixture()
        {
            var registry = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", ArmorSlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", ArmorQualityJson));
                source.Add("item.armor_curve", TestSupport.Table("item.armor_curve", ArmorCurveJson));
                source.Add("item.template", TestSupport.Table("item.template", ArmorTemplateJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", ArmorStatDefJson));
            });

            var bus = TestSupport.CreateBus();
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            var effectSink = new FakeEffectSink();
            var skillGranter = new RecordingSkillGranter();
            var unitAccess = new FakeUnitAccess().Add(ArmorPlayer, 10);
            var options = new ItemOptions { ArmorCurveId = new Id("item.armor.t6_default") };

            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, effectSink, skillGranter.Grant, unitAccess, options);

            return new ArmorFixture { Inventory = inventory, StatHost = statHost, Equipment = equipment };
        }

        private static Id GiveAndReturnInstance(ArmorFixture f, string templateId)
        {
            f.Inventory.AddItem(ArmorPlayer, new Id(templateId), 1);
            var items = f.Inventory.ListItems(ArmorPlayer);
            return items[items.Count - 1].InstanceId;
        }

        [Fact]
        public void Equip_SlotWithHasArmorTrue_WritesArmorStat()
        {
            var f = BuildArmorFixture();
            f.StatHost.RegisterUnit(ArmorPlayer);
            var instanceId = GiveAndReturnInstance(f, "item.t6_head_plain");

            f.Equipment.Equip(ArmorPlayer, instanceId, new Id("item.slot.t6_head"));

            Assert.Equal(20.0, f.StatHost.GetStat(ArmorPlayer, StatArmor)); // 曲线(1)=20 × 槽位系数 1.0
        }

        [Fact]
        public void Equip_NonWeaponSlotWithoutHasArmor_DoesNotWriteArmorStat()
        {
            // T-N2-5 旧的"非武器位且真正装备位"推断规则会把这个戒指位误判成护甲位；T-N2-6 显式
            // has_armor 字段（本槽位未登记，缺省 false）下不应写护甲——这是本任务设计层裁定的核心
            // 行为变化，见 EquipmentHost.IsArmorSlot/README.md 判断记录 20/21。
            var f = BuildArmorFixture();
            f.StatHost.RegisterUnit(ArmorPlayer);
            var instanceId = GiveAndReturnInstance(f, "item.t6_ring_plain");

            var result = f.Equipment.Equip(ArmorPlayer, instanceId, new Id("item.slot.t6_ring"));

            Assert.True(result.Success);
            Assert.Equal(0.0, f.StatHost.GetStat(ArmorPlayer, StatArmor));
        }
    }
}
