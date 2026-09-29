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
    /// <summary>
    /// ADR-0111（消费方反馈第六十一批）：<see cref="AnimStateMachine"/> 战斗姿态记账的不变量用例——
    /// 姿态是与 <see cref="AnimState"/> 正交的布尔维度，本类型只维护它，不切状态、不选剪辑。
    /// 运行时可观测量：<see cref="AnimStateMachine.IsInCombatStance"/> 的读数、
    /// <see cref="AnimStateMachine.CombatStanceChanged"/> 的触发序列、<see cref="AnimStateMachine.GetState"/>
    /// 与 <see cref="AnimStateMachine.StateChanged"/> 是否被姿态变化影响。
    /// </summary>
    public class AnimStateMachineCombatStanceTests
    {
        private static readonly Id Unit = new Id("unit.stance_hero");
        private static readonly Id Other = new Id("unit.stance_other");

        private static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        /// <summary>订阅 + 事件去重 + 与状态正交：进/出战只切姿态，不动 GetState，不触发 StateChanged；
        /// 姿态没变（重复 entered / 重复 left）不触发 CombatStanceChanged；只影响事件里点名的那个单位。</summary>
        [Fact]
        public void CombatEnteredLeft_TogglesStanceOnly_NoDuplicateEvents_OtherUnitsUntouched()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var stanceEvents = new List<(Id Id, bool InCombat)>();
            var stateChanges = 0;
            machine.CombatStanceChanged += (id, inCombat) => stanceEvents.Add((id, inCombat));
            machine.StateChanged += (id, from, to) => stateChanges++;

            Assert.False(machine.IsInCombatStance(Unit));

            bus.PublishImmediate(new CombatEnteredEvent(Unit));
            Assert.True(machine.IsInCombatStance(Unit));
            Assert.False(machine.IsInCombatStance(Other));

            bus.PublishImmediate(new CombatEnteredEvent(Unit));
            bus.PublishImmediate(new CombatLeftEvent(Unit));
            bus.PublishImmediate(new CombatLeftEvent(Unit));
            Assert.False(machine.IsInCombatStance(Unit));

            Assert.Equal(new[] { (Unit, true), (Unit, false) }, stanceEvents);
            Assert.Equal(0, stateChanges);
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
        }

        /// <summary>切换时机：姿态变化不打断瞬态（Attack 期间进战，状态仍是 Attack、StateChanged 不触发），
        /// 瞬态回落到运动态时读到的是<b>当时</b>的姿态；Death 终态不受姿态变化影响。</summary>
        [Fact]
        public void StanceChange_DoesNotInterruptTransientState_RevertSeesCurrentStance_DeathUnaffected()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var stanceAtStateChange = new List<(AnimState To, bool InCombat)>();
            machine.StateChanged += (id, from, to) => stanceAtStateChange.Add((to, machine.IsInCombatStance(id)));

            bus.PublishImmediate(new SkillCastStartEvent(Unit, new Id("skill.auto_attack"), 0.0));
            Assert.Equal(AnimState.Attack, machine.GetState(Unit));

            bus.PublishImmediate(new CombatEnteredEvent(Unit));
            Assert.Equal(AnimState.Attack, machine.GetState(Unit));
            Assert.Equal(new[] { (AnimState.Attack, false) }, stanceAtStateChange);

            machine.NotifyTransientStateFinished(Unit, AnimState.Attack);
            Assert.Equal(AnimState.Idle, machine.GetState(Unit));
            Assert.Equal((AnimState.Idle, true), stanceAtStateChange[stanceAtStateChange.Count - 1]);

            bus.PublishImmediate(new UnitDiedEvent(Unit, new Id("unit.attacker")));
            Assert.Equal(AnimState.Death, machine.GetState(Unit));
            bus.PublishImmediate(new CombatLeftEvent(Unit));
            Assert.Equal(AnimState.Death, machine.GetState(Unit));
            Assert.True(machine.IsTerminal(Unit));
            Assert.False(machine.IsInCombatStance(Unit));
        }

        /// <summary>初始姿态探针：首次跟踪时只调用一次；视图晚于进战事件创建（Track / 其它事件先跟踪）时
        /// 初始姿态取探针值且不触发变化事件；由战斗事件首次创建记录的实体不问探针（迁移前姿态已知，
        /// 探针此刻的值可能已等于事件值，用它当基线会把这次迁移误判为没变而吞掉）；Forget 清除姿态；
        /// 旧构造（无探针）初始一律非战斗。</summary>
        [Fact]
        public void CombatProbe_DeterminesInitialStanceOnce_EventCreatedEntriesSkipProbe_ForgetClears()
        {
            var bus = CreateBus();
            var alreadyInCombat = new HashSet<Id> { Unit, Other };
            var probeCalls = new List<Id>();
            var machine = new AnimStateMachine(bus, id => { probeCalls.Add(id); return alreadyInCombat.Contains(id); });
            var stanceEvents = new List<(Id Id, bool InCombat)>();
            machine.CombatStanceChanged += (id, inCombat) => stanceEvents.Add((id, inCombat));

            // 未跟踪：读数恒 false，不触发探针。
            Assert.False(machine.IsInCombatStance(Unit));
            Assert.Empty(probeCalls);

            // Track：首次跟踪调用一次探针，初始姿态 = 探针值，且这是"初始值"不是"变化"。
            machine.Track(Unit);
            machine.Track(Unit);
            Assert.Equal(new[] { Unit }, probeCalls);
            Assert.True(machine.IsInCombatStance(Unit));
            Assert.Empty(stanceEvents);

            // 其它事件先跟踪同样走探针（一次）。
            bus.PublishImmediate(new UnitStateChangedEvent(Other, "Idle", "Walk"));
            Assert.Equal(new[] { Unit, Other }, probeCalls);
            Assert.True(machine.IsInCombatStance(Other));

            // 已跟踪实体的 combat.left 是真实迁移：姿态 true -> false，触发一次。
            bus.PublishImmediate(new CombatLeftEvent(Unit));
            Assert.False(machine.IsInCombatStance(Unit));
            Assert.Equal(new[] { (Unit, false) }, stanceEvents);

            // Forget 清除姿态；再次 Track 重新问探针（此刻探针仍说在战中）。
            machine.Forget(Unit);
            Assert.False(machine.IsInCombatStance(Unit));
            machine.Track(Unit);
            Assert.True(machine.IsInCombatStance(Unit));
            Assert.Equal(3, probeCalls.Count);

            // 战斗事件首次创建记录：不问探针；探针此刻已等于事件值（进战事件晚于状态写入派发）仍要触发变化。
            var fresh = new Id("unit.stance_fresh");
            alreadyInCombat.Add(fresh);
            bus.PublishImmediate(new CombatEnteredEvent(fresh));
            Assert.Equal(3, probeCalls.Count);
            Assert.True(machine.IsInCombatStance(fresh));
            Assert.Equal((fresh, true), stanceEvents[stanceEvents.Count - 1]);

            // 旧构造（无探针）：初始姿态恒非战斗。
            var legacy = new AnimStateMachine(bus);
            legacy.Track(Unit);
            Assert.False(legacy.IsInCombatStance(Unit));
        }
    }
}
