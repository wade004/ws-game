using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// 单向平台与移动平台（ADR-0170）：从下面跳穿、从上面落下站住、走出边缘下落、主动下穿、移动平台带走乘客、位姿是平台时钟的解析函数。
    /// 期望值由抛体公式（<c>h = v·t − g·t²/2</c>）与运动声明（单程时长、停留、偏移）在用例里算出，不写死裸数。
    /// </summary>
    public class VerticalPlatformTests
    {
        private static readonly Id MapId = new Id("map.platform_test");
        private static readonly Id HeroId = new Id("unit.platform_hero");
        private static readonly Id OtherId = new Id("unit.platform_other");
        private const double Dt = 1.0 / 60.0;
        private const double Gravity = 30.0;
        private const double JumpHeight = 4.0;
        private const double PlatformTop = 3.0;

        private sealed class Fx
        {
            public WorldSim World = null!;
            public IEventBus Bus = null!;
            public PlayerUnit Hero = null!;
            public PlayerUnit Other = null!;
            public VerticalMotionHost Vertical = null!;
            public MapPlatforms Platforms = null!;
            public List<UnitLandedEvent> Landed = new List<UnitLandedEvent>();
        }

        private static Fx Build(IReadOnlyList<PlatformDef> platforms, Action<VerticalAxisOptions>? configure = null)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var hero = new PlayerUnit(HeroId, MapId, new Id("fac.platform"), new Id("arch.class.sample")) { Position = new Vec2(2, 0) };
            var other = new PlayerUnit(OtherId, MapId, new Id("fac.platform"), new Id("arch.class.sample")) { Position = new Vec2(-20, 0) };
            world.AddEntity(hero);
            world.AddEntity(other);

            var platformSet = new MapPlatforms();
            platformSet.SetPlatforms(MapId, platforms);
            var options = new VerticalAxisOptions
            {
                Gravity = Gravity, JumpHeight = JumpHeight, Terrain = FlatTerrainHeight2D.Instance, Platforms = platformSet, EmitLandedEvent = true,
            };
            configure?.Invoke(options);
            var fx = new Fx { World = world, Bus = bus, Hero = hero, Other = other, Platforms = platformSet, Vertical = new VerticalMotionHost(world, options, bus) };
            bus.Subscribe<UnitLandedEvent>(CarriersEventKeys.UnitLanded, e => fx.Landed.Add(e));
            return fx;
        }

        private static PlatformDef StaticPlatform(string id = "p1", double minX = 0, double maxX = 5, double top = PlatformTop) =>
            new PlatformDef(id, new Vec2(minX, -1), new Vec2(maxX, 1), top);

        private static void Step(Fx fx, int steps = 1)
        {
            for (var i = 0; i < steps; i++) fx.Vertical.Advance(Dt);
            fx.Bus.DispatchPending();
        }

        /// <summary>一直推进到不在空中（上限步数兜底），返回用掉的步数。</summary>
        private static int RunUntilGrounded(Fx fx, int limit = 1200)
        {
            var n = 0;
            while (fx.Vertical.IsAirborne(HeroId) && n < limit)
            {
                fx.Vertical.Advance(Dt);
                n++;
            }

            fx.Bus.DispatchPending();
            return n;
        }

        // ---------------------------------------------------------------- 缺口复现：单值地形表达不了单向平台

        [Fact]
        public void Gap_SingleValuedTerrain_LiftsAUnitStandingUnderTheRegion_SoItCannotExpressAOneWayPlatform()
        {
            // 复现：用 terrain 的矩形区域当"平台"，站在区域下方的单位被直接抬到顶面（地面只有一个值）——
            // 这就是横版关卡里"头顶悬着一块平台"在既有能力下无法表达的症状。
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var hero = new PlayerUnit(HeroId, MapId, new Id("fac.platform"), new Id("arch.class.sample")) { Position = new Vec2(2, 0) };
            world.AddEntity(hero);
            var terrain = new MapTerrainHeights();
            terrain.SetRegions(MapId, new[] { new TerrainRegion(new Vec2(0, -1), new Vec2(5, 1), ground: PlatformTop) });
            var vertical = new VerticalMotionHost(world, new VerticalAxisOptions { Gravity = Gravity, JumpHeight = JumpHeight, Terrain = terrain }, bus);
            vertical.Advance(Dt);
            Assert.Equal(PlatformTop, hero.HeightOffset, 12); // 没法留在地面：被抬上了"平台"
        }

        // ---------------------------------------------------------------- 单向：下方不受影响

        [Fact]
        public void UnitUnderAPlatform_IsNotLifted_AndWalksUnderItFreely()
        {
            var fx = Build(new[] { StaticPlatform() });
            for (var x = 0.0; x <= 5.0; x += 0.5)
            {
                fx.Hero.Position = new Vec2(x, 0);
                Step(fx);
                Assert.Equal(0.0, fx.Hero.HeightOffset, 12);
                Assert.False(fx.Vertical.IsAirborne(HeroId));
                Assert.Null(fx.Vertical.StandingPlatform(HeroId));
            }
        }

        [Fact]
        public void JumpFromBelow_RisesThroughThePlatform_AndLandsOnTopAtTheComputedStep()
        {
            var fx = Build(new[] { StaticPlatform() });
            Step(fx);
            Assert.True(fx.Vertical.Jump(HeroId));

            // 起跳初速与顶点高度由数据（重力、跳跃高度）算出；顶点高于平台，所以会穿过去再落在平台上。
            var v0 = Math.Sqrt(2.0 * Gravity * JumpHeight);
            Assert.True(JumpHeight > PlatformTop);
            var apexHeight = 0.0;
            var steps = 0;
            while (fx.Vertical.IsAirborne(HeroId) && steps < 600)
            {
                fx.Vertical.Advance(Dt);
                apexHeight = Math.Max(apexHeight, fx.Hero.HeightOffset);
                steps++;
            }

            fx.Bus.DispatchPending();
            Assert.InRange(apexHeight, JumpHeight - 0.05, JumpHeight + 1e-9);
            Assert.Equal(PlatformTop, fx.Hero.HeightOffset, 12);
            Assert.Equal("p1", fx.Vertical.StandingPlatform(HeroId));

            // 落点步数：下落段穿过顶面的时刻 t* = (v0 + sqrt(v0² − 2·g·top)) / g，离散积分最多差一步。
            var landTime = (v0 + Math.Sqrt(v0 * v0 - 2.0 * Gravity * PlatformTop)) / Gravity;
            var expectedSteps = (int)Math.Ceiling(landTime / Dt - 1e-9);
            Assert.InRange(steps, expectedSteps - 1, expectedSteps + 1);

            Assert.Single(fx.Landed);
            Assert.Equal(PlatformTop, fx.Landed[0].Height, 12);
        }

        [Fact]
        public void JumpThatDoesNotReachThePlatform_FallsBackToTheGroundBelow()
        {
            var fx = Build(new[] { StaticPlatform(top: 6.0) });
            Step(fx);
            Assert.True(fx.Vertical.Jump(HeroId)); // 顶点 4 < 平台 6
            RunUntilGrounded(fx);
            Assert.Equal(0.0, fx.Hero.HeightOffset, 12);
            Assert.Null(fx.Vertical.StandingPlatform(HeroId));
        }

        [Fact]
        public void StandingOnAPlatform_HoldsTheTopHeight_WhileWalkingAlongIt()
        {
            var fx = Build(new[] { StaticPlatform() });
            Step(fx);
            fx.Vertical.Jump(HeroId);
            RunUntilGrounded(fx);
            for (var x = 1.0; x <= 4.0; x += 0.5)
            {
                fx.Hero.Position = new Vec2(x, 0);
                Step(fx);
                Assert.Equal(PlatformTop, fx.Hero.HeightOffset, 12);
                Assert.False(fx.Vertical.IsAirborne(HeroId));
            }
        }

        // ---------------------------------------------------------------- 走出边缘

        [Fact]
        public void WalkingOffThePlatformEdge_StartsALedgeFall_ThatAllowsCoyoteJump_AndLandsOnTheGround()
        {
            var fx = Build(new[] { StaticPlatform() });
            Step(fx);
            fx.Vertical.Jump(HeroId);
            RunUntilGrounded(fx);
            Assert.Equal("p1", fx.Vertical.StandingPlatform(HeroId));

            fx.Hero.Position = new Vec2(5.5, 0); // 越过平台右缘
            Step(fx);
            Assert.True(fx.Vertical.IsAirborne(HeroId));
            Assert.True(fx.Vertical.IsLedgeFall(HeroId));
            Assert.Null(fx.Vertical.StandingPlatform(HeroId));

            RunUntilGrounded(fx);
            Assert.Equal(0.0, fx.Hero.HeightOffset, 12);
            // 自由落体：从顶面高度落到 0 的步数由 h0 = g·t²/2 算出。
            Assert.Equal(2, fx.Landed.Count);
            var expected = Math.Sqrt(2.0 * PlatformTop / Gravity);
            Assert.InRange(fx.Landed[1].AirSeconds, expected, expected + Dt + 1e-9);
        }

        // ---------------------------------------------------------------- 主动下穿

        [Fact]
        public void DropThrough_FallsThroughTheStandingPlatform_AndLandsBelow_NotOnItAgain()
        {
            var fx = Build(new[] { StaticPlatform() });
            Step(fx);
            Assert.False(fx.Vertical.DropThrough(HeroId)); // 站在地面：拒绝
            fx.Vertical.Jump(HeroId);
            RunUntilGrounded(fx);
            Assert.Equal("p1", fx.Vertical.StandingPlatform(HeroId));

            Assert.True(fx.Vertical.DropThrough(HeroId));
            Assert.True(fx.Vertical.IsAirborne(HeroId));
            Assert.False(fx.Vertical.IsLedgeFall(HeroId)); // 主动下穿不是边缘下落，不可土狼起跳
            Assert.False(fx.Vertical.JumpFromLedge(HeroId));
            Assert.Null(fx.Vertical.StandingPlatform(HeroId));

            var previous = fx.Hero.HeightOffset;
            var fellBelow = false;
            for (var i = 0; i < 600 && fx.Vertical.IsAirborne(HeroId); i++)
            {
                fx.Vertical.Advance(Dt);
                Assert.True(fx.Hero.HeightOffset <= previous + 1e-12); // 一路下降，没有被平台托住
                previous = fx.Hero.HeightOffset;
                if (previous < PlatformTop - 0.5) fellBelow = true;
            }

            Assert.True(fellBelow);
            Assert.Equal(0.0, fx.Hero.HeightOffset, 12);
            Assert.Null(fx.Vertical.StandingPlatform(HeroId));
        }

        [Fact]
        public void Plunge_ReplacesTheVerticalSpeedWithADownwardOne_AndLandsOnTheComputedStep_AndIsRefusedOnTheGround()
        {
            const double downSpeed = 12.0;
            var fx = Build(new[] { StaticPlatform(minX: 100, maxX: 101) }); // 平台在别处，不干扰
            Step(fx);
            Assert.False(fx.Vertical.Plunge(HeroId, downSpeed)); // 地面上：拒绝
            Assert.Throws<ArgumentOutOfRangeException>(() => fx.Vertical.Plunge(HeroId, 0));
            fx.Vertical.Jump(HeroId);
            for (var i = 0; i < 6; i++) fx.Vertical.Advance(Dt); // 上升中
            var height = fx.Hero.HeightOffset;
            Assert.True(fx.Vertical.GetVerticalSpeed(HeroId) > 0);
            Assert.True(fx.Vertical.Plunge(HeroId, downSpeed));
            Assert.Equal(-downSpeed, fx.Vertical.GetVerticalSpeed(HeroId), 12);
            Assert.Equal(height, fx.Hero.HeightOffset, 12); // 高度不变，只换速度

            // 落地步数：h(t) = height − v·t − g·t²/2 = 0 的正根。
            var t = (-downSpeed + Math.Sqrt(downSpeed * downSpeed + 2.0 * Gravity * height)) / Gravity;
            var expected = (int)Math.Ceiling(t / Dt - 1e-9);
            var steps = RunUntilGrounded(fx);
            Assert.InRange(steps, expected - 1, expected + 1);
            Assert.Equal(0.0, fx.Hero.HeightOffset, 12);
        }

        [Fact]
        public void DropThrough_WithoutPlatformCapability_IsRefusedAndHarmless()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            world.AddEntity(new PlayerUnit(HeroId, MapId, new Id("fac.platform"), new Id("arch.class.sample")));
            var vertical = new VerticalMotionHost(world, new VerticalAxisOptions { Terrain = FlatTerrainHeight2D.Instance }, bus);
            Assert.False(vertical.DropThrough(HeroId));
            Assert.Null(vertical.StandingPlatform(HeroId));
        }

        [Fact]
        public void PlatformsWithoutTerrain_AreRejectedAtAssembly()
        {
            var platforms = new MapPlatforms();
            Assert.Throws<ArgumentException>(() => new VerticalAxisOptions { Platforms = platforms }.Validate());
        }

        // ---------------------------------------------------------------- 出生/传送到平台上

        [Fact]
        public void SpawnedExactlyOnAPlatformTop_IsRecognisedAsStandingOnIt_ButAHoveringUnitIsNot()
        {
            var fx = Build(new[] { StaticPlatform() });
            fx.Hero.HeightOffset = PlatformTop;
            fx.Other.Position = new Vec2(2, 0);
            fx.Other.HeightOffset = PlatformTop + 1.5; // 悬空单位：静态高度
            Step(fx);
            Assert.Equal("p1", fx.Vertical.StandingPlatform(HeroId));
            Assert.Null(fx.Vertical.StandingPlatform(OtherId));
            Assert.Equal(PlatformTop + 1.5, fx.Other.HeightOffset, 12);
        }

        // ---------------------------------------------------------------- 移动平台

        private static PlatformDef Mover(double travel, double pause, double dx, double lift = 0.0, double phase = 0.0) =>
            new PlatformDef("mv", new Vec2(0, -1), new Vec2(3, 1), PlatformTop, new PlatformMotionDef(new Vec2(dx, 0), lift, travel, pause, phase));

        private static Fx StandOnMover(PlatformDef mover)
        {
            var fx = Build(new[] { mover });
            fx.Hero.Position = new Vec2(1.5, 0);
            fx.Hero.HeightOffset = PlatformTop;
            Step(fx);
            Assert.Equal("mv", fx.Vertical.StandingPlatform(HeroId));
            return fx;
        }

        [Fact]
        public void MovingPlatform_CarriesItsRider_ByTheClosedFormPose_AndLeavesOthersAlone()
        {
            const double travel = 2.0;
            const double pause = 0.5;
            const double dx = 6.0;
            var motion = new PlatformMotionDef(new Vec2(dx, 0), 0.0, travel, pause);
            var fx = StandOnMover(Mover(travel, pause, dx));
            var startX = fx.Hero.Position.X;
            var startTime = fx.Platforms.TimeSeconds; // 单位从这一刻起被带走
            var otherStart = fx.Other.Position;

            // 逐步核对：乘客位置 = 起点 + 偏移 × 运动进度（解析式），不是积分累加。
            var time = 0.0;
            for (var i = 0; i < 400; i++)
            {
                fx.Vertical.Advance(Dt);
                time += Dt;
                var expectedX = startX + dx * (motion.Fraction(fx.Platforms.TimeSeconds) - motion.Fraction(startTime));
                Assert.True(Math.Abs(expectedX - fx.Hero.Position.X) < 1e-9, $"i={i} t={fx.Platforms.TimeSeconds} exp={expectedX} act={fx.Hero.Position.X}");
                Assert.Equal(PlatformTop, fx.Hero.HeightOffset, 12);
                Assert.Equal("mv", fx.Vertical.StandingPlatform(HeroId));
            }

            Assert.Equal(startTime + time, fx.Platforms.TimeSeconds, 9);
            Assert.Equal(otherStart, fx.Other.Position); // 不在平台上的单位不动
        }

        [Fact]
        public void CarriedRiders_AreWrittenThroughTheUnitAccess_SoTheSpatialIndexFollowsThem()
        {
            const double travel = 2.0;
            const double dx = 6.0;
            var fx = StandOnMover(Mover(travel, 0.0, dx));
            var spatial = new Adapters.Stub.StubSpatialQuery();
            spatial.Register(HeroId, fx.Hero.Position, 0.1, new[] { "unit" });
            fx.Vertical.UnitAccess = new WorldUnitAccess(fx.World, spatial);
            for (var i = 0; i < 60; i++) fx.Vertical.Advance(Dt);
            var found = spatial.QueryRadius(fx.Hero.Position, 0.5, new QueryFilter(new[] { "unit" }));
            Assert.Contains(HeroId, found); // 索引里的位置跟着乘客走，不是停在起点
            Assert.True(fx.Hero.Position.X > 1.5 + 1.0);
        }

        [Fact]
        public void MovingPlatform_AtTheEndPause_HoldsStill_AndReturnsToTheStart_ThePhaseFollowsTheDeclaration()
        {
            const double travel = 1.0;
            const double pause = 1.0;
            var motion = new PlatformMotionDef(new Vec2(4, 0), 0.0, travel, pause);
            Assert.Equal(0.0, motion.Fraction(0.0), 12);
            Assert.Equal(0.5, motion.Fraction(travel / 2), 12);
            Assert.Equal(1.0, motion.Fraction(travel), 12);
            Assert.Equal(1.0, motion.Fraction(travel + pause - 1e-9), 6); // 终点停留
            Assert.Equal(0.5, motion.Fraction(travel + pause + travel / 2), 12); // 回程
            Assert.Equal(0.0, motion.Fraction(2 * (travel + pause) - 0.2 * travel + 0.2 * travel), 12); // 周期结束回到起点停留
            Assert.Equal(motion.Fraction(0.3), motion.Fraction(0.3 + 2 * (travel + pause)), 9); // 周期性
            var shifted = new PlatformMotionDef(new Vec2(4, 0), 0.0, travel, pause, phase: 0.25);
            Assert.Equal(motion.Fraction(0.25 + 0.4), shifted.Fraction(0.4), 12);
        }

        [Fact]
        public void LiftingPlatform_RaisesItsRider_ByTheLiftTimesTheFraction()
        {
            const double travel = 3.0;
            const double lift = 2.5;
            var motion = new PlatformMotionDef(Vec2.Zero, lift, travel);
            var fx = StandOnMover(Mover(travel, 0.0, 0.0, lift));
            for (var i = 0; i < 120; i++)
            {
                fx.Vertical.Advance(Dt);
                Assert.Equal(PlatformTop + lift * motion.Fraction(fx.Platforms.TimeSeconds), fx.Hero.HeightOffset, 9);
            }

            Assert.True(fx.Hero.HeightOffset > PlatformTop + 0.5);
        }

        [Fact]
        public void RiderWhoJumps_IsNoLongerCarried_AndLandsBackOnTheMovingPlatformUnderTheirFeet()
        {
            const double travel = 4.0;
            const double dx = 1.6; // 空中约 1 秒平台只挪 0.4，起跳点仍在平台范围内
            var fx = StandOnMover(Mover(travel, 0.0, dx, phase: 0.0));
            fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.Jump(HeroId));
            var xAtJump = fx.Hero.Position.X;
            var steps = 0;
            while (fx.Vertical.IsAirborne(HeroId) && steps < 600)
            {
                fx.Vertical.Advance(Dt);
                steps++;
                Assert.Equal(xAtJump, fx.Hero.Position.X, 12); // 空中不被平台带走（水平位置只由自己的移动决定）
            }

            // 平台在这段时间里挪了 dx·空中时间/单程时长（远小于它的宽度），起跳点仍在范围内，单位落回平台。
            Assert.Equal("mv", fx.Vertical.StandingPlatform(HeroId));
            Assert.Equal(PlatformTop, fx.Hero.HeightOffset, 9);
        }

        [Fact]
        public void RiderLeftBehindByTheMovingPlatform_FallsOffTheEdge()
        {
            const double travel = 2.0;
            const double dx = 20.0;
            var fx = StandOnMover(Mover(travel, 0.0, dx));
            // 乘客自己往回走，平台向前跑——很快走出平台后缘。
            for (var i = 0; i < 200 && !fx.Vertical.IsAirborne(HeroId); i++)
            {
                fx.Hero.Position = new Vec2(fx.Hero.Position.X - 0.2, 0);
                fx.Vertical.Advance(Dt);
            }

            Assert.True(fx.Vertical.IsAirborne(HeroId));
            Assert.True(fx.Vertical.IsLedgeFall(HeroId));
            RunUntilGrounded(fx);
            Assert.Equal(0.0, fx.Hero.HeightOffset, 12);
        }

        [Fact]
        public void SetTime_RestoresThePoseOfAnySavedMoment_WithoutMovingAnyone()
        {
            const double travel = 2.0;
            const double dx = 6.0;
            var motion = new PlatformMotionDef(new Vec2(dx, 0), 0.0, travel);
            var fx = Build(new[] { Mover(travel, 0.0, dx) });
            fx.Platforms.SetTime(1.3);
            Assert.True(fx.Platforms.TryGetPose(MapId, "mv", out var min, out var max, out var top));
            Assert.Equal(dx * motion.Fraction(1.3), min.X, 12);
            Assert.Equal(3.0 + dx * motion.Fraction(1.3), max.X, 12);
            Assert.Equal(PlatformTop, top, 12);
            Assert.Empty(fx.Platforms.LastMotions);
            Assert.Equal(1.3, fx.Platforms.TimeSeconds, 12);
        }

        [Fact]
        public void TwoRunsOfTheSamePlatformScenario_AreBitIdentical()
        {
            double[] Run()
            {
                var fx = StandOnMover(Mover(2.0, 0.5, 6.0, 1.0));
                var trace = new List<double>();
                for (var i = 0; i < 300; i++)
                {
                    fx.Vertical.Advance(Dt);
                    trace.Add(fx.Hero.Position.X);
                    trace.Add(fx.Hero.HeightOffset);
                }

                return trace.ToArray();
            }

            Assert.Equal(Run(), Run());
        }

        // ---------------------------------------------------------------- 多块平台与高度选择

        [Fact]
        public void StackedPlatforms_AFallingUnitLandsOnTheFirstOneItCrosses_TheHighest()
        {
            var fx = Build(new[]
            {
                StaticPlatform("low", top: 2.0),
                StaticPlatform("high", top: 5.0),
            });
            fx.Hero.HeightOffset = 8.0;
            Step(fx);
            fx.Vertical.Launch(HeroId, 0.5); // 从 8 起飞，立刻下落
            RunUntilGrounded(fx);
            Assert.Equal(5.0, fx.Hero.HeightOffset, 12);
            Assert.Equal("high", fx.Vertical.StandingPlatform(HeroId));
        }
    }
}
