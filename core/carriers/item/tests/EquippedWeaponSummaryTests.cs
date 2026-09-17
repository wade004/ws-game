using System.Collections.Generic;
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
    /// 消费方反馈第 52 条（<c>architecture/落地计划/消费方反馈-2026-09-17-编辑器-第52-53条.md</c>）：
    /// <see cref="EquipmentHost.GetEquippedWeaponSummary"/>/<see
    /// cref="EquipmentHost.GetAllEquippedWeaponSummaries"/> 与 <see cref="EquippedWeaponSummary"/> 的
    /// 落地验收——输出须与消费方此前自建适配层手工搬运 <c>ItemInstance.TemplateId</c>/<c>Quality</c>/
    /// <c>Affixes</c> 三字段的结果逐字段一致（纯搬运、不计算数值），并覆盖反馈原文未展开但落地计划明确
    /// 要求的三个边界：空槽位、完全未装备武器、双持（同一单位两个武器槽同时有装备）。
    /// <para>
    /// 本文件用独立的最小夹具（两个武器槽 main_hand/off_hand + 一个非武器槽 chest），不复用
    /// <see cref="EquipmentHostTests"/> 的共享 <c>TemplateJson</c>/<c>SlotJson</c>——那份夹具只登记了
    /// 一个武器槽，无法覆盖双持场景，另起一份可避免为了凑双持改动其它用例已经依赖的共享字符串。
    /// </para>
    /// </summary>
    public sealed class EquippedWeaponSummaryTests
    {
        private const string SlotJson =
            "[" +
            "{\"id\": \"item.slot.dw_main_hand\", \"name_key\": \"l10n.item.slot.dw_main_hand\", \"is_weapon\": true}," +
            "{\"id\": \"item.slot.dw_off_hand\", \"name_key\": \"l10n.item.slot.dw_off_hand\", \"is_weapon\": true}," +
            "{\"id\": \"item.slot.dw_chest\", \"name_key\": \"l10n.item.slot.dw_chest\"}" +
            "]";

        private const string QualityJson =
            "[" +
            "{\"id\": \"item.quality.dw_common\", \"name_key\": \"l10n.item.quality.dw_common\"}," +
            "{\"id\": \"item.quality.dw_rare\", \"name_key\": \"l10n.item.quality.dw_rare\"}" +
            "]";

        private const string StatDefJson =
            "[{\"id\": \"stat.dw_strength\", \"name_key\": \"l10n.stat.dw_strength\", \"group\": \"primary\"}]";

        private const string TemplateJson =
            "[" +
            "{\"id\": \"item.dw_sword\", \"slot\": \"item.slot.dw_main_hand\", \"quality\": \"item.quality.dw_common\"," +
            " \"item_level\": 10, \"display_ref\": \"display.item.dw_sword\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.dw_sword\"," +
            " \"stats\": [{\"stat\": \"stat.dw_strength\", \"op\": \"flat\", \"value\": 5}]," +
            " \"weapon_profile\": {\"damage_min\": 3, \"damage_max\": 6, \"speed\": 2.0, \"weapon_school\": \"skill.school.physical\"}}," +
            "{\"id\": \"item.dw_dagger\", \"slot\": \"item.slot.dw_off_hand\", \"quality\": \"item.quality.dw_rare\"," +
            " \"item_level\": 8, \"display_ref\": \"display.item.dw_dagger\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.dw_dagger\"," +
            " \"stats\": [{\"stat\": \"stat.dw_strength\", \"op\": \"flat\", \"value\": 2}]," +
            " \"weapon_profile\": {\"damage_min\": 1, \"damage_max\": 3, \"speed\": 1.4, \"weapon_school\": \"skill.school.physical\"}}," +
            "{\"id\": \"item.dw_chest_armor\", \"slot\": \"item.slot.dw_chest\", \"quality\": \"item.quality.dw_common\"," +
            " \"item_level\": 1, \"display_ref\": \"display.item.dw_chest_armor\", \"stack_size\": 1," +
            " \"name_key\": \"l10n.item.dw_chest_armor\"," +
            " \"stats\": [{\"stat\": \"stat.dw_strength\", \"op\": \"flat\", \"value\": 1}]}" +
            "]";

        private static readonly Id Player = new Id("player.dw_hero");
        private static readonly Id MainHandSlot = new Id("item.slot.dw_main_hand");
        private static readonly Id OffHandSlot = new Id("item.slot.dw_off_hand");
        private static readonly Id ChestSlot = new Id("item.slot.dw_chest");

        private sealed class Fixture
        {
            public IDataRegistryView Registry = null!;
            public InventoryHost Inventory = null!;
            public EquipmentHost Equipment = null!;
        }

        private static Fixture Build()
        {
            var registry = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.template", TestSupport.Table("item.template", TemplateJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
            });

            var bus = TestSupport.CreateBus();
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            var effectSink = new FakeEffectSink();
            var skillGranter = new RecordingSkillGranter();
            var unitAccess = new FakeUnitAccess().Add(Player, 10);
            statHost.RegisterUnit(Player);

            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, effectSink, skillGranter.Grant, unitAccess);

            return new Fixture { Registry = registry, Inventory = inventory, Equipment = equipment };
        }

        private static Id GiveAndEquip(Fixture f, string templateId, Id slot, IReadOnlyList<Id>? affixes = null)
        {
            f.Inventory.AddItem(Player, new Id(templateId), 1, null, affixes);
            var items = f.Inventory.ListItems(Player);
            var instanceId = items[items.Count - 1].InstanceId;
            var result = f.Equipment.Equip(Player, instanceId, slot);
            Assert.True(result.Success, $"装备 {templateId} 到 {slot} 应成功：{result.Reason}");
            return instanceId;
        }

        [Fact]
        public void GetEquippedWeaponSummary_MatchesManualAssemblyFromItemInstance_FieldByField()
        {
            var f = Build();
            var affixes = new List<Id> { new Id("item.affix.dw_sharp") };
            GiveAndEquip(f, "item.dw_sword", MainHandSlot, affixes);

            // 手工搬运基线：消费方此前自建适配层的做法——GetAllEquippedInstances 拿到 ItemInstance，
            // 再手工搬运 TemplateId/Quality/Affixes 三字段（见反馈原文 StandardPlayerWeaponAdapter 的
            // 描述）。
            var manualInstance = f.Equipment.GetAllEquippedInstances(Player)[MainHandSlot];
            var manual = new EquippedWeaponSummary(manualInstance.TemplateId, manualInstance.Quality, manualInstance.Affixes);

            var viaApi = f.Equipment.GetEquippedWeaponSummary(Player, MainHandSlot);

            Assert.NotNull(viaApi);
            Assert.Equal(manual.TemplateId, viaApi!.Value.TemplateId);
            Assert.Equal(manual.QualityId, viaApi.Value.QualityId);
            Assert.Equal(manual.AffixIds, viaApi.Value.AffixIds);
            Assert.Equal(new Id("item.dw_sword"), viaApi.Value.TemplateId);
            Assert.Equal(new Id("item.quality.dw_common"), viaApi.Value.QualityId);
            Assert.Equal(affixes, viaApi.Value.AffixIds);
            Assert.Equal(manual, viaApi.Value);
        }

        [Fact]
        public void GetEquippedWeaponSummary_EmptySlot_ReturnsNull()
        {
            var f = Build();
            GiveAndEquip(f, "item.dw_sword", MainHandSlot);

            // off_hand 这个武器槽从未装备过任何东西——不是"没有武器"（该单位其实有主手武器），
            // 而是"这一个槽位空"，两者是反馈原文与落地计划要求分别覆盖的两个不同边界。
            var summary = f.Equipment.GetEquippedWeaponSummary(Player, OffHandSlot);

            Assert.Null(summary);
            Assert.False(f.Equipment.GetAllEquippedWeaponSummaries(Player).ContainsKey(OffHandSlot));
        }

        [Fact]
        public void GetEquippedWeaponSummary_NoWeaponEquippedAtAll_ReturnsNullAndEmptyMap()
        {
            var f = Build();
            // 该单位完全没有调用过 Equip——连非武器槽都没有装备任何东西。

            Assert.Null(f.Equipment.GetEquippedWeaponSummary(Player, MainHandSlot));
            Assert.Null(f.Equipment.GetEquippedWeaponSummary(Player, OffHandSlot));
            Assert.Empty(f.Equipment.GetAllEquippedWeaponSummaries(Player));
        }

        [Fact]
        public void GetAllEquippedWeaponSummaries_DualWielding_ReturnsBothSlotsIndependently()
        {
            var f = Build();
            var offHandAffixes = new List<Id> { new Id("item.affix.dw_swift"), new Id("item.affix.dw_keen") };
            GiveAndEquip(f, "item.dw_sword", MainHandSlot);
            GiveAndEquip(f, "item.dw_dagger", OffHandSlot, offHandAffixes);
            // 顺带装备一个非武器槽，确认武器摘要 API 不会漏掉/错配非武器槽（同判断记录"不限定只用于
            // 武器槽"）。
            GiveAndEquip(f, "item.dw_chest_armor", ChestSlot);

            var all = f.Equipment.GetAllEquippedWeaponSummaries(Player);

            Assert.Equal(3, all.Count);

            Assert.True(all.TryGetValue(MainHandSlot, out var mainHand));
            Assert.Equal(new Id("item.dw_sword"), mainHand.TemplateId);
            Assert.Equal(new Id("item.quality.dw_common"), mainHand.QualityId);
            Assert.Empty(mainHand.AffixIds);

            Assert.True(all.TryGetValue(OffHandSlot, out var offHand));
            Assert.Equal(new Id("item.dw_dagger"), offHand.TemplateId);
            Assert.Equal(new Id("item.quality.dw_rare"), offHand.QualityId);
            Assert.Equal(offHandAffixes, offHand.AffixIds);

            Assert.True(all.TryGetValue(ChestSlot, out var chest));
            Assert.Equal(new Id("item.dw_chest_armor"), chest.TemplateId);

            // 两个武器槽互不影响——主手没有词缀、副手有两条词缀，各自独立。
            Assert.NotEqual(mainHand, offHand);
        }
    }
}
