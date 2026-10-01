using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// sim_loop 边界/错误路径补测（T-M1，2026-10-01 测试覆盖第四批）：<see cref="SimClockHost"/>、
    /// <see cref="SimTimers"/>、<see cref="WorldSim"/>、<see cref="TurnScheduler"/> 的非法参数、
    /// 失效句柄、Dispose、阶段处理器异常后的状态、意图提交守卫。
    /// </summary>
    public sealed class SimLoopBoundaryTests
    {
        private static readonly Id Hero = new Id("unit.hero");
        private static readonly Id Foe = new Id("unit.foe");

        // -----------------------------------------------------------------
        // SimClockHost
        // -----------------------------------------------------------------

        [Fact]
        public void Clock_Advance_Negative_Throws_AndLeavesStateUntouched()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world);
            clock.Advance(clock.StepSeconds * 2.5); // 2 tick + 余 0.5 步
            var tickBefore = clock.TickIndex;

            var ex = Assert.Throws<ArgumentException>(() => clock.Advance(-0.001));
            Assert.Equal("realDeltaSeconds", ex.ParamName);

            Assert.Equal(tickBefore, clock.TickIndex);
            // 累积器未被污染：再推进半步（加一点浮点余量）凑满一步。
            clock.Advance(clock.StepSeconds * 0.5 + 1e-9);
            Assert.Equal(tickBefore + 1, clock.TickIndex);
        }

        [Fact]
        public void Clock_Advance_NaN_Throws_AndDoesNotPoisonAccumulator()
        {
            // 复现：realDeltaSeconds &lt; 0 对 NaN 为 false，NaN 进入累积器后所有后续 Advance 都返回 NaN 且永不产 tick。
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var clock = new SimClockHost(world);

            Assert.Throws<ArgumentException>(() => clock.Advance(double.NaN));

            clock.Advance(clock.StepSeconds);
            Assert.Equal(1, clock.TickIndex);
        }

        [Fact]
        public void Clock_SetTimeScale_Negative_Throws_AndKeepsPreviousScale()
        {
            var clock = new SimClockHost(new WorldSim(SimLoopTestSupport.CreateBus()));
            clock.SetTimeScale(0.5);

            Assert.Throws<ArgumentException>(() => clock.SetTimeScale(-0.1));

            Assert.Equal(0.5, clock.TimeScale);
        }

        [Fact]
        public void Clock_SetTimeScale_NaN_Throws_AndKeepsPreviousScale()
        {
            var clock = new SimClockHost(new WorldSim(SimLoopTestSupport.CreateBus()));
            clock.SetTimeScale(2.0);

            Assert.Throws<ArgumentException>(() => clock.SetTimeScale(double.NaN));

            Assert.Equal(2.0, clock.TimeScale);
        }

        [Fact]
        public void Clock_SetTimeScale_Zero_IsAllowed_AndFreezesTicking()
        {
            var clock = new SimClockHost(new WorldSim(SimLoopTestSupport.CreateBus()));

            clock.SetTimeScale(0.0);
            clock.Advance(10.0);

            Assert.Equal(0.0, clock.TimeScale);
            Assert.Equal(0, clock.TickIndex);
        }

        [Fact]
        public void Clock_Constructor_RejectsNullWorld_AndNonPositiveOptions()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());

            Assert.Throws<ArgumentNullException>(() => new SimClockHost(null!));
            Assert.Throws<ArgumentException>(() => new SimClockHost(world, new SimLoopOptions { StepSeconds = 0 }));
            Assert.Throws<ArgumentException>(() => new SimClockHost(world, new SimLoopOptions { StepSeconds = -1 }));
            Assert.Throws<ArgumentException>(() => new SimClockHost(world, new SimLoopOptions { StepSeconds = double.NaN }));
            Assert.Throws<ArgumentException>(() => new SimClockHost(world, new SimLoopOptions { MaxCatchUpSteps = 0 }));
            Assert.Throws<ArgumentException>(() => new SimClockHost(world, new SimLoopOptions { MaxCatchUpSteps = -3 }));
        }

        [Fact]
        public void Clock_Constructor_RejectsInvalidDefaultTimeScale_LikeSetTimeScale()
        {
            // 复现：SetTimeScale 拒绝负/NaN，但构造期 DefaultTimeScale 原先不校验，
            // 负缩放让累积器倒退（时间倒流）。两个入口口径应一致。
            var world = new WorldSim(SimLoopTestSupport.CreateBus());

            Assert.Throws<ArgumentException>(() => new SimClockHost(world, new SimLoopOptions { DefaultTimeScale = -1.0 }));
            Assert.Throws<ArgumentException>(() => new SimClockHost(world, new SimLoopOptions { DefaultTimeScale = double.NaN }));
            Assert.Equal(0.0, new SimClockHost(world, new SimLoopOptions { DefaultTimeScale = 0.0 }).TimeScale);
        }

        [Fact]
        public void Clock_ConfigureStep_NonPositiveMaxCatchUp_ThrowsAndKeepsConfig()
        {
            var clock = new SimClockHost(new WorldSim(SimLoopTestSupport.CreateBus()));
            var step = clock.StepSeconds;
            var max = clock.MaxCatchUpSteps;

            Assert.Throws<ArgumentException>(() => clock.ConfigureStep(0.5, 0));
            Assert.Throws<ArgumentException>(() => clock.ConfigureStep(double.NaN, 3));

            Assert.Equal(step, clock.StepSeconds);
            Assert.Equal(max, clock.MaxCatchUpSteps);
        }

        // -----------------------------------------------------------------
        // SimTimers
        // -----------------------------------------------------------------

        [Fact]
        public void Timers_StaleHandle_RemainingAndIsExpiredThrow_IsAliveFalse_CancelIsNoOp()
        {
            var timers = new SimTimers();
            var handle = timers.Create(1.0);
            timers.Cancel(handle);

            Assert.False(timers.IsAlive(handle));
            Assert.Throws<ArgumentException>(() => timers.Remaining(handle));
            Assert.Throws<ArgumentException>(() => timers.IsExpired(handle));
            timers.Cancel(handle); // 二次取消静默
            Assert.False(timers.IsAlive(handle));
        }

        [Fact]
        public void Timers_DefaultHandle_IsNeverAlive()
        {
            var timers = new SimTimers();
            timers.Create(1.0);

            Assert.False(timers.IsAlive(default));
            Assert.Throws<ArgumentException>(() => timers.Remaining(default));
        }

        [Fact]
        public void Timers_Create_ZeroDuration_IsImmediatelyExpired_ButStillAlive()
        {
            var timers = new SimTimers();
            var handle = timers.Create(0.0);

            Assert.True(timers.IsAlive(handle));
            Assert.True(timers.IsExpired(handle));
            Assert.Equal(0.0, timers.Remaining(handle));
        }

        [Fact]
        public void Timers_Create_NaNDuration_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SimTimers().Create(double.NaN));
        }

        [Fact]
        public void Timers_ExpiredTimer_StaysAliveWithNegativeRemaining_UntilCancelled()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var handle = world.Timers.Create(1.0);

            world.Tick(SimStep.Continuous(3.0));

            Assert.True(world.Timers.IsAlive(handle));
            Assert.True(world.Timers.IsExpired(handle));
            Assert.Equal(1.0 - 3.0, world.Timers.Remaining(handle), 9);
        }

        [Fact]
        public void Timers_RescaleAll_MultipliesEveryAliveTimer_AndSkipsCancelled()
        {
            var timers = new SimTimers();
            var a = timers.Create(4.0);
            var b = timers.Create(10.0);
            var cancelled = timers.Create(7.0);
            timers.Cancel(cancelled);

            const double factor = 0.25;
            timers.RescaleAll(factor);

            Assert.Equal(4.0 * factor, timers.Remaining(a), 9);
            Assert.Equal(10.0 * factor, timers.Remaining(b), 9);
            Assert.False(timers.IsAlive(cancelled));
        }

        [Fact]
        public void Timers_RescaleAll_OnEmpty_IsNoOp()
        {
            var timers = new SimTimers();
            timers.RescaleAll(3.0);

            var handle = timers.Create(2.0);
            Assert.Equal(2.0, timers.Remaining(handle));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        public void Timers_RescaleAll_NonPositiveOrNaNFactor_ThrowsAndLeavesTimersUntouched(double factor)
        {
            var timers = new SimTimers();
            var handle = timers.Create(5.0);

            Assert.Throws<ArgumentException>(() => timers.RescaleAll(factor));

            Assert.Equal(5.0, timers.Remaining(handle));
        }

        // -----------------------------------------------------------------
        // WorldSim
        // -----------------------------------------------------------------

        [Fact]
        public void World_Constructor_NullBus_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new WorldSim(null!));
        }

        [Fact]
        public void World_Dispose_IsIdempotent_AndStopsRoundEndedTimerAdvance()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var handle = world.Timers.Create(10.0);

            Assert.False(world.IsDisposed);
            bus.PublishImmediate(new SimRoundEndedEvent(0));
            Assert.Equal(10.0 - 1.0, world.Timers.Remaining(handle), 9);

            world.Dispose();
            world.Dispose();

            Assert.True(world.IsDisposed);
            bus.PublishImmediate(new SimRoundEndedEvent(1));
            Assert.Equal(10.0 - 1.0, world.Timers.Remaining(handle), 9);
        }

        [Fact]
        public void World_Dispose_DoesNotPreventFurtherTicks()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            world.Dispose();

            world.Tick(SimStep.Continuous(0.1));

            Assert.Equal(1, world.TickIndex);
        }

        [Fact]
        public void World_AddEntity_Null_AndAllocateEntityId_Empty_Throw()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());

            Assert.Throws<ArgumentNullException>(() => world.AddEntity(null!));
            Assert.Throws<ArgumentException>(() => world.AllocateEntityId(""));
            Assert.Throws<ArgumentException>(() => world.AllocateEntityId(null!));
            Assert.Throws<ArgumentNullException>(() => world.RegisterPhaseHandler(TickPhase.AiDecision, null!));
        }

        [Fact]
        public void World_AttachDiscreteRouting_NullArguments_Throw()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var clock = new SimClockHost(world);
            var scheduler = new TurnScheduler(world, id => 0, id => false, bus);

            Assert.Throws<ArgumentNullException>(() => world.AttachDiscreteRouting(null!, scheduler));
            Assert.Throws<ArgumentNullException>(() => world.AttachDiscreteRouting(clock, null!));
        }

        [Fact]
        public void World_PhaseHandlerThrows_PropagatesAndAbortsTick_WithoutRunningCleanupOrAdvancingTickIndex()
        {
            // README 判断记录（ADR-0079 一节）：Tick 的异常语义是"阶段内异常中止整拍"，不加 try/finally 强行跑阶段 8。
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var entity = new TestEntity(new Id("test.e_1"), new Id("map.a"));
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.1)); // 先让 AddEntity 的入队事件走完
            var tickBefore = world.TickIndex;

            var finishedCount = 0;
            bus.Subscribe(SimEventKeys.TickFinished, _ => finishedCount++);

            var shouldThrow = true;
            world.RegisterPhaseHandler(TickPhase.CombatResolution,
                new DelegatePhaseHandler((s, w) =>
                {
                    if (shouldThrow)
                    {
                        throw new InvalidOperationException("boom");
                    }
                }));

            world.MarkForDestruction(entity.EntityId);
            Assert.Throws<InvalidOperationException>(() => world.Tick(SimStep.Continuous(0.1)));

            Assert.Equal(tickBefore, world.TickIndex);
            Assert.Equal(0, finishedCount);
            Assert.NotNull(world.GetEntity(entity.EntityId));
            Assert.True(world.IsPendingDestruction(entity.EntityId));

            // 恢复后下一拍照常完成阶段 8：实体被清理，tick 计数前进。
            shouldThrow = false;
            world.Tick(SimStep.Continuous(0.1));

            Assert.Equal(tickBefore + 1, world.TickIndex);
            Assert.Null(world.GetEntity(entity.EntityId));
            Assert.Equal(1, finishedCount);
        }

        [Fact]
        public void World_PhaseHandlerThrows_DoesNotLeaveTickingFlagStuck()
        {
            // 复现：Tick 抛异常后 _isTicking 仍为 true，tick 外的 AppendCurrentIntent 守卫失效，
            // 意图被静默写入一个会在下一拍被覆盖的列表（丢失）。修复：_isTicking 用 try/finally 复位
            // （只复位该标志，不改"阶段内异常中止整拍、不跑阶段 8"的既有异常语义）。
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            world.RegisterPhaseHandler(TickPhase.AiDecision,
                new DelegatePhaseHandler((s, w) => throw new InvalidOperationException("boom")));

            Assert.Throws<InvalidOperationException>(() => world.Tick(SimStep.Continuous(0.1)));

            Assert.Throws<InvalidOperationException>(
                () => world.AppendCurrentIntent(new Intent(Hero, "move")));
        }

        [Fact]
        public void World_PhaseHandlerThrows_ThenSubmittedIntentsStillReachNextTick()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var fail = true;
            var seen = new List<Intent>();
            world.RegisterPhaseHandler(TickPhase.IntentCollection,
                new DelegatePhaseHandler((s, w) =>
                {
                    if (fail)
                    {
                        throw new InvalidOperationException("boom");
                    }

                    seen.AddRange(w.CurrentIntents);
                }));

            Assert.Throws<InvalidOperationException>(() => world.Tick(SimStep.Continuous(0.1)));

            fail = false;
            var intent = new Intent(Hero, "move");
            world.SubmitIntent(intent);
            world.Tick(SimStep.Continuous(0.1));

            Assert.Equal(new[] { intent }, seen);
        }

        [Fact]
        public void World_ReentrantTick_Throws_InvalidOperationException_OuterTickCompletesNormally()
        {
            // 收口遗留修复 A6：Tick 加重入守卫。嵌套调用（阶段处理器/事件订阅者里再调 Tick）抛
            // InvalidOperationException，外层 tick 继续正常完成（计数 +1、事件各一次、意图队列不被内层打乱）。
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var started = new List<long>();
            var finished = new List<long>();
            bus.Subscribe<SimTickStartedEvent>(SimEventKeys.TickStarted, e => started.Add(e.TickIndex));
            bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, e => finished.Add(e.TickIndex));

            Exception? nested = null;
            var seenIntents = new List<Intent>();
            var inHandler = false; // 防止修复前（无守卫）无限递归，测试本身保持可复现红而不是栈溢出。
            world.RegisterPhaseHandler(TickPhase.AiDecision,
                new DelegatePhaseHandler((s, w) =>
                {
                    if (inHandler)
                    {
                        return;
                    }

                    inHandler = true;
                    seenIntents.AddRange(w.CurrentIntents);
                    try
                    {
                        w.Tick(SimStep.Continuous(0.1));
                    }
                    catch (InvalidOperationException ex)
                    {
                        nested = ex;
                    }
                    finally
                    {
                        inHandler = false;
                    }
                }));

            var intent = new Intent(Hero, "move");
            world.SubmitIntent(intent);
            world.Tick(SimStep.Continuous(0.1));

            Assert.NotNull(nested);
            Assert.Equal(1, world.TickIndex);
            Assert.Equal(new long[] { 0 }, started);
            Assert.Equal(new long[] { 0 }, finished);
            Assert.Equal(new[] { intent }, seenIntents);

            // 外层结束后守卫已复位：下一拍正常推进（处理器里的嵌套调用同样再被拒绝一次）。
            nested = null;
            world.Tick(SimStep.Continuous(0.1));
            Assert.Equal(2, world.TickIndex);
            Assert.NotNull(nested);
        }

        [Fact]
        public void World_TickAfterNestedTickRejected_StillAllowedOutsideAnyTick()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var inHandler = false;
            world.RegisterPhaseHandler(TickPhase.AiDecision,
                new DelegatePhaseHandler((s, w) =>
                {
                    if (inHandler)
                    {
                        return;
                    }

                    inHandler = true;
                    try
                    {
                        Assert.Throws<InvalidOperationException>(() => w.Tick(SimStep.Continuous(0.1)));
                    }
                    finally
                    {
                        inHandler = false;
                    }
                }));

            world.Tick(SimStep.Continuous(0.1));
            world.Tick(SimStep.Continuous(0.1));

            Assert.Equal(2, world.TickIndex);
        }

        [Fact]
        public void World_SubmitIntent_DiscreteNonCurrentActor_RecordsDiagnosticWarning_AndDoesNotQueue()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var scheduler = new TurnScheduler(world, id => 0, id => id.Equals(Hero), bus);
            var clock = new SimClockHost(world, new SimLoopOptions { StepSeconds = 1.0, MaxCatchUpSteps = 2 })
            {
                Mode = TimeModelMode.Discrete,
            };
            world.AttachDiscreteRouting(clock, scheduler);
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            scheduler.BeginCombat(new[] { Hero, Foe });
            var warningsBefore = world.DiagnosticsWarnings.Count;

            world.SubmitIntent(new Intent(Foe, "move"));

            Assert.Equal(warningsBefore + 1, world.DiagnosticsWarnings.Count);
            Assert.Contains(Foe.Value, world.DiagnosticsWarnings[world.DiagnosticsWarnings.Count - 1]);
            Assert.Null(scheduler.NextStep()); // 玩家回合仍在等待输入：被拒绝的意图没有解除阻塞
        }

        [Fact]
        public void World_SubmitIntent_DiscreteBeforeCombat_IsRejectedWithWarning()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var scheduler = new TurnScheduler(world, id => 0, id => true, bus);
            var clock = new SimClockHost(world) { Mode = TimeModelMode.Discrete };
            world.AttachDiscreteRouting(clock, scheduler);

            world.SubmitIntent(new Intent(Hero, "move"));

            Assert.Single(world.DiagnosticsWarnings);
        }

        [Fact]
        public void World_ClearAll_OnEmptyWorld_IsHarmless()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            world.ClearAll();
            world.ClearAll();
            Assert.Equal(0, world.EntityCount);
        }

        [Fact]
        public void World_MarkForDestruction_UnknownId_DoesNotThrowOnTick()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            world.MarkForDestruction(new Id("test.ghost"));

            world.Tick(SimStep.Continuous(0.1));

            Assert.False(world.IsPendingDestruction(new Id("test.ghost")));
        }

        // -----------------------------------------------------------------
        // TurnScheduler
        // -----------------------------------------------------------------

        private static (TurnScheduler scheduler, WorldSim world) BuildScheduler(Func<Id, bool>? isPlayer = null)
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var scheduler = new TurnScheduler(world, id => 0, isPlayer ?? (id => true), bus);
            scheduler.Configure(InitiativePolicy.FixedOrder, new Dictionary<string, object>());
            return (scheduler, world);
        }

        [Fact]
        public void Scheduler_BeginCombat_EmptyOrNull_ThrowsAndStaysOutOfCombat()
        {
            var (scheduler, _) = BuildScheduler();

            Assert.Throws<ArgumentException>(() => scheduler.BeginCombat(new List<Id>()));
            Assert.Throws<ArgumentException>(() => scheduler.BeginCombat(null!));

            Assert.Null(scheduler.NextStep());
            Assert.Null(scheduler.GetCurrentActor());
            Assert.Empty(scheduler.GetOrder());
        }

        [Fact]
        public void Scheduler_BeginCombat_EmptyWhileInCombat_KeepsPreviousCombatIntact()
        {
            var (scheduler, _) = BuildScheduler(id => false);
            scheduler.BeginCombat(new[] { Hero, Foe });

            Assert.Throws<ArgumentException>(() => scheduler.BeginCombat(new List<Id>()));

            Assert.Equal(new[] { Hero, Foe }, scheduler.GetOrder());
            Assert.Equal(Hero, scheduler.GetCurrentActor());
        }

        [Fact]
        public void Scheduler_SubmitIntent_OutsideCombat_ThrowsInvalidOperation()
        {
            var (scheduler, _) = BuildScheduler();

            Assert.Throws<InvalidOperationException>(() => scheduler.SubmitIntent(Hero, new Intent(Hero, "move")));
        }

        [Fact]
        public void Scheduler_SubmitIntent_AfterEndCombat_ThrowsInvalidOperation()
        {
            var (scheduler, _) = BuildScheduler();
            scheduler.BeginCombat(new[] { Hero, Foe });
            scheduler.EndCombat();

            Assert.Throws<InvalidOperationException>(() => scheduler.SubmitIntent(Hero, new Intent(Hero, "move")));
        }

        [Fact]
        public void Scheduler_SubmitIntent_IntentActorMismatchesActorId_Throws_AndDoesNotUnblock()
        {
            var (scheduler, world) = BuildScheduler();
            scheduler.BeginCombat(new[] { Hero, Foe });

            var ex = Assert.Throws<ArgumentException>(() => scheduler.SubmitIntent(Hero, new Intent(Foe, "move")));
            Assert.Equal("intent", ex.ParamName);

            Assert.Null(scheduler.NextStep());
            world.Tick(SimStep.Continuous(0.1));
            Assert.Empty(world.CurrentIntents);
        }

        [Fact]
        public void Scheduler_Load_NonObjectSection_ThrowsFormatException()
        {
            var (scheduler, _) = BuildScheduler();

            Assert.Throws<FormatException>(() => scheduler.Load(new JsonString("oops")));
            Assert.Throws<FormatException>(() => scheduler.Load(new JsonNumber(1)));
            Assert.Throws<FormatException>(() => scheduler.Load(new JsonArray(new JsonValue[0])));
        }

        [Fact]
        public void Scheduler_Constructor_NullDependencies_Throw()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);

            Assert.Throws<ArgumentNullException>(() => new TurnScheduler(null!, id => 0, id => false, bus));
            Assert.Throws<ArgumentNullException>(() => new TurnScheduler(world, null!, id => false, bus));
            Assert.Throws<ArgumentNullException>(() => new TurnScheduler(world, id => 0, null!, bus));
            Assert.Throws<ArgumentNullException>(() => new TurnScheduler(world, id => 0, id => false, null!));
        }
    }
}
