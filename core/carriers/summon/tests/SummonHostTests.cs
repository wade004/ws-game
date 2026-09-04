using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Summon;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Tests.Carriers.Creature;
using Xunit;

namespace Tests.Carriers.Summon
{
    public class SummonHostTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id OwnerId = new Id("unit.owner_test");
        private static readonly Id OwnerFaction = new Id("fac.test_player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id BasicTemplateId = new Id("creature.sample_basic");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public IEventBus Bus = null!;
            public WorldUnitAccess Units = null!;
            public CreatureFactory Factory = null!;
            public SummonHost SummonHost = null!;
            public PlayerUnit Owner = null!;
            public List<SummonCreatedEvent> CreatedEvents = null!;
            public List<SummonExpiredEvent> ExpiredEvents = null!;
        }

        private static Fixture Build(SummonOptions? options = null)
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var stats = CreatureTestSupport.MakeStatHost(registry, bus);
            var powers = CreatureTestSupport.MakePowerHost(bus, stats);
            var progression = CreatureTestSupport.MakeProgressionHost(registry, bus, stats);
            AiRegistrar registrar = (unitId, profileId, spawnPoint, rotationId) => { };
            var factory = new CreatureFactory(registry, world, bus, stats, powers, progression, units, registrar);

            var owner = new PlayerUnit(OwnerId, MapId, OwnerFaction, ArchetypeId) { Position = Vec2.Zero, Facing = 0.0 };
            world.AddEntity(owner);

            var summonHost = new SummonHost(factory, world, units, bus, options);

            var created = new List<SummonCreatedEvent>();
            var expired = new List<SummonExpiredEvent>();
            bus.Subscribe<SummonCreatedEvent>(CarriersEventKeys.SummonCreated, e => created.Add(e));
            bus.Subscribe<SummonExpiredEvent>(CarriersEventKeys.SummonExpired, e => expired.Add(e));

            return new Fixture
            {
                World = world,
                Bus = bus,
                Units = units,
                Factory = factory,
                SummonHost = summonHost,
                Owner = owner,
                CreatedEvents = created,
                ExpiredEvents = expired,
            };
        }

        [Fact]
        public void Summon_CreatesCreatureUnit_WithOwnerId()
        {
            var f = Build();

            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, new Vec2(1, 1));

            var unit = Assert.IsType<CreatureUnit>(f.World.GetEntity(summonId));
            Assert.Equal(OwnerId, unit.OwnerId);
            Assert.Equal(MapId, unit.MapId);
        }

        [Fact]
        public void Summon_InheritsOwnerFaction_WhenEnabled()
        {
            var f = Build(new SummonOptions { InheritOwnerFaction = true });

            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);

            var unit = (CreatureUnit)f.World.GetEntity(summonId)!;
            Assert.Equal(OwnerFaction, unit.FactionId);
        }

        [Fact]
        public void Summon_DoesNotInheritFaction_WhenDisabled()
        {
            var f = Build(new SummonOptions { InheritOwnerFaction = false });

            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);

            var unit = (CreatureUnit)f.World.GetEntity(summonId)!;
            // 模板自带阵营 fac.test_monster，未被 owner 的 fac.test_player 覆盖。
            Assert.Equal(new Id("fac.test_monster"), unit.FactionId);
        }

        [Fact]
        public void Summon_EnqueuesSummonCreatedEvent()
        {
            var f = Build();

            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);
            f.Bus.DispatchPending();

            var evt = Assert.Single(f.CreatedEvents);
            Assert.Equal(summonId, evt.EntityId);
            Assert.Equal(OwnerId, evt.OwnerId);
        }

        [Fact]
        public void GetOwner_ReturnsOwner_ForActiveSummon()
        {
            var f = Build();

            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);

            Assert.Equal(OwnerId, f.SummonHost.GetOwner(summonId));
        }

        [Fact]
        public void GetOwner_ReturnsNull_ForUnknownSummon()
        {
            var f = Build();

            Assert.Null(f.SummonHost.GetOwner(new Id("creature.inst_999")));
        }

        [Fact]
        public void GetSummons_ReturnsStableInsertionOrder_ForMultipleSummons()
        {
            var f = Build();

            var first = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);
            var second = f.SummonHost.Summon(OwnerId, BasicTemplateId, new Vec2(1, 0));

            var list1 = f.SummonHost.GetSummons(OwnerId);
            var list2 = f.SummonHost.GetSummons(OwnerId);

            Assert.Equal(new[] { first, second }, list1);
            Assert.Equal(list1, list2);
        }

        [Fact]
        public void Dismiss_RemovesEntity_AndEnqueuesSummonExpiredEvent_WithDismissedReason()
        {
            var f = Build();
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);
            f.Bus.DispatchPending();

            f.SummonHost.Dismiss(summonId);
            f.World.Tick(SimStep.Continuous(0.1));

            Assert.Null(f.World.GetEntity(summonId));
            Assert.Null(f.SummonHost.GetOwner(summonId));
            var evt = Assert.Single(f.ExpiredEvents);
            Assert.Equal(summonId, evt.EntityId);
            Assert.Equal(OwnerId, evt.OwnerId);
            Assert.Empty(f.SummonHost.GetSummons(OwnerId));
        }

        [Fact]
        public void Dismiss_WithExplicitReason_PassesReasonToCreatureDespawnedEvent()
        {
            var f = Build();
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);

            var despawned = new List<CreatureDespawnedEvent>();
            f.Bus.Subscribe<CreatureDespawnedEvent>(CarriersEventKeys.CreatureDespawned, e => despawned.Add(e));

            f.SummonHost.Dismiss(summonId, "expired");
            f.World.Tick(SimStep.Continuous(0.1));

            var evt = Assert.Single(despawned);
            Assert.Equal("expired", evt.Reason);
        }

        [Fact]
        public void Dismiss_UnknownSummon_Throws()
        {
            var f = Build();

            Assert.Throws<ArgumentException>(() => f.SummonHost.Dismiss(new Id("creature.inst_999")));
        }
    }
}
