using System;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Presentation.Common;
using Xunit;

namespace Tests.PresentationCommon
{
    public class WorldSimSnapshotTests
    {
        private static readonly Id MapId = new Id("map.test");

        [Fact]
        public void GetPosition_ExistingEntity_ReturnsCurrentPosition()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var entity = new TestEntity(new Id("unit.a"), MapId) { Position = new Vec2(3, 4) };
            world.AddEntity(entity);

            var snapshot = new WorldSimSnapshot(world);

            Assert.Equal(new Vec2(3, 4), snapshot.GetPosition(entity.EntityId));
        }

        [Fact]
        public void GetFacing_ExistingEntity_ReturnsCurrentFacing()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var entity = new TestEntity(new Id("unit.a"), MapId) { Facing = 1.5 };
            world.AddEntity(entity);

            var snapshot = new WorldSimSnapshot(world);

            Assert.Equal(1.5, snapshot.GetFacing(entity.EntityId));
        }

        [Fact]
        public void GetHeight_UnitEntity_ReturnsHeightOffset()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var unit = new CreatureUnit(new Id("unit.wolf"), MapId, new Id("fac.beast"), new Id("creature.wolf"))
            {
                HeightOffset = 2.5
            };
            world.AddEntity(unit);

            var snapshot = new WorldSimSnapshot(world);

            Assert.Equal(2.5, snapshot.GetHeight(unit.EntityId));
        }

        [Fact]
        public void GetHeight_NonUnitEntity_ReturnsZero()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var entity = new TestEntity(new Id("gobj.chest"), MapId, "gobj");
            world.AddEntity(entity);

            var snapshot = new WorldSimSnapshot(world);

            Assert.Equal(0.0, snapshot.GetHeight(entity.EntityId));
        }

        [Fact]
        public void Exists_UnknownEntity_ReturnsFalse()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var snapshot = new WorldSimSnapshot(world);

            Assert.False(snapshot.Exists(new Id("unit.missing")));
        }

        [Fact]
        public void GetDisplayId_UsesTemplateIdWhenPresent()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var unit = new CreatureUnit(new Id("unit.wolf_1"), MapId, new Id("fac.beast"), new Id("creature.wolf"));
            world.AddEntity(unit);

            var snapshot = new WorldSimSnapshot(world);

            Assert.Equal(new Id("creature.wolf"), snapshot.GetDisplayId(unit.EntityId));
        }

        [Fact]
        public void GetDisplayId_FallsBackToEntityIdWhenNoTemplate()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var entity = new TestEntity(new Id("unit.manual"), MapId);
            world.AddEntity(entity);

            var snapshot = new WorldSimSnapshot(world);

            Assert.Equal(entity.EntityId, snapshot.GetDisplayId(entity.EntityId));
        }

        [Fact]
        public void GetDisplayId_UnknownEntity_ReturnsNull()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var snapshot = new WorldSimSnapshot(world);

            Assert.Null(snapshot.GetDisplayId(new Id("unit.missing")));
        }

        [Fact]
        public void GetKind_MapsKnownEntityKind()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var unit = new CreatureUnit(new Id("unit.wolf_2"), MapId, new Id("fac.beast"), new Id("creature.wolf"));
            world.AddEntity(unit);

            var snapshot = new WorldSimSnapshot(world);

            Assert.Equal(ViewKind.Unit, snapshot.GetKind(unit.EntityId));
        }

        [Fact]
        public void GetKind_UnmappedEntityKind_ReturnsNull()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var entity = new TestEntity(new Id("x.unmapped"), MapId, "unmapped_kind");
            world.AddEntity(entity);

            var snapshot = new WorldSimSnapshot(world);

            Assert.Null(snapshot.GetKind(entity.EntityId));
        }

        [Fact]
        public void GetPosition_UnknownEntity_Throws()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var snapshot = new WorldSimSnapshot(world);

            Assert.Throws<InvalidOperationException>(() => snapshot.GetPosition(new Id("unit.missing")));
        }
    }
}
