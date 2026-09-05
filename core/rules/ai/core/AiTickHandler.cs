using System;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;

namespace Core.Rules.Ai
{
    /// <summary>
    /// 把 <see cref="AiHost.Step"/> 接入 sim_loop 的 tick 编排，挂在
    /// <see cref="TickPhase.AiDecision"/>（见 03_运行时骨架.md 第 4.2 节步骤 2"AI 决策"）。
    /// 对全部已注册单位按 <see cref="AiHost.RegisteredUnitIds"/>（Id 序数）依次调用
    /// <see cref="AiHost.Step"/>，把产生的 <c>move</c> 意图逐条追加进本 tick 的意图列表。
    /// <para>
    /// 集成任务已补齐本模块 README"契约缺口"一节记录的缺口：<see cref="IWorldSim"/> 新增
    /// <see cref="IWorldSim.AppendCurrentIntent"/>，语义正是 03 第 4.2 节步骤 2 文字描述的
    /// "AI 决策……为非玩家单位生成本 tick 的意图，追加进意图列表"——本处理器改用它，AI 产生的
    /// <c>move</c> 意图立即进入 <see cref="IWorldSim.CurrentIntents"/>，在同一 tick 内继续走完
    /// 步骤 3～8，不再有原先"下一 tick 才生效"的一 tick 延迟。<c>AppendCurrentIntent</c> 只在
    /// <c>Tick</c> 执行期间可调用，本处理器的 <see cref="Execute"/> 本身就是在 tick 内被
    /// <c>WorldSim</c> 调用，正常情况下恒可用；仍用 try/catch 兜底回退到
    /// <see cref="IWorldSim.SubmitIntent"/>（进"下一 tick"队列），覆盖测试用非标准调用方式
    /// （不经 <c>WorldSim.Tick</c> 直接调用本方法）等边角场景，不让这类场景直接抛异常中断。
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

            if (step.Kind == SimStepKind.Discrete)
            {
                ExecuteDiscrete(step, world);
                return;
            }

            foreach (var unitId in _host.RegisteredUnitIds)
            {
                var intents = _host.Step(unitId, step.Dt);
                foreach (var intent in intents)
                {
                    try
                    {
                        world.AppendCurrentIntent(intent);
                    }
                    catch (InvalidOperationException)
                    {
                        // 兜底：非标准调用方式（不经 WorldSim.Tick）下 AppendCurrentIntent 会抛异常，
                        // 回退到"下一 tick 生效"的 SubmitIntent，见本类型顶部注释。
                        world.SubmitIntent(intent);
                    }
                }
            }
        }

        /// <summary>
        /// 离散步下"AI 决策阶段只为当前行动者产意图"（见 03_运行时骨架.md 第 4.2 节步骤 2、
        /// ADR-0013 决策 6"AI 仍用同一张优先级表，在自己回合求值一次"）：只有当
        /// <see cref="SimStep.ActorId"/> 是一个已在 <see cref="AiHost"/> 登记的非玩家单位（即
        /// <see cref="AiHost.RegisteredUnitIds"/> 包含它）时才求值一次；玩家行动者的意图经
        /// <c>TurnScheduler.SubmitIntent</c> 在此之前已提交，不经本处理器。<paramref name="dt"/>
        /// 传 1.0（离散模式下一个时间单位 = 一回合，见 ADR-0013 决策 1），保证按
        /// <c>decision_interval</c> 节流的优先级表求值逻辑在"每回合求值一次"这一前提下总能触发
        /// （而不是被"该单位距上次决策不足 decision_interval 秒"误判为跳过）。
        /// </summary>
        private void ExecuteDiscrete(SimStep step, IWorldSim world)
        {
            if (!step.ActorId.HasValue)
            {
                return;
            }

            var actorId = step.ActorId.Value;
            var isRegistered = false;
            foreach (var unitId in _host.RegisteredUnitIds)
            {
                if (unitId.Equals(actorId))
                {
                    isRegistered = true;
                    break;
                }
            }

            if (!isRegistered)
            {
                return; // 玩家行动者（或未登记 AI 的行动者），本处理器不介入。
            }

            var intents = _host.Step(actorId, 1.0);
            foreach (var intent in intents)
            {
                try
                {
                    world.AppendCurrentIntent(intent);
                }
                catch (InvalidOperationException)
                {
                    world.SubmitIntent(intent);
                }
            }
        }
    }
}
