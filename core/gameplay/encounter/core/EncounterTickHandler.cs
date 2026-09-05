using System;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// 挂在 <see cref="TickPhase.TriggerEvaluation"/> 阶段的处理器（见 08 第 9 节汇总表
    /// <c>Encounter/Level</c> 行"AiHost.evaluate...每 tick 经 EncounterTickHandler（TickPhase.
    /// TriggerEvaluation）调用"）：逐个仍活跃的遭遇实例调用 <see cref="IEncounterHost.Evaluate"/>。
    /// 本类型只负责"谁来调用、什么时候调用"，不含任何判定逻辑（判定逻辑都在
    /// <see cref="EncounterHost.Evaluate"/> 里）。
    /// <para>
    /// 判断记录（收边任务修正，离散/连续两种模式下的调用时机差异，08 第 4.3 节"阶段切换在离散模式下
    /// 于回合结束时生效：phases[].enterCondition 在每个 sim.turn_ended/sim.round_ended 之后求值
    /// 一次...连续模式下按 tick 频率求值，行为不变"）：此前实现不区分 <see cref="SimStep.Kind"/>，
    /// 每次 <see cref="Execute"/> 都无条件求值一次——当时理由是"本项目离散模式未启用（ADR-0013），
    /// 没有真实调度器会产生 Discrete 步来触发"，但离散模式已在收边 H4 任务接通（<c>WorldSim.
    /// AttachDiscreteRouting</c>、<c>GameplayAssembly.Advance</c>），该理由不再成立：
    /// <see cref="Core.Foundation.SimLoop.TurnScheduler.NextStep"/> 只产生 <c>StepPhase.Act</c> 一种
    /// 离散步（<see cref="StepPhase.TurnStart"/>/<see cref="StepPhase.TurnEnd"/>/
    /// <see cref="StepPhase.RoundEnd"/> 三个阶段值只经事件（<c>sim.turn_started</c>/
    /// <c>sim.turn_ended</c>/<c>sim.round_ended</c>）触达外部，不作为 <see cref="SimStep"/> 传给
    /// <see cref="ITickPhaseHandler.Execute"/>），若不加区分会导致：①同一行动者的一次行动过程中
    /// 途（Act 步）就可能切换阶段，与"避免同一行动者的行动过程中途切换 ai.rotation"的文档意图冲突；
    /// ②每个 Act 步都重复求值波次/胜负，浪费且与"回合结束时求值"的措辞不符。改为：<paramref name="bus"/>
    /// 非空时（<c>GameplayAssembly</c> 接线时恒传入，见其构造函数第 12.5 步），构造期直接订阅
    /// <see cref="SimEventKeys.TurnEnded"/>/<see cref="SimEventKeys.RoundEnded"/>（<c>TurnScheduler</c>
    /// 用 <c>PublishImmediate</c> 同步发布，见该类型 <c>AdvanceToNextActor</c>），命中时求值；
    /// <see cref="Execute"/> 本身遇到 <see cref="SimStepKind.Discrete"/> 步（即 Act 步）直接跳过，不
    /// 再重复求值；<see cref="SimStepKind.Continuous"/> 步不变，仍按 tick 频率求值（连续模式行为
    /// 不变）。<paramref name="bus"/> 为 <c>null</c>（向后兼容旧用法/本模块自身单元测试，见
    /// <c>EncounterHostTests</c>/<c>LevelHostTests</c> 直接构造 <see cref="EncounterTickHandler"/>
    /// 手动调用 <see cref="Execute"/> 逐步驱动，不途经 <c>WorldSim</c>/<c>TurnScheduler</c>）时保留
    /// 收边之前的行为：<see cref="Execute"/> 不区分 <see cref="SimStep.Kind"/>，每次调用都求值——
    /// 这些测试传入的 <see cref="SimStep"/> 恒为 <see cref="SimStepKind.Continuous"/>，行为不受影响。
    /// </para>
    /// </summary>
    public sealed class EncounterTickHandler : ITickPhaseHandler
    {
        private readonly IEncounterHost _host;
        private readonly IEventBus? _bus;

        public EncounterTickHandler(IEncounterHost host, IEventBus? bus = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _bus = bus;

            if (_bus != null)
            {
                _bus.Subscribe<SimTurnEndedEvent>(SimEventKeys.TurnEnded, _ => EvaluateAll());
                _bus.Subscribe<SimRoundEndedEvent>(SimEventKeys.RoundEnded, _ => EvaluateAll());
            }
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (_bus != null && step.Kind == SimStepKind.Discrete)
            {
                // 离散步（恒为 Act，见类型判断记录）：求值时机改由构造期订阅的
                // sim.turn_ended/sim.round_ended 事件驱动，这里不再重复求值。
                return;
            }

            EvaluateAll();
        }

        private void EvaluateAll()
        {
            var activeIds = _host.ActiveInstanceIds;
            for (var i = 0; i < activeIds.Count; i++)
            {
                _host.Evaluate(activeIds[i]);
            }
        }
    }
}
