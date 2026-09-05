using System;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Unit
{
    public class WorldUnitAccessTests
    {
        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static WorldSim NewWorld() => new WorldSim(NewBus());

        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id CreatureTemplateId = new Id("creature.grey_wolf");

        [Fact]
        public void Exists_TrueForUnit_FalseForUnknownId()
        {
            var world = NewWorld();
            var units = new WorldUnitAccess(world);
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            world.AddEntity(player);

            Assert.True(units.Exists(player.EntityId));
            Assert.False(units.Exists(new Id("unit.unknown")));
        }

        [Fact]
        public void AllUnits_OnlyIncludesUnitsSortedById()
        {
            var world = NewWorld();
            var units = new WorldUnitAccess(world);
            var creature = new CreatureUnit(new Id("unit.wolf"), MapId, FactionId, CreatureTemplateId);
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            world.AddEntity(creature);
            world.AddEntity(player);

            var all = units.AllUnits;

            Assert.Equal(new[] { player.EntityId, creature.EntityId }, all);
        }

        [Fact]
        public void AllUnits_ExcludesNonUnitEntities()
        {
            var world = NewWorld();
            var units = new WorldUnitAccess(world);
            world.AddEntity(new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId));
            world.AddEntity(new NonUnitTestEntity(new Id("gobj.chest_1"), MapId));

            var all = units.AllUnits;

            Assert.Single(all);
            Assert.Equal(new Id("unit.hero"), all[0]);
        }

        [Fact]
        public void GetPosition_SetPosition_RoundTrips()
        {
            var world = NewWorld();
            var units = new WorldUnitAccess(world);
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId) { Position = new Vec2(1, 2) };
            world.AddEntity(player);

            Assert.Equal(new Vec2(1, 2), units.GetPosition(player.EntityId));

            units.SetPosition(player.EntityId, new Vec2(5, 6));

            Assert.Equal(new Vec2(5, 6), units.GetPosition(player.EntityId));
        }

        [Fact]
        public void SetPosition_SyncsSpatialIndex_WhenInjected()
        {
            var world = NewWorld();
            var spatial = new StubSpatialQuery();
            var units = new WorldUnitAccess(world, spatial);
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            world.AddEntity(player);
            // 首次登记（ISpatialQuery.Register）不是 WorldUnitAccess 的职责——由
            // core/carriers/assembly/EntitySpatialSyncHost 订阅 entity.created 处理（见
            // WorldUnitAccess 构造函数判断记录）；本测试直接模拟"已登记"状态，只验证
            // SetPosition 经 UpdatePosition 同步位置这一件事。
            spatial.Register(player.EntityId, Vec2.Zero, 0.5, new[] { "unit" });

            units.SetPosition(player.EntityId, new Vec2(10, 10));

            var nearest = spatial.Nearest(new Vec2(10, 10), Core.Foundation.EngineAdapter.QueryFilter.None);
            Assert.Equal(player.EntityId, nearest);
        }

        [Fact]
        public void SetPosition_UnregisteredId_DoesNotThrow_AndIsNoOp()
        {
            var world = NewWorld();
            var spatial = new StubSpatialQuery();
            var units = new WorldUnitAccess(world, spatial);
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            world.AddEntity(player);

            var ex = Record.Exception(() => units.SetPosition(player.EntityId, new Vec2(10, 10)));

            Assert.Null(ex);
            Assert.Null(spatial.Nearest(new Vec2(10, 10), Core.Foundation.EngineAdapter.QueryFilter.None));
        }

        [Fact]
        public void GetFaction_GetLevel_GetFacing_ReadUnderlyingFields()
        {
            var world = NewWorld();
            var units = new WorldUnitAccess(world);
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId)
            {
                Level = 7,
                Facing = 1.25,
            };
            world.AddEntity(player);

            Assert.Equal(FactionId, units.GetFaction(player.EntityId));
            Assert.Equal(7, units.GetLevel(player.EntityId));
            Assert.Equal(1.25, units.GetFacing(player.EntityId));
        }

        [Fact]
        public void SetAlive_False_DoesNotDestroyEntity()
        {
            var world = NewWorld();
            var units = new WorldUnitAccess(world);
            var creature = new CreatureUnit(new Id("unit.wolf"), MapId, FactionId, CreatureTemplateId);
            world.AddEntity(creature);

            Assert.True(units.IsAlive(creature.EntityId));

            units.SetAlive(creature.EntityId, false);

            Assert.False(units.IsAlive(creature.EntityId));
            Assert.True(units.Exists(creature.EntityId));
            Assert.NotNull(world.GetEntity(creature.EntityId));
            Assert.Equal(EntityLifecycle.Active, creature.Lifecycle);
        }

        [Fact]
        public void GetTemplateId_GetTags_GetMapId_ReadUnderlyingFields()
        {
            var world = NewWorld();
            var units = new WorldUnitAccess(world);
            var creature = new CreatureUnit(new Id("unit.wolf"), MapId, FactionId, CreatureTemplateId);
            creature.Tags.Add(new Id("tag.beast"));
            world.AddEntity(creature);

            Assert.Equal(CreatureTemplateId, units.GetTemplateId(creature.EntityId));
            Assert.Equal(new[] { new Id("tag.beast") }, units.GetTags(creature.EntityId));
            Assert.Equal(MapId, units.GetMapId(creature.EntityId));
        }

        [Fact]
        public void AccessorsOnUnknownUnit_Throw()
        {
            var world = NewWorld();
            var units = new WorldUnitAccess(world);

            Assert.Throws<InvalidOperationException>(() => units.GetPosition(new Id("unit.unknown")));
        }

        /// <summary>非 Unit 的最小 Entity 子类，供 <see cref="AllUnits_ExcludesNonUnitEntities"/> 验证
        /// 过滤条件（同惯例 <c>core/rules/tests/Integration/TestUnit.cs</c>：只在测试里定义，不代表
        /// 任何正式类型）。</summary>
        private sealed class NonUnitTestEntity : Entity
        {
            public override string Kind => EntityKinds.Gobj;

            public NonUnitTestEntity(Id entityId, Id mapId) : base(entityId, mapId)
            {
            }
        }
    }
}
