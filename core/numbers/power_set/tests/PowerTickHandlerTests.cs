using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Xunit;

namespace Tests.Numbers.PowerSet
{
    /// <summary>记录 <see cref="IPowerHost.AdvanceAll"/> 调用次数与参数的测试替身，
    /// 其余成员本测试用不到，一律抛异常以便一旦被意外调用能立刻发现。</summary>
    internal sealed class RecordingPowerHost : IPowerHost
    {
        public readonly List<double> AdvanceAllCalls = new List<double>();

        public void AdvanceAll(double timeUnits) => AdvanceAllCalls.Add(timeUnits);

        public void RegisterUnit(Id unitId, IReadOnlyList<Id> powerTypes) => throw new NotImplementedException();
        public void UnregisterUnit(Id unitId) => throw new NotImplementedException();
        public bool HasPower(Id unitId, Id powerType) => throw new NotImplementedException();
        public double GetPower(Id unitId, Id powerType) => throw new NotImplementedException();
        public double GetPowerMax(Id unitId, Id powerType) => throw new NotImplementedException();
        public void ModifyPower(Id unitId, Id powerType, double delta, Id sourceId) => throw new NotImplementedException();
        public void SetInCombat(Id unitId, bool inCombat) => throw new NotImplementedException();
        public void Advance(Id unitId, double timeUnits) => throw new NotImplementedException();
        public void RecomputeMax(Id unitId) => throw new NotImplementedException();
    }

    public class PowerTickHandlerTests
    {
        private static IWorldSim MakeWorld()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(SimEventKeys.TickStarted, "sim", new[] { "tickIndex", "dt" }),
                new EventDefinition(SimEventKeys.TickFinished, "sim", new[] { "tickIndex" }),
                new EventDefinition(SimEventKeys.EntityCreated, "entity", new[] { "entityId", "kind", "displayId" }),
                new EventDefinition(SimEventKeys.EntityDestroyed, "entity", new[] { "entityId" }),
            });
            return new WorldSim(new EventBus(catalog));
        }

        [Fact]
        public void Execute_ContinuousStep_CallsAdvanceAllWithDt()
        {
            var host = new RecordingPowerHost();
            var handler = new PowerTickHandler(host);
            var world = MakeWorld();

            handler.Execute(SimStep.Continuous(0.25), world);

            Assert.Single(host.AdvanceAllCalls);
            Assert.Equal(0.25, host.AdvanceAllCalls[0]);
        }

        [Fact]
        public void Execute_DiscreteStep_DoesNotAdvance_AndWarns()
        {
            var host = new RecordingPowerHost();
            var diagnostics = new InMemoryPowerDiagnostics();
            var handler = new PowerTickHandler(host, diagnostics);
            var world = MakeWorld();

            handler.Execute(SimStep.Discrete(new Id("unit.hero"), StepPhase.Act), world);

            Assert.Empty(host.AdvanceAllCalls);
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void RegisteredOnWorldSim_ContinuousTick_InvokesHandler()
        {
            var host = new RecordingPowerHost();
            var handler = new PowerTickHandler(host);
            var world = MakeWorld();
            world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, handler);

            world.Tick(SimStep.Continuous(0.5));

            Assert.Single(host.AdvanceAllCalls);
            Assert.Equal(0.5, host.AdvanceAllCalls[0]);
        }
    }
}
