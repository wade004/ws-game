using System;
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
    /// 判断记录（离散/连续两种模式下的调用时机差异，08 第 4.3 节"离散模式下于回合结束时生效…
    /// 连续模式下按 tick 频率求值，行为不变"）：本类型不区分 <see cref="SimStep.Kind"/>，每次
    /// <see cref="Execute"/> 被调用都无条件遍历一次全部活跃实例——本项目离散模式未启用
    /// （ADR-0013），"回合结束时求值"这一时机差异没有真实调度器会产生 Discrete 步来触发，
    /// 因此本类型按连续模式的"每 tick 求值"实现即可覆盖当前项目的实际运行路径；若未来启用离散
    /// 模式，需要由调用方只在 <c>step.Phase == StepPhase.TurnEnd/RoundEnd</c> 时才注册/触发本
    /// 处理器（不属于本任务范围，见 README"契约缺口"一节）。
    /// </para>
    /// </summary>
    public sealed class EncounterTickHandler : ITickPhaseHandler
    {
        private readonly IEncounterHost _host;

        public EncounterTickHandler(IEncounterHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            var activeIds = _host.ActiveInstanceIds;
            for (var i = 0; i < activeIds.Count; i++)
            {
                _host.Evaluate(activeIds[i]);
            }
        }
    }
}
