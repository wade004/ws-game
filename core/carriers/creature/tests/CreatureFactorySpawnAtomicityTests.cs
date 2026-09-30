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
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// ADR-0125 第三批探针缺陷：<see cref="CreatureFactory"/> 的 <c>SpawnCore</c> 非原子——<c>AddEntity</c>
    /// 之后任一登记步骤（Stats/Progression/Powers 登记、AI 登记）抛出，实体与已完成的登记都残留。
    /// 不变量：Spawn 抛出后，世界实体集合、Stats/Powers/Progression 对本次新实体 id 的登记、AI 登记调用
    /// 与 <see cref="CreatureSpawnedEvent"/> 与调用前逐项相等；异常原样向上抛；别处（调用方）先于本次
    /// 调用建立的同 id 登记不被回滚误伤。
    /// </summary>
    public sealed class CreatureFactorySpawnAtomicityTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id EliteTemplateId = new Id("creature.sample_elite"); // 有成长曲线 + AI 行为
        private static readonly Id HealthPower = new Id("arch.power.health");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public IEventBus Bus = null!;
            public StatHost Stats = null!;
            public PowerHost Powers = null!;
            public ProgressionHost Progression = null!;
            public CreatureFactory Factory = null!;
            public List<Id> AiCalls = null!;
            public List<CreatureSpawnedEvent> SpawnedEvents = null!;
            public List<EntityCreatedEvent> CreatedEvents = null!;
            public List<EntityDestroyedEvent> DestroyedEvents = null!;
            public Exception? AiThrows;
        }

        private static Fixture Build()
        {
            var f = new Fixture();
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);
            f.Bus = bus;
            f.World = new WorldSim(bus);
            var units = new WorldUnitAccess(f.World);
            f.Stats = CreatureTestSupport.MakeStatHost(registry, bus);
            f.Powers = CreatureTestSupport.MakePowerHost(registry, bus, f.Stats);
            f.Progression = CreatureTestSupport.MakeProgressionHost(registry, bus, f.Stats);
            f.AiCalls = new List<Id>();
            AiRegistrar registrar = (unitId, profileId, spawnPoint, rotationId) =>
            {
                f.AiCalls.Add(unitId);
                if (f.AiThrows != null)
                {
                    throw f.AiThrows;
                }
            };
            f.Factory = new CreatureFactory(registry, f.World, bus, f.Stats, f.Powers, f.Progression, units, registrar, null);
            f.SpawnedEvents = new List<CreatureSpawnedEvent>();
            f.CreatedEvents = new List<EntityCreatedEvent>();
            f.DestroyedEvents = new List<EntityDestroyedEvent>();
            bus.Subscribe<CreatureSpawnedEvent>(CarriersEventKeys.CreatureSpawned, e => f.SpawnedEvents.Add(e));
            bus.Subscribe<EntityCreatedEvent>(SimEventKeys.EntityCreated, e => f.CreatedEvents.Add(e));
            bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e => f.DestroyedEvents.Add(e));
            return f;
        }

        /// <summary>按 <see cref="IWorldSim.AllocateEntityId"/> 的规则（同一种类下一个序号）算出本次 Spawn 将要分配的实体 id。</summary>
        private static Id PredictNextCreatureId(Fixture f)
        {
            var probe = new WorldSim(CreatureTestSupport.CreateBus());
            return probe.AllocateEntityId(EntityKinds.Creature);
        }

        private static void AssertWorldRolledBack(Fixture f, Id id, int entityCountBefore)
        {
            f.Bus.DispatchPending(); // 排空（created + destroyed 成对送达）后再比较
            Assert.Null(f.World.GetEntity(id));
            Assert.False(f.World.IsPendingDestruction(id));
            Assert.Equal(entityCountBefore, f.World.EntityCount);
            Assert.Empty(f.World.QueryEntities(new EntityFilter(EntityKinds.Creature)));
            Assert.Empty(f.SpawnedEvents);
            // 世界里出现过又被回滚的实体，订阅者必须同时看到 created 与 destroyed（空间索引、AI 外壳等靠 destroyed 自清理）。
            Assert.Equal(f.CreatedEvents.Count, f.DestroyedEvents.Count);
        }

        [Fact]
        public void Spawn_AiRegistrarThrows_RollsBackEntityAndAllRegistrations_AndRethrowsOriginal()
        {
            var f = Build();
            var id = PredictNextCreatureId(f);
            var boom = new InvalidOperationException("boom-ai-registrar");
            f.AiThrows = boom;
            f.Bus.DispatchPending();
            var before = f.World.EntityCount;

            var ex = Assert.Throws<InvalidOperationException>(() => f.Factory.Spawn(EliteTemplateId, MapId, new Vec2(3, 4), 0));

            Assert.Same(boom, ex);
            Assert.Equal(new[] { id }, f.AiCalls); // AI 登记确实被走到（抛出点在末尾），之前所有步骤都已落地又被回滚
            AssertWorldRolledBack(f, id, before);
            Assert.False(f.Stats.IsRegistered(id));
            Assert.False(f.Powers.HasPower(id, HealthPower));
            Assert.Throws<ArgumentException>(() => f.Progression.GetLevel(id));
        }

        [Fact]
        public void Spawn_StatsRegisterUnitAlreadyRegistered_RollsBackEntity_AndKeepsForeignRegistration()
        {
            var f = Build();
            var id = PredictNextCreatureId(f);
            f.Stats.RegisterUnit(id); // 调用方先于本次 Spawn 建立的同 id 登记：回滚不得误注销它
            f.Bus.DispatchPending();
            var before = f.World.EntityCount;

            Assert.Throws<InvalidOperationException>(() => f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0));

            AssertWorldRolledBack(f, id, before);
            Assert.True(f.Stats.IsRegistered(id));
            Assert.Empty(f.AiCalls);
            Assert.False(f.Powers.HasPower(id, HealthPower));
        }

        [Fact]
        public void Spawn_PowersRegisterUnitAlreadyRegistered_RollsBackEntityStatsAndProgression_AndKeepsForeignPowers()
        {
            var f = Build();
            var id = PredictNextCreatureId(f);
            f.Stats.RegisterUnit(id);
            f.Powers.RegisterUnit(id, new[] { HealthPower });
            f.Stats.UnregisterUnit(id); // 只留下一份"外来"的 Powers 登记，Stats 留给本次 Spawn 自己登记
            f.Bus.DispatchPending();
            var before = f.World.EntityCount;

            Assert.Throws<InvalidOperationException>(() => f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0));

            AssertWorldRolledBack(f, id, before);
            Assert.False(f.Stats.IsRegistered(id));                       // 本次登记的被回滚
            Assert.Throws<ArgumentException>(() => f.Progression.GetLevel(id)); // 本次登记的被回滚
            Assert.True(f.Powers.HasPower(id, HealthPower));              // 外来登记原样保留
            Assert.Empty(f.AiCalls);
        }

        /// <summary>回滚之后工厂仍可用：下一次 Spawn 成功，且只有成功的那次产生 CreatureSpawnedEvent。</summary>
        [Fact]
        public void Spawn_AfterRolledBackFailure_NextSpawnSucceedsNormally()
        {
            var f = Build();
            f.AiThrows = new InvalidOperationException("boom-once");
            Assert.Throws<InvalidOperationException>(() => f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0));
            f.AiThrows = null;
            f.Bus.DispatchPending();

            var second = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);
            f.Bus.DispatchPending();

            Assert.NotNull(f.World.GetEntity(second));
            Assert.True(f.Stats.IsRegistered(second));
            Assert.True(f.Powers.HasPower(second, HealthPower));
            Assert.Single(f.SpawnedEvents);
            Assert.Equal(second, f.SpawnedEvents[0].EntityId);
        }
    }
}
