using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Creature
{
    public class CreatureFactoryTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id BasicTemplateId = new Id("creature.sample_basic");
        private static readonly Id EliteTemplateId = new Id("creature.sample_elite");
        private static readonly Id PowerStat = new Id("stat.power");
        private static readonly Id MaxHealthStat = new Id("stat.max_health");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public IEventBus Bus = null!;
            public WorldUnitAccess Units = null!;
            public StatHost Stats = null!;
            public PowerHost Powers = null!;
            public ProgressionHost Progression = null!;
            public CreatureFactory Factory = null!;
            public List<(Id unitId, Id profileId, Vec2 spawnPoint, Id? rotationId)> AiCalls = null!;
            public List<CreatureSpawnedEvent> SpawnedEvents = null!;
            public List<CreatureDespawnedEvent> DespawnedEvents = null!;
        }

        private static Fixture Build(CreatureOptions? options = null)
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var stats = CreatureTestSupport.MakeStatHost(registry, bus);
            var powers = CreatureTestSupport.MakePowerHost(bus, stats);
            var progression = CreatureTestSupport.MakeProgressionHost(registry, bus, stats);

            var aiCalls = new List<(Id, Id, Vec2, Id?)>();
            AiRegistrar registrar = (unitId, profileId, spawnPoint, rotationId) =>
                aiCalls.Add((unitId, profileId, spawnPoint, rotationId));

            var factory = new CreatureFactory(registry, world, bus, stats, powers, progression, units, registrar, options);

            var spawned = new List<CreatureSpawnedEvent>();
            var despawned = new List<CreatureDespawnedEvent>();
            bus.Subscribe<CreatureSpawnedEvent>(CarriersEventKeys.CreatureSpawned, e => spawned.Add(e));
            bus.Subscribe<CreatureDespawnedEvent>(CarriersEventKeys.CreatureDespawned, e => despawned.Add(e));

            return new Fixture
            {
                World = world,
                Bus = bus,
                Units = units,
                Stats = stats,
                Powers = powers,
                Progression = progression,
                Factory = factory,
                AiCalls = aiCalls,
                SpawnedEvents = spawned,
                DespawnedEvents = despawned,
            };
        }

        [Fact]
        public void Spawn_CreatesEntity_WithCreatureKindAndFaction()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, new Vec2(1, 2), 0.5);

            var entity = f.World.GetEntity(id);
            Assert.NotNull(entity);
            Assert.Equal("creature", entity!.Kind);
            var unit = Assert.IsType<CreatureUnit>(entity);
            Assert.Equal(new Id("fac.test_monster"), unit.FactionId);
            Assert.Equal(BasicTemplateId, unit.TemplateId);
            Assert.Equal(new Vec2(1, 2), unit.Position);
            Assert.Equal(0.5, unit.Facing);
        }

        [Fact]
        public void Spawn_WithoutGrowth_AppliesTierMultiplierOnly()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);

            // tier.normal 倍率 1：最终值 = base_stats 原值。
            Assert.Equal(10.0, f.Stats.GetStat(id, PowerStat), 6);
            Assert.Equal(100.0, f.Stats.GetStat(id, MaxHealthStat), 6);
        }

        [Fact]
        public void Spawn_WithGrowthCurve_AppliesTierMultiplierAndAccumulatesFromLevel2()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);

            // tier.elite 倍率 2：base 10*2=20，成长曲线 2/3 级各 +5 → +10，最终 30。
            Assert.Equal(30.0, f.Stats.GetStat(id, PowerStat), 6);
            // base 100*2=200，成长曲线只在 3 级 +20 → 最终 220。
            Assert.Equal(220.0, f.Stats.GetStat(id, MaxHealthStat), 6);
        }

        [Fact]
        public void Spawn_RegistersProgressionHost_WhenStatGrowthRefPresent()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);

            Assert.Equal(3, f.Progression.GetLevel(id));
        }

        [Fact]
        public void Spawn_DoesNotRegisterProgressionHost_WhenNoStatGrowthRef()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);

            Assert.Throws<ArgumentException>(() => f.Progression.GetLevel(id));
        }

        [Fact]
        public void Spawn_RegistersPowerHost_WithStatBasedMaxReflectingGrowth()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);

            Assert.True(f.Powers.HasPower(id, WellKnownPowers.Health));
            Assert.Equal(220.0, f.Powers.GetPowerMax(id, WellKnownPowers.Health), 6);
            Assert.Equal(220.0, f.Powers.GetPower(id, WellKnownPowers.Health), 6); // start_full=true
        }

        [Fact]
        public void Spawn_ConvertsNpcFlagsToTags_AndSetsTierTag()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);
            var unit = (CreatureUnit)f.World.GetEntity(id)!;

            Assert.Contains(new Id("npc_flag.vendor"), unit.NpcFlags);
            Assert.Contains(new Id("npc_flag.questgiver"), unit.NpcFlags);
            Assert.Contains(new Id("tag.npc.vendor"), unit.Tags);
            Assert.Contains(new Id("tag.npc.questgiver"), unit.Tags);
            Assert.Contains(new Id("creature.tier.elite"), unit.Tags);
        }

        [Fact]
        public void HasFlag_ReflectsTemplateNpcFlags()
        {
            var f = Build();

            Assert.True(f.Factory.HasFlag(EliteTemplateId, NpcFlag.Vendor));
            Assert.True(f.Factory.HasFlag(EliteTemplateId, NpcFlag.Questgiver));
            Assert.False(f.Factory.HasFlag(EliteTemplateId, NpcFlag.SavePoint));
            Assert.False(f.Factory.HasFlag(BasicTemplateId, NpcFlag.Vendor));
        }

        [Fact]
        public void Spawn_WritesImmunitiesFromTemplateAndTierControlImmune()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);
            var unit = (CreatureUnit)f.World.GetEntity(id)!;

            Assert.Contains(new Id("school.sample_fire"), unit.Immunities);
            // tier.elite.control_immune = true → 默认 ImmunityTagPrefix 写入。
            Assert.Contains(new Id("immunity.control_immune"), unit.Immunities);
        }

        [Fact]
        public void Spawn_DoesNotWriteControlImmuneTag_ForNonControlImmuneTier()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);
            var unit = (CreatureUnit)f.World.GetEntity(id)!;

            Assert.DoesNotContain(new Id("immunity.control_immune"), unit.Immunities);
        }

        [Fact]
        public void Spawn_RegistersAi_WhenAiBehaviorRefPresent()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, new Vec2(3, 4), 0);

            var call = Assert.Single(f.AiCalls);
            Assert.Equal(id, call.unitId);
            Assert.Equal(new Id("ai.behavior.sample"), call.profileId);
            Assert.Equal(new Vec2(3, 4), call.spawnPoint);
            Assert.Equal(new Id("ai.rotation.sample"), call.rotationId);
        }

        [Fact]
        public void Spawn_DoesNotRegisterAi_WhenNoAiBehaviorRef()
        {
            var f = Build();

            f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);

            Assert.Empty(f.AiCalls);
        }

        [Fact]
        public void Spawn_EnqueuesCreatureSpawnedEvent_WithEntityAndTemplateId()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);
            f.Bus.DispatchPending();

            var evt = Assert.Single(f.SpawnedEvents);
            Assert.Equal(id, evt.EntityId);
            Assert.Equal(BasicTemplateId, evt.TemplateId);
        }

        [Fact]
        public void Despawn_RemovesEntity_AndEnqueuesCreatureDespawnedEventWithReason()
        {
            var f = Build();
            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);
            f.Bus.DispatchPending();

            f.Factory.Despawn(id, "died");
            f.World.Tick(SimStep.Continuous(0.1));

            Assert.Null(f.World.GetEntity(id));
            var evt = Assert.Single(f.DespawnedEvents);
            Assert.Equal(id, evt.EntityId);
            Assert.Equal("died", evt.Reason);
        }

        [Fact]
        public void Spawn_UnknownTemplate_Throws()
        {
            var f = Build();

            Assert.Throws<ArgumentException>(() =>
                f.Factory.Spawn(new Id("creature.does_not_exist"), MapId, Vec2.Zero, 0));
        }

        [Fact]
        public void Spawn_UnknownTier_Throws()
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var stats = CreatureTestSupport.MakeStatHost(registry, bus);
            var powers = CreatureTestSupport.MakePowerHost(bus, stats);
            var progression = CreatureTestSupport.MakeProgressionHost(registry, bus, stats);
            AiRegistrar registrar = (unitId, profileId, spawnPoint, rotationId) => { };

            // 用绕过引用完整性校验的假 registry 装配工厂：证明 CreatureFactory 自身对未知 tier
            // 有防御性检查（正常数据管线里这一分支已在 reference_integrity 校验阶段被拦截）。
            var orphanRegistry = new CreatureTestSupport.UnknownTierRegistryView();
            var factory = new CreatureFactory(orphanRegistry, world, bus, stats, powers, progression, units, registrar);

            Assert.Throws<ArgumentException>(() =>
                factory.Spawn(new Id("creature.sample_orphan"), MapId, Vec2.Zero, 0));
        }
    }
}
