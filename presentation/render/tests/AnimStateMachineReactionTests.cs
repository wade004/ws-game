// M5-S4（ADR-0147，手感设计/03 第 4.5 节、04 第 3 节）：受击反应驱动的姿势请求序列。
// 复现：同一组命中经"裁决事件"驱动时，受击子键序列 = 反应→姿势键映射表；无反应（none/霸体）不播任何受击动画；
// 不变量：有硬直的反应保持到逻辑层硬直结束（与硬直时钟同源）、flinch 不打断动作、未传反应查询时与此前逐位一致。
using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    public class AnimStateMachineReactionTests
    {
        private static readonly Id Hero = new Id("unit.hero");
        private static readonly Id Foe = new Id("unit.foe");

        private sealed class FakeReactions : IHitReactionQuery
        {
            public readonly Dictionary<Id, int> Remaining = new Dictionary<Id, int>();
            public readonly HashSet<Id> Staggered = new HashSet<Id>();
            public bool IsStaggered(Id unitId) => Staggered.Contains(unitId);
            public bool IsDowned(Id unitId) => false;
            public int RemainingStaggerTicks(Id unitId) => Remaining.TryGetValue(unitId, out var n) ? n : 0;
        }

        private sealed class Rig
        {
            public readonly IEventBus Bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            public readonly FakeReactions Reactions = new FakeReactions();
            public readonly AnimStateMachine Machine;
            public readonly List<string> Requests = new List<string>();
            private long _tick;

            public Rig(bool reactionDriven = true)
            {
                Machine = reactionDriven ? new AnimStateMachine(Bus, null, Reactions) : new AnimStateMachine(Bus);
                Machine.StateChangedWithSkill += (id, from, to, skill) => Record(id, to);
                Machine.StateRetriggered += (id, state, skill) => Record(id, state);
            }

            private void Record(Id id, AnimState state)
            {
                if (state == AnimState.Hit)
                {
                    var sub = Machine.GetHitPoseSub(id);
                    Requests.Add(sub == null ? "hit" : "hit." + sub);
                }
                else
                {
                    Requests.Add(state.ToString().ToLowerInvariant());
                }
            }

            /// <summary>登记一次硬直（与 HitFeelHost 同口径：记录先于事件落地）并发 reaction_applied。</summary>
            public void React(Id target, HitReaction reaction, int stun, int downed, int getup)
            {
                var duration = stun + downed + getup;
                if (duration > 0)
                {
                    Reactions.Remaining[target] = duration;
                    Reactions.Staggered.Add(target);
                }

                Bus.PublishImmediate(new CombatReactionAppliedEvent(target, reaction, Foe, new Id("atk.1"), duration, stun, downed, getup));
            }

            /// <summary>推进 n 个 tick：硬直剩余递减，归零移出硬直，每 tick 发 sim.tick_finished。</summary>
            public void Tick(int n = 1)
            {
                for (var i = 0; i < n; i++)
                {
                    foreach (var id in new List<Id>(Reactions.Remaining.Keys))
                    {
                        Reactions.Remaining[id]--;
                        if (Reactions.Remaining[id] <= 0)
                        {
                            Reactions.Remaining.Remove(id);
                            Reactions.Staggered.Remove(id);
                        }
                    }

                    Bus.PublishImmediate(new SimTickFinishedEvent(++_tick));
                }
            }

            public void Block(Id target) => Bus.PublishImmediate(new CombatHitConfirmedEvent(
                new Id("atk.2"), 0, Foe, target, null, HitResult.Block, 0, 0, false, false, Vec2.Zero, Vec2.Zero, Vec2.Zero, "light", 0, 0,
                HitReaction.Flinch));

            public void Damage(Id target) => Bus.PublishImmediate(
                new CombatDamageDealtEvent(Foe, target, new Id("school.physical"), 5, false, HitResult.Hit));
        }

        [Fact]
        public void ReactionToPoseMapping_FollowsThe03Table_ForEveryReactionAndPhase()
        {
            var cases = new (HitReaction Reaction, int Stun, int Down, int Getup, string Expected)[]
            {
                (HitReaction.Flinch, 0, 0, 0, "hit.light"),
                (HitReaction.StaggerLight, 6, 0, 0, "hit.light"),
                (HitReaction.Stagger, 8, 0, 0, "hit.heavy"),
                (HitReaction.Knockback, 10, 0, 0, "hit.knockback"),
                (HitReaction.Knockdown, 4, 6, 0, "hit.knockback"),
            };
            foreach (var c in cases)
            {
                var rig = new Rig();
                rig.React(Hero, c.Reaction, c.Stun, c.Down, c.Getup);
                Assert.Equal(AnimState.Hit, rig.Machine.GetState(Hero));
                Assert.Equal(new[] { c.Expected }, rig.Requests);
            }
        }

        [Fact]
        public void Knockdown_StepsThroughKnockbackKnockdownGetup_ThenReturnsToLocomotionWhenStaggerEnds()
        {
            var rig = new Rig();
            rig.Bus.PublishImmediate(new UnitStateChangedEvent(Hero, "Idle", "Run"));
            rig.Requests.Clear();
            const int stun = 3, down = 4, getup = 2;
            rig.React(Hero, HitReaction.Knockdown, stun, down, getup);
            rig.Tick(stun);
            rig.Bus.PublishImmediate(new UnitKnockedDownEvent(Hero, down, getup));
            rig.Tick(down);
            rig.Bus.PublishImmediate(new UnitGetupStartedEvent(Hero, getup, 0));

            // 复现：反应序列 = 受击后仰 → 躺姿 → 起身（03 第 4.5 节映射表）。
            Assert.Equal(new[] { "hit.knockback", "hit.knockdown", "hit.getup" }, rig.Requests);
            Assert.Equal(AnimState.Hit, rig.Machine.GetState(Hero));

            // 不变量：保持到逻辑层硬直（三段之和）结束的那个 tick 才回到运动态；中途播放器的完成回调被忽略。
            rig.Machine.NotifyTransientStateFinished(Hero, AnimState.Hit);
            Assert.Equal(AnimState.Hit, rig.Machine.GetState(Hero));
            rig.Tick(getup - 1);
            Assert.Equal(AnimState.Hit, rig.Machine.GetState(Hero));
            rig.Bus.PublishImmediate(new UnitGetupFinishedEvent(Hero));
            rig.Tick();
            Assert.Equal(AnimState.Move, rig.Machine.GetState(Hero));
            Assert.Null(rig.Machine.GetHitPoseSub(Hero));
        }

        [Fact]
        public void Knockdown_WithZeroStunSegment_EntersTheLyingPoseDirectly_WithoutDuplicateRetrigger()
        {
            var rig = new Rig();
            rig.React(Hero, HitReaction.Knockdown, 0, 5, 0);
            rig.Bus.PublishImmediate(new UnitKnockedDownEvent(Hero, 5, 0));
            Assert.Equal(new[] { "hit.knockdown" }, rig.Requests);
        }

        [Fact]
        public void ReactionNone_AndBlockedHitsWithoutReaction_PlayNoHitAnimation_ButDamageStillDoesNotDriveHit()
        {
            // 复现：霸体/反应为 none 的命中——伤害落地、命中确认都发生，但没有 reaction_applied。
            var rig = new Rig();
            rig.Damage(Hero);
            rig.Bus.PublishImmediate(new CombatHitConfirmedEvent(
                new Id("atk.3"), 0, Foe, Hero, null, HitResult.Hit, 5, 0.1, false, false, Vec2.Zero, Vec2.Zero, Vec2.Zero, "light", 0, 0,
                HitReaction.None));
            Assert.Empty(rig.Requests);
            Assert.Equal(AnimState.Idle, rig.Machine.GetState(Hero));

            // 对照：未接反应查询的状态机（此前行为）由伤害落地驱动 Hit。
            var legacy = new Rig(reactionDriven: false);
            legacy.Damage(Hero);
            Assert.Equal(new[] { "hit" }, legacy.Requests);
            Assert.False(legacy.Machine.IsReactionDriven);
        }

        [Fact]
        public void Flinch_DoesNotInterruptAnActionOrAHold_ButStrongerReactionDoes()
        {
            var rig = new Rig();
            rig.Bus.PublishImmediate(new SkillCastStartEvent(Hero, new Id("skill.slash"), 0.0));
            Assert.Equal(AnimState.Attack, rig.Machine.GetState(Hero));
            rig.React(Hero, HitReaction.Flinch, 0, 0, 0);
            Assert.Equal(AnimState.Attack, rig.Machine.GetState(Hero)); // 不打断动作

            rig.React(Hero, HitReaction.Stagger, 8, 0, 0);
            Assert.Equal(AnimState.Hit, rig.Machine.GetState(Hero));
            Assert.Equal("heavy", rig.Machine.GetHitPoseSub(Hero));

            rig.React(Hero, HitReaction.Flinch, 0, 0, 0); // 硬直保持中的轻抖动不换姿势
            Assert.Equal("heavy", rig.Machine.GetHitPoseSub(Hero));

            // 较弱的有硬直反应打在更长的硬直上：记录没被刷新（剩余更长），不换姿势。
            rig.Bus.PublishImmediate(new CombatReactionAppliedEvent(Hero, HitReaction.StaggerLight, Foe, new Id("atk.4"), 3, 3, 0, 0));
            Assert.Equal("heavy", rig.Machine.GetHitPoseSub(Hero));
        }

        [Fact]
        public void Block_ShakesOnceWithoutHold_AndTheFollowingFlinchDoesNotReplay()
        {
            var rig = new Rig();
            rig.Block(Hero);
            rig.React(Hero, HitReaction.Flinch, 0, 0, 0);
            Assert.Equal(new[] { "hit.block" }, rig.Requests);

            // 单次抖动：播放完成即回落（没有硬直保持）。
            rig.Machine.NotifyTransientStateFinished(Hero, AnimState.Hit);
            Assert.Equal(AnimState.Idle, rig.Machine.GetState(Hero));
        }

        [Fact]
        public void Death_WinsOverAHold_AndClearsIt()
        {
            var rig = new Rig();
            rig.React(Hero, HitReaction.Knockback, 10, 0, 0);
            rig.Bus.PublishImmediate(new UnitDiedEvent(Hero, (Id?)Foe));
            Assert.Equal(AnimState.Death, rig.Machine.GetState(Hero));
            Assert.Null(rig.Machine.GetHitPoseSub(Hero));
            rig.Tick(12);
            Assert.Equal(AnimState.Death, rig.Machine.GetState(Hero));
        }

        [Fact]
        public void HitstopBeforeStagger_HoldsUntilTheLogicLayerStaggerEnds_NotForAFixedPresentationTime()
        {
            // 受击方顿帧期间硬直尚未起算：查询里"剩余 = 整段时长"，逻辑层的硬直时钟不走，表现层保持同样不走。
            var rig = new Rig();
            rig.React(Hero, HitReaction.Stagger, 4, 0, 0);
            rig.Reactions.Staggered.Remove(Hero); // 顿帧期：尚未处于硬直，但剩余仍是整段
            rig.Bus.PublishImmediate(new SimTickFinishedEvent(100));
            Assert.Equal(AnimState.Hit, rig.Machine.GetState(Hero));
            rig.Reactions.Staggered.Add(Hero);
            rig.Tick(3);
            Assert.Equal(AnimState.Hit, rig.Machine.GetState(Hero));
            rig.Tick();
            Assert.Equal(AnimState.Idle, rig.Machine.GetState(Hero));
        }

        [Fact]
        public void PoseRequestWithSub_ChainsSubFirstThenFallsBackToBaseHit()
        {
            var ctx = new PoseContext(LocomotionGait.Run, "2h", "wounded");
            var request = ctx.ToRequest("hit", inCombat: true, PoseKeys.HitSubHeavy);
            Assert.Equal(
                new[] { "hit.heavy.combat.2h.wounded", "hit.heavy.combat.2h", "hit.heavy.combat", "hit.heavy", "hit.combat.2h.wounded", "hit.combat.2h", "hit.combat", "hit" },
                request.Chain());
            Assert.Equal("hit", PoseRequest.Base("hit").FullKey());
            // 空中：light/heavy/knockback 改请求 hit.air；躺姿与格挡抖动不随空中改键。
            var air = new PoseContext(LocomotionGait.Idle, null, null, AirPhase.Fall);
            Assert.True(air.TryGetAirHitRequest(false, PoseKeys.HitSubKnockback, out var airReq));
            Assert.Equal(new[] { "hit.air", "hit.launch", "hit" }, airReq.Chain());
            Assert.False(air.TryGetAirHitRequest(false, PoseKeys.HitSubKnockdown, out _));
            Assert.False(air.TryGetAirHitRequest(false, PoseKeys.HitSubBlock, out _));
            // 无子键的请求与此前逐位一致。
            Assert.Equal(ctx.ToRequest("hit", true).Chain(), ctx.ToRequest("hit", true, null).Chain());
        }
    }
}
