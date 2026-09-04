using System;
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
    /// </summary>
    public sealed class CombatTickHandler : ITickPhaseHandler
    {
        private readonly ICombatHost _host;
        private readonly ICombatDiagnostics _diagnostics;

        public CombatTickHandler(ICombatHost host, ICombatDiagnostics? diagnostics = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _diagnostics = diagnostics ?? new InMemoryCombatDiagnostics();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (step.Kind == SimStepKind.Continuous)
            {
                _host.Update(step.Dt);
                return;
            }

            _diagnostics.Warn(
                "CombatTickHandler 收到 Discrete 步，本项目未启用离散时间模型（见 ADR-0013、" +
                "落地方案与分阶段计划.md T1-5 禁止事项），本次 tick 不推进脱战判定");
        }
    }
}
