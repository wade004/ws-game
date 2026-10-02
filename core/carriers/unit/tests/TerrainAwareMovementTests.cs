using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// M4-W1a（ADR-0130 追加决定，M4-V 遗留限制 1～6）：寻路与路径校验感知地形台阶、路径分段按折线精确检测、
    /// 落地只在下落时发生、走下台地无论是否设置 step_height 都下落（落差阈值 <c>FallHeight</c>）、阻挡停点精确到 1e-6。
    /// 每条有复现（X 变到 Y，期望值由规则算出）与不变量（缺省行为不变）。
    /// </summary>
    public class TerrainAwareMovementTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id Faction = new Id("fac.tam");
        private static readonly Id Archetype = new Id("arch.class.sample");
        private static readonly Id HeroId = new Id("unit.tam_hero");
        private const double Dt = 1.0 / 60.0;

        private sealed class Fx
        {
            public WorldSim World = null!;
            public PlayerUnit Hero = null!;
            public VerticalMotionHost Vertical = null!;
            public MapTerrainHeights Terrain = null!;
            public MovementOptions MovementOptions = null!;
            public INavigation2D? Nav;
            public List<(Id Unit, MoveStopReason Reason)> Stops = new List<(Id, MoveStopReason)>();
            public List<MoveFailReason> Fails = new List<MoveFailReason>();
            public double MaxAbsY;
        }

        private static TerrainRegion Flat() => new TerrainRegion(new Vec2(-1000, -1000), new Vec2(1000, 1000), ground: 0.0);

        private static TerrainRegion Block(double x0, double y0, double x1, double y1, double height) =>
            new TerrainRegion(new Vec2(x0, y0), new Vec2(x1, y1), ground: height);

        private static Fx Build(
            Action<VerticalAxisOptions>? configure, INavigation2D? nav, IReadOnlyList<TerrainRegion>? regions, double moveSpeed = 10.0)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var hero = new PlayerUnit(HeroId, MapId, Faction, Archetype) { Position = Vec2.Zero };
            world.AddEntity(hero);

            var terrain = new MapTerrainHeights();
            if (regions != null) terrain.SetRegions(MapId, regions);
            var options = new VerticalAxisOptions();
            if (regions != null) options.Terrain = terrain;
            configure?.Invoke(options);

            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, moveSpeed);
            var movement = new MovementHost(world);
            var movementOptions = new MovementOptions { Vertical = options };
            var handler = new MovementTickHandler(new WorldUnitAccess(world), stats, new FakeAuraQuery(), movement, bus, nav, movementOptions);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);
            var vertical = new VerticalMotionHost(world, options);
            handler.VerticalAxis = vertical;
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new VerticalMotionTickHandler(vertical));

            var fx = new Fx { World = world, Hero = hero, Vertical = vertical, Terrain = terrain, MovementOptions = movementOptions, Nav = nav };
            movement.OnMoveStopped += (u, _, r) => fx.Stops.Add((u, r));
            movement.OnMoveFailedDetailed += (_, _, _, reason) => fx.Fails.Add(reason);
            return fx;
        }

        private static JsonObject Target(double x, double y) =>
            new JsonObjectBuilder().Add("x", new JsonNumber(x)).Add("y", new JsonNumber(y)).Build();

        private static void Tick(Fx fx, double dt)
        {
            fx.World.Tick(SimStep.Continuous(dt));
            fx.MaxAbsY = Math.Max(fx.MaxAbsY, Math.Abs(fx.Hero.Position.Y));
        }

        private static void MoveTo(Fx fx, double x, double y) =>
            fx.World.SubmitIntent(new Intent(HeroId, "move", Target(x, y)));

        // ================================================================== 限制 1：寻路感知台阶

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PathTo_ATargetBehindAStepHigherThanStepHeight_DetoursAroundItInsteadOfBeingBlocked(bool withNavigation)
        {
            INavigation2D? nav = withNavigation ? new StubNavigation2D() : null;
            var fx = Build(o => o.StepHeight = 0.5, nav, new[] { Flat(), Block(10, -3, 20, 3, 2.0) });
            fx.Vertical.Advance(Dt);
            MoveTo(fx, 25, 0);
            for (var i = 0; i < 100; i++) Tick(fx, 0.1);

            Assert.True(Vec2.Distance(fx.Hero.Position, new Vec2(25, 0)) <= fx.MovementOptions.ArrivalEpsilon + 1e-9, $"应绕过台阶到达，实际 {fx.Hero.Position} stops={string.Join(",", fx.Stops)} fails={string.Join(",", fx.Fails)} mode={fx.Hero.MovementState.Mode} path={(fx.Hero.MovementState.CurrentPath == null ? "null" : string.Join(";", fx.Hero.MovementState.CurrentPath))}");
            Assert.True(fx.MaxAbsY > 3.0, $"绕行时应走到台阶侧面之外，最大 |y|={fx.MaxAbsY}");
            Assert.DoesNotContain(fx.Stops, s => s.Reason == MoveStopReason.TerrainBlocked);
            Assert.Empty(fx.Fails);
            Assert.Equal(0.0, fx.Hero.HeightOffset); // 全程在地面，没有爬台阶
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PathTo_ATargetOnTopOfAnUnclimbableStep_FailsWithNoPath_AndTheUnitStaysPut(bool withNavigation)
        {
            INavigation2D? nav = withNavigation ? new StubNavigation2D() : null;
            var fx = Build(o => o.StepHeight = 0.5, nav, new[] { Flat(), Block(10, -3, 20, 3, 2.0) });
            fx.Vertical.Advance(Dt);
            MoveTo(fx, 15, 0);
            for (var i = 0; i < 20; i++) Tick(fx, 0.1);

            Assert.Contains(MoveFailReason.NoPath, fx.Fails);
            Assert.Equal(Vec2.Zero, fx.Hero.Position);
            Assert.Null(fx.Hero.MovementState.CurrentPath);
        }

        [Fact]
        public void PathTo_AStepWithinStepHeight_IsClimbedStraightOver()
        {
            var fx = Build(o => o.StepHeight = 2.5, new StubNavigation2D(), new[] { Flat(), Block(10, -3, 20, 3, 2.0) });
            fx.Vertical.Advance(Dt);
            MoveTo(fx, 25, 0);
            for (var i = 0; i < 60; i++) Tick(fx, 0.1);
            Assert.Equal(25.0, fx.Hero.Position.X, 1);
            Assert.True(fx.MaxAbsY < 1e-9, "台阶在 step_height 之内：直接爬过去，不绕行");
        }

        /// <summary>数着"带地形约束的 FindPath"被调用了几次的导航（其余走桩）。</summary>
        private sealed class SpyNavigation : INavigation2D
        {
            private readonly StubNavigation2D _inner = new StubNavigation2D();
            public int TerrainFindPathCalls;

            public void BuildNavMesh(Id mapId) => _inner.BuildNavMesh(mapId);

            public bool IsWalkable(Id mapId, Vec2 point) => _inner.IsWalkable(mapId, point);

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to) => _inner.FindPath(mapId, from, to);

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to, ITerrainStepConstraint? terrain)
            {
                TerrainFindPathCalls++;
                return _inner.FindPath(mapId, from, to, terrain);
            }

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => _inner.Raycast(mapId, from, to);

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects) => _inner.SetBlocking(mapId, rects);

            public void Clear(Id mapId) => _inner.Clear(mapId);

            public int GetBlockingVersion(Id mapId) => _inner.GetBlockingVersion(mapId);
        }

        [Fact]
        public void Default_WithoutStepHeightOrTerrain_NeverUsesTheTerrainAwarePlanner()
        {
            // 不变量：缺省（没有地形 / 没有 step_height）走旧的 FindPath，逐位不变。
            var plain = new SpyNavigation();
            var a = Build(null, plain, null);
            a.Vertical.Advance(Dt);
            MoveTo(a, 25, 0);
            for (var i = 0; i < 40; i++) Tick(a, 0.1);
            Assert.Equal(0, plain.TerrainFindPathCalls);
            Assert.Equal(25.0, a.Hero.Position.X, 1);

            var noStep = new SpyNavigation();
            var b = Build(null, noStep, new[] { Flat(), Block(10, -3, 20, 3, 2.0) });
            b.Vertical.Advance(Dt);
            MoveTo(b, 25, 0);
            for (var i = 0; i < 40; i++) Tick(b, 0.1);
            Assert.Equal(0, noStep.TerrainFindPathCalls);
            Assert.Equal(25.0, b.Hero.Position.X, 1); // 没有 step_height：不阻挡，直接走过去（跟随地面）

            var active = new SpyNavigation();
            var c = Build(o => o.StepHeight = 0.5, active, new[] { Flat() });
            c.Vertical.Advance(Dt);
            MoveTo(c, 25, 0);
            for (var i = 0; i < 40; i++) Tick(c, 0.1);
            Assert.True(active.TerrainFindPathCalls > 0);
        }

        // ================================================================== 限制 2：路径校验感知地形

        [Fact]
        public void Revalidate_ASegmentThatNowCrossesAStep_TriggersAReplanAroundIt()
        {
            var nav = new StubNavigation2D();
            var fx = Build(o => o.StepHeight = 0.5, nav, new[] { Flat() });
            fx.Vertical.Advance(Dt);
            MoveTo(fx, 25, 0);
            Tick(fx, 0.1); // 路径按空地形规划成直线，单位出发

            // 行走途中出现台阶（地形数据变化），导航版本变化触发路径校验。
            fx.Terrain.SetRegions(MapId, new[] { Flat(), Block(10, -3, 20, 3, 2.0) });
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(100, 100), new Vec2(101, 101)) });
            for (var i = 0; i < 100; i++) Tick(fx, 0.1);

            Assert.True(Vec2.Distance(fx.Hero.Position, new Vec2(25, 0)) <= fx.MovementOptions.ArrivalEpsilon + 1e-9, $"实际 {fx.Hero.Position}");
            Assert.True(fx.MaxAbsY > 3.0);
            Assert.DoesNotContain(fx.Stops, s => s.Reason == MoveStopReason.TerrainBlocked);
        }

        [Fact]
        public void Revalidate_WithoutTerrainChange_KeepsTheStraightPath()
        {
            var nav = new StubNavigation2D();
            var fx = Build(o => o.StepHeight = 0.5, nav, new[] { Flat() });
            fx.Vertical.Advance(Dt);
            MoveTo(fx, 25, 0);
            Tick(fx, 0.1);
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(100, 100), new Vec2(101, 101)) });
            for (var i = 0; i < 40; i++) Tick(fx, 0.1);
            Assert.Equal(25.0, fx.Hero.Position.X, 1);
            Assert.True(fx.MaxAbsY < 1e-9);
        }

        // ================================================================== 限制 3：折线精确分段

        /// <summary>固定返回一条折线的导航（不看地形，用来固定路径形状）。</summary>
        private sealed class FixedPathNavigation : INavigation2D
        {
            private readonly Vec2[] _path;

            public FixedPathNavigation(params Vec2[] path) => _path = path;

            public void BuildNavMesh(Id mapId) { }

            public bool IsWalkable(Id mapId, Vec2 point) => true;

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to) => new List<Vec2>(_path);

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => null;

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects) { }

            public void Clear(Id mapId) { }
        }

        [Fact]
        public void PathFollow_ACornerTurnedInOneTick_IsNotBlockedByAStepOnlyTheChordCrosses()
        {
            // 折线 (0,0)→(0,10)→(10,10)；弦 (0,0)→(10,10) 穿过 (5,5) 的台阶，折线本身不碰它。
            var nav = new FixedPathNavigation(Vec2.Zero, new Vec2(0, 10), new Vec2(10, 10));
            var fx = Build(o => o.StepHeight = 0.5, nav, new[] { Flat(), Block(4, 4, 6, 6, 2.0) });
            fx.Vertical.Advance(Dt);
            MoveTo(fx, 10, 10);
            Tick(fx, 2.5); // 速度 10 × 2.5 = 25 ≥ 折线总长 20：一个 tick 内拐过路点到终点

            Assert.True(Vec2.Distance(fx.Hero.Position, new Vec2(10, 10)) <= 1e-9, $"实际 {fx.Hero.Position}");
            Assert.DoesNotContain(fx.Stops, s => s.Reason == MoveStopReason.TerrainBlocked);
        }

        [Fact]
        public void PathFollow_AStepOnTheSecondSegmentOfACornerTick_StopsExactlyAtItsEdge()
        {
            // 折线 (0,0)→(10,0)→(10,10)；台阶盖住 (10,5)：弦 (0,0)→(10,10) 不碰它，折线第二段碰。路径规划后台阶才出现。
            var nav = new FixedPathNavigation(Vec2.Zero, new Vec2(10, 0), new Vec2(10, 10));
            var fx = Build(o => o.StepHeight = 0.5, nav, new[] { Flat() });
            fx.Vertical.Advance(Dt);
            MoveTo(fx, 10, 10);
            Tick(fx, 0.01); // 出发
            fx.Terrain.SetRegions(MapId, new[] { Flat(), Block(9, 4, 11, 6, 2.0) });
            Tick(fx, 2.5);

            Assert.Contains(fx.Stops, s => s.Reason == MoveStopReason.TerrainBlocked);
            Assert.Equal(10.0, fx.Hero.Position.X, 6);
            var eps = fx.MovementOptions.ArrivalEpsilon;
            Assert.True(fx.Hero.Position.Y < 4.0 && fx.Hero.Position.Y > 4.0 - eps - 1e-6, $"应停在台阶边缘前一个到达容差之内，实际 y={fx.Hero.Position.Y}");
        }

        // ================================================================== 限制 4：落地只在下落时发生

        [Fact]
        public void Landing_WhileRisingIntoAHigherGroundWithinStepHeight_DoesNotLand_ButLandsOnceFalling()
        {
            const double g = 30;
            var platform = Block(5, -100, 10, 100, 0.3);
            var fx = Build(o => { o.Gravity = g; o.StepHeight = 0.5; }, null, new[] { platform });
            fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.Launch(HeroId, 10.0));
            fx.Vertical.Advance(Dt);
            var v0 = 10.0;
            var h1 = v0 * Dt - 0.5 * g * Dt * Dt;
            Assert.Equal(h1, fx.Hero.HeightOffset, 9);

            fx.Hero.Position = new Vec2(7, 0); // 水平进入平台：脚下 0.3 > 当前高度，但仍在上升
            fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.IsAirborne(HeroId), "上升中不落地");
            Assert.True(fx.Vertical.GetVerticalSpeed(HeroId) > 0.0);

            var steps = 2;
            while (fx.Vertical.IsAirborne(HeroId) && steps < 2000)
            {
                fx.Vertical.Advance(Dt);
                steps++;
            }

            Assert.False(fx.Vertical.IsAirborne(HeroId));
            Assert.Equal(0.3, fx.Hero.HeightOffset, 12);
            var apexSteps = v0 / g / Dt;
            Assert.True(steps > apexSteps, $"应在越过顶点后才落地：落地步 {steps}，顶点步 {apexSteps}");
        }

        [Fact]
        public void Landing_DefaultWithoutTerrain_IsBitIdentical_ToTheFlatGroundProjectile()
        {
            const double g = 30;
            var fx = Build(o => o.Gravity = g, null, null);
            fx.Vertical.Launch(HeroId, 6.0);
            var n = 0;
            while (fx.Vertical.IsAirborne(HeroId) && n < 1000)
            {
                fx.Vertical.Advance(Dt);
                n++;
                if (fx.Vertical.IsAirborne(HeroId))
                {
                    Assert.Equal(6.0 * (n * Dt) - 0.5 * g * (n * Dt) * (n * Dt), fx.Hero.HeightOffset, 9);
                }
            }

            Assert.Equal(0.0, fx.Hero.HeightOffset);
            var expectedSteps = 1;
            while (6.0 * (expectedSteps * Dt) - 0.5 * g * (expectedSteps * Dt) * (expectedSteps * Dt) > 0.0) expectedSteps++;
            Assert.InRange(n, expectedSteps, expectedSteps + 1); // 恰在抛体落回地面的那一步（浮点边界上允许差一步）
        }

        // ================================================================== 限制 5：走下台地

        [Fact]
        public void WalkingOffAPlatform_FallsWithOrWithoutStepHeight_FromTheSameHeight()
        {
            foreach (var stepHeight in new double?[] { null, 0.5, 4.0 })
            {
                var fx = Build(o => { o.Gravity = 30; o.StepHeight = stepHeight; o.FallHeight = 1.0; },
                    null, new[] { Block(-100, -100, 5, 100, 3.0) });
                fx.Hero.Position = new Vec2(2, 0);
                fx.Vertical.Advance(Dt);
                Assert.Equal(3.0, fx.Hero.HeightOffset);
                fx.Hero.Position = new Vec2(6, 0);
                fx.Vertical.Advance(Dt);
                Assert.True(fx.Vertical.IsAirborne(HeroId), $"step_height={stepHeight}：落差 3 > fall_height 1 应下落");
                Assert.Equal(3.0, fx.Hero.HeightOffset);
                Assert.Equal(0.0, fx.Vertical.GetVerticalSpeed(HeroId));
            }
        }

        [Fact]
        public void WalkingDownASmallLedgeOrAGentleSlope_StaysGlued_UnderFallHeight()
        {
            var ledge = Build(o => { o.Gravity = 30; o.FallHeight = 1.0; }, null, new[] { Block(-100, -100, 5, 100, 0.6) });
            ledge.Hero.Position = new Vec2(2, 0);
            ledge.Vertical.Advance(Dt);
            ledge.Hero.Position = new Vec2(6, 0);
            ledge.Vertical.Advance(Dt);
            Assert.False(ledge.Vertical.IsAirborne(HeroId));
            Assert.Equal(0.0, ledge.Hero.HeightOffset); // 0.6 ≤ fall_height：贴地

            var slope = Build(o => o.Gravity = 30, null, new[] { new TerrainRegion(new Vec2(0, -100), new Vec2(100, 100), ground: 10.0, slopeX: -1.0) });
            slope.Hero.Position = new Vec2(0, 0);
            slope.Vertical.Advance(Dt);
            for (var x = 0.0; x <= 8.0; x += 0.05)
            {
                slope.Hero.Position = new Vec2(x, 0);
                slope.Vertical.Advance(Dt);
                Assert.False(slope.Vertical.IsAirborne(HeroId), $"x={x}");
                Assert.Equal(10.0 - x, slope.Hero.HeightOffset, 9);
            }
        }

        [Fact]
        public void FallHeightOption_RejectsNegativeAndNonFinite()
        {
            Assert.ThrowsAny<ArgumentException>(() => new VerticalMotionHost(new WorldSim(new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false })),
                new VerticalAxisOptions { FallHeight = -1.0 }));
        }

        // ================================================================== 限制 6：精确停点

        [Fact]
        public void DirectionMove_StopPoint_IsTheExactStepEdge_NotAStepShort()
        {
            // 取一个与采样间隔（缺省 0.1）不对齐的台阶边缘位置 10.03；停点 = 台阶边缘 − ArrivalEpsilon 回退，误差只来自该回退。
            var fx = Build(o => o.StepHeight = 0.5, null, new[] { Flat(), Block(10.03, -100, 1000, 100, 2.0) });
            fx.Vertical.Advance(Dt);
            var hit = fx.Vertical.TerrainBlockPoint(fx.Hero, new Vec2(0, 0), new Vec2(20, 0));
            Assert.NotNull(hit);
            Assert.True(Math.Abs(hit!.Value.X - 10.03) <= 1e-6, $"阻挡点应精确到 1e-6，实际 {hit.Value.X}");

            // 同一面墙从不同起点出发，阻挡点不变（与采样格对齐无关）。
            foreach (var x0 in new[] { 0.0, 0.0123, 3.14159, 9.99 })
            {
                var h = fx.Vertical.TerrainBlockPoint(fx.Hero, new Vec2(x0, 0), new Vec2(20, 0));
                Assert.True(Math.Abs(h!.Value.X - 10.03) <= 1e-6);
            }
        }
    }
}
