using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    public class WorldSimEntityTests
    {
        [Fact]
        public void AddEntity_RegistersEntity_ActivatesLifecycle_AndDispatchesEntityCreatedWithCorrectFields()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var mapId = new Id("map.test_zone");
            var entity = new TestEntity(new Id("unit.hero"), mapId, kind: "unit");

            Id capturedEntityId = default;
            string? capturedKind = null;
            Id capturedDisplayId = default;
            bus.Subscribe<EntityCreatedEvent>(SimEventKeys.EntityCreated, e =>
            {
                capturedEntityId = e.EntityId;
                capturedKind = e.Kind;
                capturedDisplayId = e.DisplayId;
            });

            world.AddEntity(entity);

            Assert.Same(entity, world.GetEntity(entity.EntityId));
            Assert.Equal(EntityLifecycle.Active, entity.Lifecycle);
            Assert.Equal(1, world.EntityCount);

            // AddEntity 在 tick 外调用：entity.created 只入队，需要显式 DispatchPending
            // （或走一次 Tick）才会送达订阅者。
            Assert.Null(capturedKind);

            bus.DispatchPending();

            Assert.Equal(entity.EntityId, capturedEntityId);
            Assert.Equal("unit", capturedKind);
            Assert.Equal(entity.EntityId, capturedDisplayId); // TemplateId 为空，displayId = EntityId
        }

        [Fact]
        public void AddEntity_DuplicateEntityId_Throws()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var id = new Id("unit.hero");
            world.AddEntity(new TestEntity(id, new Id("map.test_zone")));

            Assert.Throws<InvalidOperationException>(() =>
                world.AddEntity(new TestEntity(id, new Id("map.test_zone"))));
        }

        [Fact]
        public void MarkForDestruction_RemovesEntity_AndDispatchesEntityDestroyed_WithinSameTick()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var entity = new TestEntity(new Id("unit.hero"), new Id("map.test_zone"));
            world.AddEntity(entity);
            bus.DispatchPending(); // 冲掉 entity.created，避免干扰下面的断言

            var destroyedReceived = false;
            Id destroyedId = default;
            bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e =>
            {
                destroyedReceived = true;
                destroyedId = e.EntityId;
            });

            world.MarkForDestruction(entity.EntityId);

            // 标记后、下一次 Tick 的生命周期清理阶段之前，实体仍在集合中。
            Assert.NotNull(world.GetEntity(entity.EntityId));

            world.Tick(SimStep.Continuous(1.0 / 60.0));

            Assert.True(destroyedReceived);
            Assert.Equal(entity.EntityId, destroyedId);
            Assert.Null(world.GetEntity(entity.EntityId));
            Assert.Equal(0, world.EntityCount);
        }

        [Fact]
        public void QueryEntities_FiltersByKindMapIdAndPredicate_AndSortsResultByEntityId()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var mapA = new Id("map.zone_a");
            var mapB = new Id("map.zone_b");

            var unitC = new TestEntity(new Id("unit.c_hero"), mapA, kind: "unit");
            var unitA = new TestEntity(new Id("unit.a_hero"), mapA, kind: "unit");
            var gobjInMapA = new TestEntity(new Id("gobj.chest"), mapA, kind: EntityKinds.Gobj);
            var unitInMapB = new TestEntity(new Id("unit.b_hero"), mapB, kind: "unit");

            world.AddEntity(unitC);
            world.AddEntity(unitA);
            world.AddEntity(gobjInMapA);
            world.AddEntity(unitInMapB);

            var unitsInMapA = world.QueryEntities(new EntityFilter(kind: "unit", mapId: mapA));
            Assert.Equal(
                new[] { unitA.EntityId, unitC.EntityId }, // "unit.a_hero" < "unit.c_hero" 序数排序
                unitsInMapA.Select(e => e.EntityId).ToArray());

            var byPredicate = world.QueryEntities(new EntityFilter(predicate: e => e.MapId == mapB));
            Assert.Single(byPredicate);
            Assert.Equal(unitInMapB.EntityId, byPredicate[0].EntityId);

            var all = world.QueryEntities(default);
            Assert.Equal(4, all.Count);
        }

        [Fact]
        public void ClearAll_RemovesAllEntities_EnqueuesEntityDestroyedForEach_ClearsTimersAndPendingDestruction()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);

            var unitA = new TestEntity(new Id("unit.a_hero"), new Id("map.zone_a"), kind: "unit");
            var unitB = new TestEntity(new Id("unit.b_hero"), new Id("map.zone_a"), kind: "unit");
            world.AddEntity(unitA);
            world.AddEntity(unitB);
            bus.DispatchPending(); // 冲掉两条 entity.created，避免干扰下面的断言

            // 额外标记一个待销毁实体、创建一个计时器，验证 ClearAll 一并清空。
            world.MarkForDestruction(unitA.EntityId);
            var timerHandle = world.Timers.Create(5.0);
            Assert.True(world.Timers.IsAlive(timerHandle));

            var destroyedIds = new System.Collections.Generic.List<Id>();
            bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e => destroyedIds.Add(e.EntityId));

            world.ClearAll();

            // 立即移除：EntityCount 归零，GetEntity 查不到。
            Assert.Equal(0, world.EntityCount);
            Assert.Null(world.GetEntity(unitA.EntityId));
            Assert.Null(world.GetEntity(unitB.EntityId));

            // entity.destroyed 只是入队，未显式 DispatchPending 前订阅者收不到。
            Assert.Empty(destroyedIds);
            bus.DispatchPending();
            Assert.Equal(
                new[] { unitA.EntityId, unitB.EntityId }, // 按 EntityId 序数排序："unit.a_hero" < "unit.b_hero"
                destroyedIds.ToArray());

            // 计时器被清空：旧句柄不再存活。
            Assert.False(world.Timers.IsAlive(timerHandle));

            // 待销毁列表被清空：ClearAll 之前标记的 unitA 不会在后续 Tick 里重复触发销毁事件。
            var extraDestroyed = new System.Collections.Generic.List<Id>();
            bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e => extraDestroyed.Add(e.EntityId));
            world.Tick(SimStep.Continuous(1.0 / 60.0));
            Assert.Empty(extraDestroyed);
        }

        [Fact]
        public void AllocateEntityId_ProducesSequentialDeterministicValidIds_PerKind()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());

            var unitId1 = world.AllocateEntityId("unit");
            var unitId2 = world.AllocateEntityId("unit");
            var gobjId1 = world.AllocateEntityId(EntityKinds.Gobj);

            Assert.Equal("unit.inst_1", unitId1.Value);
            Assert.Equal("unit.inst_2", unitId2.Value);
            Assert.Equal("gobj.inst_1", gobjId1.Value); // 不同 kind 各自独立计数

            Assert.True(Id.IsValidFormat(unitId1.Value));
            Assert.True(Id.IsValidFormat(unitId2.Value));
            Assert.True(Id.IsValidFormat(gobjId1.Value));
        }
    }
}
