using System;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Rules.Combat
{
    /// <summary>
    /// 把 <see cref="ICombatHost.Update"/> 接入 sim_loop 的 tick 编排，挂在
    /// <see cref="TickPhase.CombatResolution"/>（见 03_运行时骨架.md 第 4.2 节八步固定顺序"5.
    /// 战斗结算"、06 第 4.5 节脱战判定）。效果结算本身（<see cref="ICombatHost.ResolveEffect"/>）
    /// 由 skill 在读条/引导完成的那一步即时调用，不经本处理器——本处理器只负责"随时间推进"的
    /// 脱战判定（惯例同 <c>Core.Numbers.PowerSet.PowerTickHandler</c>）。
    /// <para>
    /// ADR-0013 离散时间模型：连续步下按 <c>step.Dt</c> 驱动（每 tick 推进真实经过的秒数）；
    /// 离散步恒 <c>Dt = 0</c>（见 <see cref="SimStep.Discrete"/> 构造函数），不代表"经过了多少
    /// 时间"，因此本处理器的 <see cref="Execute"/> 对 Discrete 步不做任何事——脱战判定改由
    /// <c>bus</c> 非空时订阅的 <c>sim.round_ended</c> 驱动：每轮结束调用一次
    /// <see cref="ICombatHost.Update"/>(1.0)（"1 轮"）。<c>1.0</c> 与
    /// <see cref="CombatOptions.LeaveCombatDelay"/> 的单位需要对齐——由
    /// <c>Core.Gameplay.Assembly.TimeModelSwitch</c> 在切入/切出离散模式时，把
    /// <see cref="CombatOptions.LeaveCombatDelay"/> 按 <c>seconds_per_turn</c> 换算为等效轮数
    /// （同一惯例 <c>SimTimers.RescaleAll</c>，见该类型判断记录），本处理器因此不需要另行知道
    /// <c>seconds_per_turn</c>。<paramref name="bus"/> 为空（未装配离散模式的调用方，如既有
    /// <c>RulesAssembly</c> 独立测试）时不订阅，行为与本任务之前完全一致。
    /// </para>
    /// </summary>
    public sealed class CombatTickHandler : ITickPhaseHandler
    {
        private readonly ICombatHost _host;
        private readonly ICombatDiagnostics _diagnostics;

        public CombatTickHandler(ICombatHost host, ICombatDiagnostics? diagnostics = null, IEventBus? bus = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _diagnostics = diagnostics ?? new InMemoryCombatDiagnostics();

            bus?.Subscribe<SimRoundEndedEvent>(SimEventKeys.RoundEnded, _ => _host.Update(1.0));
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (step.Kind == SimStepKind.Continuous)
            {
                _host.Update(step.Dt);
            }

            // 离散步：脱战判定改由构造期订阅的 sim.round_ended 驱动（见类型注释），本方法不做
            // 任何事、也不再发诊断警告（离散时间模型已经落地，Discrete 步不再是"未启用"的异常
            // 输入，见 ADR-0013）。
        }
    }
}
