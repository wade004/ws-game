using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SceneRouter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>
    /// M4-W1a（ADR-0130 追加决定"台阶判定精确化 / 寻路感知台阶"）：<see cref="TerrainStepMath"/> 的滑窗规则与精确停点、
    /// <see cref="TerrainStepPathPlanner"/> 与测试桩的地形感知寻路。期望值由规则算出（台阶边缘位置、坡脚 = 起点 + 台阶/坡度），
    /// 不变量：停点与采样对齐无关、返回路径每一段都不被地形挡、同一输入同一路径、不传地形约束时与旧 FindPath 逐位相同。
    /// </summary>
    public sealed class TerrainAwareNavigationTests
    {
        private static readonly Id Map = new Id("map.m4w1a_nav");
        private const double Step = 0.5;
        private const double Window = 0.1;

        private sealed class Constraint : ITerrainStepConstraint
        {
            private readonly ITerrainHeight2D _terrain;

            public Constraint(ITerrainHeight2D terrain) => _terrain = terrain;

            public Vec2? FirstStepBlock(Id mapId, Vec2 from, Vec2 to) =>
                TerrainStepMath.FirstRiseBlock(_terrain, mapId, from, to, Step, Window);
        }

        private static MapTerrainHeights Terrain(params ITerrainShape[] shapes)
        {
            var t = new MapTerrainHeights();
            t.SetShapes(Map, shapes);
            return t;
        }

        private static TerrainRegion Block(double x0, double y0, double x1, double y1, double height) =>
            new TerrainRegion(new Vec2(x0, y0), new Vec2(x1, y1), ground: height);

        // ---------------------------------------------------------------- 精确停点

        [Fact]
        public void FirstRiseBlock_StopsExactlyAtTheCliffEdge_WhateverTheSampleAlignment()
        {
            var terrain = Terrain(Block(10, -100, 1000, 100, 2.0));
            foreach (var startX in new[] { 0.0, 0.013, 0.057, 3.3333, 7.77777, 9.95 })
            {
                var hit = TerrainStepMath.FirstRiseBlock(terrain, Map, new Vec2(startX, 0), new Vec2(20, 0), Step, Window);
                Assert.NotNull(hit);
                Assert.Equal(10.0, hit!.Value.X, 6);
                Assert.Equal(0.0, hit.Value.Y, 12);
            }
        }

        [Fact]
        public void FirstRiseBlock_OnASteepSlope_StopsAtTheFootPlusStepOverSlope_WhateverTheStartOffset()
        {
            // 斜面从 x=5 起、坡度 20：窗口 0.1 内升高 2 > 台阶 0.5，坡脚之后走过 step/slope = 0.025 时第一次成立。
            const double slope = 20.0;
            var terrain = Terrain(new TerrainRegion(new Vec2(5, -100), new Vec2(100, 100), ground: 0.0, slopeX: slope));
            // 注：区域含 x=5 起点高度 0（ground + slope·(x − min.x)）。
            foreach (var startX in new[] { 0.0, 0.0137, 1.234567, 4.9 })
            {
                var hit = TerrainStepMath.FirstRiseBlock(terrain, Map, new Vec2(startX, 0), new Vec2(50, 0), Step, Window);
                Assert.NotNull(hit);
                Assert.Equal(5.0 + Step / slope, hit!.Value.X, 6);
            }
        }

        [Fact]
        public void FirstRiseBlock_GentleSlope_NeverBlocks_AndSteepnessThresholdIsStepOverWindow()
        {
            // 阈值坡度 = step / window = 5：坡度 4.9 放行，5.1 挡住。
            var gentle = Terrain(new TerrainRegion(new Vec2(0, -100), new Vec2(100, 100), slopeX: 4.9));
            var steep = Terrain(new TerrainRegion(new Vec2(0, -100), new Vec2(100, 100), slopeX: 5.1));
            Assert.Null(TerrainStepMath.FirstRiseBlock(gentle, Map, new Vec2(0, 0), new Vec2(50, 0), Step, Window));
            Assert.NotNull(TerrainStepMath.FirstRiseBlock(steep, Map, new Vec2(0, 0), new Vec2(50, 0), Step, Window));
        }

        [Fact]
        public void FirstRiseBlockFromFoot_ComparesAgainstTheCurrentFoot_NotTheGroundBehind()
        {
            var terrain = Terrain(Block(10, -100, 1000, 100, 2.0));
            var high = TerrainStepMath.FirstRiseBlockFromFoot(terrain, Map, new Vec2(0, 0), new Vec2(20, 0), Step, Window, foot: 5.0);
            Assert.Null(high);
            var low = TerrainStepMath.FirstRiseBlockFromFoot(terrain, Map, new Vec2(0, 0), new Vec2(20, 0), Step, Window, foot: 1.0);
            Assert.NotNull(low);
            Assert.Equal(10.0, low!.Value.X, 6);
            // 脚比台阶面只低 0.4 ≤ 台阶高度：放行。
            Assert.Null(TerrainStepMath.FirstRiseBlockFromFoot(terrain, Map, new Vec2(0, 0), new Vec2(20, 0), Step, Window, foot: 1.6));
        }

        [Fact]
        public void FirstDrop_FindsTheEdgeOfAPlatform_AndIgnoresRisesAndGentleSlopes()
        {
            var platform = Terrain(Block(-100, -100, 10, 100, 3.0));
            var drop = TerrainStepMath.FirstDrop(platform, Map, new Vec2(0, 0), new Vec2(20, 0), 0.2, Window);
            Assert.NotNull(drop);
            Assert.Equal(10.0, drop!.Value.X, 6);

            var rise = Terrain(Block(10, -100, 1000, 100, 2.0));
            Assert.Null(TerrainStepMath.FirstDrop(rise, Map, new Vec2(0, 0), new Vec2(20, 0), 0.2, Window));

            var gentle = Terrain(new TerrainRegion(new Vec2(0, -100), new Vec2(100, 100), ground: 20.0, slopeX: -1.0));
            Assert.Null(TerrainStepMath.FirstDrop(gentle, Map, new Vec2(0, 0), new Vec2(15, 0), 0.2, Window));
        }

        [Fact]
        public void FirstRiseBlock_SegmentEndingBeforeTheWall_OrOnFlatTerrain_ReturnsNull()
        {
            var terrain = Terrain(Block(10, -100, 1000, 100, 2.0));
            Assert.Null(TerrainStepMath.FirstRiseBlock(terrain, Map, new Vec2(0, 0), new Vec2(9.99, 0), Step, Window));
            Assert.Null(TerrainStepMath.FirstRiseBlock(FlatTerrainHeight2D.Instance, Map, new Vec2(0, 0), new Vec2(50, 0), Step, Window));
            Assert.Null(TerrainStepMath.FirstRiseBlock(terrain, Map, new Vec2(3, 3), new Vec2(3, 3), Step, Window));
        }

        // ---------------------------------------------------------------- 地形感知寻路

        private static StubNavigation2D NewStub() => new StubNavigation2D();

        [Fact]
        public void StubFindPath_DetoursAroundAStepHigherThanStepHeight()
        {
            var terrain = Terrain(Block(10, -3, 20, 3, 2.0));
            var constraint = new Constraint(terrain);
            var nav = NewStub();
            var from = new Vec2(0, 0);
            var to = new Vec2(25, 0);

            Assert.NotNull(constraint.FirstStepBlock(Map, from, to)); // 直线被台阶挡住
            var path = nav.FindPath(Map, from, to, constraint);
            Assert.NotNull(path);
            Assert.Equal(from, path![0]);
            Assert.Equal(to, path[path.Count - 1]);
            Assert.True(path.Count >= 3);
            for (var i = 0; i + 1 < path.Count; i++)
            {
                Assert.Null(constraint.FirstStepBlock(Map, path[i], path[i + 1]));
            }

            var length = 0.0;
            for (var i = 0; i + 1 < path.Count; i++) length += Vec2.Distance(path[i], path[i + 1]);
            Assert.True(length > Vec2.Distance(from, to));
            Assert.True(length < Vec2.Distance(from, to) + 8.0, $"绕行不应过长，实际 {length}");

            var again = nav.FindPath(Map, from, to, constraint);
            Assert.Equal(path, again); // 确定性：同一输入同一路径
        }

        [Fact]
        public void StubFindPath_TargetOnTopOfAStepTooHigh_IsUnreachable_AndNullTerrainKeepsTheOldBehavior()
        {
            var terrain = Terrain(Block(10, -3, 20, 3, 2.0));
            var constraint = new Constraint(terrain);
            var nav = NewStub();
            Assert.Null(nav.FindPath(Map, new Vec2(0, 0), new Vec2(15, 0), constraint));

            // 不传地形约束：与旧 FindPath 逐位相同（直线）。
            var old = nav.FindPath(Map, new Vec2(0, 0), new Vec2(25, 0));
            var viaNull = nav.FindPath(Map, new Vec2(0, 0), new Vec2(25, 0), null);
            Assert.Equal(old, viaNull);
            Assert.Equal(2, old!.Count);
        }

        [Fact]
        public void StubFindPath_StepWithinStepHeight_IsWalkedStraightOver()
        {
            var terrain = Terrain(Block(10, -3, 20, 3, 0.4)); // 0.4 ≤ 台阶 0.5
            var path = NewStub().FindPath(Map, new Vec2(0, 0), new Vec2(25, 0), new Constraint(terrain));
            Assert.Equal(2, path!.Count);
        }

        [Fact]
        public void StubFindPath_RespectsBlockingRectsAndTerrainTogether()
        {
            var terrain = Terrain(Block(10, -3, 20, 3, 2.0));
            var constraint = new Constraint(terrain);
            var nav = NewStub();
            // 台阶北侧再放一堵阻挡墙：只剩南侧通道。
            nav.SetBlocking(Map, new[] { new Rect(new Vec2(9, 2.5), new Vec2(21, 8)) });
            var path = nav.FindPath(Map, new Vec2(0, 0), new Vec2(25, 0), constraint);
            Assert.NotNull(path);
            for (var i = 0; i + 1 < path!.Count; i++)
            {
                Assert.Null(constraint.FirstStepBlock(Map, path[i], path[i + 1]));
                Assert.Null(nav.Raycast(Map, path[i], path[i + 1]));
            }

            Assert.Contains(path, p => p.Y < -3.0); // 只能从台阶南侧绕
        }

        [Fact]
        public void StubTryFindNearestReachable_PicksAGroundPointNextToAnUnclimbableTopTarget()
        {
            var terrain = Terrain(Block(10, -3, 20, 3, 2.0));
            var constraint = new Constraint(terrain);
            var nav = NewStub();
            var from = new Vec2(0, 0);
            var click = new Vec2(15, 0);
            Assert.True(nav.TryFindNearestReachable(Map, from, click, 8.0, constraint, out var reachable));
            Assert.NotNull(nav.FindPath(Map, from, reachable, constraint));
            Assert.Equal(0.0, terrain.GetGroundHeight(Map, reachable));
            Assert.True(Vec2.Distance(click, reachable) <= 3.5, $"应取台阶旁最近的地面点，实际 {reachable}");

            // 无地形约束：点击点本身。
            Assert.True(nav.TryFindNearestReachable(Map, from, click, 8.0, null, out var plain));
            Assert.Equal(click, plain);
        }

        // ---------------------------------------------------------------- 默认接口实现

        private sealed class StraightNavigation : INavigation2D
        {
            public void BuildNavMesh(Id mapId) { }

            public bool IsWalkable(Id mapId, Vec2 point) => true;

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to) => new List<Vec2> { from, to };

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => null;

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects) { }

            public void Clear(Id mapId) { }
        }

        [Fact]
        public void DefaultInterfaceImpl_TerrainFindPath_VerifiesSegments_AndReturnsNullInsteadOfIgnoringTheTerrain()
        {
            var terrain = Terrain(Block(10, -3, 20, 3, 2.0));
            var constraint = new Constraint(terrain);
            INavigation2D nav = new StraightNavigation();
            Assert.Null(nav.FindPath(Map, new Vec2(0, 0), new Vec2(25, 0), constraint));
            var free = nav.FindPath(Map, new Vec2(0, 0), new Vec2(5, 0), constraint);
            Assert.NotNull(free);
            Assert.Equal(2, free!.Count);
            Assert.Equal(2, nav.FindPath(Map, new Vec2(0, 0), new Vec2(25, 0), null)!.Count);
        }
    }
}
