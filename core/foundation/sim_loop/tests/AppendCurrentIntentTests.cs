using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// 集成任务补齐的契约缺口 <see cref="IWorldSim.AppendCurrentIntent"/>（见
    /// <c>core/foundation/sim_loop/contracts/IWorldSim.cs</c> 该方法注释、
    /// <c>core/rules/ai/core/AiTickHandler.cs</c>）：本次 tick 内追加的意图应立即出现在
    /// <see cref="IWorldSim.CurrentIntents"/>，且只在 <see cref="IWorldSim.Tick"/> 执行期间可调用。
    /// </summary>
    public class AppendCurrentIntentTests
    {
        [Fact]
        public void AppendCurrentIntent_DuringTick_AppearsInCurrentIntents_SameTick()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var actorId = new Id("unit.a");
            var appendedIntent = new Intent(actorId, "appended");

            IReadOnlyList<Intent>? seenByLaterPhase = null;

            // AiDecision 阶段追加一条意图，CombatResolution 阶段（更晚）应该已经能看到它——
            // 证明 AppendCurrentIntent 让本 tick 内产生的意图立即参与本 tick 剩余阶段，
            // 不需要等到下一个 Tick。
            world.RegisterPhaseHandler(TickPhase.AiDecision,
                new DelegatePhaseHandler((step, w) => w.AppendCurrentIntent(appendedIntent)));
            world.RegisterPhaseHandler(TickPhase.CombatResolution,
                new DelegatePhaseHandler((step, w) => seenByLaterPhase = w.CurrentIntents));

            world.Tick(SimStep.Continuous(1.0 / 60.0));

            Assert.NotNull(seenByLaterPhase);
            Assert.Contains(appendedIntent, seenByLaterPhase);

            // tick 结束（阶段 8 完成）后 CurrentIntents 清空，追加的意图不再可见。
            Assert.Empty(world.CurrentIntents);
        }

        [Fact]
        public void AppendCurrentIntent_OutsideTick_Throws()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var intent = new Intent(new Id("unit.a"), "move");

            // 尚未调用过 Tick：tick 外调用应抛异常，不是静默忽略或悄悄进队列。
            Assert.Throws<InvalidOperationException>(() => world.AppendCurrentIntent(intent));

            // 跑完一次 Tick 之后（上一次 Tick 已完成）同样应该抛异常。
            world.Tick(SimStep.Continuous(1.0 / 60.0));
            Assert.Throws<InvalidOperationException>(() => world.AppendCurrentIntent(intent));
        }
    }
}
