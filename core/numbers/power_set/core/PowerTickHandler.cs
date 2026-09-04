using System;
using Core.Foundation.SimLoop;

namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// 把 <see cref="IPowerHost"/> 的时间推进接入 sim_loop 的 tick 编排（见 03_运行时骨架.md
    /// 第 4.2 节八步固定顺序）。挂在 <see cref="TickPhase.TriggerEvaluation"/>（拍板：资源
    /// 回复/衰减属于"随时间推进的被动结算"，语义上更接近该阶段的触发评估，而非阶段 1"输入意图
    /// 收集"；06 原文未规定 PowerSet 应挂在哪个可注册阶段，sim_loop 只开放六个可注册阶段，
    /// 本任务书拍板选定 TriggerEvaluation，判断记录见本模块 README）。
    /// 连续步（<see cref="SimStepKind.Continuous"/>）用 <see cref="SimStep.Dt"/> 调用
    /// <see cref="IPowerHost.AdvanceAll"/>；离散步（本项目暂不启用，见 ADR-0013、落地方案
    /// T1-5）不推进，只记一条警告。
    /// </summary>
    public sealed class PowerTickHandler : ITickPhaseHandler
    {
        private readonly IPowerHost _host;
        private readonly IPowerDiagnostics _diagnostics;

        public PowerTickHandler(IPowerHost host, IPowerDiagnostics? diagnostics = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _diagnostics = diagnostics ?? new InMemoryPowerDiagnostics();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (step.Kind == SimStepKind.Continuous)
            {
                _host.AdvanceAll(step.Dt);
                return;
            }

            _diagnostics.Warn(
                "PowerTickHandler 收到 Discrete 步，本项目未启用离散时间模型（见 ADR-0013、" +
                "落地方案与分阶段计划.md T1-5 禁止事项），本次 tick 不推进资源回复/衰减");
        }
    }
}
