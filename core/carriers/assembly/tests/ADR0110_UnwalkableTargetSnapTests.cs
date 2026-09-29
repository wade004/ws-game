using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// ADR-0110《导航契约新增"最近可走点"》端到端验收（消费方反馈第六十批）：真实
    /// <see cref="CarriersAssembly"/>（含真实 <see cref="MovementHost"/>/<see cref="MovementTickHandler"/>）
    /// + 带阻挡的 <see cref="StubNavigation2D"/>。期望值一律由排序规则从零算出（主键 = 到点击点距离按格宽
    /// 量化、次键 = 到单位距离、再按坐标字典序，候选限定为"与单位连通"——连通的判据就是导航自己的
    /// <see cref="INavigation2D.FindPath"/>，可走判定只用 <see cref="INavigation2D.IsWalkable"/>），不写死裸坐标。
    /// 两条复现用例 + 七支不变量用例（"两个导航实现结果一致"在
    /// <c>adapters/conformance/Runtime/Navigation2DScenarios</c>，桩与 Unity 网格实现共用同一组输入）。
    /// </summary>
    public sealed class ADR0110_UnwalkableTargetSnapTests
    {
        private static readonly Id Map = new Id("map.adr0110");
        private static readonly Id PlayerId = new Id("unit.adr0110_player");
        private static readonly Id FactionId = new Id("fac.adr0110_player");
        private static readonly Id ClassId = new Id("arch.class.adr0110_sample");

        private static readonly Vec2 UnitStart = new Vec2(0.5, 0);
        private static readonly Vec2 Click = new Vec2(3, 0);

        /// <summary>单位一侧（左）到点击点之间的厚墙，点击点正好在墙正中：左右两侧最近可走格并列。</summary>
        private static readonly Rect[] ThickWall = { new Rect(new Vec2(2, -1), new Vec2(4, 1)) };

        private sealed class Fixture : IDisposable
        {
            public WorldSim World = null!;
            public CarriersAssembly Carriers = null!;
            public PlayerUnit Player = null!;
            public StubNavigation2D Nav = null!;

            /// <summary>移动系统实际使用的导航（<see cref="Nav"/> 本身，或包了一层的替身）。</summary>
            public INavigation2D MoveNav = null!;
            public readonly List<(Id Unit, Vec2 From, Vec2 To, MoveFailReason Reason)> Failed =
                new List<(Id, Vec2, Vec2, MoveFailReason)>();
            public readonly List<(Id Unit, Vec2 Requested, Vec2 Resolved)> Adjusted =
                new List<(Id, Vec2, Vec2)>();

            public Vec2 Position => Carriers.Units.GetPosition(PlayerId);

            public void RunTicks(int count, double dt)
            {
                for (var i = 0; i < count; i++)
                {
                    World.Tick(SimStep.Continuous(dt));
                }
            }

            public void Request(Vec2 target) =>
                Carriers.Movement.Request(MoveRequest.ToTarget(PlayerId, target));

            public void Dispose() => World.Dispose();
        }

        private static Fixture Build(
            IReadOnlyList<Rect> blockers, Action<MovementOptions>? configure = null,
            Func<StubNavigation2D, INavigation2D>? wrapNav = null)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            // MovementTickHandler.ResolveSpeed 按默认 "stat.move_speed" 读 StatHost，必须登记（default_base=4）。
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.move_speed\", \"name_key\": \"l10n.stat.move_speed.name\", " +
                "\"group\": \"primary\", \"default_base\": 4}" +
                "]}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "ADR0110_UnwalkableTargetSnapTests 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var nav = new StubNavigation2D();
            nav.SetBlocking(Map, blockers);

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(20260929UL);
            INavigation2D moveNav = wrapNav != null ? wrapNav(nav) : nav;
            var carriers = new CarriersAssembly(bus, registry, rng, world, spatial, moveNav);

            var player = new PlayerUnit(PlayerId, Map, FactionId, ClassId) { Position = UnitStart };
            world.AddEntity(player);
            carriers.Rules.Stats.RegisterUnit(PlayerId);
            bus.DispatchPending();

            var fx = new Fixture { World = world, Carriers = carriers, Player = player, Nav = nav, MoveNav = moveNav };
            carriers.Movement.OnMoveFailedDetailed += (u, f, t, r) => fx.Failed.Add((u, f, t, r));
            carriers.Movement.OnMoveTargetAdjusted += (u, req, res) => fx.Adjusted.Add((u, req, res));
            configure?.Invoke(carriers.MovementOptions);
            return fx;
        }

        /// <summary>第三方风格的导航包装：只实现契约必需成员并数 <c>FindPath</c> 的调用次数（含失败次数），
        /// ADR-0110 的三个成员保持接口的默认实现（近似：环采样候选 + 逐个 <c>FindPath</c> 试探）。</summary>
        private class CountingNav : INavigation2D
        {
            protected readonly StubNavigation2D Inner;
            public int FindPathCalls;
            public int FailedFindPathCalls;

            public CountingNav(StubNavigation2D inner) => Inner = inner;

            public void ResetCounters()
            {
                FindPathCalls = 0;
                FailedFindPathCalls = 0;
            }

            public void BuildNavMesh(Id mapId) => Inner.BuildNavMesh(mapId);

            public bool IsWalkable(Id mapId, Vec2 point) => Inner.IsWalkable(mapId, point);

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to)
            {
                var path = Inner.FindPath(mapId, from, to);
                FindPathCalls++;
                if (path == null)
                {
                    FailedFindPathCalls++;
                }

                return path;
            }

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => Inner.Raycast(mapId, from, to);

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects) => Inner.SetBlocking(mapId, rects);

            public void Clear(Id mapId) => Inner.Clear(mapId);

            public int GetBlockingVersion(Id mapId) => Inner.GetBlockingVersion(mapId);
        }

        /// <summary>内置实现风格：ADR-0110 的三个成员都转发给测试桩的精确实现（桩内部的寻路判定不经过本包装，
        /// 因此 <see cref="CountingNav.FailedFindPathCalls"/> 只统计移动系统自己发起的寻路）。</summary>
        private sealed class ExactCountingNav : CountingNav, INavigation2D
        {
            public ExactCountingNav(StubNavigation2D inner) : base(inner) { }

            public bool TryFindNearestWalkable(Id mapId, Vec2 point, double maxRadius, Vec2 preferNear, out Vec2 walkable) =>
                Inner.TryFindNearestWalkable(mapId, point, maxRadius, preferNear, out walkable);

            public int FindNearestWalkableCandidates(
                Id mapId, Vec2 point, double maxRadius, Vec2 preferNear, int maxCount, List<Vec2> results) =>
                Inner.FindNearestWalkableCandidates(mapId, point, maxRadius, preferNear, maxCount, results);

            public bool TryFindNearestReachable(Id mapId, Vec2 from, Vec2 point, double maxRadius, out Vec2 reachable) =>
                Inner.TryFindNearestReachable(mapId, from, point, maxRadius, out reachable);
        }

        /// <summary>近似实现：<c>TryFindNearestReachable</c> 只按几何取最近可走点、不管连通性（第三方近似实现可能给出
        /// 的"看起来可达实际走不到的点"），几何候选枚举转发给桩的精确实现。</summary>
        private sealed class ApproximateNav : CountingNav, INavigation2D
        {
            public ApproximateNav(StubNavigation2D inner) : base(inner) { }

            public bool TryFindNearestWalkable(Id mapId, Vec2 point, double maxRadius, Vec2 preferNear, out Vec2 walkable) =>
                Inner.TryFindNearestWalkable(mapId, point, maxRadius, preferNear, out walkable);

            public int FindNearestWalkableCandidates(
                Id mapId, Vec2 point, double maxRadius, Vec2 preferNear, int maxCount, List<Vec2> results) =>
                Inner.FindNearestWalkableCandidates(mapId, point, maxRadius, preferNear, maxCount, results);

            public bool TryFindNearestReachable(Id mapId, Vec2 from, Vec2 point, double maxRadius, out Vec2 reachable) =>
                Inner.TryFindNearestWalkable(mapId, point, maxRadius, from, out reachable);
        }

        /// <summary>按 ADR-0110 排序规则从零算出的候选序列（不使用任何被测的搜索实现；格几何取
        /// <see cref="NavGridLayout.Compute(IReadOnlyList{Rect}, double)"/>，可走判定只用 IsWalkable）。
        /// <paramref name="reachableFrom"/> 非空时再要求"与该点连通"，连通的判据就是导航自己的
        /// <c>FindPath(from, 候选) != null</c>（规则本身，不依赖任何被测的连通实现）。<paramref name="point"/>
        /// 本身满足条件时排第一（原样返回，不量化到格心）。</summary>
        private static List<Vec2> RuleRanking(
            INavigation2D nav, IReadOnlyList<Rect> blockers, Vec2 point, Vec2 prefer, double radius,
            Vec2? reachableFrom = null)
        {
            bool Qualifies(Vec2 c) =>
                nav.IsWalkable(Map, c) && (!reachableFrom.HasValue || nav.FindPath(Map, reachableFrom.Value, c) != null);

            var layout = NavGridLayout.Compute(blockers);
            var pool = new List<(long K1, long K2, double X, double Y)>();
            for (var ix = 0; ix < layout.Width; ix++)
            {
                for (var iy = 0; iy < layout.Height; iy++)
                {
                    var c = layout.CellCenter(ix, iy);
                    var d = Vec2.Distance(point, c);
                    if (d > radius || !Qualifies(c))
                    {
                        continue;
                    }

                    pool.Add((
                        (long)Math.Floor(d / layout.CellSize + 1e-9),
                        (long)Math.Round(Vec2.Distance(c, prefer) / 1e-9),
                        c.X,
                        c.Y));
                }
            }

            var ranked = pool
                .OrderBy(e => e.K1).ThenBy(e => e.K2).ThenBy(e => e.X).ThenBy(e => e.Y)
                .Select(e => new Vec2(e.X, e.Y))
                .ToList();
            if (Qualifies(point))
            {
                ranked.Insert(0, point);
            }

            return ranked;
        }

        /// <summary>四周被 1 宽阻挡带围住的地面 <c>[0,10]x[0,6]</c>。</summary>
        private static readonly Rect[] EnclosingFrame =
        {
            new Rect(new Vec2(-1, -1), new Vec2(0, 7)),
            new Rect(new Vec2(10, -1), new Vec2(11, 7)),
            new Rect(new Vec2(0, -1), new Vec2(10, 0)),
            new Rect(new Vec2(0, 6), new Vec2(10, 7)),
        };

        /// <summary><see cref="EnclosingFrame"/> 的东侧阻挡带在 <c>y∈(2,4)</c> 处打开一个缺口（包围盒不变）。</summary>
        private static readonly Rect[] EnclosingFrameWithGap =
        {
            new Rect(new Vec2(-1, -1), new Vec2(0, 7)),
            new Rect(new Vec2(10, -1), new Vec2(11, 2)),
            new Rect(new Vec2(10, 4), new Vec2(11, 7)),
            new Rect(new Vec2(0, -1), new Vec2(10, 0)),
            new Rect(new Vec2(0, 6), new Vec2(10, 7)),
        };

        // -----------------------------------------------------------------
        // 复现用例（修复前必须真红）：点击落在厚墙正中，开启吸附后单位走到规则算出的最近可走点。
        // -----------------------------------------------------------------

        [Fact]
        public void Repro_SnapToNearestWalkable_ClickInsideWall_WalksToRuleComputedPoint()
        {
            using var fx = Build(ThickWall, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable);
            Assert.False(fx.Nav.IsWalkable(Map, Click));

            var expected = RuleRanking(fx.Nav, ThickWall, Click, UnitStart, 8.0, reachableFrom: UnitStart)[0];
            Assert.True(expected.X < 2, $"应取与单位连通的一侧（厚墙居中点击），规则期望 x={expected.X}");

            fx.Request(Click);
            fx.RunTicks(3, 1.0);

            Assert.Equal(expected, fx.Position);
            Assert.True(fx.Position.X < 2, $"最终位置应在单位所在一侧，实际 x={fx.Position.X}");
            Assert.Empty(fx.Failed);
            var adjusted = Assert.Single(fx.Adjusted);
            Assert.Equal(PlayerId, adjusted.Unit);
            Assert.Equal(Click, adjusted.Requested);
            Assert.Equal(expected, adjusted.Resolved);
        }

        /// <summary>
        /// 复现用例二（1b，修复前必须真红）：单位在四周被阻挡带围住的图内侧，点击带**外侧** 3 个单位处的
        /// 可走点——该点可走但不可达，离它最近的一批可走点全在带外侧。单位应停在内侧贴带、由规则算出的
        /// 期望点（与单位连通 + 排序规则枚举），"目标被调整"通知 1 次，没有 NoPath。
        /// </summary>
        [Fact]
        public void Repro_ClickOutsideBoundaryBand_UnreachableWalkable_WalksToRuleComputedReachablePoint()
        {
            var start = new Vec2(5, 3);
            var click = new Vec2(-3, 3);
            using var fx = Build(EnclosingFrame, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable);
            fx.Player.Position = start;
            Assert.True(fx.Nav.IsWalkable(Map, click), "点击点本身可走（在阻挡带外侧），只是不可达");
            Assert.Null(fx.Nav.FindPath(Map, start, click));

            var expected = RuleRanking(fx.Nav, EnclosingFrame, click, start, 8.0, reachableFrom: start)[0];
            Assert.True(expected.X > 0 && expected.X < 10 && expected.Y > 0 && expected.Y < 6,
                $"期望点应在带内侧（与单位连通的最近可走点），实际 {expected}");

            fx.Request(click);
            fx.RunTicks(6, 1.0);

            Assert.Equal(expected, fx.Position);
            Assert.Empty(fx.Failed);
            var adjusted = Assert.Single(fx.Adjusted);
            Assert.Equal(PlayerId, adjusted.Unit);
            Assert.Equal(click, adjusted.Requested);
            Assert.Equal(expected, adjusted.Resolved);
        }

        // -----------------------------------------------------------------
        // 不变量 ①：默认 Reject 下同一场景仍是 NoPath、单位不动、失败目标 == 原始点。
        // -----------------------------------------------------------------

        [Fact]
        public void Invariant_1_DefaultReject_SameScenarioStillNoPath_UnitStays_FailedTargetIsRequested()
        {
            using var fx = Build(ThickWall);
            Assert.Equal(UnwalkableTargetPolicy.Reject, fx.Carriers.MovementOptions.UnwalkableTargetPolicy);

            fx.Request(Click);
            fx.RunTicks(3, 1.0);

            Assert.Equal(UnitStart, fx.Position);
            var failed = Assert.Single(fx.Failed);
            Assert.Equal(MoveFailReason.NoPath, failed.Reason);
            Assert.Equal(Click, failed.To);
            Assert.Empty(fx.Adjusted);
        }

        // -----------------------------------------------------------------
        // 不变量 ②：地图范围外（地面图外围的阻挡外框带内）的点被吸附回边界内可走点。
        // -----------------------------------------------------------------

        [Fact]
        public void Invariant_2_ClickOutsideMapInsideBoundaryFrame_SnapsToWalkablePointInsideBoundary()
        {
            // 地面图 [0,10]x[0,6]，外围一圈 1 宽阻挡外框；点击点在左侧外框带中央（地图范围外）。
            var frame = EnclosingFrame;
            var start = new Vec2(5, 3);
            var click = new Vec2(-0.5, 3);
            using var fx = Build(frame, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable);
            fx.Player.Position = start;
            Assert.False(fx.Nav.IsWalkable(Map, click));

            var expected = RuleRanking(fx.Nav, frame, click, start, 8.0, reachableFrom: start)[0];
            Assert.True(expected.X > 0 && expected.X < 10 && expected.Y > 0 && expected.Y < 6,
                $"期望点应落在地面图边界内（单位一侧优先），实际 {expected}");

            fx.Request(click);
            fx.RunTicks(4, 1.0);

            Assert.Equal(expected, fx.Position);
            Assert.True(fx.Nav.IsWalkable(Map, fx.Position));
            Assert.Empty(fx.Failed);
            var adjusted = Assert.Single(fx.Adjusted);
            Assert.Equal(click, adjusted.Requested);
            Assert.Equal(expected, adjusted.Resolved);
        }

        // -----------------------------------------------------------------
        // 不变量 ③（只针对近似实现那条路径）：导航的 TryFindNearestReachable 是近似的、给出了实际走不到的点时，
        // 移动系统改用几何后续候选；候选耗尽才 NoPath，失败目标 == 原始点。内置（精确）实现不会走到这里。
        // -----------------------------------------------------------------

        private static readonly Rect[] PocketWalls =
        {
            new Rect(new Vec2(2, -1), new Vec2(2.75, 1)),
            new Rect(new Vec2(3.25, -1), new Vec2(4, 1)),
            new Rect(new Vec2(2.75, -1), new Vec2(3.25, -0.25)),
            new Rect(new Vec2(2.75, 0.25), new Vec2(3.25, 1)),
        };

        [Fact]
        public void Invariant_3_ApproximateNavigationGivesUnreachablePoint_FallsBackToLaterCandidates_ExhaustedGivesNoPathWithRequestedTarget()
        {
            var click = new Vec2(2.4, 0);
            var rankNav = new StubNavigation2D();
            rankNav.SetBlocking(Map, PocketWalls);
            var ranking = RuleRanking(rankNav, PocketWalls, click, UnitStart, 8.0);

            // 阶段 A：候选上限足够（默认 8）→ 跳过孤岛上的前几名，取第一个 FindPath 可达的候选。
            using (var fx = Build(
                PocketWalls, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable,
                s => new ApproximateNav(s)))
            {
                var limit = fx.Carriers.MovementOptions.UnwalkableTargetCandidates;
                var expected = ranking.Take(limit).First(c => fx.Nav.FindPath(Map, UnitStart, c) != null);
                var expectedIndex = ranking.IndexOf(expected);
                Assert.True(expectedIndex >= 1, "夹具应让最近点落在不可达孤岛上（否则本用例不检验候选回退）");
                Assert.Null(fx.Nav.FindPath(Map, UnitStart, ranking[0]));

                fx.Request(click);
                fx.RunTicks(3, 1.0);

                Assert.Equal(expected, fx.Position);
                Assert.Empty(fx.Failed);
                var adjusted = Assert.Single(fx.Adjusted);
                Assert.Equal(click, adjusted.Requested);
                Assert.Equal(expected, adjusted.Resolved);
            }

            // 阶段 B：候选上限只覆盖孤岛上的候选 → 全部失败，NoPath，失败目标仍是原始点，不触发"被调整"。
            using (var fx = Build(
                PocketWalls,
                o =>
                {
                    o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable;
                    o.UnwalkableTargetCandidates = 2;
                },
                s => new ApproximateNav(s)))
            {
                Assert.All(ranking.Take(2), c => Assert.Null(fx.Nav.FindPath(Map, UnitStart, c)));

                fx.Request(click);
                fx.RunTicks(3, 1.0);

                Assert.Equal(UnitStart, fx.Position);
                var failed = Assert.Single(fx.Failed);
                Assert.Equal(MoveFailReason.NoPath, failed.Reason);
                Assert.Equal(click, failed.To);
                Assert.Empty(fx.Adjusted);
            }
        }

        // -----------------------------------------------------------------
        // 不变量 ④：搜索半径内没有可走点 → NoPath，失败目标 == 原始点。
        // -----------------------------------------------------------------

        [Fact]
        public void Invariant_4_NoWalkablePointWithinRadius_NoPathWithRequestedTarget()
        {
            using var fx = Build(ThickWall, o =>
            {
                o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable;
                o.UnwalkableTargetSnapRadius = 0.5;
            });
            Assert.Empty(RuleRanking(fx.Nav, ThickWall, Click, UnitStart, 0.5));

            fx.Request(Click);
            fx.RunTicks(3, 1.0);

            Assert.Equal(UnitStart, fx.Position);
            var failed = Assert.Single(fx.Failed);
            Assert.Equal(MoveFailReason.NoPath, failed.Reason);
            Assert.Equal(Click, failed.To);
            Assert.Empty(fx.Adjusted);
        }

        // -----------------------------------------------------------------
        // 不变量 ⑤：阻挡变化触发重规划后，目标按原始点重新解析（原始点后来变可走了就去原始点）。
        // -----------------------------------------------------------------

        [Fact]
        public void Invariant_5_BlockingChangeReplan_ResolvesFromRequestedTargetAgain()
        {
            using var fx = Build(ThickWall, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable);
            var firstResolved = RuleRanking(fx.Nav, ThickWall, Click, UnitStart, 8.0, reachableFrom: UnitStart)[0];

            fx.Request(Click);
            fx.RunTicks(1, 0.1);
            var adjusted = Assert.Single(fx.Adjusted);
            Assert.Equal(firstResolved, adjusted.Resolved);
            Assert.True(fx.Position.X > UnitStart.X && fx.Position.X < firstResolved.X,
                $"阻挡变化前应在去往解析点的半路上，实际 x={fx.Position.X}");

            // 墙被拆掉：原始点变可走 → 默认 Replan 策略从原始请求目标重新解析，直接去原始点（不是旧解析点）。
            fx.Nav.SetBlocking(Map, Array.Empty<Rect>());
            Assert.True(fx.Nav.IsWalkable(Map, Click));
            fx.RunTicks(30, 0.1);

            Assert.Equal(Click, fx.Position);
            Assert.Empty(fx.Failed);
            Assert.Single(fx.Adjusted); // 重新解析结果 == 原始点，不再触发"被调整"。
        }

        // -----------------------------------------------------------------
        // 不变量 ⑥：几何上最近的可走点在不连通的孤岛上 → 直接选中与单位连通的那一个，移动系统没有发生任何
        // 失败的寻路尝试（计数型导航替身）；阳性对照：只有默认（近似）实现的导航会逐个试探、失败次数 > 0。
        // -----------------------------------------------------------------

        [Fact]
        public void Invariant_6_NearestOnUnreachableIsland_PicksConnectedPointDirectly_NoFailedPathAttempt_WithPositiveControl()
        {
            var click = new Vec2(2.7, 0); // 落在左墙内，离封闭的小隔间比离单位这一侧近。
            var rankNav = new StubNavigation2D();
            rankNav.SetBlocking(Map, PocketWalls);
            var geometric = RuleRanking(rankNav, PocketWalls, click, UnitStart, 8.0);
            Assert.Null(rankNav.FindPath(Map, UnitStart, geometric[0]));
            var expected = RuleRanking(rankNav, PocketWalls, click, UnitStart, 8.0, reachableFrom: UnitStart)[0];
            Assert.NotEqual(geometric[0], expected);

            // 内置（精确）实现风格：直接选中连通的点，零次失败寻路。
            using (var fx = Build(
                PocketWalls, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable,
                s => new ExactCountingNav(s)))
            {
                var counting = (CountingNav)fx.MoveNav;
                counting.ResetCounters();

                fx.Request(click);
                fx.RunTicks(3, 1.0);

                Assert.Equal(expected, fx.Position);
                Assert.Empty(fx.Failed);
                var adjusted = Assert.Single(fx.Adjusted);
                Assert.Equal(expected, adjusted.Resolved);
                Assert.True(counting.FindPathCalls >= 1, "移动系统应至少发起过一次（成功的）寻路");
                Assert.Equal(0, counting.FailedFindPathCalls);
            }

            // 阳性对照：同一场景、导航只有默认（近似）实现——先试到孤岛上的点，失败次数 > 0（证明计数器看得见失败），
            // 但最终仍走到一个与单位连通的点。
            using (var fx = Build(
                PocketWalls, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable,
                s => new CountingNav(s)))
            {
                var counting = (CountingNav)fx.MoveNav;
                counting.ResetCounters();

                fx.Request(click);
                fx.RunTicks(3, 1.0);

                Assert.Empty(fx.Failed);
                Assert.NotNull(fx.Nav.FindPath(Map, UnitStart, fx.Position));
                Assert.True(fx.Position.X < 2, $"默认实现最终也应落在单位所在一侧，实际 {fx.Position}");
                Assert.True(counting.FailedFindPathCalls >= 1,
                    "默认（近似）实现按候选逐个试探，应至少有一次落在孤岛上的失败寻路，实际 " + counting.FailedFindPathCalls);
            }
        }

        // -----------------------------------------------------------------
        // 不变量 ⑦：阻挡版本变化后连通信息重算——打开一个缺口后，原本不连通（不可达）的点变为可达并被选中。
        // -----------------------------------------------------------------

        [Fact]
        public void Invariant_7_BlockingVersionChange_OpeningGapMakesPreviouslyUnreachablePointReachableAndSelected()
        {
            var start = new Vec2(5, 3);
            var click = new Vec2(12.5, 3); // 东侧阻挡带外侧的可走点。
            using var fx = Build(EnclosingFrame, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable);
            fx.Player.Position = start;

            // 阶段 A：四周封死，点击点可走但不可达 → 走到房间内侧、与单位连通的最近点。
            var expectedClosed = RuleRanking(fx.Nav, EnclosingFrame, click, start, 8.0, reachableFrom: start)[0];
            Assert.True(expectedClosed.X < 10, $"封死时期望点应在房间内侧，实际 {expectedClosed}");
            fx.Request(click);
            fx.RunTicks(6, 1.0);
            Assert.Equal(expectedClosed, fx.Position);
            var first = Assert.Single(fx.Adjusted);
            Assert.Equal(expectedClosed, first.Resolved);

            // 阶段 B：东带打开缺口（阻挡版本变化）→ 同一个点击点现在可达，被原样选中，直接走过去。
            var versionBefore = fx.Nav.GetBlockingVersion(Map);
            fx.Nav.SetBlocking(Map, EnclosingFrameWithGap);
            Assert.NotEqual(versionBefore, fx.Nav.GetBlockingVersion(Map));
            Assert.NotNull(fx.Nav.FindPath(Map, fx.Position, click));
            var expectedOpen = RuleRanking(fx.Nav, EnclosingFrameWithGap, click, fx.Position, 8.0, reachableFrom: fx.Position)[0];
            Assert.Equal(click, expectedOpen);

            fx.Request(click);
            fx.RunTicks(6, 1.0);
            Assert.Equal(click, fx.Position);
            Assert.Empty(fx.Failed);
            Assert.Single(fx.Adjusted); // 第二次解析结果 == 原始点，不再触发"被调整"。
        }
    }
}
