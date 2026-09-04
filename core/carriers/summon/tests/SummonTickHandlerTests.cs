using System;
using System.Collections.Generic;
using Core.Carriers.Creature;
using Core.Carriers.Summon;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Tests.Carriers.Creature;
using Xunit;

namespace Tests.Carriers.Summon
{
    public class SummonTickHandlerTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id OwnerId = new Id("unit.owner_test");
        private static readonly Id OwnerFaction = new Id("fac.test_player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id BasicTemplateId = new Id("creature.sample_basic");

        /// <summary>供测试捕获某次 <see cref="IWorldSim.Tick"/> 内、<see cref="TickPhase.MovementAndNavigation"/>
        /// 阶段开始时可见的 <see cref="IWorldSim.CurrentIntents"/>（此时 <c>SummonTickHandler</c>
        /// 挂在更早的 <see cref="TickPhase.AiDecision"/> 已经执行完毕，惯例同
        /// <c>core/foundation/sim_loop/tests/AppendCurrentIntentTests</c> 的捕获写法）。</summary>
        private sealed class CaptureIntentsHandler : ITickPhaseHandler
        {
            public IReadOnlyList<Intent> Captured { get; private set; } = Array.Empty<Intent>();

            public void Execute(SimStep step, IWorldSim world) => Captured = world.CurrentIntents;
        }

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public CreatureFactory Factory = null!;
            public SummonHost SummonHost = null!;
            public FakeCombatHost Combat = null!;
            public CaptureIntentsHandler Capture = null!;
            public PlayerUnit Owner = null!;
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

            var owner = new PlayerUnit(OwnerId, MapId, OwnerFaction, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(owner);

            var summonHost = new SummonHost(factory, world, units, bus);
            var combat = new FakeCombatHost();
            var handler = new SummonTickHandler(summonHost, units, combat, options);
            var capture = new CaptureIntentsHandler();

            world.RegisterPhaseHandler(TickPhase.AiDecision, handler);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, capture);

            return new Fixture
            {
                World = world,
                Units = units,
                Factory = factory,
                SummonHost = summonHost,
                Combat = combat,
                Capture = capture,
                Owner = owner,
            };
        }

        private static Intent? FindMoveIntent(IReadOnlyList<Intent> intents, Id actorId)
        {
            for (var i = 0; i < intents.Count; i++)
            {
                if (intents[i].ActorId.Equals(actorId) && intents[i].Kind == "move")
                {
                    return intents[i];
                }
            }
            return null;
        }

        [Fact]
        public void Execute_Expired_DismissesWithExpiredReasonAndRemovesEntity()
        {
            var f = Build();
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero, duration: 0.5);

            f.World.Tick(SimStep.Continuous(1.0));

            Assert.Null(f.World.GetEntity(summonId));
            Assert.Null(f.SummonHost.GetOwner(summonId));
        }

        [Fact]
        public void Execute_NotYetExpired_KeepsSummonAlive()
        {
            var f = Build();
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero, duration: 5.0);

            f.World.Tick(SimStep.Continuous(1.0));

            Assert.NotNull(f.World.GetEntity(summonId));
            Assert.Equal(OwnerId, f.SummonHost.GetOwner(summonId));
        }

        [Fact]
        public void Execute_OwnerDead_DismissesWithOwnerLostReasonAndRemovesEntity()
        {
            var f = Build();
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);
            f.Units.SetAlive(OwnerId, false);

            f.World.Tick(SimStep.Continuous(1.0));

            Assert.Null(f.World.GetEntity(summonId));
        }

        [Fact]
        public void Execute_WithinFollowDistance_ProducesNoMoveIntent()
        {
            var f = Build();
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, new Vec2(1, 0));

            f.World.Tick(SimStep.Continuous(1.0));

            Assert.Null(FindMoveIntent(f.Capture.Captured, summonId));
        }

        [Fact]
        public void Execute_BeyondFollowDistance_ProducesMoveIntentStoppingShortOfOwner()
        {
            var f = Build();
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, new Vec2(10, 0));

            f.World.Tick(SimStep.Continuous(1.0));

            var intent = FindMoveIntent(f.Capture.Captured, summonId);
            Assert.NotNull(intent);
            var args = intent!.Value.Args;
            Assert.Equal(1.5, ((JsonNumber)args["x"]).Value, 6);
            Assert.Equal(0.0, ((JsonNumber)args["y"]).Value, 6);
        }

        [Fact]
        public void Execute_InCombatWithJoinCombatTrue_DoesNotFollow()
        {
            var f = Build(new SummonOptions { JoinCombat = true, SyncCombatState = false });
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, new Vec2(10, 0));
            f.Combat.SetInCombat(summonId, true);

            f.World.Tick(SimStep.Continuous(1.0));

            Assert.Null(FindMoveIntent(f.Capture.Captured, summonId));
        }

        [Fact]
        public void Execute_JoinCombatFalse_StillFollowsWhileInCombat()
        {
            var f = Build(new SummonOptions { JoinCombat = false, SyncCombatState = false });
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, new Vec2(10, 0));
            f.Combat.SetInCombat(summonId, true);

            f.World.Tick(SimStep.Continuous(1.0));

            Assert.NotNull(FindMoveIntent(f.Capture.Captured, summonId));
        }

        [Fact]
        public void Execute_SyncCombatState_NotifiesCombatEvent_WhenOwnerInCombatAndSummonNot()
        {
            var f = Build(new SummonOptions { SyncCombatState = true });
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);
            f.Combat.SetInCombat(OwnerId, true);

            f.World.Tick(SimStep.Continuous(1.0));

            Assert.Contains(summonId, f.Combat.NotifyCalls);
            Assert.True(f.Combat.IsInCombat(summonId));
        }

        [Fact]
        public void Execute_SyncCombatStateDisabled_DoesNotNotifyCombatEvent()
        {
            var f = Build(new SummonOptions { SyncCombatState = false });
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, Vec2.Zero);
            f.Combat.SetInCombat(OwnerId, true);

            f.World.Tick(SimStep.Continuous(1.0));

            Assert.DoesNotContain(summonId, f.Combat.NotifyCalls);
        }

        [Fact]
        public void Execute_DiscreteStep_DoesNothing()
        {
            var f = Build();
            var summonId = f.SummonHost.Summon(OwnerId, BasicTemplateId, new Vec2(10, 0), duration: 0.1);

            f.World.Tick(SimStep.Discrete(OwnerId, StepPhase.Act));

            // 离散步不推进：既不到期销毁，也不产生跟随意图。
            Assert.NotNull(f.World.GetEntity(summonId));
            Assert.Equal(OwnerId, f.SummonHost.GetOwner(summonId));
        }
    }
}
