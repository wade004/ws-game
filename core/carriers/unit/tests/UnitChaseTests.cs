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

            /// <summary>置为 <c>true</c> 后 <see cref="FindPath"/> 恒返回 <c>null</c>（模拟目标进入
            /// 永久不可达区域），仍然计数——供"追击目标不可达"分支复用同一个假寻路器，不必另建一个
            /// 专门返回 null 的类型。</summary>
            public bool AlwaysUnreachable { get; set; }

            public void BuildNavMesh(Id mapId) => _inner.BuildNavMesh(mapId);

            public bool IsWalkable(Id mapId, Vec2 point) => _inner.IsWalkable(mapId, point);

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to)
            {
                FindPathCallCount++;
                return AlwaysUnreachable ? null : _inner.FindPath(mapId, from, to);
            }

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => _inner.Raycast(mapId, from, to);

            public NavRayHit? RaycastWithNormal(Id mapId, Vec2 from, Vec2 to) => _inner.RaycastWithNormal(mapId, from, to);

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
        // 不变量⑤：目标每 tick 位移 0.1、累计 < FollowRepathDistance(0.5) 时不重新规划路径；
        // 目标进入永久不可达区域时，重新规划失败恰好触发一次既有的寻路失败通知并结束追击（不再
        // 每 tick 无声重试）。
        // ------------------------------------------------------------------

        [Fact]
        public void ToUnit_TargetDriftsBelowRepathThreshold_DoesNotReplan_UnreachableTargetFailsOnceAndEndsChase()
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

            // ---- 追加分支：目标进入永久不可达区域（消费方复核后拍板，见 AdvanceChase 判断记录
            // "needsRepath"里 else 分支）----
            var failedEvents = new List<(Id UnitId, Vec2 From, Vec2 To)>();
            var failedDetailedEvents = new List<(Id UnitId, Vec2 From, Vec2 To, MoveFailReason Reason)>();
            fixture.Host.OnMoveFailed += (unitId, from, to) => failedEvents.Add((unitId, from, to));
            fixture.Host.OnMoveFailedDetailed += (unitId, from, to, reason) => failedDetailedEvents.Add((unitId, from, to, reason));

            nav.AlwaysUnreachable = true;
            // 再漂一次，累计再次超过 0.5，触发下一次重新规划——这次寻路失败。
            // ADR-0102（修订 ADR-0097 决策 5）：AlwaysUnreachable 时直接回退点与
            // TryFindStandoffCandidatePath 采样的全部候选（含直接回退点，总计
            // MovementOptions.ChaseStandoffCandidates 个）都会失败，才真正判定为"到不了"——
            // 期望调用次数按规则算出，不写死裸数：本次重规划前 FindPathCallCount 已经是 2，
            // 全部候选耗尽新增 ChaseStandoffCandidates 次调用。
            var candidatesPerFailedReplan = new MovementOptions().ChaseStandoffCandidates;
            var expectedCallsAfterUnreachableReplan = 2 + candidatesPerFailedReplan;
            targetPos += new Vec2(0, 0.6);
            fixture.Units.SetPosition(TargetId, targetPos);
            Tick(fixture, 1.0);

            Assert.Equal(expectedCallsAfterUnreachableReplan, nav.FindPathCallCount);
            var failed = Assert.Single(failedEvents);
            Assert.Equal(ChaserId, failed.UnitId);
            var failedDetailed = Assert.Single(failedDetailedEvents);
            Assert.Equal(ChaserId, failedDetailed.UnitId);
            Assert.Equal(MoveFailReason.NoPath, failedDetailed.Reason);
            Assert.False(fixture.Chaser.MovementState.Chase.HasValue);
            Assert.Null(fixture.Chaser.MovementState.CurrentPath);
            Assert.Equal(MoveMode.Idle, fixture.Chaser.MovementState.Mode);

            // 目标继续移动，追击请求已经清空——不应该再调用 FindPath，也不应该再触发失败通知。
            fixture.Units.SetPosition(TargetId, targetPos + new Vec2(0, 5));
            Tick(fixture, 1.0);
            Tick(fixture, 1.0);

            Assert.Equal(expectedCallsAfterUnreachableReplan, nav.FindPathCallCount);
            Assert.Single(failedEvents);
            Assert.Single(failedDetailedEvents);
        }

        // ------------------------------------------------------------------
        // 不变量③（ADR-0102）：ChaseStandoffCandidates = 1 时不采样，行为与 1.82.0（ADR-0102 落地前）
        // 逐字一致——直接回退点失败即视为"到不了"，只触发一次 FindPath 调用。
        // ------------------------------------------------------------------

        [Fact]
        public void ToUnit_ChaseStandoffCandidatesDisabled_DoesNotSample_BehavesLike1820()
        {
            var nav = new CountingNavigation2D { AlwaysUnreachable = true };
            var options = new MovementOptions { ChaseStandoffCandidates = 1 };
            var fixture = Build(chaserSpeed: 10.0, targetStart: new Vec2(4, 0), navigation: nav, options: options);

            var failedDetailedEvents = new List<MoveFailReason>();
            fixture.Host.OnMoveFailedDetailed += (unitId, from, to, reason) => failedDetailedEvents.Add(reason);

            RequestChase(fixture, stopRange: 1.5);
            Tick(fixture, 1.0);

            Assert.Equal(1, nav.FindPathCallCount);
            var reason = Assert.Single(failedDetailedEvents);
            Assert.Equal(MoveFailReason.NoPath, reason);
            Assert.False(fixture.Chaser.MovementState.Chase.HasValue);
        }

        // ------------------------------------------------------------------
        // ADR-0102 复现：目标紧贴一块阻挡矩形，直接回退点恰好落在阻挡内，但停止距离圆上其它候选点
        // 可走——采样应找到第一个成功候选，追击正常推进、不触发失败通知。
        // ------------------------------------------------------------------

        [Fact]
        public void ToUnit_DirectStandoffPointBlocked_SamplesCandidate_SucceedsWithoutMoveFailed()
        {
            var nav = new CountingNavigation2D();
            // 阻挡矩形只盖住直接回退点 (2.5, 0)（目标 (4,0) 沿"单位(0,0)→目标"方向回退 StopRange=1.5
            // 得到），Y 方向留 ±0.4——第一个采样候选（偏移 +δ=+22.5°，见 TryFindStandoffCandidatePath）
            // 落在 (2.614, -0.574)，|y|=0.574 > 0.4，在阻挡矩形之外，故直接回退点失败、第一个候选
            // 成功，期望 FindPathCallCount = 1（直接点）+ 1（第一个候选）= 2。
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(2.2, -0.4), new Vec2(2.8, 0.4)) });
            var fixture = Build(chaserSpeed: 10.0, targetStart: new Vec2(4, 0), navigation: nav);

            var failedEvents = new List<Id>();
            fixture.Host.OnMoveFailed += (unitId, from, to) => failedEvents.Add(unitId);
            var startPos = fixture.Units.GetPosition(ChaserId);

            RequestChase(fixture, stopRange: 1.5);
            for (var i = 0; i < 5; i++)
            {
                Tick(fixture, 1.0);
            }

            Assert.Empty(failedEvents);
            Assert.NotEqual(startPos, fixture.Units.GetPosition(ChaserId));
            Assert.True(DistanceChaserToTarget(fixture) <= 1.5 + 0.25 + 1e-6);
            Assert.Equal(2, nav.FindPathCallCount);
        }

        // ------------------------------------------------------------------
        // D15（测试覆盖梳理 2026-10-01；设计决定，见 ADR-0125 / ADR-0102）：追击单位自身站在不可走格
        // 时不做特殊处理——INavigation2D.FindPath 端点契约保证起点不可走时任何终点都返回 null，
        // 直接回退点与全部采样候选一起失败，表现与"目标真的处于永久不可达区域"完全相同：
        // 触发一次 NoPath 失败通知并结束追击。两种成因的可观测结果逐项相等。
        // ------------------------------------------------------------------

        [Fact]
        public void ToUnit_ChaserStandsOnUnwalkableCell_FailsLikeUnreachableTarget_NoSpecialHandling()
        {
            // 成因 A：追击单位自己站在阻挡矩形里，目标本身可达。
            var navBlockedStart = new CountingNavigation2D();
            navBlockedStart.SetBlocking(MapId, new[] { new Rect(new Vec2(-0.5, -0.5), new Vec2(0.5, 0.5)) });
            var a = Build(chaserSpeed: 10.0, targetStart: new Vec2(4, 0), navigation: navBlockedStart);
            Assert.False(navBlockedStart.IsWalkable(MapId, a.Units.GetPosition(ChaserId)));
            Assert.True(navBlockedStart.IsWalkable(MapId, a.Units.GetPosition(TargetId)));
            var failedA = new List<MoveFailReason>();
            a.Host.OnMoveFailedDetailed += (unitId, from, to, reason) => failedA.Add(reason);

            // 成因 B：追击单位站在可走处，但目标被判定永久不可达。
            var navUnreachable = new CountingNavigation2D { AlwaysUnreachable = true };
            var b = Build(chaserSpeed: 10.0, targetStart: new Vec2(4, 0), navigation: navUnreachable);
            var failedB = new List<MoveFailReason>();
            b.Host.OnMoveFailedDetailed += (unitId, from, to, reason) => failedB.Add(reason);

            RequestChase(a, stopRange: 1.5);
            RequestChase(b, stopRange: 1.5);
            Tick(a, 1.0);
            Tick(b, 1.0);

            // 期望调用次数按规则算出：直接回退点 + 其余采样候选，共 ChaseStandoffCandidates 次。
            var expectedCalls = new MovementOptions().ChaseStandoffCandidates;
            Assert.Equal(expectedCalls, navBlockedStart.FindPathCallCount);
            Assert.Equal(navUnreachable.FindPathCallCount, navBlockedStart.FindPathCallCount);
            Assert.Equal(MoveFailReason.NoPath, Assert.Single(failedA));
            Assert.Equal(Assert.Single(failedB), Assert.Single(failedA));
            Assert.False(a.Chaser.MovementState.Chase.HasValue);
            Assert.False(b.Chaser.MovementState.Chase.HasValue);
            Assert.Equal(MoveMode.Idle, a.Chaser.MovementState.Mode);
            Assert.Equal(Vec2.Zero, a.Units.GetPosition(ChaserId)); // 没有被挪出阻挡格
        }
    }
}
