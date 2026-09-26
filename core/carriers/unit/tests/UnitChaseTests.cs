// ADR-0097《以单位为目标的追击移动请求》：MoveRequest.ToUnit 端到端测试（直接驱动
// MovementTickHandler，惯例同 MovementTickHandlerTests——不走 CarriersAssembly 装配）。
using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    public class UnitChaseTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id OtherMapId = new Id("map.other");
        private static readonly Id FactionId = new Id("fac.player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id ChaserId = new Id("unit.chaser");
        private static readonly Id TargetId = new Id("unit.target");

        /// <summary>计数 <see cref="INavigation2D.FindPath"/> 调用次数的包装（惯例同本模块既有
        /// FakeStatHost/FakeAuraQuery"只覆盖测试实际用到的行为"）：真正的直线寻路/阻挡判定全部委托给
        /// <see cref="StubNavigation2D"/>，只在外面加一层调用计数，供
        /// "MovementOptions.FollowRepathDistance 未超阈值不重规划"用例断言调用次数。</summary>
        private sealed class CountingNavigation2D : INavigation2D
        {
            private readonly StubNavigation2D _inner = new StubNavigation2D();

            public int FindPathCallCount { get; private set; }

            public void BuildNavMesh(Id mapId) => _inner.BuildNavMesh(mapId);

            public bool IsWalkable(Id mapId, Vec2 point) => _inner.IsWalkable(mapId, point);

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to)
            {
                FindPathCallCount++;
                return _inner.FindPath(mapId, from, to);
            }

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => _inner.Raycast(mapId, from, to);

            public int GetBlockingVersion(Id mapId) => _inner.GetBlockingVersion(mapId);

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects) => _inner.SetBlocking(mapId, rects);

            public void Clear(Id mapId) => _inner.Clear(mapId);
        }

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public List<IEvent> Events = null!;
            public WorldUnitAccess Units = null!;
            public FakeStatHost Stats = null!;
            public MovementHost Host = null!;
            public PlayerUnit Chaser = null!;
            public CreatureUnit Target = null!;
            public List<(Id UnitId, Vec2 Position, MoveStopReason Reason)> StoppedEvents = null!;
        }

        private static Fixture Build(
            double chaserSpeed = 10.0,
            Vec2? targetStart = null,
            INavigation2D? navigation = null,
            MovementOptions? options = null)
        {
            // StrictCatalog=false：本测试只关心 unit.moved/unit.state_changed 两个 key 的派发，
            // 惯例同 MovementTickHandlerTests.Build。
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var events = new List<IEvent>();
            bus.Subscribe(CarriersEventKeys.UnitMoved, e => events.Add(e));
            bus.Subscribe(CarriersEventKeys.UnitStateChanged, e => events.Add(e));

            var world = new WorldSim(bus);
            var chaser = new PlayerUnit(ChaserId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            var target = new CreatureUnit(TargetId, MapId, FactionId, new Id("creature.dummy"))
            {
                Position = targetStart ?? new Vec2(4, 0),
            };
            world.AddEntity(chaser);
            world.AddEntity(target);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            stats.SetBase(ChaserId, new MovementOptions().MoveSpeedStat, chaserSpeed);
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);
            var handler = new MovementTickHandler(units, stats, auras, host, bus, navigation, options);

            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            var stopped = new List<(Id, Vec2, MoveStopReason)>();
            host.OnMoveStopped += (unitId, position, reason) => stopped.Add((unitId, position, reason));

            return new Fixture
            {
                World = world,
                Events = events,
                Units = units,
                Stats = stats,
                Host = host,
                Chaser = chaser,
                Target = target,
                StoppedEvents = stopped,
            };
        }

        private static void RequestChase(Fixture fixture, double stopRange, MoveMode mode = MoveMode.Run) =>
            fixture.Host.Request(MoveRequest.ToUnit(ChaserId, TargetId, stopRange, mode));

        private static void Tick(Fixture fixture, double dt = 1.0) => fixture.World.Tick(SimStep.Continuous(dt));

        private static double DistanceChaserToTarget(Fixture fixture) =>
            (fixture.Units.GetPosition(TargetId) - fixture.Units.GetPosition(ChaserId)).Length;

        // ------------------------------------------------------------------
        // 复现：A 追 B，若干 tick 后停在 StopRange 内且朝向目标；B 走远后 A 再次追击并再次停下。
        // ------------------------------------------------------------------

        [Fact]
        public void ToUnit_ApproachesThenStopsWithinStopRange_ThenResumesWhenTargetMovesAway()
        {
            var fixture = Build(chaserSpeed: 10.0, targetStart: new Vec2(4, 0));
            RequestChase(fixture, stopRange: 1.5);

            for (var i = 0; i < 5; i++)
            {
                Tick(fixture, 1.0);
            }

            Assert.True(DistanceChaserToTarget(fixture) <= 1.5 + 1e-6);
            Assert.Equal(MoveMode.Idle, fixture.Chaser.MovementState.Mode);

            var toTarget = fixture.Units.GetPosition(TargetId) - fixture.Units.GetPosition(ChaserId);
            var expectedFacing = Math.Atan2(toTarget.Y, toTarget.X);
            Assert.Equal(expectedFacing, fixture.Chaser.Facing, 6);

            // 把 B 挪到 A 当前位置 5 单位外——A 应再次追击，并再次停到 StopRange 内。
            var chaserPos = fixture.Units.GetPosition(ChaserId);
            fixture.Units.SetPosition(TargetId, chaserPos + new Vec2(5, 0));

            for (var i = 0; i < 5; i++)
            {
                Tick(fixture, 1.0);
            }

            Assert.True(DistanceChaserToTarget(fixture) <= 1.5 + 1e-6);
            Assert.Equal(MoveMode.Idle, fixture.Chaser.MovementState.Mode);
        }

        // ------------------------------------------------------------------
        // 不变量①：目标在 StopRange 与 StopRange+FollowResumeSlack 之间来回微动——追击单位不动（滞回）。
        // ------------------------------------------------------------------

        [Fact]
        public void ToUnit_TargetOscillatesWithinHysteresisBand_ChaserStaysStill()
        {
            var options = new MovementOptions { FollowResumeSlack = 0.25 };
            var fixture = Build(chaserSpeed: 10.0, targetStart: new Vec2(4, 0), options: options);
            RequestChase(fixture, stopRange: 1.5);

            for (var i = 0; i < 5; i++)
            {
                Tick(fixture, 1.0);
            }

            Assert.True(DistanceChaserToTarget(fixture) <= 1.5 + 1e-6);
            Assert.Equal(MoveMode.Idle, fixture.Chaser.MovementState.Mode);
            var chaserPos = fixture.Units.GetPosition(ChaserId);

            // StopRange=1.5，FollowResumeSlack=0.25 → 恢复靠近的阈值是 1.75；下面让目标在
            // 1.5～1.75 之间来回移动（恒 ≤ 1.75），追击单位应该全程保持不动。
            var farInBand = chaserPos + new Vec2(1.7, 0);
            var nearInBand = chaserPos + new Vec2(1.5, 0);

            for (var i = 0; i < 6; i++)
            {
                fixture.Units.SetPosition(TargetId, i % 2 == 0 ? farInBand : nearInBand);
                Tick(fixture, 1.0);

                Assert.Equal(chaserPos, fixture.Units.GetPosition(ChaserId));
                Assert.Equal(MoveMode.Idle, fixture.Chaser.MovementState.Mode);
            }
        }

        // ------------------------------------------------------------------
        // 不变量②：目标死亡——追击自动结束（清请求、单位静止、收到一次 ChaseTargetLost 事件）。
        // ------------------------------------------------------------------

        [Fact]
        public void ToUnit_TargetDies_AutoEndsChase_RaisesChaseTargetLostOnce()
        {
            var fixture = Build(chaserSpeed: 10.0, targetStart: new Vec2(4, 0));
            RequestChase(fixture, stopRange: 1.5);

            Tick(fixture, 1.0); // 开始靠近

            Assert.Equal(MoveMode.Run, fixture.Chaser.MovementState.Mode);
            Assert.True(fixture.Chaser.MovementState.Chase.HasValue);

            fixture.Target.Alive = false;
            var positionBeforeDeathTick = fixture.Units.GetPosition(ChaserId);

            Tick(fixture, 1.0);

            Assert.False(fixture.Chaser.MovementState.Chase.HasValue);
            Assert.Null(fixture.Chaser.MovementState.CurrentPath);
            Assert.Equal(MoveMode.Idle, fixture.Chaser.MovementState.Mode);
            Assert.Equal(positionBeforeDeathTick, fixture.Units.GetPosition(ChaserId));

            var lostEvents = fixture.StoppedEvents.FindAll(e => e.Reason == MoveStopReason.ChaseTargetLost);
            var single = Assert.Single(lostEvents);
            Assert.Equal(ChaserId, single.UnitId);

            // 再跑几个 tick：确认自动结束是终态，不会反复触发。
            Tick(fixture, 1.0);
            Tick(fixture, 1.0);
            Assert.Single(fixture.StoppedEvents.FindAll(e => e.Reason == MoveStopReason.ChaseTargetLost));
        }

        [Fact]
        public void ToUnit_TargetOnDifferentMap_AutoEndsChase()
        {
            var fixture = Build(chaserSpeed: 10.0, targetStart: new Vec2(4, 0));
            RequestChase(fixture, stopRange: 1.5);

            Tick(fixture, 1.0);
            Assert.True(fixture.Chaser.MovementState.Chase.HasValue);

            fixture.Target.MapId = OtherMapId;
            Tick(fixture, 1.0);

            Assert.False(fixture.Chaser.MovementState.Chase.HasValue);
            Assert.Equal(MoveMode.Idle, fixture.Chaser.MovementState.Mode);
            Assert.Contains(fixture.StoppedEvents, e => e.Reason == MoveStopReason.ChaseTargetLost);
        }

        // ------------------------------------------------------------------
        // 不变量③：追击期间下达 ToTarget——追击停止（Chase 被清空，替换为普通路径跟随）。
        // ------------------------------------------------------------------

        [Fact]
        public void ToUnit_ReplacedByToTarget_StopsChasing()
        {
            var fixture = Build(chaserSpeed: 10.0, targetStart: new Vec2(4, 0));
            RequestChase(fixture, stopRange: 1.5);

            Tick(fixture, 1.0);
            Assert.True(fixture.Chaser.MovementState.Chase.HasValue);

            fixture.Host.Request(MoveRequest.ToTarget(ChaserId, new Vec2(-10, 0)));
            Tick(fixture, 1.0);

            Assert.False(fixture.Chaser.MovementState.Chase.HasValue);
            Assert.NotNull(fixture.Chaser.MovementState.CurrentPath);
            Assert.Contains(fixture.StoppedEvents, e => e.Reason == MoveStopReason.Replaced);
        }

        // ------------------------------------------------------------------
        // 不变量④：stopRange <= 0 抛异常。
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void ToUnit_NonPositiveStopRange_Throws(double stopRange)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MoveRequest.ToUnit(ChaserId, TargetId, stopRange));
        }

        // ------------------------------------------------------------------
        // 不变量⑤：目标每 tick 位移 0.1、累计 < FollowRepathDistance(0.5) 时不重新规划路径。
        // ------------------------------------------------------------------

        [Fact]
        public void ToUnit_TargetDriftsBelowRepathThreshold_DoesNotReplan()
        {
            var nav = new CountingNavigation2D();
            // speed 取一个较小值，保证追击单位在被观测的 4 个 tick 内还没走到上一次规划的终点
            // （不然"路径耗尽"会成为触发重新规划的另一个原因，混淆本用例只想单独验证的
            // "FollowRepathDistance 未超阈值不重规划"这一个条件，见 AdvanceChase 判断记录
            // "needsRepath"）。
            var fixture = Build(chaserSpeed: 0.2, targetStart: new Vec2(10, 0), navigation: nav);
            RequestChase(fixture, stopRange: 1.5);

            Tick(fixture, 1.0); // 首次规划，FindPathCallCount → 1
            Assert.Equal(1, nav.FindPathCallCount);

            var targetPos = fixture.Units.GetPosition(TargetId);
            for (var i = 0; i < 4; i++)
            {
                // 每 tick 沿同一方向漂移 0.1，4 次累计 0.4 < FollowRepathDistance 默认 0.5。
                targetPos += new Vec2(0, 0.1);
                fixture.Units.SetPosition(TargetId, targetPos);
                Tick(fixture, 1.0);
            }

            Assert.Equal(1, nav.FindPathCallCount);

            // 再漂一次，累计到 0.5——本实现用 "> 阈值" 判定，恰好等于阈值仍不触发。
            targetPos += new Vec2(0, 0.1);
            fixture.Units.SetPosition(TargetId, targetPos);
            Tick(fixture, 1.0);
            Assert.Equal(1, nav.FindPathCallCount);

            // 再漂一次，累计超过 0.5——应当触发一次新的规划。
            targetPos += new Vec2(0, 0.1);
            fixture.Units.SetPosition(TargetId, targetPos);
            Tick(fixture, 1.0);
            Assert.Equal(2, nav.FindPathCallCount);
        }
    }
}
