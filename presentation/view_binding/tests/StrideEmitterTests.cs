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

        /// <summary>D23（ADR-0125）复现 + 不变量一：实体销毁后同 id 再生成在远处，第一次
        /// <c>unit.moved</c> 只建立新基准，不得拿销毁前的旧位置算位移（修复前会按旧位置 (0,0) 到新位置
        /// 的巨大距离判为瞬移，虚发一条 <c>unit.stride_completed</c>）。</summary>
        [Fact]
        public void EntityDestroyed_ThenSameIdRespawnedFarAway_FirstMoveProducesNoSpuriousStride()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.reused");
            var templateId = new Id("creature.reused");
            const double strideDistance = 2.0;
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero)); // 建立基准
            bus.DispatchPending();
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(strideDistance, 0))); // 正常走出一个步幅
            bus.DispatchPending();
            Assert.Single(received);
            received.Clear();

            // 销毁 -> 同 id 在远处（远超瞬移阈值）重新生成。
            bus.Enqueue(new EntityDestroyedEvent(unitId));
            bus.DispatchPending();
            var farAway = strideDistance * StrideEmitter.TeleportDistanceMultiplier * 10;
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(farAway, 0)));
            bus.DispatchPending();

            Assert.Empty(received);
        }

        /// <summary>D23 不变量二：销毁同时清掉累计余数——销毁前累计了一段不足一个步幅的余数，
        /// 同 id 重生后从零累计：再走 (stride - 余数) 这么远不得触发（若余数残留就会触发）。</summary>
        [Fact]
        public void EntityDestroyed_ClearsAccumulatedRemainder_RespawnedUnitAccumulatesFromZero()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.reused_acc");
            var templateId = new Id("creature.reused_acc");
            const double strideDistance = 10.0;
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero));
            bus.DispatchPending();
            var carried = strideDistance * 0.9;
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(carried, 0))); // 累计 0.9 个步幅，未触发
            bus.DispatchPending();
            Assert.Empty(received);

            bus.Enqueue(new EntityDestroyedEvent(unitId));
            bus.DispatchPending();

            // 重生：首个 unit.moved 只建基准；再走 0.2 个步幅。若旧余数 0.9 残留，会凑满 1.1 个步幅而触发。
            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero));
            bus.DispatchPending();
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(strideDistance * 0.2, 0)));
            bus.DispatchPending();

            Assert.Empty(received);
        }

        /// <summary>D23 不变量三：只清被销毁实体自己的状态，其它实体的累计不受影响。</summary>
        [Fact]
        public void EntityDestroyed_DoesNotDisturbOtherEntitiesState()
        {
            var (_, world, bus, display) = Build();
            var keepId = new Id("unit.keep");
            var goneId = new Id("unit.gone");
            var templateId = new Id("creature.shared");
            const double strideDistance = 10.0;
            SpawnUnit(world, keepId, templateId, Vec2.Zero);
            SpawnUnit(world, goneId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            foreach (var id in new[] { keepId, goneId })
            {
                bus.Enqueue(new UnitMovedEvent(id, Vec2.Zero));
            }
            bus.DispatchPending();
            bus.Enqueue(new UnitMovedEvent(keepId, new Vec2(strideDistance * 0.6, 0)));
            bus.DispatchPending();
            Assert.Empty(received);

            bus.Enqueue(new EntityDestroyedEvent(goneId));
            bus.DispatchPending();

            // keep 累计的 0.6 仍在：再走 0.5 个步幅共 1.1 个，恰触发一次。
            bus.Enqueue(new UnitMovedEvent(keepId, new Vec2(strideDistance * 1.1, 0)));
            bus.DispatchPending();

            var only = Assert.Single(received);
            Assert.Equal(keepId, only.UnitId);
        }

        /// <summary>Dispose 同时退订 <c>unit.moved</c> 与实体销毁事件：之后再投喂任何事件都不再产生输出、
        /// 也不抛异常。</summary>
        [Fact]
        public void Dispose_UnsubscribesFromMovedAndDestroyedEvents()
        {
            var (emitter, world, bus, display) = Build();
            var unitId = new Id("unit.disposed");
            var templateId = new Id("creature.disposed");
            const double strideDistance = 2.0;
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            emitter.Dispose();

            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero));
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(strideDistance * 3, 0)));
            bus.Enqueue(new EntityDestroyedEvent(unitId));
            bus.DispatchPending();

            Assert.Empty(received);
        }

        // -----------------------------------------------------------------
        // T-M42（测试覆盖剩余项第四批）：瞬移阈值边界（> 而非 >=）、读档后位置突变、事件字段契约、
        // 实体解析的退化路径。期望值由 TeleportDistanceMultiplier 与步幅距离算出，不写裸数。
        // -----------------------------------------------------------------

        /// <summary>阈值边界：单次位移恰为 8×步幅距离不算瞬移（判定是严格大于），按整除发
        /// floor(delta / stride) = 倍数 条，累计余数为 0；位移比阈值大一个浮点最小量才被钳制成一条。</summary>
        [Fact]
        public void DisplacementExactlyAtTeleportThreshold_IsNotClamped_JustAboveIsClampedToOne()
        {
            var (_, world, bus, display) = Build();
            var templateId = new Id("creature.boundary");
            const double strideDistance = 1.0;
            display.Register(templateId, strideDistance);
            var threshold = strideDistance * StrideEmitter.TeleportDistanceMultiplier;

            // 恰等于阈值。
            var atId = SpawnUnit(world, new Id("unit.boundary_at"), templateId, Vec2.Zero);
            var received = Listen(bus);
            bus.Enqueue(new UnitMovedEvent(atId, Vec2.Zero));
            bus.DispatchPending();
            bus.Enqueue(new UnitMovedEvent(atId, new Vec2(threshold, 0)));
            bus.DispatchPending();

            var expectedAtThreshold = (int)System.Math.Floor(threshold / strideDistance);
            Assert.Equal(expectedAtThreshold, received.Count);
            Assert.Equal((int)StrideEmitter.TeleportDistanceMultiplier, received.Count);

            // 比阈值大一个浮点最小量：被钳制，只发一条。
            received.Clear();
            var aboveId = SpawnUnit(world, new Id("unit.boundary_above"), templateId, Vec2.Zero);
            bus.Enqueue(new UnitMovedEvent(aboveId, Vec2.Zero));
            bus.DispatchPending();
            bus.Enqueue(new UnitMovedEvent(aboveId, new Vec2(System.Math.BitIncrement(threshold), 0)));
            bus.DispatchPending();

            Assert.Equal(aboveId, Assert.Single(received).UnitId);
        }

        /// <summary>读档路径：SceneRouter 读档会 <c>world.ClearAll()</c>（逐实体 EntityDestroyed）再重建实体。
        /// 读档后同 id 实体出现在远离读档前的位置，首个 <c>unit.moved</c> 只建立新基准，不得按读档前的位置算位移
        /// 而虚发步幅事件；此后正常行走照常累计。</summary>
        [Fact]
        public void AfterWorldClearAllAndRestoreAtFarPosition_NoSpuriousStride_ThenNormalWalkingAccumulates()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.restored");
            var templateId = new Id("creature.restored");
            const double strideDistance = 2.0;
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero));
            bus.DispatchPending();
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(strideDistance * 0.75, 0))); // 带着一段未满的累计读档
            bus.DispatchPending();
            Assert.Empty(received);

            // 读档：清空世界（经 EntityDestroyed 通知），再把实体放到远离原位置的存档坐标。
            world.ClearAll();
            bus.DispatchPending();
            var restoredAt = new Vec2(strideDistance * StrideEmitter.TeleportDistanceMultiplier * 50, 7);
            SpawnUnit(world, unitId, templateId, restoredAt);

            bus.Enqueue(new UnitMovedEvent(unitId, restoredAt));
            bus.DispatchPending();
            Assert.Empty(received);

            // 从读档位置正常走一个步幅：恰好一条，且不含旧累计（旧累计 0.75 若残留会提前触发）。
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(restoredAt.X + strideDistance * 0.5, restoredAt.Y)));
            bus.DispatchPending();
            Assert.Empty(received);
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(restoredAt.X + strideDistance, restoredAt.Y)));
            bus.DispatchPending();
            Assert.Single(received);
        }

        /// <summary>契约：<see cref="UnitStrideCompletedEvent.TryGetField"/> 只暴露 <c>unitId</c>；
        /// <c>position</c>（Vec2 复合类型，Expr 无对应类型）与未知字段名一律返回 false，事件 key 固定。</summary>
        [Fact]
        public void UnitStrideCompletedEvent_TryGetField_ExposesUnitIdOnly_PositionIsFalse()
        {
            var unitId = new Id("unit.contract");
            var position = new Vec2(3, 4);
            var evt = new UnitStrideCompletedEvent(unitId, position);

            Assert.Equal(ViewBindingEventKeys.UnitStrideCompleted, evt.Key);
            Assert.True(evt.TryGetField("unitId", out var value));
            Assert.Equal(unitId, value.AsId);
            Assert.False(evt.TryGetField("position", out _));
            Assert.False(evt.TryGetField("Position", out _));
            Assert.False(evt.TryGetField("nonexistent", out _));
            Assert.Equal(position, evt.Position);
        }

        /// <summary>每条步幅事件携带触发它的那次 <c>unit.moved</c> 的位置；一次位移跨越多个步幅时，
        /// 全部事件都带同一个（最终）位置。</summary>
        [Fact]
        public void StrideEvents_CarryThePositionOfTheTriggeringMove()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.pos_carrier");
            var templateId = new Id("creature.pos_carrier");
            const double strideDistance = 1.0;
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero));
            bus.DispatchPending();
            var destination = new Vec2(strideDistance * 3, 0);
            bus.Enqueue(new UnitMovedEvent(unitId, destination));
            bus.DispatchPending();

            Assert.Equal((int)System.Math.Floor(destination.X / strideDistance), received.Count);
            Assert.All(received, e => Assert.Equal(destination, e.Position));
        }

        /// <summary>原地不动（位移为 0）的 <c>unit.moved</c> 不发事件、不改变累计。</summary>
        [Fact]
        public void ZeroDisplacementMove_EmitsNothing_AndKeepsAccumulation()
        {
            var (_, world, bus, display) = Build();
            var unitId = new Id("unit.idle");
            var templateId = new Id("creature.idle");
            const double strideDistance = 4.0;
            SpawnUnit(world, unitId, templateId, Vec2.Zero);
            display.Register(templateId, strideDistance);
            var received = Listen(bus);

            bus.Enqueue(new UnitMovedEvent(unitId, Vec2.Zero));
            bus.DispatchPending();
            var half = new Vec2(strideDistance * 0.5, 0);
            bus.Enqueue(new UnitMovedEvent(unitId, half));
            bus.DispatchPending();
            bus.Enqueue(new UnitMovedEvent(unitId, half)); // 原地
            bus.DispatchPending();
            Assert.Empty(received);

            // 累计仍是 0.5 个步幅：再走 0.5 个恰好触发一次。
            bus.Enqueue(new UnitMovedEvent(unitId, new Vec2(strideDistance, 0)));
            bus.DispatchPending();
            Assert.Single(received);
        }

        /// <summary>实体解析退化：世界里没有该实体时，用 <c>unitId</c> 本身作为 display.map 的逻辑 id
        /// （<c>entity?.TemplateId ?? unitId</c>）；有实体但 TemplateId 为空同样退回 <c>unitId</c>。</summary>
        [Fact]
        public void StrideDistanceResolution_FallsBackToUnitId_WhenEntityMissingOrTemplateIdNull()
        {
            var (_, world, bus, display) = Build();
            const double strideDistance = 1.0;
            var ghostId = new Id("unit.ghost_not_in_world");
            var noTemplateId = new Id("unit.no_template");
            display.Register(ghostId, strideDistance);
            display.Register(noTemplateId, strideDistance);
            world.AddEntity(new PlayerUnit(noTemplateId, new Id(MapId), new Id("fac.player"), new Id("arch.sample")));
            var received = Listen(bus);

            foreach (var id in new[] { ghostId, noTemplateId })
            {
                bus.Enqueue(new UnitMovedEvent(id, Vec2.Zero));
                bus.DispatchPending();
                bus.Enqueue(new UnitMovedEvent(id, new Vec2(strideDistance, 0)));
                bus.DispatchPending();
            }

            Assert.Equal(2, received.Count);
            Assert.Contains(received, e => e.UnitId.Equals(ghostId));
            Assert.Contains(received, e => e.UnitId.Equals(noTemplateId));
        }

        /// <summary>构造守卫：任一依赖为 null 都抛 ArgumentNullException。</summary>
        [Fact]
        public void Constructor_NullArguments_Throw()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var display = new FakeDisplayInfoRegistry();

            Assert.Throws<System.ArgumentNullException>(() => new StrideEmitter(null!, display, bus));
            Assert.Throws<System.ArgumentNullException>(() => new StrideEmitter(world, null!, bus));
            Assert.Throws<System.ArgumentNullException>(() => new StrideEmitter(world, display, null!));
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
