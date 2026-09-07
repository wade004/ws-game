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

        // ------------------------------------------------------------------
        // ADR-0017 决策 c：StateChangedWithSkill
        // ------------------------------------------------------------------

        [Fact]
        public void SkillCastStart_RaisesStateChangedWithSkill_CarryingSkillId()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var received = new List<(AnimState From, AnimState To, Id? SkillId)>();
            machine.StateChangedWithSkill += (id, from, to, skillId) => received.Add((from, to, skillId));

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.fireball"), 1.5));

            Assert.Contains(received, t => t.From == AnimState.Idle && t.To == AnimState.Cast && t.SkillId == new Id("skill.fireball"));
        }

        [Fact]
        public void StateChangedWithSkill_FiresAlongsideStateChanged_ForSameTransition()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var plainCount = 0;
            var withSkillCount = 0;
            machine.StateChanged += (_, _, _) => plainCount++;
            machine.StateChangedWithSkill += (_, _, _, _) => withSkillCount++;

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));

            Assert.Equal(1, plainCount);
            Assert.Equal(1, withSkillCount);
        }

        [Fact]
        public void NonSkillTransitions_StateChangedWithSkill_CarriesNullSkillId()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            Id? receivedSkillId = new Id("sentinel.should_be_overwritten");
            machine.StateChangedWithSkill += (_, _, _, skillId) => receivedSkillId = skillId;

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Idle", "Run"));

            Assert.Null(receivedSkillId);
        }

        [Fact]
        public void ExistingStateChangedSubscribers_KeepWorking_UnawareOfNewEvent()
        {
            // 兼容性回归：只订阅旧的三元组事件（同 W3b AnimClipResolver 现有接线），不应受影响。
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var transitions = new List<(AnimState From, AnimState To)>();
            machine.StateChanged += (id, from, to) => transitions.Add((from, to));

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.fireball"), 1.5));

            Assert.Contains((AnimState.Idle, AnimState.Cast), transitions);
        }

        // ------------------------------------------------------------------
        // H5b 根治（游戏侧复核发现 2）：StateRetriggered——同状态重入的瞬态态重播语义。
        // ------------------------------------------------------------------

        /// <summary>连续两次普攻，第二次在第一次 Attack 动画播完前到达（第一次没有调用
        /// NotifyTransientStateFinished）：第二次不应静默丢弃，应触发 StateRetriggered，且不触发
        /// StateChanged/StateChangedWithSkill（状态数值确实没变）。</summary>
        [Fact]
        public void SkillCastStart_SameStateReentry_Attack_RaisesStateRetriggered_NotStateChanged()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var changedCount = 0;
            var retriggered = new List<(AnimState State, Id? SkillId)>();
            machine.StateChanged += (_, _, _) => changedCount++;
            machine.StateRetriggered += (id, state, skillId) => retriggered.Add((state, skillId));

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));
            Assert.Equal(AnimState.Attack, machine.GetState(Unit));
            Assert.Equal(1, changedCount);
            Assert.Empty(retriggered);

            // 第二次普攻在第一次 Attack 动画播完之前到达：状态数值仍是 Attack，不产生 StateChanged，
            // 但应该重触发一次，携带这次触发的技能 id。
            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));
            Assert.Equal(AnimState.Attack, machine.GetState(Unit));
            Assert.Equal(1, changedCount);
            Assert.Single(retriggered);
            Assert.Equal((AnimState.Attack, (Id?)new Id("skill.auto_attack")), retriggered[0]);
        }

        /// <summary>连续两次受击（同一实体在第一次 Hit 动画播完前又挨了一下）同样应该重触发。</summary>
        [Fact]
        public void CombatDamageDealt_SameStateReentry_Hit_RaisesStateRetriggered()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var retriggerCount = 0;
            machine.StateRetriggered += (_, _, _) => retriggerCount++;

            bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.attacker"), Unit, new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit));
            Assert.Equal(AnimState.Hit, machine.GetState(Unit));
            Assert.Equal(0, retriggerCount);

            bus.PublishImmediate(new CombatDamageDealtEvent(
                new Id("unit.attacker"), Unit, new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit));
            Assert.Equal(AnimState.Hit, machine.GetState(Unit));
            Assert.Equal(1, retriggerCount);
        }

        /// <summary>Idle/Move 等持续态保持幂等：同状态重入不应重触发（09 第 4.2 节勘误"持续态保持
        /// 幂等"）。运动态本就不经 TryEnter（见 RequestOverride 判断记录），本用例断言
        /// OnUnitStateChanged 路径确实不会意外触发 StateRetriggered。</summary>
        [Fact]
        public void UnitStateChanged_SameLocomotionReentry_DoesNotRaiseStateRetriggered()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var retriggerCount = 0;
            machine.StateRetriggered += (_, _, _) => retriggerCount++;

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Idle", "Walk"));
            Assert.Equal(AnimState.Move, machine.GetState(Unit));

            bus.PublishImmediate(new UnitStateChangedEvent(Unit, "Walk", "Run"));
            Assert.Equal(AnimState.Move, machine.GetState(Unit));

            Assert.Equal(0, retriggerCount);
        }

        /// <summary>Death 终态：TryEnter 更早的"当前已是 Death"检查直接返回，不应触发
        /// StateRetriggered（终态之后全部事件都应被忽略，含重触发）。</summary>
        [Fact]
        public void UnitDied_AlreadyDead_FurtherDeathEvents_DoNotRaiseStateRetriggered()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var retriggerCount = 0;
            machine.StateRetriggered += (_, _, _) => retriggerCount++;

            bus.PublishImmediate(new UnitDiedEvent(Unit, new Id("unit.attacker")));
            Assert.Equal(AnimState.Death, machine.GetState(Unit));

            bus.PublishImmediate(new UnitDiedEvent(Unit, new Id("unit.attacker")));
            Assert.Equal(AnimState.Death, machine.GetState(Unit));
            Assert.Equal(0, retriggerCount);
        }

        [Fact]
        public void Dispose_UnsubscribesStateRetriggered_SubsequentReentryDoesNotInvokeCallback()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var retriggerCount = 0;
            machine.StateRetriggered += (_, _, _) => retriggerCount++;

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));
            machine.Dispose();

            // Dispose 已退订全部事件订阅，后续发布不应再驱动状态机（同 Dispose_UnsubscribesAll 既有
            // 用例惯例），自然也不会有 StateRetriggered。
            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));
            Assert.Equal(0, retriggerCount);
        }
    }
}
