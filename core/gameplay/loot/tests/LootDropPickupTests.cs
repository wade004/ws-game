using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
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

        // -----------------------------------------------------------------
        // H4 补齐：掉落物固定复用同一个"地面拾取物外观"逻辑 id（见
        // DroppedLootEntity.GenericDisplayTemplateId 判断记录），WorldSim.AddEntity 才能算出稳定
        // 的 displayId，而不是每次都退化成不可能有 display.map 覆盖的逐实例 EntityId。
        // -----------------------------------------------------------------

        [Fact]
        public void Drop_SetsGenericDisplayTemplateId_OnTheEntity()
        {
            var f = NewFixture(SingleChanceTable);
            var lootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });

            var entity = f.World.GetEntity(lootId);
            Assert.NotNull(entity);
            Assert.Equal(DroppedLootEntity.GenericDisplayTemplateId, entity!.TemplateId);
        }

        [Fact]
        public void Drop_EntityCreatedEvent_CarriesGenericDisplayId_NotThePerInstanceEntityId()
        {
            var f = NewFixture(SingleChanceTable);
            EntityCreatedEvent? captured = null;
            f.Bus.Subscribe<EntityCreatedEvent>(SimEventKeys.EntityCreated, e => captured = e);

            var lootId = f.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });
            f.Bus.DispatchPending();

            Assert.NotNull(captured);
            Assert.Equal(DroppedLootEntity.GenericDisplayTemplateId, captured!.DisplayId);
            Assert.NotEqual(lootId, captured.DisplayId); // 不是逐实例的 EntityId（H3a 如实记录的缺口 2）。
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

        // -----------------------------------------------------------------
        // GP-PRES-01 收口回归（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
        // 读档恢复的地面掉落在 ISceneRouter.LoadScene 触发 IWorldSim.ClearAll 时被清空——LootHost
        // 自己的跟踪表（_dropped/_order）不受 ClearAll 影响，但 IWorldSim 侧的实体已经不在了。
        // 下面三条用例直接模拟"读档 → ClearAll（场景切换）→ ReattachToWorld（post-load 钩子）"
        // 这条真实链路，不依赖 Presentation/Unity。
        // -----------------------------------------------------------------

        /// <summary>核心回归：Drop（模拟"读档恢复出一件掉落物，此刻仍在世界里"）→ ClearAll（模拟
        /// 场景切换）→ 断言 IWorldSim 侧已经找不到该实体（复现 GP-PRES-01 的清空现象）→
        /// ReattachToWorld → 断言实体重新出现在 IWorldSim 里，且位置/物品/OwnerHint/ExpireAt 与
        /// 清空前一致。</summary>
        [Fact]
        public void ReattachToWorld_AfterWorldClearAll_RestoresDroppedLootIntoWorld()
        {
            var f = NewFixture(SingleChanceTable, new LootOptions { DefaultLifetime = 100 });
            var mapId = new Id("map.sample_1");
            var items = new[] { new ItemStack(new Id("item.sample_ore"), 3) };
            var lootId = f.Host.Drop(mapId, new Vec2(2, 3), items, ownerHint: new Id("player.sample_owner"));

            Assert.NotNull(f.World.GetEntity(lootId)); // 清空前：确实在世界里。

            f.World.ClearAll();
            Assert.Null(f.World.GetEntity(lootId)); // 复现 GP-PRES-01：ClearAll 后从 IWorldSim 消失。
            Assert.Contains(lootId, f.Host.ActiveLootIds); // 但 LootHost 自己的跟踪表不受影响。

            f.Host.ReattachToWorld(mapId);

            var reattached = f.World.GetEntity(lootId);
            Assert.NotNull(reattached);
            var restored = Assert.IsType<DroppedLootEntity>(reattached);
            Assert.Equal(new Vec2(2, 3), restored.Position);
            Assert.Single(restored.Items);
            Assert.Equal(3, restored.Items[0].Count);
            Assert.Equal(new Id("player.sample_owner"), restored.OwnerHint);
            Assert.Equal(100.0, restored.ExpireAt);
        }

        /// <summary>只重接属于目标地图的掉落物：属于另一张地图的记录不应该被塞进当前正在激活的
        /// <see cref="IWorldSim"/>（见 <see cref="LootHost.ReattachToWorld"/> 判断记录"按 mapId
        /// 过滤"）。</summary>
        [Fact]
        public void ReattachToWorld_OnlyReattachesEntitiesForGivenMap()
        {
            var f = NewFixture(SingleChanceTable);
            var mapA = new Id("map.sample_1");
            var mapB = new Id("map.sample_2");
            var items = new[] { new ItemStack(new Id("item.sample_ore"), 1) };

            var lootA = f.Host.Drop(mapA, new Vec2(0, 0), items);
            var lootB = f.Host.Drop(mapB, new Vec2(1, 1), items);

            f.World.ClearAll();
            Assert.Null(f.World.GetEntity(lootA));
            Assert.Null(f.World.GetEntity(lootB));

            f.Host.ReattachToWorld(mapA);

            Assert.NotNull(f.World.GetEntity(lootA));
            Assert.Null(f.World.GetEntity(lootB)); // 另一张地图的掉落物不应被带回当前世界。
        }

        /// <summary>幂等：连续调用两次（例如同一张地图反复触发 post-load 钩子）不应抛"实体 id 重复"
        /// 异常，也不应产生重复条目。</summary>
        [Fact]
        public void ReattachToWorld_CalledTwice_IsIdempotent_DoesNotThrow()
        {
            var f = NewFixture(SingleChanceTable);
            var mapId = new Id("map.sample_1");
            var items = new[] { new ItemStack(new Id("item.sample_ore"), 1) };
            var lootId = f.Host.Drop(mapId, new Vec2(0, 0), items);

            f.World.ClearAll();
            f.Host.ReattachToWorld(mapId);

            var ex = Record.Exception(() => f.Host.ReattachToWorld(mapId));
            Assert.Null(ex);
            Assert.NotNull(f.World.GetEntity(lootId));
            Assert.Single(f.Host.ActiveLootIds);
        }

        /// <summary>首次进图（从未发生过 ClearAll）时调用应是安全的空操作：实体本就在世界里，
        /// <see cref="LootHost.ReattachToWorld"/> 跳过已存在的实体，不抛异常。</summary>
        [Fact]
        public void ReattachToWorld_WhenEntityStillInWorld_IsNoOp()
        {
            var f = NewFixture(SingleChanceTable);
            var mapId = new Id("map.sample_1");
            var items = new[] { new ItemStack(new Id("item.sample_ore"), 1) };
            var lootId = f.Host.Drop(mapId, new Vec2(5, 5), items);

            var ex = Record.Exception(() => f.Host.ReattachToWorld(mapId));
            Assert.Null(ex);

            var entity = Assert.IsType<DroppedLootEntity>(f.World.GetEntity(lootId));
            Assert.Equal(new Vec2(5, 5), entity.Position);
        }

        // -----------------------------------------------------------------
        // 外部审核阻塞项 1 收口回归（architecture/落地计划/audit-20260907/followup-2026-09-07.md
        // "外部审核阻塞项处理"一节）：读档一致性——DroppedLootPersistable.Load 此前只管"增补"，
        // 从不清理"当前跟踪、但这次读档的存档快照里已经不再提及"的旧记录，见
        // LootHost.ClearDroppedExcept 判断记录。下面三条用例复现外部审核实测的缺口"保存空掉落档 →
        // 产生物品 B → 读取旧档 → 清场并重新挂载"后 B 仍出现，以及正常场景不受影响、跨地图场景
        // 掉落物只出现在正确的地图上。
        // -----------------------------------------------------------------

        /// <summary>核心复现：存一份不含任何掉落物的存档 → 之后产生一件掉落物 B → 读取那份旧档 →
        /// B 应该消失（既不在 <see cref="LootHost.ActiveLootIds"/> 里，也不能被拾取）。</summary>
        [Fact]
        public void SaveEmpty_SpawnB_LoadOld_BGone()
        {
            var f = NewFixture(SingleChanceTable);
            var persistable = new DroppedLootPersistable(f.Host);

            // 保存一份空掉落档（此刻还没有任何掉落物）。
            var savedEmpty = persistable.Save();
            Assert.IsType<JsonArray>(savedEmpty);
            Assert.Empty((JsonArray)savedEmpty);

            // 之后产生一件掉落物 B。
            var lootB = f.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });
            Assert.Contains(lootB, f.Host.ActiveLootIds);

            // 读取旧档（不含 B）：B 应该被清掉。
            persistable.Load(savedEmpty);

            Assert.Empty(f.Host.ActiveLootIds);
            Assert.DoesNotContain(lootB, f.Host.ActiveLootIds);
            Assert.False(f.Host.TryGetDropped(lootB, out _));

            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, new Id("map.sample_1"), new Vec2(0, 0));
            Assert.False(f.Host.PickUp(unitId, lootB).Success);
        }

        /// <summary>
        /// AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：与上面
        /// <see cref="SaveEmpty_SpawnB_LoadOld_BGone"/>（存档段本身存在、只是数组为空）互补——本用例
        /// 覆盖"本段整体缺失"（<c>data is JsonNull</c>，如旧格式存档从未写过 <c>world.dropped_loot</c>
        /// 段）这一路径：修复前 <c>Load</c> 对 <c>JsonNull</c> 直接 no-op 返回，不调用
        /// <c>ClearDroppedExcept</c>，运行期已产生的掉落物原样保留。
        /// </summary>
        [Fact]
        public void Load_NullData_ClearsAllTrackedDroppedLoot()
        {
            var f = NewFixture(SingleChanceTable);
            var persistable = new DroppedLootPersistable(f.Host);

            var lootB = f.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });
            Assert.Contains(lootB, f.Host.ActiveLootIds);

            persistable.Load(JsonNull.Instance);

            Assert.Empty(f.Host.ActiveLootIds);
            Assert.False(f.Host.TryGetDropped(lootB, out _));
        }

        /// <summary>正常场景（存档含掉落物 A → 场景清空 → 读同一份档）不应受"清理陈旧记录"这一根治
        /// 修复影响：A 仍应存在于世界里且可拾取——同 id 的记录既在"要保留的集合"里，也在
        /// <see cref="LootHost.RestoreDropped"/> 的"原地覆写/重新 AddEntity"两个既有分支的覆盖范围
        /// 内，不会被 <see cref="LootHost.ClearDroppedExcept"/> 误删。</summary>
        [Fact]
        public void SaveWithA_Load_AfterSceneClear_APresentAndPickable()
        {
            var f = NewFixture(SingleChanceTable);
            var persistable = new DroppedLootPersistable(f.Host);
            var mapId = new Id("map.sample_1");

            var lootA = f.Host.Drop(mapId, new Vec2(2, 2), new[] { new ItemStack(new Id("item.sample_ore"), 1) });
            var saved = persistable.Save();

            // 模拟真实 ShellHost.LoadGame 顺序（ISaveSystem.Load 先于 ISceneRouter.LoadScene/
            // ClearAll）：先读档（此刻 A 仍在世界里，走 RestoreDropped 的"原地覆写"分支），再清空
            // 场景，再由 post_load 钩子重新挂载。
            persistable.Load(saved);
            f.World.ClearAll();
            Assert.Null(f.World.GetEntity(lootA));

            f.Host.ReattachToWorld(mapId);

            var entity = f.World.GetEntity(lootA);
            Assert.NotNull(entity);
            Assert.IsType<DroppedLootEntity>(entity);

            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, mapId, new Vec2(2, 2));
            var result = f.Host.PickUp(unitId, lootA);
            Assert.True(result.Success);
        }

        /// <summary>跨地图：在 A 图存档（掉落物只在 A 图）→ 移动到 B 图（A 图的掉落物随场景切换
        /// 从 <see cref="IWorldSim"/> 消失，但 LootHost 自己的跟踪表仍记得）→ 读取那份旧档、再切回
        /// A 图——掉落物应该出现在 A 图，绝不应该出现在 B 图。</summary>
        [Fact]
        public void SaveOnMapA_Load_ReturnToMapA_LootPresentOnA_AbsentFromMapB()
        {
            var f = NewFixture(SingleChanceTable);
            var persistable = new DroppedLootPersistable(f.Host);
            var mapA = new Id("map.sample_1");
            var mapB = new Id("map.sample_2");

            var lootA = f.Host.Drop(mapA, new Vec2(1, 1), new[] { new ItemStack(new Id("item.sample_ore"), 1) });
            var saved = persistable.Save();

            // 移动到 B 图：场景切换清空世界，B 图不应捞到属于 A 图的掉落物。
            f.World.ClearAll();
            f.Host.ReattachToWorld(mapB);
            Assert.Null(f.World.GetEntity(lootA));

            // 读取旧档（world 此刻是空的——世界侧已经被上一次 ClearAll 清空，符合"读档时旧局掉落物
            // 已经不在 IWorldSim 里"的常见情形，见 LootHost.RestoreDropped 判断记录）。
            persistable.Load(saved);

            // 切回 A 图：掉落物应该重新出现。
            f.World.ClearAll();
            f.Host.ReattachToWorld(mapA);

            var entityOnA = f.World.GetEntity(lootA);
            Assert.NotNull(entityOnA);
            Assert.Equal(mapA, ((DroppedLootEntity)entityOnA!).MapId);

            // 再切到 B 图：不应该把 A 图的掉落物错误地带过去。
            f.World.ClearAll();
            f.Host.ReattachToWorld(mapB);
            Assert.Null(f.World.GetEntity(lootA));
        }
    }
}
