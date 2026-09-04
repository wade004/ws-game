using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    public class NotEnabledTests
    {
        [Fact]
        public void NotEnabledTurnScheduler_EveryMethod_ThrowsNotSupported()
        {
            var scheduler = new NotEnabledTurnScheduler();
            var actorId = new Id("unit.hero");

            Assert.Throws<NotSupportedException>(() => scheduler.NextStep());
            Assert.Throws<NotSupportedException>(() => scheduler.BeginCombat(new List<Id> { actorId }));
            Assert.Throws<NotSupportedException>(() => scheduler.EndCombat());
            Assert.Throws<NotSupportedException>(() =>
                scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>()));
            Assert.Throws<NotSupportedException>(() => scheduler.SubmitIntent(actorId, new object()));
            Assert.Throws<NotSupportedException>(() => scheduler.EndTurn(actorId));
            Assert.Throws<NotSupportedException>(() => scheduler.GetOrder());
            Assert.Throws<NotSupportedException>(() => scheduler.GetCurrentActor());
        }

        [Fact]
        public void NotEnabledPacingPolicy_EveryMethod_ThrowsNotSupported()
        {
            var pacing = new NotEnabledPacingPolicy();

            Assert.Throws<NotSupportedException>(() => pacing.Mode());
            Assert.Throws<NotSupportedException>(() => pacing.OnPlaybackFinished());
        }

        [Fact]
        public void Tick_WithDiscreteStep_RunsEightStepsWithoutThrowing_DoesNotAdvanceTimers_AndLogsWarning()
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

            // 离散步不推进计时器（本项目暂不启用离散时间模型）。
            Assert.Equal(remainingBefore, world.Timers.Remaining(timerHandle));
            Assert.NotEmpty(world.DiagnosticsWarnings);
        }
    }
}
