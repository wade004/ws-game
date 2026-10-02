using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// 空中姿势（ADR-0130 追加决定）：空中阶段（<see cref="AirPhase"/>）由竖直运动派生并经 <see cref="PoseSelector"/> 发布，
    /// <see cref="PoseContext.TryGetAirRequest"/> 把"状态 + 阶段"变成空中姿势请求（<c>jump.rise/fall/land</c>、<c>hit.air</c>、
    /// <c>attack.air[.族]</c>），<see cref="AnimStateMachine.AttachAirPhaseSource"/> 让状态机据此进出 Jump。
    /// 键与回落链本身在 <c>AirPoseKeyTests</c> 覆盖；这里覆盖请求拼装、阶段派生与状态机联动。不变量：没有任何空中阶段输入时，
    /// 上下文、状态与改动前逐位一致。
    /// </summary>
    public class AirPoseTests
    {
        private static readonly Id Hero = new Id("unit.air_hero");
        private static readonly Id Other = new Id("unit.air_other");

        private static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        // ---------- PoseContext：空中请求拼装 ----------

        [Fact]
        public void Context_WithoutAirPhase_NeverProducesAirRequests_AndStringFormIsUnchanged()
        {
            var ctx = new PoseContext(LocomotionGait.Run, "2h", "wounded");
            Assert.Equal(AirPhase.None, ctx.Air);
            Assert.False(ctx.IsAirborne);
            foreach (var state in new[] { "idle", "move", "attack", "cast", "hit", "death", "jump" })
            {
                Assert.False(ctx.TryGetAirRequest(state, out _), state);
            }

            Assert.Equal("Run/2h/wounded", ctx.ToString());
            Assert.Equal(PoseContext.Empty, new PoseContext(LocomotionGait.Idle));
        }

        [Theory]
        [InlineData(AirPhase.Rise, "jump.rise")]
        [InlineData(AirPhase.Fall, "jump.fall")]
        [InlineData(AirPhase.Land, "jump.land")]
        public void Context_JumpState_MapsEveryAirPhaseToItsKey(AirPhase phase, string expectedFullKey)
        {
            var ctx = new PoseContext(LocomotionGait.Idle, null, null, phase);
            Assert.True(ctx.TryGetAirRequest("jump", out var request));
            Assert.Equal(expectedFullKey, request.FullKey());
        }

        [Fact]
        public void Context_HitAndAttack_UseAirKeysOnlyWhileAirborne_NotDuringTheLandWindow()
        {
            foreach (var phase in new[] { AirPhase.Rise, AirPhase.Fall })
            {
                var ctx = new PoseContext(LocomotionGait.Idle, "greatsword", null, phase);
                Assert.True(ctx.TryGetAirRequest("hit", out var hit));
                Assert.Equal("hit.air", hit.FullKey());
                Assert.True(ctx.TryGetAirRequest("attack", out var attack));
                Assert.Equal("attack.air.greatsword", attack.FullKey());
                Assert.False(ctx.TryGetAirRequest("idle", out _));
                Assert.False(ctx.TryGetAirRequest("move", out _));
            }

            var landing = new PoseContext(LocomotionGait.Idle, "greatsword", null, AirPhase.Land);
            Assert.False(landing.TryGetAirRequest("hit", out _));
            Assert.False(landing.TryGetAirRequest("attack", out _));
        }

        [Fact]
        public void Context_AttackWithoutFamily_UsesTheFamilylessAirKey()
        {
            var ctx = new PoseContext(LocomotionGait.Idle, null, null, AirPhase.Fall);
            Assert.True(ctx.TryGetAirRequest("attack", out var attack));
            Assert.Equal(new[] { "attack.air", "attack" }, attack.Chain());
        }

        [Fact]
        public void Context_VariantOverload_CarriesStanceFamilyAndVariantIntoTheAirKeys()
        {
            // 复现（M4-W1b）：战斗姿态 + 武器族 + 变体都进空中键；回落链先去变体、再去武器族、再去姿态。
            var ctx = new PoseContext(LocomotionGait.Idle, "sword", "wounded", AirPhase.Fall);
            Assert.True(ctx.TryGetAirRequest("jump", true, out var jump));
            Assert.Equal("jump.fall.combat.sword.wounded", jump.FullKey());
            Assert.True(ctx.TryGetAirRequest("hit", true, out var hit));
            Assert.Equal("hit.air.combat.sword.wounded", hit.FullKey());
            Assert.True(ctx.TryGetAirRequest("attack", true, out var attack));
            Assert.Equal("attack.air.combat.sword.wounded", attack.FullKey());

            Assert.True(ctx.TryGetAirRequest("attack", false, out var peace));
            Assert.Equal("attack.air.sword.wounded", peace.FullKey()); // 和平姿态省略 .combat

            // 不变量：落地窗口里 hit/attack 仍不走空中键；非空中状态不产生请求；Land 阶段的 jump 带维度。
            var landing = new PoseContext(LocomotionGait.Idle, "sword", "wounded", AirPhase.Land);
            Assert.False(landing.TryGetAirRequest("hit", true, out _));
            Assert.True(landing.TryGetAirRequest("jump", true, out var land));
            Assert.Equal("jump.land.combat.sword.wounded", land.FullKey());
            Assert.False(ctx.TryGetAirRequest("idle", true, out _));
        }

        [Fact]
        public void Context_VariantOverload_WithoutDimensionsMatchesTheTwoArgumentOverload()
        {
            // 不变量：没有姿态/武器族/变体时两个重载给出相同的请求。
            foreach (var phase in new[] { AirPhase.Rise, AirPhase.Fall, AirPhase.Land })
            {
                var ctx = new PoseContext(LocomotionGait.Idle, null, null, phase);
                foreach (var state in new[] { "jump", "hit", "attack", "idle" })
                {
                    var a = ctx.TryGetAirRequest(state, out var r1);
                    var b = ctx.TryGetAirRequest(state, false, out var r2);
                    Assert.Equal(a, b);
                    Assert.Equal(r1, r2);
                }
            }
        }

        [Fact]
        public void Context_AirPhaseParticipatesInEquality()
        {
            var a = new PoseContext(LocomotionGait.Walk, "1h", null, AirPhase.Rise);
            var b = new PoseContext(LocomotionGait.Walk, "1h", null, AirPhase.Fall);
            Assert.NotEqual(a, b);
            Assert.Equal(a, new PoseContext(LocomotionGait.Walk, "1h", null, AirPhase.Rise));
            Assert.Contains("Rise", a.ToString());
        }

        // ---------- PoseSelector ----------

        [Fact]
        public void Selector_SetAirPhase_PublishesOnlyOnChange_AndKeepsOtherDimensions()
        {
            var selector = new PoseSelector();
            var changes = new List<Id>();
            selector.ContextChanged += changes.Add;

            selector.SetFamily(Hero, "2h");
            changes.Clear();

            selector.SetAirPhase(Hero, AirPhase.Rise);
            selector.SetAirPhase(Hero, AirPhase.Rise);
            selector.SetAirPhase(Hero, AirPhase.Fall);
            selector.SetAirPhase(Hero, AirPhase.None);
            Assert.Equal(new[] { Hero, Hero, Hero }, changes);

            selector.SetAirPhase(Hero, AirPhase.Land);
            var ctx = selector.GetContext(Hero);
            Assert.Equal(AirPhase.Land, ctx.Air);
            Assert.Equal("2h", ctx.Family);
        }

        [Fact]
        public void Selector_ClearingAirPhaseOnAnUnknownEntity_CreatesNothing()
        {
            var selector = new PoseSelector();
            var changes = 0;
            selector.ContextChanged += _ => changes++;
            selector.SetAirPhase(Other, AirPhase.None);
            Assert.Equal(0, changes);
            Assert.Equal(PoseContext.Empty, selector.GetContext(Other));
        }

        [Fact]
        public void Selector_GaitObservation_DoesNotDisturbTheAirPhase()
        {
            var selector = new PoseSelector();
            selector.SetAirPhase(Hero, AirPhase.Fall);
            selector.Observe(Hero, 1.0);
            Assert.Equal(AirPhase.Fall, selector.GetContext(Hero).Air);
        }

        // ---------- AirPoseFeeder ----------

        private sealed class ScriptedVertical : IVerticalMotion
        {
            public readonly Dictionary<Id, double> Speeds = new Dictionary<Id, double>();

            public bool IsAirborne(Id unitId) => Speeds.ContainsKey(unitId);

            public double GetVerticalSpeed(Id unitId) => Speeds.TryGetValue(unitId, out var v) ? v : 0.0;

            public bool Launch(Id unitId, double initialSpeed) => false;

            public bool LaunchToApex(Id unitId, double apexHeight) => false;

            public bool Jump(Id unitId) => false;

            public IReadOnlyList<Id> AirborneUnits() => Speeds.Keys.OrderBy(k => k.Value, StringComparer.Ordinal).ToList();
        }

        private static AirPhase Phase(PoseSelector selector, Id id) => selector.GetContext(id).Air;

        [Fact]
        public void Feeder_DerivesRiseFallThenLandForExactlyTheHoldWindow_ThenNone()
        {
            const int hold = 3;
            var motion = new ScriptedVertical();
            var selector = new PoseSelector();
            using var feeder = new AirPoseFeeder(CreateBus(), motion, selector, hold);

            motion.Speeds[Hero] = 3.0;
            feeder.Observe();
            Assert.Equal(AirPhase.Rise, Phase(selector, Hero));

            motion.Speeds[Hero] = 0.0; // 顶点：速度不再向上 → 归为下降
            feeder.Observe();
            Assert.Equal(AirPhase.Fall, Phase(selector, Hero));
            motion.Speeds[Hero] = -2.0;
            feeder.Observe();
            Assert.Equal(AirPhase.Fall, Phase(selector, Hero));

            motion.Speeds.Remove(Hero); // 落地
            for (var i = 0; i < hold; i++)
            {
                feeder.Observe();
                Assert.Equal(AirPhase.Land, Phase(selector, Hero));
            }

            feeder.Observe();
            Assert.Equal(AirPhase.None, Phase(selector, Hero));
            Assert.Equal(0, feeder.ActiveCount);
        }

        [Fact]
        public void Feeder_ZeroHold_ClearsStraightToNone()
        {
            var motion = new ScriptedVertical();
            var selector = new PoseSelector();
            using var feeder = new AirPoseFeeder(CreateBus(), motion, selector, 0);
            motion.Speeds[Hero] = 1.0;
            feeder.Observe();
            motion.Speeds.Remove(Hero);
            feeder.Observe();
            Assert.Equal(AirPhase.None, Phase(selector, Hero));
        }

        [Fact]
        public void Feeder_RelaunchDuringTheLandWindow_CancelsTheLandAndRises()
        {
            var motion = new ScriptedVertical();
            var selector = new PoseSelector();
            using var feeder = new AirPoseFeeder(CreateBus(), motion, selector, 5);
            motion.Speeds[Hero] = 1.0;
            feeder.Observe();
            motion.Speeds.Remove(Hero);
            feeder.Observe();
            Assert.Equal(AirPhase.Land, Phase(selector, Hero));

            motion.Speeds[Hero] = 4.0;
            feeder.Observe();
            Assert.Equal(AirPhase.Rise, Phase(selector, Hero));
            motion.Speeds.Remove(Hero);
            feeder.Observe();
            Assert.Equal(AirPhase.Land, Phase(selector, Hero)); // 新一轮落地窗口从头计
        }

        [Fact]
        public void Feeder_TracksEachUnitIndependently_AndNeverTouchesGroundedUnits()
        {
            var motion = new ScriptedVertical();
            var selector = new PoseSelector();
            using var feeder = new AirPoseFeeder(CreateBus(), motion, selector, 2);
            motion.Speeds[Hero] = 2.0;
            feeder.Observe();
            Assert.Equal(AirPhase.Rise, Phase(selector, Hero));
            Assert.Equal(PoseContext.Empty, selector.GetContext(Other));
        }

        [Fact]
        public void Feeder_ObservesOnEveryTickFinished_AndStopsAfterDispose()
        {
            var bus = CreateBus();
            var motion = new ScriptedVertical();
            var selector = new PoseSelector();
            var feeder = new AirPoseFeeder(bus, motion, selector, 2);
            motion.Speeds[Hero] = 2.0;
            bus.PublishImmediate(new SimTickFinishedEvent(1));
            Assert.Equal(AirPhase.Rise, Phase(selector, Hero));

            feeder.Dispose();
            motion.Speeds[Hero] = -2.0;
            bus.PublishImmediate(new SimTickFinishedEvent(2));
            Assert.Equal(AirPhase.Rise, Phase(selector, Hero)); // 已释放：不再更新
        }

        [Fact]
        public void Feeder_WithARealVerticalHost_FollowsTheParabola()
        {
            var bus = CreateBus();
            var world = new WorldSim(bus);
            world.AddEntity(new PlayerUnit(Hero, new Id("map.air"), new Id("fac.air"), new Id("arch.class.sample")));
            var host = new VerticalMotionHost(world, new VerticalAxisOptions { Gravity = 24 });
            var selector = new PoseSelector();
            using var feeder = new AirPoseFeeder(bus, host, selector, 4);

            const double dt = 1.0 / 60.0;
            host.Launch(Hero, 6.0);
            var sawRise = false;
            var sawFall = false;
            var firstLand = -1;
            for (var i = 0; i < 400 && firstLand < 0; i++)
            {
                host.Advance(dt);
                feeder.Observe();
                var phase = Phase(selector, Hero);
                // 期望由速度符号算出：v = v0 − g·t。
                if (host.IsAirborne(Hero))
                {
                    var expected = host.GetVerticalSpeed(Hero) > 0.0 ? AirPhase.Rise : AirPhase.Fall;
                    Assert.Equal(expected, phase);
                    sawRise |= phase == AirPhase.Rise;
                    sawFall |= phase == AirPhase.Fall;
                }
                else
                {
                    firstLand = i;
                    Assert.Equal(AirPhase.Land, phase);
                }
            }

            Assert.True(sawRise && sawFall && firstLand > 0);
        }

        // ---------- AnimStateMachine 联动 ----------

        private static AnimStateMachine Machine(IEventBus bus, PoseSelector? airSource)
        {
            var machine = new AnimStateMachine(bus);
            if (airSource != null) machine.AttachAirPhaseSource(airSource);
            return machine;
        }

        [Fact]
        public void StateMachine_EntersJumpWhenAirborne_StaysThroughLand_AndRevertsWhenTheWindowEnds()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            var machine = Machine(bus, selector);
            var transitions = new List<(AnimState From, AnimState To)>();
            machine.StateChanged += (_, from, to) => transitions.Add((from, to));

            selector.SetAirPhase(Hero, AirPhase.Rise);
            Assert.Equal(AnimState.Jump, machine.GetState(Hero));
            selector.SetAirPhase(Hero, AirPhase.Fall);
            selector.SetAirPhase(Hero, AirPhase.Land);
            Assert.Equal(AnimState.Jump, machine.GetState(Hero));
            selector.SetAirPhase(Hero, AirPhase.None);
            Assert.Equal(AnimState.Idle, machine.GetState(Hero));

            Assert.Equal(new[] { (AnimState.Idle, AnimState.Jump), (AnimState.Jump, AnimState.Idle) }, transitions);
        }

        [Fact]
        public void StateMachine_WithoutAnAirSource_NeverEntersJumpOnItsOwn()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            var machine = Machine(bus, null);
            selector.SetAirPhase(Hero, AirPhase.Rise);
            Assert.Equal(AnimState.Idle, machine.GetState(Hero));
        }

        [Fact]
        public void StateMachine_HitInTheAir_KeepsHit_ThenFallsBackToJumpWhileStillAirborne()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            var machine = Machine(bus, selector);

            selector.SetAirPhase(Hero, AirPhase.Rise);
            machine.RequestOverride(Hero, AnimState.Hit);
            Assert.Equal(AnimState.Hit, machine.GetState(Hero));

            selector.SetAirPhase(Hero, AirPhase.Fall);
            Assert.Equal(AnimState.Hit, machine.GetState(Hero)); // 受击优先级高于跳跃，空中阶段变化不打断

            machine.NotifyTransientStateFinished(Hero, AnimState.Hit);
            Assert.Equal(AnimState.Jump, machine.GetState(Hero)); // 受击播完时仍在空中 → 回到跳跃姿势，而不是站桩待机

            selector.SetAirPhase(Hero, AirPhase.None);
            Assert.Equal(AnimState.Idle, machine.GetState(Hero));
        }

        [Fact]
        public void StateMachine_HitOnTheGround_RevertsToIdle_AsBefore()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            var machine = Machine(bus, selector);
            machine.RequestOverride(Hero, AnimState.Hit);
            machine.NotifyTransientStateFinished(Hero, AnimState.Hit);
            Assert.Equal(AnimState.Idle, machine.GetState(Hero));
        }

        [Fact]
        public void StateMachine_DeathIsTerminal_AirPhaseCannotPullItBack()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            var machine = Machine(bus, selector);
            selector.SetAirPhase(Hero, AirPhase.Fall);
            bus.PublishImmediate(new UnitDiedEvent(Hero, Other));
            Assert.Equal(AnimState.Death, machine.GetState(Hero));
            selector.SetAirPhase(Hero, AirPhase.None);
            Assert.Equal(AnimState.Death, machine.GetState(Hero));
        }

        [Fact]
        public void StateMachine_AttackInTheAir_FinishesBackIntoJump_NotLocomotion()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            var machine = Machine(bus, selector);
            selector.SetAirPhase(Hero, AirPhase.Rise);
            machine.RequestOverride(Hero, AnimState.Attack);
            Assert.Equal(AnimState.Attack, machine.GetState(Hero));
            machine.NotifyTransientStateFinished(Hero, AnimState.Attack);
            Assert.Equal(AnimState.Jump, machine.GetState(Hero));
        }

        [Fact]
        public void StateMachine_DisposeDetachesTheAirSource()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            var machine = Machine(bus, selector);
            machine.Dispose();
            selector.SetAirPhase(Hero, AirPhase.Rise);
            Assert.Equal(AnimState.Idle, machine.GetState(Hero));
        }
    }
}
