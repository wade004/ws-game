using System;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;

namespace Core.Rules.Ai
{
    /// <summary>
    /// 把 <see cref="AiHost.Step"/> 接入 sim_loop 的 tick 编排，挂在
    /// <see cref="TickPhase.AiDecision"/>（见 03_运行时骨架.md 第 4.2 节步骤 2"AI 决策"）。
    /// 对全部已注册单位按 <see cref="AiHost.RegisteredUnitIds"/>（Id 序数）依次调用
    /// <see cref="AiHost.Step"/>，把产生的 <c>move</c> 意图逐条 <see cref="IWorldSim.SubmitIntent"/>。
    /// <para>
    /// 契约缺口（见本模块 README"契约缺口"一节）：<see cref="IWorldSim"/> 只提供
    /// <see cref="IWorldSim.SubmitIntent"/>（进入"下一 tick 待收集"队列）与只读的
    /// <see cref="IWorldSim.CurrentIntents"/>（本 tick 阶段 1 已固定的快照，阶段 8 结束才清空，
    /// 阶段 2～8 期间无法追加），没有"把一条意图追加进本 tick 当前意图列表"的方法——而 03 第 4.2 节
    /// 步骤 2 的文字描述是"AI 决策……为非玩家单位生成本 tick 的意图，追加进意图列表"，暗示 AI 意图
    /// 应该在同一 tick 内继续走完步骤 3～8。本实现按契约实际提供的方法用 <c>SubmitIntent</c>，
    /// 产生的 <c>move</c> 意图会在<b>下一次</b> <c>Tick</c> 的阶段 1 才进入 <c>CurrentIntents</c>，
    /// 即移动结算相对"当期决策"存在一个 tick 的延迟；本任务判断这一延迟在固定步长连续模式下
    /// （见 SimLoopOptions 默认每秒 60 步）可接受，不影响状态机转移本身的正确性（转移判定读的是
    /// <c>IUnitAccess</c> 的即时状态，不依赖本 tick 是否已经应用位移）。真正消除这一延迟需要集成
    /// 任务给 <c>IWorldSim</c> 补一个 <c>AppendCurrentIntent</c>（或等价物）——不在本任务范围内，
    /// 已在任务汇报中提出。
    /// </para>
    /// </summary>
    public sealed class AiTickHandler : ITickPhaseHandler
    {
        private readonly AiHost _host;
        private readonly IExprDiagnostics _diagnostics;

        public AiTickHandler(AiHost host, IExprDiagnostics? diagnostics = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            if (step.Kind != SimStepKind.Continuous)
            {
                _diagnostics.Warn(
                    "AiTickHandler 收到 Discrete 步，本项目未启用离散时间模型（见 ADR-0013、" +
                    "落地方案与分阶段计划.md T1-5 禁止事项），本次 tick 不推进 AI 决策");
                return;
            }

            foreach (var unitId in _host.RegisteredUnitIds)
            {
                var intents = _host.Step(unitId, step.Dt);
                foreach (var intent in intents)
                {
                    world.SubmitIntent(intent);
                }
            }
        }
    }
}
