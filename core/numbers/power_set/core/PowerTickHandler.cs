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
    /// <see cref="IPowerHost.AdvanceAll"/>；离散步不推进，只记一条警告（见 <see cref="Execute"/>
    /// 判断记录）。
    /// <para>
    /// 判断记录（DOC-111-03 根治，architecture/落地计划/audit-6739f50-20260909，P3——取代下方
    /// <see cref="Execute"/> 已废止的旧警告文案"本项目未启用离散时间模型"）：ADR-0013 与
    /// sim_loop 现已提供基础离散调度（<see cref="SimStepKind.Discrete"/>），"离散时间模型"整体
    /// 早已不是未实现状态；本类型收到离散步只记警告、不推进的，只是这一个具体资源处理器自己的
    /// 连续-only 边界——离散步下是否需要把资源回复/衰减换算成按回合结算（以及换算规则本身），是
    /// 具体游戏的策略决定，不是本模块自己要不要支持离散步的问题。不应把这条边界误读成"整个框架
    /// 的离散时间模型未实现"，也不应与 ATB/day_cycle（架构判断记录里明确的非目标）混为一谈。
    /// </para>
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
                "PowerTickHandler 收到 Discrete 步：资源回复/衰减是否换算为按离散步结算由游戏策略" +
                "决定（本处理器本身只支持连续步调用 AdvanceAll，见类型判断记录），本次 tick 不推进" +
                "资源回复/衰减");
        }
    }
}
