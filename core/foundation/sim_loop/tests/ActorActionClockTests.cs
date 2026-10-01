using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// 行动者动作时钟默认实现（<see cref="ActorActionClock"/>）：先过 <c>ActorActionClockContractTests</c> 假实现的同一组契约断言
    /// （嵌套取大、到期解除、无条件释放、句柄归零），再验证真实实现独有的 tick 内暂停口径与世界宿主接线。
    /// 期望值全由"暂停 tick 数"算出，不写死裸数。
    /// </summary>
    public class ActorActionClockTests
    {
        private static readonly Id A = new Id("unit.a");
        private static readonly Id B = new Id("unit.b");

        private static void Advance(ActorActionClock clock, int ticks)
        {
            for (var i = 0; i < ticks; i++) clock.Advance();
        }

        [Fact]
        public void NestedPause_TakesTheLargerRemaining_NotTheSum()
        {
            var clock = new ActorActionClock();
            clock.Pause(A, 5);
            clock.Pause(A, 3);
            Assert.Equal(5, clock.RemainingPausedTicks(A));
            clock.Pause(A, 9);
            Assert.Equal(9, clock.RemainingPausedTicks(A));
            Assert.True(clock.IsPaused(A));
            Assert.False(clock.IsPaused(B));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(7)]
        public void ActionTicks_DoNotAdvanceWhilePaused_AndExpireAutomatically(int pausedTicks)
        {
            var clock = new ActorActionClock();
            clock.Pause(A, pausedTicks);
            Advance(clock, pausedTicks);
            Assert.Equal(0, clock.ActionTicks(A) - 0);
            Assert.False(clock.IsPaused(A));
            Assert.Equal(0, clock.TotalPauseHandleCount);
            clock.Advance();
            Assert.Equal(1, clock.ActionTicks(A));
            Assert.Equal(pausedTicks + 1, clock.ActionTicks(B));
        }

        [Fact]
        public void ReleaseAll_ClearsHandles_PerActorAndGlobally()
        {
            var clock = new ActorActionClock();
            clock.Pause(A, 10);
            clock.Pause(B, 10);
            Assert.Equal(2, clock.TotalPauseHandleCount);
            clock.ReleaseAll(A);
            Assert.False(clock.IsPaused(A));
            Assert.Equal(0, clock.PauseHandleCount(A));
            Assert.Equal(1, clock.TotalPauseHandleCount);
            clock.ReleaseAll();
            Assert.Equal(0, clock.TotalPauseHandleCount);
            Assert.False(clock.IsPaused(B));
        }

        [Fact]
        public void Pause_RejectsNonPositiveTicks()
        {
            var clock = new ActorActionClock();
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Pause(A, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Pause(A, -1));
        }

        [Fact]
        public void PauseInsideATick_FreezesExactlyTheFollowingTicks()
        {
            var clock = new ActorActionClock();
            const int pause = 3;
            clock.BeginTick(0);
            clock.Pause(A, pause);
            // 本 tick 内（步骤 5/7）施加的暂停不影响本 tick：A 在本 tick 里仍视为前进。
            Assert.Equal(pause, clock.RemainingPausedTicks(A));
            clock.EndTick();
            var frozenObserved = 0;
            var before = clock.ActionTicks(A);
            for (var i = 0; i < pause + 2; i++)
            {
                clock.BeginTick(clock.Now);
                if (clock.IsPaused(A)) frozenObserved++;
                clock.EndTick();
            }

            Assert.Equal(pause, frozenObserved);
            Assert.Equal(pause + 2 - pause, clock.ActionTicks(A) - before);
        }

        [Fact]
        public void RemainingAndIsPaused_AreConsistent_AtEveryTickOfAWindow()
        {
            var clock = new ActorActionClock();
            clock.Pause(A, 4);
            for (var i = 0; i < 6; i++)
            {
                clock.BeginTick(clock.Now);
                Assert.Equal(clock.IsPaused(A), clock.RemainingPausedTicks(A) > 0);
                clock.EndTick();
            }
        }

        [Fact]
        public void TickIndexRegression_ClearsAllWindows_LikeSceneUnload()
        {
            var clock = new ActorActionClock();
            Advance(clock, 5);
            clock.Pause(A, 10);
            clock.BeginTick(0);
            Assert.False(clock.IsPaused(A));
            Assert.Equal(0, clock.TotalPauseHandleCount);
        }

        [Fact]
        public void AttachedToWorld_FreezesAfterTheHitTick_RegardlessOfSubscriberOrderWithinTheTick()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var clock = new ActorActionClock();
            using var attached = clock.Attach(bus);
            const int hitTick = 1;
            const int pause = 2;
            var snapshots = new List<(long Tick, bool Paused)>();
            bus.Subscribe<SimTickStartedEvent>(SimEventKeys.TickStarted, e => snapshots.Add((e.TickIndex, clock.IsPaused(A))));
            bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, e =>
            {
                if (e.TickIndex == hitTick) clock.Pause(A, pause);
            });

            for (var i = 0; i < hitTick + pause + 3; i++) world.Tick(SimStep.Continuous(0.1));

            foreach (var (tick, paused) in snapshots)
            {
                var expected = tick > hitTick && tick <= hitTick + pause;
                Assert.True(expected == paused, $"tick {tick}: 期望暂停={expected}，实际={paused}");
            }

            Assert.Equal(0, clock.TotalPauseHandleCount);
        }
    }
}
