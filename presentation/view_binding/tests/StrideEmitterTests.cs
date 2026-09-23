using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Presentation.ViewBinding;
using Xunit;

namespace Tests.PresentationViewBinding
{
    /// <summary>ADR-0078：<see cref="StrideEmitter"/> 的单元测试——只覆盖累计/触发/钳制这套纯逻辑
    /// 本身（不依赖真实 <c>MovementTickHandler</c> 推进，直接构造 <see cref="UnitMovedEvent"/> 入队，
    /// 同 <c>presentation/view_binding/tests/ViewBinderTests.cs</c> 一贯的"手工投喂事件"测试风格）；
    /// 经真实 <c>PresentationAssembly.Stride</c>/真实 <c>WorldSim.Tick</c> 推进的端到端证据见
    /// <c>presentation/assembly/tests/PresentationAssemblyTests.cs</c>。</summary>
    public class StrideEmitterTests
    {
        private const string MapId = "map.stride_test";

        private static (StrideEmitter Emitter, IWorldSim World, IEventBus Bus, FakeDisplayInfoRegistry Display)
            Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var display = new FakeDisplayInfoRegistry();
            var emitter = new StrideEmitter(world, display, bus);
            return (emitter, world, bus, display);
        }

        /// <summary>用 <see cref="PlayerUnit"/> 造一个最小实体，<c>TemplateId</c> 即
        /// <see cref="StrideEmitter"/> 解析步幅距离时查 <c>display.map.logical_id</c> 用的 id（同
        /// <c>WorldSim.AddEntity</c>/<c>ViewBinder.OnEntityCreated</c> 既有 <c>entity.TemplateId ??
        /// entity.EntityId</c> 惯例）。</summary>
        private static Id SpawnUnit(IWorldSim world, Id unitId, Id templateId, Vec2 position)
        {
            var unit = new PlayerUnit(unitId, new Id(MapId), new Id("fac.player"), new Id("arch.sample"))
            {
                Position = position,
                TemplateId = templateId,
            };
            world.AddEntity(unit);
            return unitId;
        }

        private static List<UnitStrideCompletedEvent> Listen(IEventBus bus)
        {
            var received = new List<UnitStrideCompletedEvent>();
            bus.Subscribe<UnitStrideCompletedEvent>(
                ViewBindingEventKeys.UnitStrideCompleted, e => received.Add(e));
            return received;
        }

