using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Gameplay.Loot;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Loot
{
    public class LootDropPickupTests
    {
        private const string SingleChanceTable =
            "[{\"id\": \"loot.sample_single\", \"groups\": [" +
            "{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}}" +
            "]}]}]";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public IWorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public FakeInventoryHost Inventory = null!;
            public LootHost Host = null!;
            public double SimTime;
        }

        private static Fixture NewFixture(string lootTableRowsJson, LootOptions? options = null)
        {
            var fixture = new Fixture();
            fixture.Bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistry(fixture.Bus, lootTableRowsJson);
            fixture.World = LootTestSupport.NewWorld(fixture.Bus);
            fixture.Units = new WorldUnitAccess(fixture.World);
            fixture.Inventory = new FakeInventoryHost();

            fixture.Host = new LootHost(
                registry, new RngHost(1), fixture.Bus, fixture.World, fixture.Units, fixture.Inventory,
                new FakeExprHostFactory(), () => fixture.SimTime, options: options);

            return fixture;
        }

        [Fact]
        public void PickUp_WithinRange_Succeeds()
        {
            var f = NewFixture(SingleChanceTable);
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.sample_1"), new Vec2(0, 0));

            var items = f.Host.Roll(new Id("loot.sample_single"), new RollContext(unitId));
            var lootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(1, 0), items);

            var result = f.Host.PickUp(unitId, lootId);

            Assert.True(result.Success);
            Assert.Equal(1, f.Inventory.CountOf(unitId, new Id("item.sample_ore")));
        }

        [Fact]
        public void PickUp_BeyondRange_FailsTooFar()
        {
            var f = NewFixture(SingleChanceTable, new LootOptions { PickupRange = 3 });
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.sample_1"), new Vec2(0, 0));

            var lootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(100, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });

            var result = f.Host.PickUp(unitId, lootId);

            Assert.False(result.Success);
            Assert.Equal(LootPickupFailureReason.TooFar, result.Reason);
        }

        [Fact]
        public void PickUp_UnknownLootInstance_FailsNotFound()
        {
            var f = NewFixture(SingleChanceTable);
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.sample_1"), new Vec2(0, 0));

            var result = f.Host.PickUp(unitId, new Id("loot.inst_999"));

            Assert.False(result.Success);
            Assert.Equal(LootPickupFailureReason.NotFound, result.Reason);
        }

        [Fact]
        public void PickUp_Partial_TakesWhatFitsAndLeavesRemainderOnGround()
        {
            var f = NewFixture(SingleChanceTable, new LootOptions { FullPolicy = LootPickupPolicy.Partial });
            f.Inventory.MaxTotalItems = 6;
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.sample_1"), new Vec2(0, 0));

            var items = new List<ItemStack>
            {
                new ItemStack(new Id("item.sample_a"), 5),
                new ItemStack(new Id("item.sample_b"), 5),
            };
            var lootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), items);

            var result = f.Host.PickUp(unitId, lootId);

            Assert.True(result.Success);
            Assert.Equal(5, f.Inventory.CountOf(unitId, new Id("item.sample_a")));
            Assert.Equal(1, f.Inventory.CountOf(unitId, new Id("item.sample_b")));

            // 掉落物仍活跃（还有剩余），剩余数量应为 b:4。
            Assert.Contains(lootId, f.Host.ActiveLootIds);
            Assert.True(f.Host.TryGetDropped(lootId, out var entity));
            Assert.Single(entity.Items);
            Assert.Equal(4, entity.Items[0].Count);
        }

        [Fact]
        public void PickUp_Reject_RollsBackEverythingWhenNotAllFits()
        {
            var f = NewFixture(SingleChanceTable, new LootOptions { FullPolicy = LootPickupPolicy.Reject });
            f.Inventory.MaxTotalItems = 6;
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.sample_1"), new Vec2(0, 0));

            var items = new List<ItemStack>
            {
                new ItemStack(new Id("item.sample_a"), 5),
                new ItemStack(new Id("item.sample_b"), 5),
            };
            var lootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), items);

            var result = f.Host.PickUp(unitId, lootId);

            Assert.False(result.Success);
            Assert.Equal(LootPickupFailureReason.Rejected, result.Reason);
            Assert.Equal(0, f.Inventory.CountOf(unitId, new Id("item.sample_a")));
            Assert.Equal(0, f.Inventory.CountOf(unitId, new Id("item.sample_b")));

            Assert.True(f.Host.TryGetDropped(lootId, out var entity));
            Assert.Equal(2, entity.Items.Count);
        }

        [Fact]
        public void Expiry_DestroysDroppedLootAfterLifetime()
        {
            var f = NewFixture(SingleChanceTable, new LootOptions { DefaultLifetime = 10 });
            f.SimTime = 0;
            var lootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });

            Assert.Contains(lootId, f.Host.ActiveLootIds);

            var handler = new LootExpiryTickHandler(f.Host, () => f.SimTime);
            f.SimTime = 5;
            handler.Execute(SimStep.Continuous(5), f.World);
            Assert.Contains(lootId, f.Host.ActiveLootIds); // 尚未到期

            f.SimTime = 11;
            handler.Execute(SimStep.Continuous(6), f.World);

            Assert.DoesNotContain(lootId, f.Host.ActiveLootIds);
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.sample_1"), new Vec2(0, 0));
            Assert.False(f.Host.PickUp(unitId, lootId).Success);
        }

        [Fact]
        public void LootRolledAndPickedUpEvents_CarryExpectedFields()
        {
            var f = NewFixture(SingleChanceTable);
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.sample_1"), new Vec2(0, 0));

            LootRolledEvent? rolled = null;
            LootPickedUpEvent? pickedUp = null;
            f.Bus.Subscribe<LootRolledEvent>(LootEventKeys.Rolled, e => rolled = e);
            f.Bus.Subscribe<LootPickedUpEvent>(LootEventKeys.PickedUp, e => pickedUp = e);

            var context = new RollContext(unitId, contextId: new Id("unit.sample_ctx"));
            var items = f.Host.Roll(new Id("loot.sample_single"), context);
            var lootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), items);
            f.Host.PickUp(unitId, lootId);

            f.Bus.DispatchPending();

            Assert.NotNull(rolled);
            Assert.Equal(new Id("loot.sample_single"), rolled!.TableId);
            Assert.Equal(new Id("unit.sample_ctx"), rolled.ContextId);
            Assert.Single(rolled.Items);

            Assert.NotNull(pickedUp);
            Assert.Equal(unitId, pickedUp!.UnitId);
            Assert.Equal(lootId, pickedUp.LootInstanceId);
            Assert.Single(pickedUp.Items);
        }

        [Fact]
        public void CreatureDeathLootListener_GeneratesLootAtDeathPosition()
        {
            var f = NewFixture(SingleChanceTable);
            var templates = new FakeCreatureTemplateQuery();
            var lootTableId = new Id("loot.sample_single");
            templates.Add(new Id("creature.sample_wolf"), lootTableId);

            var listener = new CreatureDeathLootListener(f.Bus, f.Host, templates, f.Units, f.World);

            var deathPos = new Vec2(5, 7);
            var creatureId = new Id("creature.inst_1");
            LootTestSupport.AddCreature(f.World, creatureId, new Id("map.sample_1"), new Id("creature.sample_wolf"), deathPos);

            var killerId = new Id("player.sample_killer");
            f.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId));
            f.Bus.DispatchPending();

            Assert.Single(f.Host.ActiveLootIds);
            var lootId = f.Host.ActiveLootIds[0];
            Assert.True(f.Host.TryGetDropped(lootId, out var entity));
            Assert.Equal(deathPos, entity.Position);
            Assert.Equal(new Id("map.sample_1"), entity.MapId);
            Assert.Equal(killerId, entity.OwnerHint);
        }

        [Fact]
        public void DroppedLootPersistable_RoundTripsThroughSaveAndLoad()
        {
            var f1 = NewFixture(SingleChanceTable, new LootOptions { DefaultLifetime = 100 });
            f1.SimTime = 0;
            var items = new[] { new ItemStack(new Id("item.sample_ore"), 3) };
            var lootId = f1.Host.Drop(new Id("map.sample_1"), new Vec2(2, 3), items, ownerHint: new Id("player.sample_owner"));

            var persistable1 = new DroppedLootPersistable(f1.Host);
            var saved = persistable1.Save();

            // 独立的第二套装配（新 LootHost/WorldSim），模拟"读档"场景。
            var f2 = NewFixture(SingleChanceTable);
            var persistable2 = new DroppedLootPersistable(f2.Host);
            persistable2.Load(saved);

            Assert.Contains(lootId, f2.Host.ActiveLootIds);
            Assert.True(f2.Host.TryGetDropped(lootId, out var restored));
            Assert.Equal(new Vec2(2, 3), restored.Position);
            Assert.Single(restored.Items);
            Assert.Equal(3, restored.Items[0].Count);
            Assert.Equal(new Id("player.sample_owner"), restored.OwnerHint);
            Assert.Equal(100.0, restored.ExpireAt);

            // 读档后继续 Drop 不应与恢复的实体 id 冲突（见 README 判断记录 5）。
            var newLootId = f2.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), items);
            Assert.NotEqual(lootId, newLootId);
        }

        /// <summary>U3 排障发现的契约缺口回归测试（见 <see cref="LootHost.RestoreDropped"/> 判断
        /// 记录）：同一局游戏内"存档 -&gt; （不清空世界）-&gt; 立即读档"这条路径——
        /// <c>Presentation.Shell.ShellHost.LoadGame</c> 的既有实现顺序是 <c>ISaveSystem.Load</c>
        /// 先于 <c>ISceneRouter.LoadScene</c>（真正触发 <c>IWorldSim.ClearAll</c> 的地方）——存档
        /// 快照里的地面掉落物在读档这一刻仍然原样存在于世界里，<see cref="LootHost.RestoreDropped"/>
        /// 此前直接调 <c>IWorldSim.AddEntity</c> 会因为 id 已存在抛
        /// <c>InvalidOperationException("实体 id 重复")</c>。</summary>
        [Fact]
        public void DroppedLootPersistable_LoadOntoSameLiveWorld_DoesNotThrow_OverwritesInPlace()
        {
            var f = NewFixture(SingleChanceTable, new LootOptions { DefaultLifetime = 100 });
            f.SimTime = 0;
            var items = new[] { new ItemStack(new Id("item.sample_ore"), 3) };
            var lootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(2, 3), items, ownerHint: new Id("player.sample_owner"));

            var persistable = new DroppedLootPersistable(f.Host);
            var saved = persistable.Save();

            // 不清空 f.World/f.Host，直接在同一个存活世界上重放"读档"——模拟真实 ShellHost.LoadGame
            // 顺序（Load 早于 LoadScene/ClearAll）。
            var ex = Record.Exception(() => persistable.Load(saved));
            Assert.Null(ex);

            Assert.Single(f.Host.ActiveLootIds);
            Assert.Contains(lootId, f.Host.ActiveLootIds);
            Assert.True(f.Host.TryGetDropped(lootId, out var restored));
            Assert.Equal(new Vec2(2, 3), restored.Position);
            Assert.Single(restored.Items);
            Assert.Equal(3, restored.Items[0].Count);
            Assert.Equal(new Id("player.sample_owner"), restored.OwnerHint);
            Assert.Equal(100.0, restored.ExpireAt);

            // 读档后继续 Drop 仍不应与恢复的实体 id 冲突。
            var newLootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), items);
            Assert.NotEqual(lootId, newLootId);
        }
    }
}
