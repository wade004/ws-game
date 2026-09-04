using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Gameplay.AreaTrigger;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>用一个记录调用次数的 <see cref="IAreaTriggerHost"/> 假实现验证
    /// <see cref="AreaTriggerTickHandler"/> 只在位置变化时才调用 <see cref="IAreaTriggerHost.Evaluate"/>
    /// （不依赖真实 <see cref="AreaTriggerHost"/> 的业务逻辑，只关心"调没调、调了几次"）。</summary>
    internal sealed class SpyAreaTriggerHost : IAreaTriggerHost
    {
        public int EvaluateCallCount;

        public Id Register(AreaTriggerDef def) => def.Id;

        public void Unregister(Id triggerId)
        {
        }

        public void Evaluate(Id unitId, Vec2 position) => EvaluateCallCount++;

        public void LoadForMap(Id mapId, Core.Foundation.DataRegistry.IDataRegistryView data)
        {
        }

        public void UnloadMap(Id mapId)
        {
        }

        public Id RegisterTrap(Id gobjInstanceId, Core.Foundation.EngineAdapter.Shape shape, Id mapId) => gobjInstanceId;
    }

    public sealed class AreaTriggerTickHandlerTests
    {
        [Fact]
        public void Execute_FirstTick_EvaluatesOnce()
        {
            var spy = new SpyAreaTriggerHost();
            var units = new FakeUnitAccess().Add(new Id("unit.sample_player"), new Vec2(0, 0));
            var handler = new AreaTriggerTickHandler(spy, units);

            handler.Execute(SimStep.Continuous(0.1), new Core.Foundation.SimLoop.WorldSim(AreaTriggerTestSupport.NewEventBus()));

            Assert.Equal(1, spy.EvaluateCallCount);
        }

        [Fact]
        public void Execute_PositionUnchanged_DoesNotEvaluateAgain()
        {
            var spy = new SpyAreaTriggerHost();
            var units = new FakeUnitAccess().Add(new Id("unit.sample_player"), new Vec2(0, 0));
            var handler = new AreaTriggerTickHandler(spy, units);

            handler.Execute(SimStep.Continuous(0.1), new Core.Foundation.SimLoop.WorldSim(AreaTriggerTestSupport.NewEventBus()));
            handler.Execute(SimStep.Continuous(0.1), new Core.Foundation.SimLoop.WorldSim(AreaTriggerTestSupport.NewEventBus()));

            Assert.Equal(1, spy.EvaluateCallCount);
        }

        [Fact]
        public void Execute_PositionChanged_EvaluatesAgain()
        {
            var spy = new SpyAreaTriggerHost();
            var units = new FakeUnitAccess().Add(new Id("unit.sample_player"), new Vec2(0, 0));
            var handler = new AreaTriggerTickHandler(spy, units);

            handler.Execute(SimStep.Continuous(0.1), new Core.Foundation.SimLoop.WorldSim(AreaTriggerTestSupport.NewEventBus()));
            units.Move(new Id("unit.sample_player"), new Vec2(1, 1));
            handler.Execute(SimStep.Continuous(0.1), new Core.Foundation.SimLoop.WorldSim(AreaTriggerTestSupport.NewEventBus()));

            Assert.Equal(2, spy.EvaluateCallCount);
        }

        [Fact]
        public void Execute_DiscreteStep_SkipsAndWarns()
        {
            var spy = new SpyAreaTriggerHost();
            var units = new FakeUnitAccess().Add(new Id("unit.sample_player"), new Vec2(0, 0));
            var diagnostics = new InMemoryAreaTriggerDiagnostics();
            var handler = new AreaTriggerTickHandler(spy, units, diagnostics);

            handler.Execute(SimStep.Discrete(new Id("unit.sample_player"), StepPhase.Act), new Core.Foundation.SimLoop.WorldSim(AreaTriggerTestSupport.NewEventBus()));

            Assert.Equal(0, spy.EvaluateCallCount);
            Assert.NotEmpty(diagnostics.Warnings);
        }
    }
}
