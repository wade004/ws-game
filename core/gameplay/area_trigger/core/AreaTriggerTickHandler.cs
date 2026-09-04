using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// 把 <see cref="IAreaTriggerHost.Evaluate"/> 接入 sim_loop 的 tick 编排，挂在
    /// <see cref="TickPhase.TriggerEvaluation"/>（见 03_运行时骨架.md 第 4.2 节步骤 6"触发评估"、
    /// 05_对象模型与世界.md 第 7.1 节"evaluate 由移动系统在单位位置变化后调用"）。
    /// <para>
    /// 判断记录：05 原文把 <c>evaluate</c> 的调用时机描述为"移动系统在位置变化后调用"，但移动系统
    /// （<c>core/carriers/unit</c>）不在本任务允许修改的目录内，无法在那里直接挂一次回调。任务书
    /// 拍板改为本处理器在 <see cref="TickPhase.TriggerEvaluation"/>（tick 第 6 步，晚于第 4 步"移动
    /// 与导航"）对全部单位按 <see cref="IUnitAccess.AllUnits"/> 逐个检查坐标是否较上一次评估变化，
    /// 变化了才调用 <see cref="IAreaTriggerHost.Evaluate"/>——效果上等价于"位置变化后调用"，只是
    /// 检测时机从"移动系统写入的那一刻"推迟到"本 tick 触发评估阶段"，不影响确定性（同一 tick 内
    /// 结果一致）。
    /// </para>
    /// </summary>
    public sealed class AreaTriggerTickHandler : ITickPhaseHandler
    {
        private readonly IAreaTriggerHost _host;
        private readonly IUnitAccess _units;
        private readonly IAreaTriggerDiagnostics _diagnostics;

        private readonly Dictionary<Id, Vec2> _lastPositions = new Dictionary<Id, Vec2>();

        public AreaTriggerTickHandler(IAreaTriggerHost host, IUnitAccess units, IAreaTriggerDiagnostics? diagnostics = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _diagnostics = diagnostics ?? new InMemoryAreaTriggerDiagnostics();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (step.Kind != SimStepKind.Continuous)
            {
                _diagnostics.Warn(
                    "AreaTriggerTickHandler 收到 Discrete 步，本项目未启用离散时间模型（见 ADR-0013），本次 tick 不推进区域触发评估");
                return;
            }

            foreach (var unitId in _units.AllUnits)
            {
                var position = _units.GetPosition(unitId);
                if (_lastPositions.TryGetValue(unitId, out var last) && last.Equals(position))
                {
                    continue;
                }

                _lastPositions[unitId] = position;
                _host.Evaluate(unitId, position);
            }
        }
    }
}
