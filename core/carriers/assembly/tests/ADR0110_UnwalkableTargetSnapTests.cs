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
    /// 量化、次键 = 到单位距离、再按坐标字典序，可走判定只用 <see cref="INavigation2D.IsWalkable"/>），
    /// 不写死裸坐标。一条复现用例 + 六支不变量用例（第六支"两个导航实现结果一致"在
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

        private static Fixture Build(IReadOnlyList<Rect> blockers, Action<MovementOptions>? configure = null)
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
            var carriers = new CarriersAssembly(bus, registry, rng, world, spatial, nav);

            var player = new PlayerUnit(PlayerId, Map, FactionId, ClassId) { Position = UnitStart };
            world.AddEntity(player);
            carriers.Rules.Stats.RegisterUnit(PlayerId);
            bus.DispatchPending();

            var fx = new Fixture { World = world, Carriers = carriers, Player = player, Nav = nav };
            carriers.Movement.OnMoveFailedDetailed += (u, f, t, r) => fx.Failed.Add((u, f, t, r));
            carriers.Movement.OnMoveTargetAdjusted += (u, req, res) => fx.Adjusted.Add((u, req, res));
            configure?.Invoke(carriers.MovementOptions);
            return fx;
        }

        /// <summary>按 ADR-0110 排序规则从零算出的候选序列（不使用任何被测的搜索实现；格几何取
        /// <see cref="NavGridLayout.Compute(IReadOnlyList{Rect}, double)"/>，可走判定只用 IsWalkable）。</summary>
        private static List<Vec2> RuleRanking(
            INavigation2D nav, IReadOnlyList<Rect> blockers, Vec2 point, Vec2 prefer, double radius)
        {
            var layout = NavGridLayout.Compute(blockers);
            var pool = new List<(long K1, long K2, double X, double Y)>();
            for (var ix = 0; ix < layout.Width; ix++)
            {
                for (var iy = 0; iy < layout.Height; iy++)
                {
                    var c = layout.CellCenter(ix, iy);
                    var d = Vec2.Distance(point, c);
                    if (d > radius || !nav.IsWalkable(Map, c))
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

            return pool
                .OrderBy(e => e.K1).ThenBy(e => e.K2).ThenBy(e => e.X).ThenBy(e => e.Y)
                .Select(e => new Vec2(e.X, e.Y))
                .ToList();
        }

        // -----------------------------------------------------------------
        // 复现用例（修复前必须真红）：点击落在厚墙正中，开启吸附后单位走到规则算出的最近可走点。
        // -----------------------------------------------------------------

        [Fact]
        public void Repro_SnapToNearestWalkable_ClickInsideWall_WalksToRuleComputedPoint()
        {
            using var fx = Build(ThickWall, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable);
            Assert.False(fx.Nav.IsWalkable(Map, Click));

            var expected = RuleRanking(fx.Nav, ThickWall, Click, UnitStart, 8.0)[0];
            Assert.True(expected.X < 2, $"并列时应取单位所在一侧（薄厚墙居中点击），规则期望 x={expected.X}");

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
            var frame = new[]
            {
                new Rect(new Vec2(-1, -1), new Vec2(0, 7)),
                new Rect(new Vec2(10, -1), new Vec2(11, 7)),
                new Rect(new Vec2(0, -1), new Vec2(10, 0)),
                new Rect(new Vec2(0, 6), new Vec2(10, 7)),
            };
            var start = new Vec2(5, 3);
            var click = new Vec2(-0.5, 3);
            using var fx = Build(frame, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable);
            fx.Player.Position = start;
            Assert.False(fx.Nav.IsWalkable(Map, click));

            var expected = RuleRanking(fx.Nav, frame, click, start, 8.0)[0];
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
        // 不变量 ③：最近点在不可达孤岛上时改用后续候选；候选耗尽才 NoPath，失败目标 == 原始点。
        // -----------------------------------------------------------------

        private static readonly Rect[] PocketWalls =
        {
            new Rect(new Vec2(2, -1), new Vec2(2.75, 1)),
            new Rect(new Vec2(3.25, -1), new Vec2(4, 1)),
            new Rect(new Vec2(2.75, -1), new Vec2(3.25, -0.25)),
            new Rect(new Vec2(2.75, 0.25), new Vec2(3.25, 1)),
        };

        [Fact]
        public void Invariant_3_NearestOnUnreachableIsland_FallsBackToLaterCandidate_ExhaustedGivesNoPathWithRequestedTarget()
        {
            var click = new Vec2(2.4, 0);
            var rankNav = new StubNavigation2D();
            rankNav.SetBlocking(Map, PocketWalls);
            var ranking = RuleRanking(rankNav, PocketWalls, click, UnitStart, 8.0);

            // 阶段 A：候选上限足够（默认 8）→ 跳过孤岛上的前几名，取第一个 FindPath 可达的候选。
            using (var fx = Build(PocketWalls, o => o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable))
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
            using (var fx = Build(PocketWalls, o =>
            {
                o.UnwalkableTargetPolicy = UnwalkableTargetPolicy.SnapToNearestWalkable;
                o.UnwalkableTargetCandidates = 2;
            }))
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
            var firstResolved = RuleRanking(fx.Nav, ThickWall, Click, UnitStart, 8.0)[0];

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
    }
}
