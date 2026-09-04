using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    public class WorldSimTickOrderTests
    {
        [Fact]
        public void Tick_ExecutesRegisteredPhaseHandlers_InDeclaredTickPhaseOrder_WithBoundaryEvents()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var log = new List<string>();

            bus.Subscribe<SimTickStartedEvent>(SimEventKeys.TickStarted, _ => log.Add("event:tick_started"));
            bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ => log.Add("event:tick_finished"));

            var registrablePhases = new[]
            {
                TickPhase.IntentCollection,
                TickPhase.AiDecision,
                TickPhase.SkillPipeline,
                TickPhase.MovementAndNavigation,
                TickPhase.CombatResolution,
                TickPhase.TriggerEvaluation,
            };

            foreach (var phase in registrablePhases)
            {
                var capturedPhase = phase; // 避免闭包捕获循环变量的经典陷阱
                world.RegisterPhaseHandler(phase, new DelegatePhaseHandler((step, w) => log.Add("phase:" + capturedPhase)));
            }

            world.Tick(SimStep.Continuous(1.0 / 60.0));

            var expected = new List<string>
            {
                "event:tick_started",
                "phase:IntentCollection",
                "phase:AiDecision",
                "phase:SkillPipeline",
                "phase:MovementAndNavigation",
                "phase:CombatResolution",
                "phase:TriggerEvaluation",
                "event:tick_finished",
            };

            Assert.Equal(expected, log);
        }

        [Fact]
        public void EventsEnqueuedDuringAPhase_AreNotVisibleToSubscribersUntilEventDispatchPhase()
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var markerKey = new Id("test.phase_marker");

            var received = false;
            bus.Subscribe(markerKey, _ => received = true);

            var receivedStateSeenByPhase4 = false;

            // 阶段 3（SkillPipeline）的处理器 Enqueue 一个事件……
            world.RegisterPhaseHandler(TickPhase.SkillPipeline, new DelegatePhaseHandler((step, w) =>
            {
                bus.Enqueue(new GenericEvent(markerKey));
            }));

            // ……阶段 4（MovementAndNavigation）的处理器此时应该还没收到。
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new DelegatePhaseHandler((step, w) =>
            {
                receivedStateSeenByPhase4 = received;
            }));

            world.Tick(SimStep.Continuous(1.0 / 60.0));

            Assert.False(receivedStateSeenByPhase4);
            Assert.True(received); // Tick 返回后（阶段 7 已批量派发），订阅者已经收到
        }

        [Fact]
        public void RegisterPhaseHandler_OnEventDispatchOrLifecycleCleanup_ThrowsArgumentException()
        {
            var world = new WorldSim(SimLoopTestSupport.CreateBus());
            var handler = new DelegatePhaseHandler((step, w) => { });

            Assert.Throws<ArgumentException>(() => world.RegisterPhaseHandler(TickPhase.EventDispatch, handler));
            Assert.Throws<ArgumentException>(() => world.RegisterPhaseHandler(TickPhase.LifecycleCleanup, handler));
        }
    }
}
