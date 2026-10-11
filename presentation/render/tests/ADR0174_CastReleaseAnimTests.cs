using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// ADR-0174：施法三段动作——读条期间保持施法姿势（被受击顶掉后回到施法姿势）、读条正常完成进入释放（Attack 态带技能 id）、
    /// 被打断/失败直接回落；瞬发技能直接进 Attack 并带技能 id。释放动作查询（<see cref="AnimStateMachine.ReleaseAnimProbe"/>）缺省为空时全部行为不变。
    /// </summary>
    public class ADR0174_CastReleaseAnimTests
    {
        private static readonly Id Unit = new Id("unit.crt_hero");
        private static readonly Id Attacker = new Id("unit.crt_foe");
        private static readonly Id Bolt = new Id("skill.crt_bolt");       // 声明了释放动作的读条技能
        private static readonly Id Plain = new Id("skill.crt_plain");     // 没声明释放动作的读条技能
        private static readonly Id Blink = new Id("skill.crt_blink");     // 瞬发
        private static readonly Id School = new Id("school.crt");

        private sealed class Rig
        {
            public IEventBus Bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            public AnimStateMachine Machine = null!;
            public List<(AnimState From, AnimState To, Id? Skill)> Changes = new List<(AnimState, AnimState, Id?)>();

            public Rig(bool withProbe)
            {
                Machine = new AnimStateMachine(Bus);
                if (withProbe)
                {
                    Machine.ReleaseAnimProbe = (caster, skill) => skill.Equals(Bolt) || skill.Equals(Blink);
                }

                Machine.StateChangedWithSkill += (id, from, to, skill) => Changes.Add((from, to, skill));
            }

            public AnimState State => Machine.GetState(Unit);

            public void Start(Id skill, double castTime) => Bus.PublishImmediate(new SkillCastStartEvent(Unit, skill, castTime));

            public void Success(Id skill, bool instant = false) =>
                Bus.PublishImmediate(new SkillCastSuccessEvent(Unit, skill, Array.Empty<Id>(), instant, instant ? 0 : 1.5));

            public void Interrupted(Id skill) => Bus.PublishImmediate(new SkillCastInterruptedEvent(Unit, skill, Attacker));

            public void Failed(Id skill) => Bus.PublishImmediate(new SkillCastFailedEvent(Unit, skill, CastFailureReason.Silenced));

            public void Hit() => Bus.PublishImmediate(new CombatDamageDealtEvent(Attacker, Unit, School, 5.0, isCrit: false, HitResult.Hit));
        }

        [Fact]
        public void CastCompletes_WithReleaseDeclared_EntersAttackWithSkill_UntilClipFinishes()
        {
            var r = new Rig(withProbe: true);
            r.Start(Bolt, 1.5);
            Assert.Equal(AnimState.Cast, r.State);

            r.Success(Bolt);

            Assert.Equal(AnimState.Attack, r.State);
            Assert.Equal((AnimState.Cast, AnimState.Attack, (Id?)Bolt), r.Changes[^1]);

            r.Machine.NotifyTransientStateFinished(Unit, AnimState.Attack);
            Assert.Equal(AnimState.Idle, r.State);
        }

        [Fact]
        public void CastCompletes_WithoutProbe_OrReleaseNotDeclared_RevertsImmediately_AsBefore()
        {
            var noProbe = new Rig(withProbe: false);
            noProbe.Start(Bolt, 1.5);
            noProbe.Success(Bolt);
            Assert.Equal(AnimState.Idle, noProbe.State);

            var probe = new Rig(withProbe: true);
            probe.Start(Plain, 1.5);
            probe.Success(Plain);
            Assert.Equal(AnimState.Idle, probe.State);
        }

        [Fact]
        public void CastInterruptedOrFailed_RevertsImmediately_NoRelease()
        {
            var r = new Rig(withProbe: true);
            r.Start(Bolt, 1.5);
            r.Interrupted(Bolt);
            Assert.Equal(AnimState.Idle, r.State);
            Assert.DoesNotContain(r.Changes, c => c.To == AnimState.Attack);

            r.Start(Bolt, 1.5);
            r.Failed(Bolt);
            Assert.Equal(AnimState.Idle, r.State);
            Assert.DoesNotContain(r.Changes, c => c.To == AnimState.Attack);
        }

        [Fact]
        public void InstantSkill_EntersAttackCarryingSkillId_SoTheResolverCanPickItsReleaseClip()
        {
            var r = new Rig(withProbe: true);
            r.Start(Blink, 0.0);
            r.Success(Blink, instant: true);

            Assert.Equal(AnimState.Attack, r.State);
            Assert.Equal((AnimState.Idle, AnimState.Attack, (Id?)Blink), r.Changes[0]);
            Assert.Single(r.Changes); // 瞬发的收尾事件不再触发第二次切换（N19）
        }

        [Fact]
        public void HitDuringCast_ReturnsToCastWhenHitFinishes_WhileTheCastContinues()
        {
            var r = new Rig(withProbe: true);
            r.Start(Bolt, 1.5);
            r.Hit();
            Assert.Equal(AnimState.Hit, r.State);

            r.Machine.NotifyTransientStateFinished(Unit, AnimState.Hit);

            Assert.Equal(AnimState.Cast, r.State);
            Assert.Equal((AnimState.Hit, AnimState.Cast, (Id?)Bolt), r.Changes[^1]);

            r.Success(Bolt);
            Assert.Equal(AnimState.Attack, r.State);
        }

        [Fact]
        public void HitDuringCast_ThenCastEnds_HitFinishesToLocomotion_NotBackToCast()
        {
            foreach (var end in new Action<Rig>[] { r => r.Interrupted(Bolt), r => r.Success(Bolt), r => r.Failed(Bolt) })
            {
                var r = new Rig(withProbe: true);
                r.Start(Bolt, 1.5);
                r.Hit();
                end(r);
                Assert.Equal(AnimState.Hit, r.State); // 受击姿势不被收尾事件打断
                r.Machine.NotifyTransientStateFinished(Unit, AnimState.Hit);
                Assert.Equal(AnimState.Idle, r.State);
            }
        }

        private static readonly Id Instance1 = new Id("cast.crt_1");
        private static readonly Id Instance2 = new Id("cast.crt_2");

        [Fact]
        public void AnotherRequestRefusedDuringCast_DoesNotEndTheRunningCastPose_ThenReleaseStillPlays()
        {
            // 读条期间另一个施法请求被拒（Busy 等，校验阶段失败不带实例 id；敌人 AI 每个决策间隔都会再请求一次）：读条姿势继续。
            var r = new Rig(withProbe: true);
            r.Bus.PublishImmediate(new SkillCastStartEvent(Unit, Bolt, 1.5, Instance1));
            Assert.Equal(AnimState.Cast, r.State);

            r.Bus.PublishImmediate(new SkillCastFailedEvent(Unit, Bolt, CastFailureReason.Busy, castInstanceId: null));
            Assert.Equal(AnimState.Cast, r.State);
            r.Bus.PublishImmediate(new SkillCastFailedEvent(Unit, Plain, CastFailureReason.OnCooldown, Instance2));
            Assert.Equal(AnimState.Cast, r.State);

            r.Hit();
            r.Machine.NotifyTransientStateFinished(Unit, AnimState.Hit);
            Assert.Equal(AnimState.Cast, r.State); // 被忽略的失败没有清掉读条记录，受击后仍回到施法姿势

            r.Bus.PublishImmediate(new SkillCastSuccessEvent(Unit, Bolt, Array.Empty<Id>(), false, 1.5, Instance1));
            Assert.Equal(AnimState.Attack, r.State);
        }

        [Fact]
        public void RunningCastItselfFails_SameInstanceId_RevertsAsBefore()
        {
            var r = new Rig(withProbe: true);
            r.Bus.PublishImmediate(new SkillCastStartEvent(Unit, Bolt, 1.5, Instance1));
            r.Bus.PublishImmediate(new SkillCastFailedEvent(Unit, Bolt, CastFailureReason.Interrupted, Instance1));
            Assert.Equal(AnimState.Idle, r.State);
        }

        [Fact]
        public void Invariant_AfterAnyCastEnd_NeverStaysInCast_AndNeverResumesACastThatEnded()
        {
            // 固定种子的随机事件序列：每个收尾事件之后，把瞬态播完（通知结束），状态一定回到运动态；
            // 只要状态是 Cast，就必须有一次尚未收尾的读条。
            var rng = new Random(20261009);
            var r = new Rig(withProbe: true);
            var casting = false;
            for (var step = 0; step < 600; step++)
            {
                switch (rng.Next(7))
                {
                    case 0: if (!casting) { r.Start(rng.Next(2) == 0 ? Bolt : Plain, 1.5); casting = true; } break;
                    case 1: if (casting) { r.Success(Bolt); casting = false; } break;
                    case 2: if (casting) { r.Interrupted(Bolt); casting = false; } break;
                    case 3: if (casting) { r.Failed(Bolt); casting = false; } break;
                    case 4: r.Hit(); break;
                    case 5: r.Machine.NotifyTransientStateFinished(Unit, r.State); break;
                    default: break;
                }

                if (r.State == AnimState.Cast)
                {
                    Assert.True(casting, $"step {step}: 读条已收尾却仍停在 Cast");
                }
            }

            if (casting) { r.Interrupted(Bolt); }
            for (var i = 0; i < 4; i++) { r.Machine.NotifyTransientStateFinished(Unit, r.State); }
            Assert.Equal(AnimState.Idle, r.State);
        }
    }
}
