using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.DisplayInfo;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Presentation.ViewBinding;
using Xunit;

namespace Tests.PresentationViewBinding
{
    /// <summary>
    /// 脚步的两个来源（ADR-0148，手感设计/04 第 5 节）：动画剪辑的 <c>footstep</c> 标记与几何步幅事件 <c>unit.stride_completed</c>。
    /// 剪辑声明了标记的单位以标记为唯一来源——<see cref="StrideEmitter.Suppress"/> 返回真的单位不再发步幅事件；
    /// 没有标记的单位（缺省 null 判定、或判定返回假）逐位保持原行为。
    /// </summary>
    public sealed class StrideEmitterSuppressTests
    {
        private const string MapId = "map.stride_suppress";
        private const double Stride = 2.0;

        private static (StrideEmitter Emitter, IEventBus Bus, List<UnitStrideCompletedEvent> Received, Id UnitId) Build()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var display = new StrideDisplayRegistry();
            var emitter = new StrideEmitter(world, display, bus);
            var unitId = new Id("unit.walker");
            var templateId = new Id("creature.walker");
            world.AddEntity(new PlayerUnit(unitId, new Id(MapId), new Id("fac.player"), new Id("arch.sample"))
            {
                Position = Vec2.Zero,
                TemplateId = templateId,
            });
            display.Register(templateId, Stride);
            var received = new List<UnitStrideCompletedEvent>();
            bus.Subscribe<UnitStrideCompletedEvent>(ViewBindingEventKeys.UnitStrideCompleted, e => received.Add(e));
            return (emitter, bus, received, unitId);
        }

        private static void Walk(IEventBus bus, Id unitId, double distance)
        {
            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero));
            bus.DispatchPending();
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(distance, 0)));
            bus.DispatchPending();
        }

        [Fact]
        public void WithoutASuppressPredicate_StrideEventsAreEmittedAsBefore()
        {
            var (_, bus, received, unitId) = Build();
            Walk(bus, unitId, 5.0);
            Assert.Equal((int)(5.0 / Stride), received.Count);
        }

        [Fact]
        public void SuppressedUnit_EmitsNoStrideEvents_ButTheAccumulationKeepsRunning()
        {
            // 复现：剪辑有 footstep 标记的单位，此前标记与步幅事件各出一次声，一步两响。
            var (emitter, bus, received, unitId) = Build();
            var suppressed = true;
            emitter.Suppress = id => id.Equals(unitId) && suppressed;

            Walk(bus, unitId, 5.0);
            Assert.Empty(received);

            // 判定变回假（例如换了没有标记的外形）：累计从当前位置接着走，余量 1 + 再走 1 = 一个整步。
            suppressed = false;
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(6.0, 0)));
            bus.DispatchPending();
            Assert.Single(received);
        }

        [Fact]
        public void SuppressPredicateReturningFalse_IsBitIdenticalToNoPredicate()
        {
            var plain = Build();
            var guarded = Build();
            guarded.Emitter.Suppress = _ => false;
            Walk(plain.Bus, plain.UnitId, 9.0);
            Walk(guarded.Bus, guarded.UnitId, 9.0);
            Assert.Equal(plain.Received.Count, guarded.Received.Count);
        }

        private sealed class StrideDisplayRegistry : IDisplayInfoRegistry
        {
            private readonly Dictionary<Id, DisplayInfo> _byLogicalId = new Dictionary<Id, DisplayInfo>();

            public void Register(Id logicalId, double strideDistance)
            {
                _byLogicalId[logicalId] = new DisplayInfo(
                    id: new Id("display.map." + logicalId.Value), category: DisplayCategory.Creature, logicalId: logicalId,
                    kind: DisplayKind.Sprite, iconId: null, vfxId: null, sfxId: null, scale: 1.0, shadow: ShadowMode.None,
                    sortOffset: 0, weaponStyleRef: null, sprite: null, model: null, strideDistance: strideDistance);
            }

            public DisplayInfo? Lookup(Id logicalId) => _byLogicalId.TryGetValue(logicalId, out var info) ? info : null;

            public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category) => new List<DisplayInfo>();

            public IReadOnlyList<DisplayInfo> All => new List<DisplayInfo>();

            public void Reload()
            {
            }
        }
    }
}
