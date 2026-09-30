using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Gameplay.AreaTrigger;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>
    /// ADR-0114 第 9 条（D9）：<see cref="AreaTriggerHost"/> 订阅 <c>entity.destroyed</c>，区内实体被
    /// 销毁时补发 <see cref="AreaTriggerLeaveReason.Despawned"/> 的离开事件并清 <c>_inside</c>；
    /// 死亡但实体仍存在不算离开。期望值全部由登记的触发体集合算出，不写死裸数。
    /// </summary>
    public sealed class AreaTriggerDespawnTests
    {
        private static readonly Id SampleMap = new Id("world.sample_map");
        private static readonly string[] TriggerIds = { "area.sample_grove", "area.sample_pond" };

        private sealed class TestUnit : Entity
        {
            public TestUnit(Id id) : base(id, SampleMap) { }

            public override string Kind => EntityKinds.Creature;
        }

        private sealed class Fixture
        {
            public IEventBus Bus = AreaTriggerTestSupport.NewEventBus();
            public WorldSim World = null!;
            public AreaTriggerHost Host = null!;
            public List<IEvent> Events = new List<IEvent>();

            public Fixture()
            {
                var registry = AreaTriggerTestSupport.BuildRegistry(Bus,
                    TriggerIds.Select(t => AreaTriggerTestSupport.QuestExploreRow(t, "world.sample_map")).ToArray());
                registry.LoadAll();
                World = new WorldSim(Bus);
                var worldState = new Core.Gameplay.WorldState.WorldState(Bus);
                Host = new AreaTriggerHost(World, worldState, Bus, new FakeExprHostFactory());
                Host.LoadForMap(SampleMap, registry);
                Bus.Subscribe(AreaTriggerEventKeys.TriggerEntered, e => Events.Add(e));
                Bus.Subscribe(AreaTriggerEventKeys.TriggerLeft, e => Events.Add(e));
            }

            public void Spawn(Id unit) => World.AddEntity(new TestUnit(unit));

            /// <summary>销毁实体：MarkForDestruction 在 Tick 阶段 8 才生效，entity.destroyed 随后派发。</summary>
            public void Destroy(Id unit)
            {
                World.MarkForDestruction(unit);
                World.Tick(SimStep.Continuous(0.016));
                Bus.DispatchPending();
            }

            public IReadOnlyList<AreaTriggerLeftEvent> Lefts() => Events.OfType<AreaTriggerLeftEvent>().ToList();
            public IReadOnlyList<AreaTriggerEnteredEvent> Entereds() => Events.OfType<AreaTriggerEnteredEvent>().ToList();
        }

        private static readonly Vec2 Inside = new Vec2(0, 0);

        /// <summary>复现（修复前 Left 事件数为 0、_inside 残留）：区内实体被销毁 → 每个所在触发体各收到一条
        /// Despawned 离开事件，且 GetActiveTriggerIds 不再包含该实体。</summary>
        [Fact]
        public void EntityDestroyedInsideZones_EmitsDespawnedLeavePerTrigger_AndClearsInside()
        {
            var f = new Fixture();
            var unit = new Id("unit.sample_player");
            f.Spawn(unit);
            f.Host.Evaluate(unit, Inside);
            Assert.Equal(TriggerIds.Length, f.Host.GetActiveTriggerIds(unit).Count);
            f.Events.Clear();

            f.Destroy(unit);

            var lefts = f.Lefts();
            Assert.Equal(TriggerIds.Length, lefts.Count);
            Assert.All(lefts, l =>
            {
                Assert.Equal(unit, l.UnitId);
                Assert.Equal(AreaTriggerLeaveReason.Despawned, l.Reason);
            });
            var expectedOrder = TriggerIds.Select(t => new Id(t)).OrderBy(x => x).ToList();
            Assert.Equal(expectedOrder, lefts.Select(l => l.TriggerId).ToList());
            Assert.Empty(f.Host.GetActiveTriggerIds(unit));
        }

        /// <summary>不变量：销毁后同 Id 再生成并再次进入 → 正常收到 Entered（不被残留的 _inside 吞掉）。</summary>
        [Fact]
        public void EntityDestroyedThenRespawnedWithSameId_ReEntersNormally()
        {
            var f = new Fixture();
            var unit = new Id("unit.sample_player");
            f.Spawn(unit);
            f.Host.Evaluate(unit, Inside);
            f.Destroy(unit);
            f.Events.Clear();

            f.Spawn(unit);
            f.Host.Evaluate(unit, Inside);
            f.Bus.DispatchPending();

            Assert.Equal(TriggerIds.Length, f.Entereds().Count);
            Assert.Empty(f.Lefts());
            Assert.Equal(TriggerIds.Length, f.Host.GetActiveTriggerIds(unit).Count);
        }

        /// <summary>不变量：单位死亡但实体仍存在（尸体仍在区内）→ 不发离开事件、仍记为在区内。</summary>
        [Fact]
        public void UnitDiedButEntityStillExists_DoesNotEmitLeave()
        {
            var f = new Fixture();
            var unit = new Id("unit.sample_player");
            f.Spawn(unit);
            f.Host.Evaluate(unit, Inside);
            f.Events.Clear();

            f.Bus.Enqueue(new UnitDiedEvent(unit, null, SampleMap, Inside));
            f.World.Tick(SimStep.Continuous(0.016));
            f.Bus.DispatchPending();

            Assert.NotNull(f.World.GetEntity(unit));
            Assert.Empty(f.Lefts());
            Assert.Equal(TriggerIds.Length, f.Host.GetActiveTriggerIds(unit).Count);
        }

        /// <summary>不变量：与 Unloaded 补发共存不重复——先卸载后销毁，每个触发体只有一条 Unloaded。</summary>
        [Fact]
        public void UnloadThenDestroy_EmitsOnlyUnloaded_NoDuplicate()
        {
            var f = new Fixture();
            var unit = new Id("unit.sample_player");
            f.Spawn(unit);
            f.Host.Evaluate(unit, Inside);
            f.Events.Clear();

            f.Host.UnloadMap(SampleMap);
            f.Bus.DispatchPending();
            f.Destroy(unit);

            var lefts = f.Lefts();
            Assert.Equal(TriggerIds.Length, lefts.Count);
            Assert.All(lefts, l => Assert.Equal(AreaTriggerLeaveReason.Unloaded, l.Reason));
        }

        /// <summary>不变量：与 Unloaded 补发共存不重复——先销毁后卸载，每个触发体只有一条 Despawned。</summary>
        [Fact]
        public void DestroyThenUnload_EmitsOnlyDespawned_NoDuplicate()
        {
            var f = new Fixture();
            var unit = new Id("unit.sample_player");
            f.Spawn(unit);
            f.Host.Evaluate(unit, Inside);
            f.Events.Clear();

            f.Destroy(unit);
            f.Host.UnloadMap(SampleMap);
            f.Bus.DispatchPending();

            var lefts = f.Lefts();
            Assert.Equal(TriggerIds.Length, lefts.Count);
            Assert.All(lefts, l => Assert.Equal(AreaTriggerLeaveReason.Despawned, l.Reason));
        }
    }
}
