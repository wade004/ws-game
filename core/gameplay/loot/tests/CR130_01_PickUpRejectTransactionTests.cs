using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Gameplay.Loot;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// CR130-01（外部审计 audit-5c444f1-20260908，P1）复现与根治，<see cref="LootHost.PickUp"/>
    /// 的 <c>Reject</c> 分支——与 <c>Core.Gameplay.Economy.EconomyHost.Buy</c> 同款缺口：某一件放不下
    /// 时，前面已经成功 <c>AddItem</c> 的堆叠按 <c>RollbackAdd</c> 补偿撤销；<c>AddItem</c> 的
    /// <c>item.added</c> 与补偿的 <c>item.removed</c> 是两条独立入队事件（<see
    /// cref="IEventBus.Enqueue"/> 只入队、不立即派发），不加事务时会在同一次 <c>DispatchPending</c>
    /// 里先后派发——下游订阅者（如 <c>QuestHost.HandleItemAdded</c> 的 <c>consumeOnProgress</c>）会把
    /// 先到的 <c>item.added</c> 当真立即产生副作用，后到的 <c>item.removed</c> 抵消不了这个副作用。
    /// <para>
    /// 本文件用真实 <see cref="InventoryHost"/>（实现 <see cref="IBatchableInventoryHost"/>，
    /// <c>LootDropPickupTests</c> 用的 <c>FakeInventoryHost</c> 不产生任何事件，无法复现/验收这条
    /// 事件序列缺口）+ 真实 <see cref="EventBus"/> 验证：整批拾取失败（<c>Rejected</c>）后，
    /// <c>DispatchPending</c> 不应派发任何 <c>item.added</c>/<c>item.removed</c> 事件——下游完全
    /// 观察不到这次失败的拾取发生过，不只是"数量最终对了"。
    /// </para>
    /// </summary>
    public sealed class CR130_01_PickUpRejectTransactionTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        [Fact]
        public void PickUpReject_PartialFailureOnSecondStack_RollsBackFirstStack_WithoutDispatchingAnyEvent()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\":\"item.slot.cr130_01_consumable\",\"name_key\":\"l10n.slot.cr130_01_consumable\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\":\"item.quality.cr130_01_common\",\"name_key\":\"l10n.quality.cr130_01_common\"}]"))
                .Add("item.template", Envelope("item.template", "["
                    + "{\"id\":\"item.cr130_01_a\",\"slot\":\"item.slot.cr130_01_consumable\",\"quality\":\"item.quality.cr130_01_common\",\"item_level\":1,\"display_ref\":\"display.item.cr130_01_a\",\"stack_size\":10,\"name_key\":\"l10n.item.cr130_01_a\"},"
                    + "{\"id\":\"item.cr130_01_b\",\"slot\":\"item.slot.cr130_01_consumable\",\"quality\":\"item.quality.cr130_01_common\",\"item_level\":1,\"display_ref\":\"display.item.cr130_01_b\",\"stack_size\":10,\"name_key\":\"l10n.item.cr130_01_b\"}]"))
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, "["
                    + "{\"id\":\"loot.cr130_01_bundle\",\"groups\":[{\"roll_mode\":\"chance_each\",\"entries\":["
                    + "{\"ref\":\"item.cr130_01_a\",\"weight_or_chance\":1.0,\"count_range\":{\"min\":1,\"max\":1}},"
                    + "{\"ref\":\"item.cr130_01_b\",\"weight_or_chance\":1.0,\"count_range\":{\"min\":1,\"max\":1}}"
                    + "]}]}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(LootSchemas.Table);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues.Select(i => i.ToString())));

            // MaxSlots=1、Partial：单个格子已经放了 9 个 A（stack_size=10），A 还能续填 1 个不占新
            // 格子；B 是不同模板，需要开一个新格子，但格子已经被 A 占满——B 会整体放不下（added=0），
            // 而排在它前面的 A 已经成功续填过一次（added=1），触发"前面成功、后面失败"的回滚路径。
            var inventory = new InventoryHost(registry, bus, new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Partial });
            var unitId = new Id("unit.cr130_01_player");
            var itemA = new Id("item.cr130_01_a");
            inventory.AddItem(unitId, itemA, 9);
            bus.DispatchPending();

            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            LootTestSupport.AddPlayer(world, unitId, new Id("map.cr130_01"), new Vec2(0, 0));

            var loot = new LootHost(
                registry, new RngHost(1), bus, world, units, inventory, new FakeExprHostFactory(),
                () => 0.0, options: new LootOptions { FullPolicy = LootPickupPolicy.Reject });

            var items = loot.Roll(new Id("loot.cr130_01_bundle"), new RollContext(unitId));
            Assert.Equal(2, items.Count);
            var lootId = loot.Drop(new Id("map.cr130_01"), new Vec2(0, 0), items);
            // 冲掉 AddPlayer/Drop 各自产生的 entity.created 一类与本条无关的世界事件，让下面的
            // "PickUp 失败后一条 item 事件都不该派发"测量不被这些无关事件的计数污染。
            bus.DispatchPending();

            var observedEvents = new List<IEvent>();
            bus.Subscribe(CarriersEventKeys.ItemAdded, e => observedEvents.Add(e));
            bus.Subscribe(CarriersEventKeys.ItemRemoved, e => observedEvents.Add(e));

            var beforeCount = inventory.CountOf(unitId, itemA);
            var result = loot.PickUp(unitId, lootId);
            var dispatched = bus.DispatchPending();
            var afterCount = inventory.CountOf(unitId, itemA);

            Assert.False(result.Success);
            Assert.Equal(LootPickupFailureReason.Rejected, result.Reason);
            Assert.Equal(9, beforeCount);
            Assert.Equal(9, afterCount);

            // CR130-01 核心验收：失败的拾取事务性回滚后，下游完全观察不到 item.added/item.removed
            // 发生过——修复前会先派发一条 item.added（A 从 9→10）、再派发一条 item.removed（A 又
            // 10→9），净数量虽然对了，但两条事件本身仍会被下游订阅者（如 consumeOnProgress 一类
            // 立即响应 item.added 的任务逻辑）观察到并产生副作用；修复后事务未 Commit 即整体 Dispose
            // 回滚，缓存事件跟着一起丢弃，一条都不会派发（PickUp 失败本身不产生任何事件，本次
            // DispatchPending 应该是彻底的空批次）。
            Assert.Equal(0, dispatched);
            Assert.Empty(observedEvents);
        }
    }
}