        [Fact]
        public void UnitWithoutStrideDistanceRegistered_EmitsZeroEvents_EvenAfterLargeMovement()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.no_stride");
            var templateId = new Id("creature.no_stride");
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            // 未登记 display.map 行 —— Lookup 恒返回 null。
            var received = Listen(bus);

            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(100, 0)));
            bus.DispatchPending();

            Assert.Empty(received);
        }

        [Fact]
        public void UnitWithZeroOrNegativeStrideDistance_EmitsZeroEvents()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.zero_stride");
            var templateId = new Id("creature.zero_stride");
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance: 0.0);
            var received = Listen(bus);

            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(50, 0)));
            bus.DispatchPending();

            Assert.Empty(received);
        }

        /// <summary>核心累计规则：步幅距离 2.0，从 (0,0) 移动到 (5,0)（首次观测不算位移，只记录起点），
        /// 随后每次 +1 沿 X 轴推进。总位移 5，按 floor(5 / 2) = 2 条整，剩余 1 保留在累计值里，
        /// 期望条数由规则本身算出，不写死裸数。</summary>
        [Fact]
        public void AccumulatedDisplacement_EmitsFloorOfDistanceOverStride_PreservesRemainder()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.walker");
            var templateId = new Id("creature.walker");
            const double strideDistance = 2.0;
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            // 首次 unit.moved 只建立"上一次位置"基准，不产生位移、不触发事件（见 StrideEmitter
            // 判断记录"首次观测不产生虚假位移"）。
            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero));
            bus.DispatchPending();
            Assert.Empty(received);

            var totalDistance = 5.0;
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(totalDistance, 0)));
            bus.DispatchPending();

            var expectedCount = (int)System.Math.Floor(totalDistance / strideDistance);
            Assert.Equal(expectedCount, received.Count);
            Assert.All(received, e => Assert.Equal(unitId, e.UnitId));

            // 余数保留：再走 strideDistance - (totalDistance % strideDistance) 之后应恰好再触发一次，
            // 证明累计值不是被清零重置、而是真的延续到了下一次移动。
            received.Clear();
            var remainder = totalDistance % strideDistance;
            var topUp = strideDistance - remainder;
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(totalDistance + topUp, 0)));
            bus.DispatchPending();

            Assert.Single(received);
        }

        /// <summary>瞬移钳制：单次位移超过步幅距离的
        /// <see cref="StrideEmitter.TeleportDistanceMultiplier"/>（8）倍，只发一条，累计清零——
        /// 紧随其后的一次正常小步移动不应该因为"钳制前残留的累计"而立即再触发一次。</summary>
        [Fact]
        public void TeleportDisplacement_EmitsExactlyOneEvent_AndResetsAccumulatorToZero()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.teleporter");
            var templateId = new Id("creature.teleporter");
            const double strideDistance = 1.0;
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero)); // 建立基准
            bus.DispatchPending();

            var teleportDistance = strideDistance * StrideEmitter.TeleportDistanceMultiplier + 1.0;
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(teleportDistance, 0)));
            bus.DispatchPending();

            Assert.Single(received); // 不是按 floor(distance / stride) 发一大串。

            // 累计清零：紧接着一次远小于一个步幅距离的正常移动不应触发（若累计未清零、残留了瞬移
            // 落地时的余数，这里就会意外多触发一次)。
            received.Clear();
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(teleportDistance + strideDistance * 0.5, 0)));
            bus.DispatchPending();

            Assert.Empty(received);
        }

        /// <summary>重新登记状态清理：单位一度登记了步幅距离并产生过累计，随后（例如换装/换外形，
        /// TemplateId 对应的 display.map 行被移除）退化为未登记，本组件应清掉该单位残留的位置/累计
        /// 状态，不让下次重新登记时被污染（见 <see cref="StrideEmitter"/> 判断记录）。</summary>
        [Fact]
        public void UnitDegradingToUnregistered_ClearsResidualState_NoStaleAccumulationOnReRegister()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.re_register");
            var templateId = new Id("creature.re_register");
            const double strideDistance = 10.0;
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero));
            bus.DispatchPending();
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(9, 0))); // 累计 9，未达 10，不触发
            bus.DispatchPending();
            Assert.Empty(received);

            // 退化为未登记。
            display.Unregister(templateId);
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(9.5, 0)));
            bus.DispatchPending();
            Assert.Empty(received); // 未登记状态下完全不发。

            // 重新登记，从 0 开始累计（不是接着此前残留的 9）。
            display.Register(templateId, strideDistance);
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(9.5 + 9.9, 0))); // 再走 9.9，未达 10
            bus.DispatchPending();
            Assert.Empty(received);
        }

        /// <summary>最小可运行 <see cref="IDisplayInfoRegistry"/> 假实现，只承载
        /// <see cref="StrideEmitter"/> 实际用到的 <see cref="Lookup"/>，其余成员本套件不会触达。</summary>
        private sealed class FakeDisplayInfoRegistry : IDisplayInfoRegistry
        {
            private readonly Dictionary<Id, DisplayInfo> _byLogicalId = new Dictionary<Id, DisplayInfo>();

            public void Register(Id logicalId, double strideDistance)
            {
                _byLogicalId[logicalId] = new DisplayInfo(
                    id: new Id("display.map." + logicalId.Value),
                    category: DisplayCategory.Creature,
                    logicalId: logicalId,
                    kind: DisplayKind.Sprite,
                    iconId: null,
                    vfxId: null,
                    sfxId: null,
                    scale: 1.0,
                    shadow: ShadowMode.None,
                    sortOffset: 0,
                    weaponStyleRef: null,
                    sprite: null,
                    model: null,
                    strideDistance: strideDistance);
            }

            public void Unregister(Id logicalId) => _byLogicalId.Remove(logicalId);

            public DisplayInfo? Lookup(Id logicalId) =>
                _byLogicalId.TryGetValue(logicalId, out var info) ? info : null;

            public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category) =>
                new List<DisplayInfo>();

            public IReadOnlyList<DisplayInfo> All => new List<DisplayInfo>();

            public void Reload()
            {
            }
        }
    }
}
