using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary><see cref="AnimStateMachine"/> 逐条转移与优先级用例（见任务书"测试覆盖每条转移与
    /// 优先级"）。</summary>
    public class AnimStateMachineTests
    {
        private static readonly Id Unit = new Id("unit.smoke_hero");

        private static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void GetState_UntrackedEntity_DefaultsToIdle()
        {
            var machine = new AnimStateMachine(CreateBus());

            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }

        [Fact]
        public void UnitStateChanged_WalkRunForced_EntersMove_IdleReturnsToIdle()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Idle", "Walk"));
            Assert.Equal(AnimState.Move, machine.GetState(Unit));

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Walk", "Run"));
            Assert.Equal(AnimState.Move, machine.GetState(Unit));

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Run", "Forced"));
            Assert.Equal(AnimState.Move, machine.GetState(Unit));

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Forced", "Idle"));
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }

        [Fact]
        public void UnitStateChanged_AiBehaviorValues_AreIgnored_NotMisreadAsLocomotion()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            // AI 行为状态机复用同一事件 key，取值全小写（06 第 5 节），不应被本状态机误判为移动模式。
            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "idle", "chase"));

            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }

        /// <summary>N19 复现与根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// 瞬发（<c>castTime == 0</c>，覆盖普攻与瞬发技能）的 <c>skill.cast_start</c>/
        /// <c>skill.cast_success</c> 在逻辑层同一次派发批次内背靠背发出——旧实现收到
        /// <c>skill.cast_success</c> 立即把 Attack 回落到 Idle，Attack 播放形态在同一帧内被切回，
        /// 攻击动画剪辑根本没有机会真正播出。修复后：<c>skill.cast_success</c> 本身不再驱动 Attack
        /// 回落，只有动画播放器完成回调驱动的 <see cref="AnimStateMachine.NotifyTransientStateFinished"/>
        /// 才能让 Attack 回落——保证瞬发攻击至少完整展示一遍 Attack 播放形态。</summary>
        [Fact]
        public void SkillCastStart_ZeroCastTime_EntersAttack_DoesNotRevertOnSuccess_OnlyOnNotifyTransientStateFinished()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var transitions = new List<(AnimState From, AnimState To)>();
            machine.StateChanged += (id, from, to) => transitions.Add((from, to));

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));
            Assert.Equal(AnimState.Attack, machine.GetState(Unit));

            // N19 核心断言：skill.cast_success（瞬发的逻辑收尾信号）本身不应该让 Attack 提前回落——
            // 动画播放器（IFrameAnimPlayer.OnComplete）此刻可能才刚刚开始播放 Attack 剪辑。
            bus.PublishImmediate(new SkillCastSuccessEvent(Unit, new Id("skill.auto_attack"), Array.Empty<Id>()));
            Assert.Equal(AnimState.Attack, machine.GetState(Unit));

            // 动画播放器（同 Adapter.Unity.Presentation.UnityViewFactory.AttachDefaultAnimation 的
            // IFrameAnimPlayer.OnComplete 接线）在 Attack 剪辑真正播完后才应该调用本方法，Attack 才
            // 真正回落。
            machine.NotifyTransientStateFinished(Unit, AnimState.Attack);
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));

            Assert.Contains((AnimState.Idle, AnimState.Attack), transitions);
            Assert.Contains((AnimState.Attack, AnimState.Idle), transitions);
        }

        [Fact]
        public void SkillCastStart_PositiveCastTime_EntersCast_RevertsOnFailedOrInterrupted()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.fireball"), 1.5));
            Assert.Equal(AnimState.Cast, machine.GetState(Unit));

            bus.PublishImmediate(new SkillCastFailedEvent(Unit, new Id("skill.fireball"), CastFailureReason.Silenced));
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.fireball"), 1.5));
            Assert.Equal(AnimState.Cast, machine.GetState(Unit));

            bus.PublishImmediate(new SkillCastInterruptedEvent(Unit, new Id("skill.fireball"), new Id("unit.attacker")));
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }

        [Fact]
        public void CombatDamageDealt_MatchingTarget_EntersHit_RevertsOnNotify()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.attacker"), Unit, new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit));
            Assert.Equal(AnimState.Hit, machine.GetState(Unit));

            machine.NotifyTransientStateFinished(Unit, AnimState.Hit);
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }

        [Fact]
        public void CombatDamageDealt_NonMatchingTarget_DoesNotAffectThisEntity()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.attacker"), new Id("unit.other_target"), new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit));

            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }

        [Fact]
        public void UnitDied_EntersDeath_TerminalIgnoresFurtherEvents()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            bus.PublishImmediate(new UnitDiedEvent(Unit, new Id("unit.attacker")));
            Assert.Equal(AnimState.Death, machine.GetState(Unit));
            Assert.True(machine.IsTerminal(Unit));

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Idle", "Run"));
            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));
            bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.attacker"), Unit, new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit));
            machine.NotifyTransientStateFinished(Unit, AnimState.Death);

            Assert.Equal(AnimState.Death, machine.GetState(Unit));
        }

        [Fact]
        public void Priority_HitOverridesCast_ButCastEndSignal_NoLongerAffectsState()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.fireball"), 1.5));
            Assert.Equal(AnimState.Cast, machine.GetState(Unit));

            // 受击（优先级 3）盖过施法（优先级 2）。
            bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.attacker"), Unit, new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit));
            Assert.Equal(AnimState.Hit, machine.GetState(Unit));

            // 过期的施法收尾信号不应把状态从 Hit 拉回 Idle（当前状态已不是 Cast）。
            bus.PublishImmediate(new SkillCastInterruptedEvent(Unit, new Id("skill.fireball"), new Id("unit.attacker")));
            Assert.Equal(AnimState.Hit, machine.GetState(Unit));

            machine.NotifyTransientStateFinished(Unit, AnimState.Hit);
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }

        [Fact]
        public void Priority_AttackDoesNotOverrideHit_ButOverridesIdle()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.attacker"), Unit, new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit));
            Assert.Equal(AnimState.Hit, machine.GetState(Unit));

            // 攻击（优先级 2）不足以盖过受击（优先级 3）。
            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));
            Assert.Equal(AnimState.Hit, machine.GetState(Unit));
        }

        [Fact]
        public void LocomotionUpdate_DuringTransientState_IsDeferred_AppliesAfterRevert()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));
            Assert.Equal(AnimState.Attack, machine.GetState(Unit));

            // 攻击期间收到移动模式变化：不应立即切走，但要记住最新运动态。
            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Idle", "Run"));
            Assert.Equal(AnimState.Attack, machine.GetState(Unit));

            // N19 根治：skill.cast_success 本身不再驱动 Attack 回落（见
            // SkillCastStart_ZeroCastTime_EntersAttack_DoesNotRevertOnSuccess_OnlyOnNotifyTransientStateFinished），
            // 真正让 Attack 回落、进而让"推迟的运动态"生效的是动画播放完成回调。
            bus.PublishImmediate(new SkillCastSuccessEvent(Unit, new Id("skill.auto_attack"), Array.Empty<Id>()));
            Assert.Equal(AnimState.Attack, machine.GetState(Unit));

            machine.NotifyTransientStateFinished(Unit, AnimState.Attack);
            Assert.Equal(AnimState.Move, machine.GetState(Unit));
        }

        [Fact]
        public void RequestOverride_Jump_EntersJump_RevertsOnNotify_RejectsLocomotionStates()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            machine.RequestOverride(Unit, AnimState.Jump);
            Assert.Equal(AnimState.Jump, machine.GetState(Unit));

            machine.NotifyTransientStateFinished(Unit, AnimState.Jump);
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));

            Assert.Throws<ArgumentException>(() => machine.RequestOverride(Unit, AnimState.Move));
        }

        [Fact]
        public void Forget_RemovesTracking_GetStateFallsBackToIdle()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Idle", "Run"));
            Assert.Equal(AnimState.Move, machine.GetState(Unit));

            machine.Forget(Unit);
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }

        [Fact]
        public void Dispose_UnsubscribesAll_SubsequentEventsDoNotChangeState()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            machine.Dispose();

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Idle", "Run"));

            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }
    }
}
