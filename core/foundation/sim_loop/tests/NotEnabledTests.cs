using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// 判断记录（文件历史）：本文件此前覆盖 <c>NotEnabledTurnScheduler</c>/<c>NotEnabledPacingPolicy</c>
    /// 两个占位实现（离散时间模型"暂不启用"阶段）。ADR-0013 落地后两个占位类型已被真实的
    /// <see cref="TurnScheduler"/>/<see cref="ImmediatePacingPolicy"/>/<see cref="WaitForPlaybackPacingPolicy"/>
    /// 取代（见 <c>TurnSchedulerTests.cs</c>/<c>PacingPolicyTests.cs</c>），本文件保留的第三条用例
    /// （<c>WorldSim.Tick</c> 处理离散步的基本契约——八步不抛异常、不推进全局计时器、记诊断警告）
    /// 依旧成立，只更新了措辞（不再是"暂不启用"，而是"离散步以行动者为粒度，全局计时器换算发生在
    /// 模式切换时刻"，见 <c>WorldSim.cs</c> 判断记录）。
    /// </summary>
    public class NotEnabledTests
    {
        [Fact]
        public void Tick_WithDiscreteStep_RunsEightStepsWithoutThrowing_DoesNotAdvanceGlobalTimers_AndLogsWarning()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var timerHandle = world.Timers.Create(1.0);
            var remainingBefore = world.Timers.Remaining(timerHandle);

            var executed = new List<string>();
            world.RegisterPhaseHandler(
                TickPhase.IntentCollection,
                new DelegatePhaseHandler((step, w) => executed.Add("intent_collection")));

            var exception = Record.Exception(() =>
                world.Tick(SimStep.Discrete(new Id("unit.hero"), StepPhase.Act)));

            Assert.Null(exception);
            Assert.Contains("intent_collection", executed);

            // 离散步不推进全局计时器（见 WorldSim.cs 判断记录：换算发生在模式切换时刻）。
            Assert.Equal(remainingBefore, world.Timers.Remaining(timerHandle));
            Assert.NotEmpty(world.DiagnosticsWarnings);
        }

        [Fact]
        public void Tick_WithDiscreteStep_OnlyCollectsCurrentActorIntent()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var hero = new Id("unit.hero");
            var bystander = new Id("unit.bystander");

            world.SubmitIntent(new Intent(hero, "move"));
            world.SubmitIntent(new Intent(bystander, "move"));

            IReadOnlyList<Intent>? seen = null;
            world.RegisterPhaseHandler(
                TickPhase.IntentCollection,
                new DelegatePhaseHandler((step, w) => seen = w.CurrentIntents));

            world.Tick(SimStep.Discrete(hero, StepPhase.Act));

            Assert.NotNull(seen);
            Assert.Single(seen!);
            Assert.Equal(hero, seen![0].ActorId);
        }
    }
}
