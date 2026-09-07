using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Item
{
    public class EquipmentHostTests
    {
        private const string SlotJson =
            "[" +
            "{\"id\": \"item.slot.main_hand\", \"name_key\": \"l10n.item.slot.main_hand\", \"is_weapon\": true}," +
            "{\"id\": \"item.slot.chest\", \"name_key\": \"l10n.item.slot.chest\"}," +
            "{\"id\": \"item.slot.ring\", \"name_key\": \"l10n.item.slot.ring\"}," +
            "{\"id\": \"item.slot.ring_left\", \"name_key\": \"l10n.item.slot.ring_left\", \"accepts\": [\"item.slot.ring\"]}," +
            "{\"id\": \"item.slot.ring_right\", \"name_key\": \"l10n.item.slot.ring_right\", \"accepts\": [\"item.slot.ring\"]}," +
            "{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.item.slot.consumable\", \"is_equipment\": false}" +
            "]";

        private const string QualityJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\"}]";

        private const string StatDefJson =
            "[" +
            "{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"group\": \"primary\"}," +
            "{\"id\": \"stat.stamina\", \"name_key\": \"l10n.stat.stamina\", \"group\": \"primary\"}" +
            "]";

        private const string SetJson =
            "[{\"id\": \"item.set.sample_dragon\", \"name_key\": \"l10n.item.set.sample_dragon\"," +
            "\"pieces\": [\"item.sample_ring\", \"item.sample_ring2\"]," +
            "\"bonuses\": [{\"count\": 2, \"aura_ref\": \"skill.aura.sample_dragon_2pc\"}]}]";

        private const string TemplateJson =
            "[" +
            "{\"id\": \"item.sample_weapon_a\", \"slot\": \"item.slot.main_hand\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 10, \"display_ref\": \"display.item.sample_weapon_a\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.sample_weapon_a\"," +
            " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 10}]," +
            " \"grants\": {\"skills\": [\"skill.sample_slash\"], \"auras\": [\"skill.aura.sample_sharpen\"]}," +
            " \"weapon_profile\": {\"damage_min\": 5, \"damage_max\": 9, \"speed\": 2.6, \"weapon_school\": \"skill.school.physical\"}," +
            " \"requirements\": {\"level\": 5}}," +
            "{\"id\": \"item.sample_weapon_b\", \"slot\": \"item.slot.main_hand\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.sample_weapon_b\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.sample_weapon_b\"," +
            " \"stats\": [{\"stat\": \"stat.strength\", \"op\": \"flat\", \"value\": 3}]," +
            " \"weapon_profile\": {\"damage_min\": 1, \"damage_max\": 3, \"speed\": 1.2, \"weapon_school\": \"skill.school.physical\"}}," +
            "{\"id\": \"item.sample_chest_armor\", \"slot\": \"item.slot.chest\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.sample_chest_armor\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.sample_chest_armor\"," +
            " \"stats\": [{\"stat\": \"stat.stamina\", \"op\": \"flat\", \"value\": 4}]," +
            // RC-05 测试专用：与 item.sample_weapon_a 授予完全相同的技能/光环——main_hand 与 chest
            // 是两个不同槽位，可以同时装备，用来测试"两件装备都授予同一技能/光环，卸下一件不应
            // 影响另一件仍在授予的同一技能/光环"（见 EquipmentHostTests 对应用例）。
            " \"grants\": {\"skills\": [\"skill.sample_slash\"], \"auras\": [\"skill.aura.sample_sharpen\"]}}," +
            "{\"id\": \"item.sample_ring\", \"slot\": \"item.slot.ring\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.sample_ring\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.sample_ring\", \"set_id\": \"item.set.sample_dragon\"," +
            " \"stats\": [{\"stat\": \"stat.stamina\", \"op\": \"flat\", \"value\": 2}]}," +
            "{\"id\": \"item.sample_ring2\", \"slot\": \"item.slot.ring\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.sample_ring2\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.sample_ring2\", \"set_id\": \"item.set.sample_dragon\"," +
            " \"stats\": [{\"stat\": \"stat.stamina\", \"op\": \"flat\", \"value\": 3}]}," +
            "{\"id\": \"item.sample_potion\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.sample_potion\", \"stack_size\": 20," +
            " \"name_key\": \"l10n.item.sample_potion\"}" +
            "]";

        private sealed class Fixture
        {
            public IDataRegistryView Registry = null!;
            public IEventBus Bus = null!;
            public InventoryHost Inventory = null!;
            public StatHost StatHost = null!;
            public FakeEffectSink EffectSink = null!;
            public RecordingSkillGranter SkillGranter = null!;
            public FakeUnitAccess UnitAccess = null!;
            public EquipmentHost Equipment = null!;
        }

        private static Fixture Build(int playerLevel = 10)
        {
            // 同一个 DataRegistry 同时装下 item.* 六张表与 stat.definition：TestSupport.BuildRegistry
            // 已经无条件注册好 stat.definition 的 schema（供各模块共用同一份最小属性定义 schema），
            // 这里只需要额外喂数据即可，不必为 StatHost 另开一个 DataRegistry 实例。
            var registry = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.set", TestSupport.Table("item.set", SetJson));
                source.Add("item.template", TestSupport.Table("item.template", TemplateJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
            });

            var bus = TestSupport.CreateBus();
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            var effectSink = new FakeEffectSink();
            var skillGranter = new RecordingSkillGranter();
            var unitAccess = new FakeUnitAccess().Add(new Id("player.hero"), playerLevel);

            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, effectSink, skillGranter.Grant, unitAccess);

            return new Fixture
            {
                Registry = registry,
                Bus = bus,
                Inventory = inventory,
                StatHost = statHost,
                EffectSink = effectSink,
                SkillGranter = skillGranter,
                UnitAccess = unitAccess,
                Equipment = equipment,
            };
        }

        private static readonly Id Player = new Id("player.hero");

        private static Id GiveAndReturnInstance(Fixture f, string templateId)
        {
            f.Inventory.AddItem(Player, new Id(templateId), 1);
            var items = f.Inventory.ListItems(Player);
            return items[items.Count - 1].InstanceId;
        }

        [Fact]
        public void Equip_AppliesStatModifiers_MatchesHandCalculated()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");

            var result = f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));

            Assert.True(result.Success);
            Assert.Equal(10, f.StatHost.GetStat(Player, new Id("stat.strength")));
        }

        [Fact]
        public void Unequip_RevertsStatModifiers()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");
            f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));

            var reverted = f.Equipment.Unequip(Player, new Id("item.slot.main_hand"));

            Assert.NotNull(reverted);
            Assert.Equal(0, f.StatHost.GetStat(Player, new Id("stat.strength")));
        }

        [Fact]
        public void Equip_GrantsSkills_CallsSkillGranterWithLearnTrue()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");

            f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));

            Assert.Contains(f.SkillGranter.Calls, c =>
                c.UnitId.Equals(Player) && c.SkillId.Equals(new Id("skill.sample_slash")) && c.Learn);
        }

        [Fact]
        public void Unequip_RevokesSkills_CallsSkillGranterWithLearnFalse()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");
            f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));

            f.Equipment.Unequip(Player, new Id("item.slot.main_hand"));

            Assert.Contains(f.SkillGranter.Calls, c =>
                c.UnitId.Equals(Player) && c.SkillId.Equals(new Id("skill.sample_slash")) && !c.Learn);
        }

        [Fact]
        public void Equip_GrantsSkill_SkillGranterCallCarriesEquippedInstanceIdAsSource()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");

            f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));

            // RC-05 收边补齐：SkillGranter 现在携带来源 id（本例即装备实例 id），供接收端按来源
            // 做引用计数（见 SkillGranter.cs 判断记录）。
            Assert.Contains(f.SkillGranter.Calls, c =>
                c.UnitId.Equals(Player) && c.SkillId.Equals(new Id("skill.sample_slash")) &&
                c.SourceId.Equals(instanceId) && c.Learn);
        }

        // -----------------------------------------------------------------
        // RC-05（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-05、
        // EquipmentHost.cs/SkillGranter.cs 判断记录）：两件装备都授予同一个技能/光环时，卸下
        // 其中一件不应影响另一件仍在授予的同一技能/光环——原实现不计来源，卸一件就整体撤销。
        // item.sample_weapon_a（main_hand）与 item.sample_chest_armor（chest）在本测试文件里被
        // 特意配成授予完全相同的 skill.sample_slash / skill.aura.sample_sharpen（见 TemplateJson
        // 判断记录），可以同时装备。
        // -----------------------------------------------------------------

        [Fact]
        public void Unequip_OneOfTwoItemsGrantingSameSkill_DoesNotForgetSkill_StillGrantedByOther()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var weaponInstance = GiveAndReturnInstance(f, "item.sample_weapon_a");
            var chestInstance = GiveAndReturnInstance(f, "item.sample_chest_armor");

            f.Equipment.Equip(Player, weaponInstance, new Id("item.slot.main_hand"));
            f.Equipment.Equip(Player, chestInstance, new Id("item.slot.chest"));

            f.Equipment.Unequip(Player, new Id("item.slot.main_hand"));

            // 修复前：EquipmentHost 卸下武器会无条件调用 SkillGranter(learn:false)，SkillHost 的
            // HashSet 不计来源，胸甲仍在授予同一技能却也会被一起遗忘。
            var learnCalls = f.SkillGranter.Calls.FindAll(c => c.SkillId.Equals(new Id("skill.sample_slash")) && c.Learn);
            var forgetCalls = f.SkillGranter.Calls.FindAll(c => c.SkillId.Equals(new Id("skill.sample_slash")) && !c.Learn);
            Assert.Equal(2, learnCalls.Count); // 两件装备各学习一次（来源不同）。
            Assert.Single(forgetCalls); // 只撤销武器这一个来源。
            Assert.Equal(weaponInstance, forgetCalls[0].SourceId);

            // 卸下最后一件（胸甲）才是真正的第二次遗忘调用——SkillHost 侧是否真的仍"知道"这个
            // 技能不由 EquipmentHost 直接暴露，SkillHost 自身的引用计数单元测试见
            // core/rules/skill/tests（本文件只覆盖 EquipmentHost→SkillGranter 这一段接线）。
            f.Equipment.Unequip(Player, new Id("item.slot.chest"));
            var forgetCallsAfterBoth = f.SkillGranter.Calls.FindAll(c => c.SkillId.Equals(new Id("skill.sample_slash")) && !c.Learn);
            Assert.Equal(2, forgetCallsAfterBoth.Count);
            Assert.Equal(chestInstance, forgetCallsAfterBoth[1].SourceId);
        }

        [Fact]
        public void Unequip_OneOfTwoItemsGrantingSameAura_DoesNotRemoveAura_StillGrantedByOther()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var weaponInstance = GiveAndReturnInstance(f, "item.sample_weapon_a");
            var chestInstance = GiveAndReturnInstance(f, "item.sample_chest_armor");

            f.Equipment.Equip(Player, weaponInstance, new Id("item.slot.main_hand"));
            f.Equipment.Equip(Player, chestInstance, new Id("item.slot.chest"));

            var appliedForSharpen = f.EffectSink.Applied.FindAll(a => a.AuraDefId.Equals(new Id("skill.aura.sample_sharpen")));
            Assert.Equal(2, appliedForSharpen.Count); // 两件装备各申请了一次（见 FakeEffectSink 判断记录：每次都返回独立 ref）。

            f.Equipment.Unequip(Player, new Id("item.slot.main_hand"));

            // 修复前：卸下武器会无条件 RemoveAura 它自己那份 ref——本用例改用"是否真的调用了
            // RemoveAura"来断言引用计数生效（Applied 用假实现天然返回独立 ref，不模拟 AuraHost
            // 真实的共享槽位叠加行为，但引用计数本身与 ref 是否共享无关，见 EquipmentHost.
            // _auraGrantRefCount 判断记录）。
            Assert.Empty(f.EffectSink.Removed); // 胸甲还穿着，不应真正撤销。

            f.Equipment.Unequip(Player, new Id("item.slot.chest"));
            Assert.Single(f.EffectSink.Removed); // 最后一件卸下才真正撤销。
        }

        [Fact]
        public void Equip_AppliesAuras_CallsEffectSinkApplyAura()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");

            f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));

            Assert.Contains(f.EffectSink.Applied, a =>
                a.TargetId.Equals(Player) && a.AuraDefId.Equals(new Id("skill.aura.sample_sharpen")) &&
                a.SourceId.Equals(instanceId));
        }

        [Fact]
        public void Unequip_RemovesAuras_CallsEffectSinkRemoveAura()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");
            f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));
            var appliedCountBefore = f.EffectSink.Applied.Count;

            f.Equipment.Unequip(Player, new Id("item.slot.main_hand"));

            Assert.Equal(appliedCountBefore, f.EffectSink.Removed.Count);
        }

        [Fact]
        public void Equip_RaisesItemEquippedEvent_WithExpectedFields()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");

            ItemEquippedEvent? received = null;
            f.Bus.Subscribe<ItemEquippedEvent>(CarriersEventKeys.ItemEquipped, evt => received = evt);

            f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));
            f.Bus.DispatchPending();

            Assert.NotNull(received);
            Assert.Equal(Player, received!.UnitId);
            Assert.Equal(instanceId, received.ItemInstanceId);
            Assert.Equal(new Id("item.slot.main_hand"), received.Slot);
        }

        [Fact]
        public void Unequip_RaisesItemUnequippedEvent_WithExpectedFields()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");
            f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));

            ItemUnequippedEvent? received = null;
            f.Bus.Subscribe<ItemUnequippedEvent>(CarriersEventKeys.ItemUnequipped, evt => received = evt);

            f.Equipment.Unequip(Player, new Id("item.slot.main_hand"));
            f.Bus.DispatchPending();

            Assert.NotNull(received);
            Assert.Equal(Player, received!.UnitId);
            Assert.Equal(new Id("item.slot.main_hand"), received.Slot);
            Assert.Equal(instanceId, received.ItemInstanceId);
        }

        [Fact]
        public void Equip_ReplacesOccupiedSlot_ReturnsReplacedRef_AndOldItemBackInInventory()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var weaponAId = GiveAndReturnInstance(f, "item.sample_weapon_a");
            f.Equipment.Equip(Player, weaponAId, new Id("item.slot.main_hand"));
            var weaponBId = GiveAndReturnInstance(f, "item.sample_weapon_b");

            var result = f.Equipment.Equip(Player, weaponBId, new Id("item.slot.main_hand"));

            Assert.True(result.Success);
            Assert.NotNull(result.Replaced);
            Assert.Equal(weaponAId, result.Replaced!.Value.InstanceId);
            Assert.NotNull(f.Inventory.FindInstance(Player, weaponAId));
            Assert.Equal(3, f.StatHost.GetStat(Player, new Id("stat.strength")));
        }

        [Fact]
        public void Equip_SlotMismatch_Fails()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");

            var result = f.Equipment.Equip(Player, instanceId, new Id("item.slot.chest"));

            Assert.False(result.Success);
            Assert.Equal(EquipFailureReason.SlotMismatch, result.Reason);
        }

        [Fact]
        public void Equip_NonEquipmentBucketSlot_Fails()
        {
            // 阶段 3 整理："item.slot.consumable" 是 is_equipment=false 的分类桶——即便 slot 完全
            // 匹配，也不可经 Equip 装备，返回 SlotMismatch（见 EquipmentHost.IsEquipmentSlot）。
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_potion");

            var result = f.Equipment.Equip(Player, instanceId, new Id("item.slot.consumable"));

            Assert.False(result.Success);
            Assert.Equal(EquipFailureReason.SlotMismatch, result.Reason);
        }

        [Fact]
        public void Equip_NotInInventory_Fails()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);

            var result = f.Equipment.Equip(Player, new Id("item.inst_999"), new Id("item.slot.main_hand"));

            Assert.False(result.Success);
            Assert.Equal(EquipFailureReason.NotInInventory, result.Reason);
        }

        [Fact]
        public void Equip_RequirementNotMet_Fails()
        {
            var f = Build(playerLevel: 1);
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_weapon_a");

            var result = f.Equipment.Equip(Player, instanceId, new Id("item.slot.main_hand"));

            Assert.False(result.Success);
            Assert.Equal(EquipFailureReason.RequirementNotMet, result.Reason);
        }

        [Fact]
        public void Equip_AcceptsCrossSlot_Succeeds()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var instanceId = GiveAndReturnInstance(f, "item.sample_ring");

            var result = f.Equipment.Equip(Player, instanceId, new Id("item.slot.ring_left"));

            Assert.True(result.Success);
            Assert.Equal(instanceId, f.Equipment.GetEquipped(Player, new Id("item.slot.ring_left"))!.Value.InstanceId);
        }

        [Fact]
        public void Equip_SetBonus_TwoPiecesAppliesAura()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var ring1 = GiveAndReturnInstance(f, "item.sample_ring");
            var ring2 = GiveAndReturnInstance(f, "item.sample_ring2");

            f.Equipment.Equip(Player, ring1, new Id("item.slot.ring_left"));
            Assert.DoesNotContain(f.EffectSink.Applied, a => a.AuraDefId.Equals(new Id("skill.aura.sample_dragon_2pc")));

            f.Equipment.Equip(Player, ring2, new Id("item.slot.ring_right"));

            Assert.Contains(f.EffectSink.Applied, a =>
                a.TargetId.Equals(Player) && a.AuraDefId.Equals(new Id("skill.aura.sample_dragon_2pc")));
        }

        [Fact]
        public void Unequip_SetBonus_DowngradeRemovesAura()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var ring1 = GiveAndReturnInstance(f, "item.sample_ring");
            var ring2 = GiveAndReturnInstance(f, "item.sample_ring2");
            f.Equipment.Equip(Player, ring1, new Id("item.slot.ring_left"));
            f.Equipment.Equip(Player, ring2, new Id("item.slot.ring_right"));
            var appliedBonusCount = f.EffectSink.Applied.FindAll(a => a.AuraDefId.Equals(new Id("skill.aura.sample_dragon_2pc"))).Count;
            Assert.Equal(1, appliedBonusCount);

            f.Equipment.Unequip(Player, new Id("item.slot.ring_left"));

            var removedCount = f.EffectSink.Removed.Count;
            Assert.True(removedCount >= 1);
        }

        // -----------------------------------------------------------------
        // GetAllEquipped（W1 收边补齐：A3 审计 #18，此前无直接测试覆盖）
        // -----------------------------------------------------------------

        [Fact]
        public void GetAllEquipped_ReturnsEmpty_WhenNothingEquipped()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);

            Assert.Empty(f.Equipment.GetAllEquipped(Player));
        }

        [Fact]
        public void GetAllEquipped_ReturnsAllEquippedSlots_KeyedBySlot()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var weaponId = GiveAndReturnInstance(f, "item.sample_weapon_a");
            var chestId = GiveAndReturnInstance(f, "item.sample_chest_armor");

            f.Equipment.Equip(Player, weaponId, new Id("item.slot.main_hand"));
            f.Equipment.Equip(Player, chestId, new Id("item.slot.chest"));

            var all = f.Equipment.GetAllEquipped(Player);

            Assert.Equal(2, all.Count);
            Assert.Equal(weaponId, all[new Id("item.slot.main_hand")].InstanceId);
            Assert.Equal(chestId, all[new Id("item.slot.chest")].InstanceId);
        }

        [Fact]
        public void GetAllEquipped_UnequipRemovesSlotFromResult()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var weaponId = GiveAndReturnInstance(f, "item.sample_weapon_a");
            f.Equipment.Equip(Player, weaponId, new Id("item.slot.main_hand"));
            Assert.Single(f.Equipment.GetAllEquipped(Player));

            f.Equipment.Unequip(Player, new Id("item.slot.main_hand"));

            Assert.Empty(f.Equipment.GetAllEquipped(Player));
        }

        // -----------------------------------------------------------------
        // RC-11（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-11）：
        // EquipmentHost.GetWeaponBaseDamage（IWeaponDamageQuery 实现）——weapon_damage_pct 效果
        // 原语（core/rules/skill.EffectDispatcher）经它取"当前武器基础伤害"。
        // -----------------------------------------------------------------

        [Fact]
        public void GetWeaponBaseDamage_EquippedWeapon_ReturnsAverageOfDamageMinAndMax()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var weaponId = GiveAndReturnInstance(f, "item.sample_weapon_a"); // damage_min=5, damage_max=9
            f.Equipment.Equip(Player, weaponId, new Id("item.slot.main_hand"));

            Assert.Equal(7.0, f.Equipment.GetWeaponBaseDamage(Player), 6); // (5+9)/2
        }

        [Fact]
        public void GetWeaponBaseDamage_NoWeaponEquipped_ReturnsZero()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);

            Assert.Equal(0.0, f.Equipment.GetWeaponBaseDamage(Player), 6);
        }

        [Fact]
        public void GetWeaponBaseDamage_OnlyNonWeaponSlotsEquipped_ReturnsZero()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var chestId = GiveAndReturnInstance(f, "item.sample_chest_armor"); // 非武器槽，无 weapon_profile。
            f.Equipment.Equip(Player, chestId, new Id("item.slot.chest"));

            Assert.Equal(0.0, f.Equipment.GetWeaponBaseDamage(Player), 6);
        }

        [Fact]
        public void GetWeaponBaseDamage_UnequippingWeapon_ReturnsToZero()
        {
            var f = Build();
            f.StatHost.RegisterUnit(Player);
            var weaponId = GiveAndReturnInstance(f, "item.sample_weapon_a");
            f.Equipment.Equip(Player, weaponId, new Id("item.slot.main_hand"));
            Assert.Equal(7.0, f.Equipment.GetWeaponBaseDamage(Player), 6);

            f.Equipment.Unequip(Player, new Id("item.slot.main_hand"));

            Assert.Equal(0.0, f.Equipment.GetWeaponBaseDamage(Player), 6);
        }
    }
}
