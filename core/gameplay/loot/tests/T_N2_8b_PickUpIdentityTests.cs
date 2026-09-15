using System;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// T-N2-8b（T-N2-8 已知缺口收口；ADR-0032 决策 7/8；见 core/gameplay/loot/README.md 判断记录
    /// 15"已知缺口"、本任务判断记录）：<see cref="LootHost.PickUp"/> 改用 <see
    /// cref="IInventoryHost.AddItem(Id, Id, int, Id?, System.Collections.Generic.IReadOnlyList{Id})"/>
    /// 带身份重载后，地面掉落物真正掷出的品质/词缀一路保真进背包；同模板不同品质（或带词缀）的物品
    /// 不与既有堆叠合并。本文件用真实 <see cref="InventoryHost"/>（不是 <c>FakeInventoryHost</c>——
    /// 假实现不落地真正的 <see cref="ItemInstance.Quality"/>/<see cref="ItemInstance.Affixes"/>
    /// 分派逻辑，同 <c>CR130_01_PickUpRejectTransactionTests</c> 判断记录一贯理由）。
    /// </summary>
    public sealed class T_N2_8b_PickUpIdentityTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static (LootHost Loot, InventoryHost Inventory, IEventBus Bus, Core.Foundation.SimLoop.IWorldSim World) NewFixture()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\":\"item.slot.n2_8b_weapon\",\"name_key\":\"l10n.slot.n2_8b_weapon\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\":\"item.quality.n2_8b_common\",\"name_key\":\"l10n.quality.n2_8b_common\"}," +
                    "{\"id\":\"item.quality.n2_8b_rare\",\"name_key\":\"l10n.quality.n2_8b_rare\"}]"))
                .Add("item.template", Envelope("item.template", "["
                    + "{\"id\":\"item.n2_8b_sword\",\"slot\":\"item.slot.n2_8b_weapon\",\"quality\":\"item.quality.n2_8b_common\",\"item_level\":1,\"display_ref\":\"display.item.n2_8b_sword\",\"stack_size\":5,\"name_key\":\"l10n.item.n2_8b_sword\"}]"))
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name,
                    "[{\"id\":\"loot.n2_8b_dummy\",\"groups\":[{\"roll_mode\":\"chance_each\",\"entries\":[" +
                    "{\"ref\":\"item.n2_8b_sword\",\"weight_or_chance\":1.0,\"count_range\":{\"min\":1,\"max\":1}}]}]}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(LootSchemas.Table);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues.Select(i => i.ToString())));

            var inventory = new InventoryHost(registry, bus);
            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var loot = new LootHost(
                registry, new RngHost(1), bus, world, units, inventory, new FakeExprHostFactory(),
                () => 0.0, options: new LootOptions { FullPolicy = LootPickupPolicy.Reject });

            return (loot, inventory, bus, world);
        }

        [Fact]
        public void PickUp_AffixedOutcome_InventoryInstanceHasMatchingQualityAndAffixes()
        {
            var f = NewFixture();
            var unitId = new Id("unit.n2_8b_player");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.n2_8b"), new Vec2(0, 0));

            var quality = new Id("item.quality.n2_8b_rare");
            var affixes = new[] { new Id("item.affix.n2_8b_sharp"), new Id("item.affix.n2_8b_heavy") };
            var outcome = new LootRollOutcome(new Id("item.n2_8b_sword"), 1, quality, affixes, itemLevel: null);
            var lootId = f.Loot.Drop(new Id("map.n2_8b"), new Vec2(0, 0), new[] { outcome });

            var result = f.Loot.PickUp(unitId, lootId);

            Assert.True(result.Success);
            var items = f.Inventory.ListItems(unitId);
            var instance = Assert.Single(items);
            Assert.Equal(quality, instance.Quality);
            Assert.Equal(affixes, instance.Affixes.ToArray());
        }

        [Fact]
        public void PickUp_SameTemplateDifferentQuality_DoesNotStack_InInventory()
        {
            var f = NewFixture();
            var unitId = new Id("unit.n2_8b_player2");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.n2_8b"), new Vec2(0, 0));

            var commonQuality = new Id("item.quality.n2_8b_common");
            var rareQuality = new Id("item.quality.n2_8b_rare");
            var outcomes = new[]
            {
                new LootRollOutcome(new Id("item.n2_8b_sword"), 1, commonQuality, null, null),
                new LootRollOutcome(new Id("item.n2_8b_sword"), 1, rareQuality, null, null),
            };
            var lootId = f.Loot.Drop(new Id("map.n2_8b"), new Vec2(0, 0), outcomes);

            var result = f.Loot.PickUp(unitId, lootId);

            Assert.True(result.Success);
            var items = f.Inventory.ListItems(unitId);
            Assert.Equal(2, items.Count);
            Assert.Contains(items, i => i.Quality.Equals(commonQuality) && i.Count == 1);
            Assert.Contains(items, i => i.Quality.Equals(rareQuality) && i.Count == 1);
        }

        [Fact]
        public void PickUpPartial_LeavesRemainingOutcomeIdentityIntact_OnGround()
        {
            var f = NewFixture();
            // MaxSlots=0（真实容量 GetCapacity 恒 int.MaxValue，不限）不适合本用例；这里用 1 格
            // Partial 策略制造"放不下"的场景，验证部分拾取后留在地面的那部分 Outcomes 保留原有
            // 品质/词缀，只是 Count 改成剩余数量（不是重新掷骰）。
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\":\"item.slot.n2_8b_weapon2\",\"name_key\":\"l10n.slot.n2_8b_weapon2\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\":\"item.quality.n2_8b_common2\",\"name_key\":\"l10n.quality.n2_8b_common2\"}," +
                    "{\"id\":\"item.quality.n2_8b_rare2\",\"name_key\":\"l10n.quality.n2_8b_rare2\"}]"))
                .Add("item.template", Envelope("item.template", "["
                    + "{\"id\":\"item.n2_8b_bow\",\"slot\":\"item.slot.n2_8b_weapon2\",\"quality\":\"item.quality.n2_8b_common2\",\"item_level\":1,\"display_ref\":\"display.item.n2_8b_bow\",\"stack_size\":10,\"name_key\":\"l10n.item.n2_8b_bow\"}]"))
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name,
                    "[{\"id\":\"loot.n2_8b_dummy2\",\"groups\":[{\"roll_mode\":\"chance_each\",\"entries\":[" +
                    "{\"ref\":\"item.n2_8b_bow\",\"weight_or_chance\":1.0,\"count_range\":{\"min\":1,\"max\":1}}]}]}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(LootSchemas.Table);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues.Select(i => i.ToString())));

            var inventory = new InventoryHost(registry, bus, new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Partial });
            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var loot = new LootHost(
                registry, new RngHost(1), bus, world, units, inventory, new FakeExprHostFactory(),
                () => 0.0, options: new LootOptions { FullPolicy = LootPickupPolicy.Partial });

            var unitId = new Id("unit.n2_8b_player3");
            LootTestSupport.AddPlayer(world, unitId, new Id("map.n2_8b2"), new Vec2(0, 0));

            var rareQuality = new Id("item.quality.n2_8b_rare2");
            var affixes = new[] { new Id("item.affix.n2_8b_swift") };
            // 已有一件非默认品质占掉唯一的格子（不可续填），随后的 8 个 rare 同模板物品放不下——
            // Partial 策略下一件都拿不到。
            Assert.True(inventory.AddItem(unitId, new Id("item.n2_8b_bow"), 1, rareQuality, affixes));

            var outcome = new LootRollOutcome(new Id("item.n2_8b_bow"), 8, rareQuality, affixes, itemLevel: null);
            var lootId = loot.Drop(new Id("map.n2_8b2"), new Vec2(0, 0), new[] { outcome });

            var result = loot.PickUp(unitId, lootId);

            Assert.False(result.Success);
            Assert.Equal(LootPickupFailureReason.InventoryFull, result.Reason);
            Assert.True(loot.TryGetDropped(lootId, out var entity));
            var remainingOutcome = Assert.Single(entity.Outcomes);
            Assert.Equal(8, remainingOutcome.Count);
            Assert.Equal(rareQuality, remainingOutcome.QualityId);
            Assert.Equal(affixes, remainingOutcome.Affixes.ToArray());
        }
    }
}
