using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using System.Collections.Generic;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 分阶段落地计划 T-N2-5（ADR-0032 决策 4/5/7/8；07 第 1.2/1.4/1.6 节修订段）验收测试：
    /// 护甲值曲线 × 槽位系数写入、词缀反解值同 sourceId 随穿戴写入、<c>requirements.level</c> 缺省
    /// 按曲线反推。数据夹具独立于 <see cref="EquipmentHostTests"/>（后者的 <c>item.slot_definition</c>/
    /// <c>item.template</c> 样例未登记 <c>item.armor_curve</c>/<c>item.req_level_curve</c>/
    /// <c>item.affix</c> 数据，不适合直接复用——同 <c>P2_05_InventoryEquipmentReloadTests</c> 一类
    /// "自带最小夹具"惯例）。
    /// </summary>
    public class T_N2_5_ArmorAffixReqLevelTests
    {
        private const string SlotJson =
            "[" +
            "{\"id\": \"item.slot.t5_weapon\", \"name_key\": \"l10n.item.slot.t5_weapon\", \"is_weapon\": true, \"budget_coefficient\": 1.0}," +
            "{\"id\": \"item.slot.t5_chest\", \"name_key\": \"l10n.item.slot.t5_chest\", \"budget_coefficient\": 2.0}" +
            "]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.t5_common\", \"name_key\": \"l10n.item.quality.t5_common\", \"budget_multiplier\": 1.0}]";

        private const string StatDefJson =
            "[" +
            "{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"category\": \"primary\"}," +
            "{\"id\": \"stat.armor\", \"name_key\": \"l10n.stat.armor\", \"category\": \"defense\"}" +
            "]";

        // 预算曲线在 item_level=1 处取 100（单点曲线，PiecewiseCurve 越界夹取到端点，等价于常数）；
        // 与 ItemOptions.BudgetCurveId 默认值 "item.budget.default" 同 id，测试不覆盖该选项。
        private const string BudgetCurveJson =
            "[{\"id\": \"item.budget.default\", \"entries\": [{\"x\": 1, \"y\": 100}]}]";

        // 护甲曲线：item_level=1 → 5，item_level=10 → 50（线性，供槽位系数缩放验证）。
        private const string ArmorCurveJson =
            "[{\"id\": \"item.armor.default\", \"entries\": [{\"x\": 1, \"y\": 5}, {\"x\": 10, \"y\": 50}]}]";

        // 需求等级曲线：item_level=1 → 1（整数，验证正常取整）；item_level=10 → 12.4（验证 Math.Ceiling
        // 向上取整为 13，不是 12）。
        private const string ReqLevelCurveJson =
            "[{\"id\": \"item.req_level.default\", \"entries\": [{\"x\": 1, \"y\": 1}, {\"x\": 10, \"y\": 12.4}]}]";

        // 词缀：budget_share=0.5，stat_mix 单条 ratio=1.0（Σratio=1，不需要归一化即验证",
        // shareOfBudget = 0.5 × 1.0 = 0.5，targetBudget = itemBudgetLimit(=100×1.0×2.0=200) × 0.5=100，
        // k 缺省 1.5、单一属性时 value = targetBudget / weight = 100（权重未登记 stat.weight，回退 1）。
        private const string AffixJson =
            "[{\"id\": \"item.affix.t5_str\", \"name_key\": \"l10n.item.affix.t5_str\"," +
            " \"budget_share\": 0.5," +
            " \"stat_mix\": [{\"stat\": \"stat.strength\", \"ratio\": 1.0}]," +
            " \"quality_pool\": \"item.quality.t5_common\", \"weight\": 1.0}]";

        private const string TemplateJson =
            "[" +
            // 护甲位、item_level=1、无 requirements——用于"带词缀穿戴/卸下"与"护甲写入"两组用例。
            "{\"id\": \"item.t5_chest_affix\", \"slot\": \"item.slot.t5_chest\", \"quality\": \"item.quality.t5_common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.t5_chest_affix\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.t5_chest_affix\"," +
            " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 5}]}," +
            // 武器位、item_level=1——用于"武器位不写护甲"用例（护甲曲线在同一夹具内存在，仅槽位不同）。
            "{\"id\": \"item.t5_weapon_plain\", \"slot\": \"item.slot.t5_weapon\", \"quality\": \"item.quality.t5_common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.t5_weapon_plain\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.t5_weapon_plain\"," +
            " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 7}]}," +
            // 护甲位、item_level=10、无 requirements——用于"未填按曲线反推"用例（曲线值 12.4 应向上
            // 取整为 13，不是 12）。
            "{\"id\": \"item.t5_chest_highlevel\", \"slot\": \"item.slot.t5_chest\", \"quality\": \"item.quality.t5_common\"," +
            " \"item_level\": 10, \"display_ref\": \"display.item.t5_chest_highlevel\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.t5_chest_highlevel\"}," +
            // 护甲位、item_level=10、显式 requirements.level=3——用于"填了以手填为准"用例（曲线本会
            // 反推出 13，手填的 3 必须优先生效）。
            "{\"id\": \"item.t5_chest_explicit_req\", \"slot\": \"item.slot.t5_chest\", \"quality\": \"item.quality.t5_common\"," +
            " \"item_level\": 10, \"display_ref\": \"display.item.t5_chest_explicit_req\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.t5_chest_explicit_req\", \"requirements\": {\"level\": 3}}" +
            "]";

        private sealed class Fixture
        {
            public IDataRegistryView Registry = null!;
            public InventoryHost Inventory = null!;
            public StatHost StatHost = null!;
            public FakeUnitAccess UnitAccess = null!;
            public EquipmentHost Equipment = null!;
        }

        private static Fixture Build(int playerLevel = 10)
        {
            var registry = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("item.armor_curve", TestSupport.Table("item.armor_curve", ArmorCurveJson));
                source.Add("item.req_level_curve", TestSupport.Table("item.req_level_curve", ReqLevelCurveJson));
                source.Add("item.affix", TestSupport.Table("item.affix", AffixJson));
                source.Add("item.template", TestSupport.Table("item.template", TemplateJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
            });

            var bus = TestSupport.CreateBus();
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            var effectSink = new FakeEffectSink();
            var skillGranter = new RecordingSkillGranter();
            var unitAccess = new FakeUnitAccess().Add(Player, playerLevel);

            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, effectSink, skillGranter.Grant, unitAccess);

            return new Fixture
            {
                Registry = registry,
                Inventory = inventory,
                StatHost = statHost,
                UnitAccess = unitAccess,
                Equipment = equipment,
            };
        }

        private static readonly Id Player = new Id("player.t5_hero");
        private static readonly Id StatStrength = new Id("stat.strength");
        private static readonly Id StatArmor = new Id("stat.armor");
        private static readonly Id AffixStr = new Id("item.affix.t5_str");

        private static Id GiveAndReturnInstance(Fixture f, string templateId)
        {
            f.Inventory.AddItem(Player, new Id(templateId), 1);
            var items = f.Inventory.ListItems(Player);
            return items[items.Count - 1].InstanceId;
        }

        // -----------------------------------------------------------------
        // 验收组 1：穿脱后属性回退（带词缀穿戴→模板 stats + 词缀反解值 + 护甲；卸下→全部回退，
        // StatHost 该 sourceId 无残留）。
        // -----------------------------------------------------------------

        [Fact]
        public void Equip_WithAffix_WritesTemplateStatsPlusAffixValuePlusArmor_SameSourceId()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.t5_chest_affix");

            var result = f.Equipment.Equip(
                Player, instanceId, new Id("item.slot.t5_chest"),
                qualityId: new Id("item.quality.t5_common"),
                affixIds: new List<Id> { AffixStr });

            Assert.True(result.Success);
            // 模板 stats（5） + 词缀反解值（预算 200 × shareOfBudget 0.5 = 100，权重缺省 1 → 值 100）
            // = 105（手算，见类型顶部 AffixJson 注释）。
            Assert.Equal(105, f.StatHost.GetStat(Player, StatStrength));
            // 护甲 = armor_curve(1)=5 × 槽位系数 2.0 = 10。
            Assert.Equal(10, f.StatHost.GetStat(Player, StatArmor));
        }

        [Fact]
        public void Unequip_AfterAffixEquip_RevertsAllModifiers_NoResidueForSourceId()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.t5_chest_affix");
            f.Equipment.Equip(
                Player, instanceId, new Id("item.slot.t5_chest"),
                qualityId: new Id("item.quality.t5_common"),
                affixIds: new List<Id> { AffixStr });

            var reverted = f.Equipment.Unequip(Player, new Id("item.slot.t5_chest"));

            Assert.NotNull(reverted);
            // 模板 stats、词缀反解值、护甲三者共用同一 sourceId（instanceId），卸下应一次性全部回退，
            // 不留任何残留（任务书硬性规则"禁止用第二个 sourceId 写词缀值"的可观测后果）。
            Assert.Equal(0, f.StatHost.GetStat(Player, StatStrength));
            Assert.Equal(0, f.StatHost.GetStat(Player, StatArmor));
        }

        // -----------------------------------------------------------------
        // 验收组 2：护甲写入（护甲位模板装备后 stat.armor 增加 曲线(item_level)×槽位系数；武器位不写
        // 护甲，即便护甲曲线存在）。
        // -----------------------------------------------------------------

        [Fact]
        public void Equip_ArmorSlotTemplate_WritesArmorStat_CurveTimesSlotCoefficient()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.t5_chest_affix");

            f.Equipment.Equip(Player, instanceId, new Id("item.slot.t5_chest"));

            Assert.Equal(10, f.StatHost.GetStat(Player, StatArmor)); // 5 × 2.0
        }

        [Fact]
        public void Equip_WeaponSlotTemplate_DoesNotWriteArmorStat_EvenWhenArmorCurveExists()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.t5_weapon_plain");

            var result = f.Equipment.Equip(Player, instanceId, new Id("item.slot.t5_weapon"));

            Assert.True(result.Success);
            Assert.Equal(7, f.StatHost.GetStat(Player, StatStrength)); // 模板自身 stats 正常写入
            Assert.Equal(0, f.StatHost.GetStat(Player, StatArmor)); // 武器位不是护甲位，不写护甲
        }

        // -----------------------------------------------------------------
        // 验收组 3：需求等级反推（未填 requirements.level 时按曲线取值并参与穿戴门槛判定，Math.Ceiling
        // 向上取整；填了以手填为准，不受曲线影响）。
        // -----------------------------------------------------------------

        [Fact]
        public void Equip_NoRequirementsLevel_DerivesFromCurve_RoundedUp_BlocksBelowThreshold()
        {
            // item.t5_chest_highlevel：item_level=10，未填 requirements，曲线值 12.4 应 Math.Ceiling
            // 为 13（不是 12）——玩家等级 12（低于 13）应被拒绝，13（等于阈值）应通过。
            var fBelow = Build(playerLevel: 12);
            fBelow.StatHost.RegisterUnit(Player);
            var instanceBelow = GiveAndReturnInstance(fBelow, "item.t5_chest_highlevel");
            var resultBelow = fBelow.Equipment.Equip(Player, instanceBelow, new Id("item.slot.t5_chest"));
            Assert.False(resultBelow.Success);
            Assert.Equal(EquipFailureReason.RequirementNotMet, resultBelow.Reason);

            var fAt = Build(playerLevel: 13);
            fAt.StatHost.RegisterUnit(Player);
            var instanceAt = GiveAndReturnInstance(fAt, "item.t5_chest_highlevel");
            var resultAt = fAt.Equipment.Equip(Player, instanceAt, new Id("item.slot.t5_chest"));
            Assert.True(resultAt.Success);
        }

        [Fact]
        public void Equip_ExplicitRequirementsLevel_OverridesCurve()
        {
            // item.t5_chest_explicit_req：item_level=10（曲线本会反推出 13），但显式填了
            // requirements.level=3——玩家等级 3 应通过（以手填为准，不看曲线）。
            var f = Build(playerLevel: 3);
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.t5_chest_explicit_req");

            var result = f.Equipment.Equip(Player, instanceId, new Id("item.slot.t5_chest"));

            Assert.True(result.Success);
        }
    }
}
