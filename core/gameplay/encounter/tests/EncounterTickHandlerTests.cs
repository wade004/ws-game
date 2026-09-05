using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Gameplay.Encounter;
using Xunit;

namespace Tests.Gameplay.Encounter
{
    /// <summary>
    /// 收边任务补齐（缺口 (b)：<c>EncounterTickHandler</c> 此前不区分离散/连续两种模式的求值时机，
    /// 08 第 4.3 节"阶段切换在离散模式下于回合结束时生效...连续模式下按 tick 频率求值，行为不变"）：
    /// 直接单元测试 <see cref="EncounterTickHandler"/> 本身"谁来调用、什么时候调用"这一层职责，不
    /// 经真实 <see cref="Core.Gameplay.Encounter.EncounterHost"/>（判定逻辑已由
    /// <c>EncounterHostTests</c> 覆盖）——用一个只计数 <see cref="IEncounterHost.Evaluate"/> 调用次数
    /// 的假实现，分别验证连续步、离散 Act 步、<c>sim.turn_ended</c>/<c>sim.round_ended</c> 事件三类
    /// 输入下的求值次数。
    /// </summary>
    public sealed class EncounterTickHandlerTests
    {
        private static readonly Id InstanceA = new Id("encounter.inst_tick_a");

        /// <summary>只记录 <see cref="Evaluate"/> 调用次数与顺序的最小假实现；<see cref="ActiveInstanceIds"/>
        /// 恒返回同一个活跃实例，<see cref="Start"/>/<see cref="Abort"/>/<see cref="GetState"/> 本类型
        /// 测试用不到，抛异常暴露误用。</summary>
        private sealed class CountingEncounterHost : Core.Gameplay.Encounter.IEncounterHost
        {
            public int EvaluateCallCount { get; private set; }

            public Id Start(Id encounterId, Id mapId, Id playerUnitId) => throw new NotImplementedException();

            public void Evaluate(Id instanceId) => EvaluateCallCount++;

            public void Abort(Id instanceId) => throw new NotImplementedException();

            public Core.Gameplay.Encounter.EncounterState GetState(Id instanceId) => throw new NotImplementedException();

            public IReadOnlyList<Id> ActiveInstanceIds { get; } = new[] { InstanceA };
        }

        private static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        // ---------------------------------------------------------------
        // 连续模式：行为不变——不论是否传入 bus，每次 Execute 都求值一次（08"连续模式下按 tick
        // 频率求值，行为不变"）。
        // ---------------------------------------------------------------

        [Fact]
        public void Execute_ContinuousStep_WithoutBus_EvaluatesEveryCall()
        {
            var host = new CountingEncounterHost();
            var handler = new EncounterTickHandler(host);

            handler.Execute(SimStep.Continuous(0.1), world: null!);
            handler.Execute(SimStep.Continuous(0.1), world: null!);

            Assert.Equal(2, host.EvaluateCallCount);
        }

        [Fact]
        public void Execute_ContinuousStep_WithBus_EvaluatesEveryCall()
        {
            var host = new CountingEncounterHost();
            var handler = new EncounterTickHandler(host, CreateBus());

            handler.Execute(SimStep.Continuous(0.1), world: null!);
            handler.Execute(SimStep.Continuous(0.1), world: null!);
            handler.Execute(SimStep.Continuous(0.1), world: null!);

            Assert.Equal(3, host.EvaluateCallCount);
        }

        // ---------------------------------------------------------------
        // 离散模式：接了 bus 时，Act 步本身不求值——TurnScheduler.NextStep 只产生 StepPhase.Act
        // 一种离散步（见类型判断记录），求值时机改由 sim.turn_ended/sim.round_ended 事件驱动。
        // ---------------------------------------------------------------

        [Fact]
        public void Execute_DiscreteActStep_WithBus_DoesNotEvaluate()
        {
            var host = new CountingEncounterHost();
            var handler = new EncounterTickHandler(host, CreateBus());

            var actorId = new Id("unit.tick_actor");
            handler.Execute(SimStep.Discrete(actorId, StepPhase.Act), world: null!);
            handler.Execute(SimStep.Discrete(actorId, StepPhase.Act), world: null!);

            Assert.Equal(0, host.EvaluateCallCount);
        }

        // 未接线 bus（惯例同本模块自身单元测试直接构造 EncounterTickHandler 手动驱动）时保留收边
        // 之前"不区分 step 种类"的行为——不属于本次要修的路径，只是确认向后兼容没有破坏。
        [Fact]
        public void Execute_DiscreteActStep_WithoutBus_StillEvaluates_BackwardCompat()
        {
            var host = new CountingEncounterHost();
            var handler = new EncounterTickHandler(host);

            var actorId = new Id("unit.tick_actor");
            handler.Execute(SimStep.Discrete(actorId, StepPhase.Act), world: null!);

            Assert.Equal(1, host.EvaluateCallCount);
        }

        [Fact]
        public void SimTurnEndedEvent_WithBus_TriggersEvaluate()
        {
            var host = new CountingEncounterHost();
            var bus = CreateBus();
            var handler = new EncounterTickHandler(host, bus);

            bus.PublishImmediate(new SimTurnEndedEvent(new Id("unit.tick_actor")));

            Assert.Equal(1, host.EvaluateCallCount);
        }

        [Fact]
        public void SimRoundEndedEvent_WithBus_TriggersEvaluate()
        {
            var host = new CountingEncounterHost();
            var bus = CreateBus();
            var handler = new EncounterTickHandler(host, bus);

            bus.PublishImmediate(new SimRoundEndedEvent(roundIndex: 0));

            Assert.Equal(1, host.EvaluateCallCount);
        }

        // 08 原文"在每个 sim.turn_ended/sim.round_ended 之后求值一次"：一整轮最后一名行动者结束
        // 回合时两个事件依次触发（见 TurnScheduler.AdvanceToNextActor），各自独立求值一次——本类型
        // 不做"同一轮内去重"，两次求值都发生（幂等：EncounterHost.Evaluate 对同一实例重复调用本就
        // 安全，见该类型测试）。
        [Fact]
        public void TurnEndedThenRoundEnded_BothTriggerEvaluate_Independently()
        {
            var host = new CountingEncounterHost();
            var bus = CreateBus();
            var handler = new EncounterTickHandler(host, bus);

            bus.PublishImmediate(new SimTurnEndedEvent(new Id("unit.tick_actor")));
            bus.PublishImmediate(new SimRoundEndedEvent(roundIndex: 0));

            Assert.Equal(2, host.EvaluateCallCount);
        }

        // 完整的一轮模拟：Act 步不求值，回合/轮结束各求值一次——验证三种输入组合在一起时的总次数，
        // 不只是逐个孤立验证。
        [Fact]
        public void MixedDiscreteStepsAndEvents_OnlyTurnEndedAndRoundEndedEvaluate()
        {
            var host = new CountingEncounterHost();
            var bus = CreateBus();
            var handler = new EncounterTickHandler(host, bus);

            var actorId = new Id("unit.tick_actor");
            handler.Execute(SimStep.Discrete(actorId, StepPhase.Act), world: null!); // 不求值
            bus.PublishImmediate(new SimTurnEndedEvent(actorId)); // 求值 #1
            handler.Execute(SimStep.Discrete(actorId, StepPhase.Act), world: null!); // 不求值
            bus.PublishImmediate(new SimTurnEndedEvent(actorId)); // 求值 #2
            bus.PublishImmediate(new SimRoundEndedEvent(roundIndex: 0)); // 求值 #3

            Assert.Equal(3, host.EvaluateCallCount);
        }
    }
}
