using System;
using System.Collections.Generic;
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
    /// 竖直轴能力包补完（ADR-0130 追加决定，M4-V）：空中控制比例、空中跳跃次数、地形（落地高度/天花板/斜坡/台阶下落/台阶阻挡）、击飞叠加。
    /// 每条行为有复现（X 变到 Y，期望值由规则算出：抛体公式、台阶高度比较、倍率乘法）与不变量（缺省选项逐位等价于 1.95.0）。
    /// </summary>
    public class VerticalAxisCompletionTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id Faction = new Id("fac.vac");
        private static readonly Id Archetype = new Id("arch.class.sample");
        private static readonly Id HeroId = new Id("unit.vac_hero");
        private static readonly Id OtherId = new Id("unit.vac_other");
        private const double Dt = 1.0 / 60.0;

        private sealed class Fx
        {
            public WorldSim World = null!;
            public PlayerUnit Hero = null!;
            public PlayerUnit Other = null!;
            public VerticalMotionHost Vertical = null!;
            public VerticalAxisOptions Options = null!;
            public MapTerrainHeights Terrain = null!;
            public MovementHost Movement = null!;
            public MovementTickHandler Handler = null!;
            public FakeStatHost Stats = null!;
            public List<(Id Unit, MoveStopReason Reason)> Stops = new List<(Id, MoveStopReason)>();
        }

        private static Fx Build(Action<VerticalAxisOptions>? configure = null, IReadOnlyList<TerrainRegion>? regions = null, double moveSpeed = 10.0)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var hero = new PlayerUnit(HeroId, MapId, Faction, Archetype) { Position = Vec2.Zero };
            var other = new PlayerUnit(OtherId, MapId, Faction, Archetype) { Position = new Vec2(0, 50) };
            world.AddEntity(hero);
            world.AddEntity(other);

            var terrain = new MapTerrainHeights();
            if (regions != null) terrain.SetRegions(MapId, regions);

            var options = new VerticalAxisOptions();
            if (regions != null) options.Terrain = terrain;
            configure?.Invoke(options);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, moveSpeed);
            var movement = new MovementHost(world);
            var movementOptions = new MovementOptions { Vertical = options };
            var handler = new MovementTickHandler(units, stats, new FakeAuraQuery(), movement, bus, null, movementOptions);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);
            var vertical = new VerticalMotionHost(world, options);
            handler.VerticalAxis = vertical;
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new VerticalMotionTickHandler(vertical));

            var fx = new Fx
            {
                World = world, Hero = hero, Other = other, Vertical = vertical, Options = options,
                Terrain = terrain, Movement = movement, Handler = handler, Stats = stats,
            };
            movement.OnMoveStopped += (u, _, r) => fx.Stops.Add((u, r));
            return fx;
        }

        private static JsonObject Dir(double dx, double dy) =>
            new JsonObjectBuilder().Add("dx", new JsonNumber(dx)).Add("dy", new JsonNumber(dy)).Build();

        private static JsonObject Target(double x, double y) =>
            new JsonObjectBuilder().Add("x", new JsonNumber(x)).Add("y", new JsonNumber(y)).Build();

        private static void MoveTick(Fx fx, double dx, double dy, double dt = Dt)
        {
            fx.World.SubmitIntent(new Intent(HeroId, "move", Dir(dx, dy)));
            fx.World.Tick(SimStep.Continuous(dt));
        }

        // ================================================================== 空中跳跃次数

        [Fact]
        public void MaxAirJumps_AllowsExactlyThatManyAirJumps_AndLandingResetsTheCount()
        {
            var fx = Build(o => { o.JumpHeight = 2.0; o.Gravity = 30; o.MaxAirJumps = 2; });
            Assert.True(fx.Vertical.Jump(HeroId));
            Assert.Equal(0, fx.Vertical.AirJumpsUsed(HeroId));
            fx.Vertical.Advance(Dt);

            Assert.True(fx.Vertical.Jump(HeroId));
            Assert.Equal(1, fx.Vertical.AirJumpsUsed(HeroId));
            Assert.True(fx.Vertical.Jump(HeroId));
            Assert.Equal(2, fx.Vertical.AirJumpsUsed(HeroId));
            Assert.False(fx.Vertical.Jump(HeroId));
            Assert.Equal(2, fx.Vertical.AirJumpsUsed(HeroId));

            for (var i = 0; i < 600 && fx.Vertical.IsAirborne(HeroId); i++) fx.Vertical.Advance(Dt);
            Assert.False(fx.Vertical.IsAirborne(HeroId));
            Assert.Equal(0, fx.Vertical.AirJumpsUsed(HeroId));
            Assert.True(fx.Vertical.Jump(HeroId));
            Assert.True(fx.Vertical.Jump(HeroId));
        }

        [Fact]
        public void MaxAirJumpsZero_RefusesAirJump_EvenWhenAllowAirJumpIsOn()
        {
            var fx = Build(o => { o.AllowAirJump = true; o.MaxAirJumps = 0; });
            Assert.True(fx.Vertical.Jump(HeroId));
            fx.Vertical.Advance(Dt);
            Assert.False(fx.Vertical.Jump(HeroId));
        }

        [Fact]
        public void MaxAirJumpsUnset_FollowsAllowAirJump_Exactly()
        {
            var off = Build();
            off.Vertical.Jump(HeroId);
            off.Vertical.Advance(Dt);
            Assert.False(off.Vertical.Jump(HeroId));

            var on = Build(o => o.AllowAirJump = true);
            on.Vertical.Jump(HeroId);
            for (var i = 0; i < 5; i++)
            {
                on.Vertical.Advance(Dt);
                Assert.True(on.Vertical.Jump(HeroId)); // 无限次，与 1.95.0 一致
            }
        }

        [Fact]
        public void BeingLaunchedMidAir_DoesNotResetOrRefundTheAirJumpCount()
        {
            var fx = Build(o => { o.MaxAirJumps = 1; });
            fx.Vertical.Jump(HeroId);
            fx.Vertical.Advance(Dt);
            fx.Vertical.Jump(HeroId);
            Assert.Equal(1, fx.Vertical.AirJumpsUsed(HeroId));
            fx.Vertical.LaunchToApex(HeroId, 1.0);
            Assert.Equal(1, fx.Vertical.AirJumpsUsed(HeroId));
            Assert.False(fx.Vertical.Jump(HeroId));
        }

        // ================================================================== 空中控制

        [Fact]
        public void GetAirControl_IsTheDeclaredRatioOnlyWhileAirborne_AndOneOtherwise()
        {
            var fx = Build(o => o.AirControl = 0.4);
            Assert.Equal(1.0, fx.Vertical.GetAirControl(HeroId));
            fx.Vertical.Launch(HeroId, 5.0);
            Assert.Equal(0.4, fx.Vertical.GetAirControl(HeroId));
            for (var i = 0; i < 600 && fx.Vertical.IsAirborne(HeroId); i++) fx.Vertical.Advance(Dt);
            Assert.Equal(1.0, fx.Vertical.GetAirControl(HeroId));

            var plain = Build();
            plain.Vertical.Launch(HeroId, 5.0);
            Assert.Equal(1.0, plain.Vertical.GetAirControl(HeroId));
        }

        [Fact]
        public void DirectionMove_InAir_IsScaledByAirControl_AndGroundSpeedIsUntouched()
        {
            const double speed = 10.0;
            const double ratio = 0.5;
            var fx = Build(o => { o.AirControl = ratio; o.Gravity = 5; }, moveSpeed: speed);

            // 地面：位移 = speed·dt。
            MoveTick(fx, 1, 0);
            Assert.Equal(speed * Dt, fx.Hero.Position.X, 9);

            fx.Vertical.Launch(HeroId, 20.0); // 飞行远多于下面的 tick 数
            var before = fx.Hero.Position.X;
            for (var i = 0; i < 10; i++) MoveTick(fx, 1, 0);
            Assert.True(fx.Vertical.IsAirborne(HeroId));
            Assert.Equal(before + 10 * speed * ratio * Dt, fx.Hero.Position.X, 9);
        }

        [Fact]
        public void AirControlZero_BlocksVoluntaryMovementInAir_ButNotOnTheGround()
        {
            var fx = Build(o => { o.AirControl = 0.0; o.Gravity = 5; });
            fx.Vertical.Launch(HeroId, 20.0);
            for (var i = 0; i < 10; i++) MoveTick(fx, 1, 0);
            Assert.Equal(0.0, fx.Hero.Position.X);

            for (var i = 0; i < 4000 && fx.Vertical.IsAirborne(HeroId); i++) fx.World.Tick(SimStep.Continuous(Dt));
            Assert.False(fx.Vertical.IsAirborne(HeroId));
            MoveTick(fx, 1, 0);
            Assert.True(fx.Hero.Position.X > 0.0);
        }

        [Fact]
        public void AirControlUnset_MovesAtFullSpeedInAir_LikeNineteen95()
        {
            var fx = Build(o => o.Gravity = 5);
            fx.Vertical.Launch(HeroId, 20.0);
            for (var i = 0; i < 10; i++) MoveTick(fx, 1, 0);
            Assert.Equal(10 * 10.0 * Dt, fx.Hero.Position.X, 9);
        }

        [Fact]
        public void ControlledDisplacement_IsNotScaledByAirControl()
        {
            // 受控位移（技能位移、击退）是外力，不是自愿移动，空中控制比例不缩放它。
            var fx = Build(o => { o.AirControl = 0.0; o.Gravity = 5; });
            fx.Vertical.Launch(HeroId, 20.0);
            fx.Movement.BeginControlledDisplacement(new ControlledDisplacementRequest(
                HeroId, Vec2.Zero, new Vec2(4, 0), 4.0, DisplacementBlockingPolicy.Stop, 0));
            fx.World.Tick(SimStep.Continuous(1.0));
            Assert.Equal(4.0, fx.Hero.Position.X, 9);
        }

        // ================================================================== 地形：落地高度 / 天花板 / 斜坡 / 台阶

        private static TerrainRegion Platform(double minX, double maxX, double height, double ceiling = double.PositiveInfinity) =>
            new TerrainRegion(new Vec2(minX, -100), new Vec2(maxX, 100), ground: height, ceiling: ceiling);

        [Fact]
        public void Terrain_LaunchedUnitLandsOnThePlatformHeight_NotOnZero()
        {
            var fx = Build(o => o.Gravity = 24, new[] { Platform(5, 10, 2.0) });
            fx.Hero.Position = new Vec2(6, 0);
            fx.Vertical.Advance(Dt); // 首次观测：脚下低于地面，抬到平台
            Assert.Equal(2.0, fx.Hero.HeightOffset);

            const double v0 = 7.0;
            fx.Vertical.Launch(HeroId, v0);
            var expectedSteps = (int)Math.Ceiling(2.0 * v0 / fx.Options.Gravity / Dt - 1e-9);
            for (var n = 1; n < expectedSteps; n++)
            {
                fx.Vertical.Advance(Dt);
                Assert.Equal(2.0 + v0 * n * Dt - 0.5 * 24 * n * n * Dt * Dt, fx.Hero.HeightOffset, 9);
                Assert.True(fx.Vertical.IsAirborne(HeroId));
            }

            fx.Vertical.Advance(Dt);
            Assert.False(fx.Vertical.IsAirborne(HeroId));
            Assert.Equal(2.0, fx.Hero.HeightOffset);
        }

        [Fact]
        public void Terrain_FlatDefault_BehavesLikeZeroGround()
        {
            // 不变量：声明了地形能力但脚下没有区域（平地 0、无天花板）时，抛体曲线与落地步数和 1.95.0 逐位一致。
            var plain = Build(o => o.Gravity = 24);
            var withTerrain = Build(o => o.Gravity = 24, new[] { Platform(500, 600, 3.0) });
            plain.Vertical.Launch(HeroId, 7.0);
            withTerrain.Vertical.Launch(HeroId, 7.0);
            for (var i = 0; i < 200; i++)
            {
                plain.Vertical.Advance(Dt);
                withTerrain.Vertical.Advance(Dt);
                Assert.Equal(plain.Hero.HeightOffset, withTerrain.Hero.HeightOffset);
                Assert.Equal(plain.Vertical.IsAirborne(HeroId), withTerrain.Vertical.IsAirborne(HeroId));
            }
        }

        [Fact]
        public void Terrain_Ceiling_ZeroesTheRisingSpeed_AndNeverLetsTheFootPassIt()
        {
            const double ceiling = 3.0;
            var fx = Build(o => o.Gravity = 24, new[] { Platform(-100, 100, 0.0, ceiling) });
            fx.Vertical.LaunchToApex(HeroId, 8.0); // 不受限的顶点 8 远高于天花板 3
            var hitCeiling = false;
            for (var i = 0; i < 400 && fx.Vertical.IsAirborne(HeroId); i++)
            {
                fx.Vertical.Advance(Dt);
                Assert.True(fx.Hero.HeightOffset <= ceiling + 1e-9, $"脚下 {fx.Hero.HeightOffset} 超过天花板");
                if (!hitCeiling && Math.Abs(fx.Hero.HeightOffset - ceiling) < 1e-12)
                {
                    hitCeiling = true;
                    Assert.Equal(0.0, fx.Vertical.GetVerticalSpeed(HeroId)); // 撞顶那一刻竖直速度清零
                }
            }

            Assert.True(hitCeiling);
            Assert.False(fx.Vertical.IsAirborne(HeroId));
            Assert.Equal(0.0, fx.Hero.HeightOffset);
        }

        [Fact]
        public void Terrain_Ceiling_CollisionStartsAFallFromTheCeilingHeight()
        {
            const double g = 24;
            const double ceiling = 3.0;
            var fx = Build(o => o.Gravity = g, new[] { Platform(-100, 100, 0.0, ceiling) });
            fx.Vertical.LaunchToApex(HeroId, 8.0);
            var steps = 0;
            while (fx.Hero.HeightOffset < ceiling && steps++ < 400) fx.Vertical.Advance(Dt);
            Assert.Equal(ceiling, fx.Hero.HeightOffset, 12);

            // 撞顶后初速 0 的抛体：下一步 h = ceiling − g·dt²/2。
            fx.Vertical.Advance(Dt);
            Assert.Equal(ceiling - 0.5 * g * Dt * Dt, fx.Hero.HeightOffset, 9);
        }

        [Fact]
        public void Terrain_Slope_GroundedUnitFootFollowsTheGroundHeight()
        {
            // 斜坡：地面 = 0.5·(x − 0)，x∈[0,100]。
            var slope = new TerrainRegion(new Vec2(0, -100), new Vec2(100, 100), ground: 0.0, slopeX: 0.5);
            var fx = Build(null, new[] { slope });
            fx.Vertical.Advance(Dt); // 登记（x=0，地面 0）
            Assert.Equal(0.0, fx.Hero.HeightOffset);

            foreach (var x in new[] { 2.0, 4.0, 8.0, 3.0 })
            {
                fx.Hero.Position = new Vec2(x, 0);
                fx.Vertical.Advance(Dt);
                Assert.Equal(0.5 * x, fx.Hero.HeightOffset, 12);
                Assert.False(fx.Vertical.IsAirborne(HeroId));
            }
        }

        [Fact]
        public void Terrain_WalkingOffALedgeHigherThanStepHeight_StartsAZeroSpeedFall()
        {
            const double g = 30;
            var fx = Build(o => { o.Gravity = g; o.StepHeight = 0.5; }, new[] { Platform(-100, 5, 3.0) });
            fx.Hero.Position = new Vec2(2, 0);
            fx.Vertical.Advance(Dt);
            Assert.Equal(3.0, fx.Hero.HeightOffset);

            fx.Hero.Position = new Vec2(6, 0); // 走出平台：落差 3 > 台阶 0.5
            fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.IsAirborne(HeroId));
            Assert.Equal(3.0, fx.Hero.HeightOffset);
            Assert.Equal(0.0, fx.Vertical.GetVerticalSpeed(HeroId));

            var expectedSteps = (int)Math.Ceiling(Math.Sqrt(2.0 * 3.0 / g) / Dt - 1e-9);
            for (var n = 1; n < expectedSteps; n++)
            {
                fx.Vertical.Advance(Dt);
                Assert.Equal(3.0 - 0.5 * g * n * n * Dt * Dt, fx.Hero.HeightOffset, 9);
            }

            fx.Vertical.Advance(Dt);
            Assert.False(fx.Vertical.IsAirborne(HeroId));
            Assert.Equal(0.0, fx.Hero.HeightOffset);
        }

        [Fact]
        public void Terrain_WalkingOffALedgeWithoutStepHeight_StartsAZeroSpeedFall()
        {
            // M4-W1a（限制 5）：step_height 只决定能否走上去；走下台地无论是否设置它都从台地高度开始下落（不再瞬间贴地）。
            var fx = Build(null, new[] { Platform(-100, 5, 3.0) });
            fx.Hero.Position = new Vec2(2, 0);
            fx.Vertical.Advance(Dt);
            fx.Hero.Position = new Vec2(6, 0);
            fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.IsAirborne(HeroId));
            Assert.Equal(3.0, fx.Hero.HeightOffset);
            Assert.Equal(0.0, fx.Vertical.GetVerticalSpeed(HeroId));
        }

        [Fact]
        public void Terrain_FloatingUnitThatWasNeverGrounded_KeepsItsStaticHeight()
        {
            var fx = Build(null, new[] { Platform(-100, 100, 0.0) });
            fx.Other.HeightOffset = 2.5; // 悬空靶：静态高度
            for (var i = 0; i < 20; i++)
            {
                fx.Other.Position = new Vec2(i, 50);
                fx.Vertical.Advance(Dt);
            }

            Assert.Equal(2.5, fx.Other.HeightOffset);
        }

        // ---------- 台阶阻挡（与导航阻挡同一出口） ----------

        private static TerrainRegion Wall(double fromX, double height) =>
            new TerrainRegion(new Vec2(fromX, -100), new Vec2(1000, 100), ground: height);

        [Fact]
        public void StepBlocking_DirectionMove_StopsAtTheStepEdge_WhenStepIsHigherThanStepHeight()
        {
            var fx = Build(o => o.StepHeight = 0.5, new[] { Wall(10, 2.0) }, moveSpeed: 10.0);
            fx.Vertical.Advance(Dt);
            for (var i = 0; i < 200; i++) MoveTick(fx, 1, 0, 0.1);
            Assert.True(fx.Hero.Position.X < 10.0, $"应停在台阶前，实际 x={fx.Hero.Position.X}");
            Assert.True(fx.Hero.Position.X > 10.0 - 0.5, $"应贴着台阶边缘，实际 x={fx.Hero.Position.X}");
            Assert.Equal(0.0, fx.Hero.HeightOffset);
        }

        [Fact]
        public void StepBlocking_StepWithinStepHeight_IsClimbed()
        {
            var fx = Build(o => o.StepHeight = 2.5, new[] { Wall(10, 2.0) }, moveSpeed: 10.0);
            fx.Vertical.Advance(Dt);
            for (var i = 0; i < 30; i++)
            {
                MoveTick(fx, 1, 0, 0.1);
            }

            Assert.True(fx.Hero.Position.X > 10.0);
            Assert.Equal(2.0, fx.Hero.HeightOffset, 12);
        }

        [Fact]
        public void StepBlocking_UnitHighEnoughInTheAir_PassesOverTheStep()
        {
            var fx = Build(o => { o.StepHeight = 0.5; o.Gravity = 1; }, new[] { Wall(10, 2.0) }, moveSpeed: 10.0);
            fx.Vertical.Advance(Dt);
            fx.Vertical.Launch(HeroId, 30.0); // 缓慢重力：长时间留空，脚下高度远高于台阶面
            fx.Hero.HeightOffset = 5.0;
            for (var i = 0; i < 8; i++) MoveTick(fx, 1, 0, 0.1);
            Assert.True(fx.Hero.Position.X > 7.0);
            for (var i = 0; i < 8; i++) MoveTick(fx, 1, 0, 0.1);
            Assert.True(fx.Hero.Position.X > 10.0, $"空中足够高应越过台阶，实际 x={fx.Hero.Position.X}");
        }

        [Fact]
        public void StepBlocking_NoStepHeight_NeverBlocks_AndUnitFollowsTheGround()
        {
            var fx = Build(null, new[] { Wall(10, 2.0) }, moveSpeed: 10.0);
            fx.Vertical.Advance(Dt);
            for (var i = 0; i < 30; i++) MoveTick(fx, 1, 0, 0.1);
            Assert.True(fx.Hero.Position.X > 10.0);
            Assert.Equal(2.0, fx.Hero.HeightOffset, 12);
        }

        [Fact]
        public void StepBlocking_PathFollow_EndsWithTerrainBlockedAndClearsThePath()
        {
            // M4-W1a：台阶在规划时已知则寻路直接判无路（见 TerrainAwareNavigationTests）；本用例锁住"规划之后地形才变"的兜底：
            // 路径按空地形规划，行走途中台阶出现，移动阻挡仍把单位截在台阶前并以 TerrainBlocked 结束。
            var fx = Build(o => o.StepHeight = 0.5, new[] { Platform(-100, 100, 0.0) }, moveSpeed: 10.0);
            fx.Vertical.Advance(Dt);
            fx.World.SubmitIntent(new Intent(HeroId, "move", Target(20, 0)));
            fx.World.Tick(SimStep.Continuous(0.1));
            fx.Terrain.SetRegions(MapId, new[] { Platform(-100, 100, 0.0), Wall(10, 2.0) });
            for (var i = 0; i < 40; i++) fx.World.Tick(SimStep.Continuous(0.1));

            Assert.True(fx.Hero.Position.X < 10.0);
            Assert.True(fx.Hero.Position.X > 10.0 - 0.5);
            Assert.Contains(fx.Stops, s => s.Unit == HeroId && s.Reason == MoveStopReason.TerrainBlocked);
            Assert.Null(fx.Hero.MovementState.CurrentPath);
            Assert.Equal(MoveMode.Idle, fx.Hero.MovementState.Mode);
        }

        [Fact]
        public void StepBlocking_ControlledDisplacement_EndsAsBlockedAtTheStep()
        {
            var fx = Build(o => o.StepHeight = 0.5, new[] { Wall(10, 2.0) });
            fx.Vertical.Advance(Dt);
            fx.Movement.BeginControlledDisplacement(new ControlledDisplacementRequest(
                HeroId, Vec2.Zero, new Vec2(20, 0), 100.0, DisplacementBlockingPolicy.Stop, 0));
            fx.World.Tick(SimStep.Continuous(1.0));
            Assert.True(fx.Hero.Position.X < 10.0 && fx.Hero.Position.X > 9.0, $"x={fx.Hero.Position.X}");
            Assert.Contains(fx.Stops, s => s.Reason == MoveStopReason.DisplacementBlocked);
        }

        [Fact]
        public void TerrainBlockPoint_ReturnsTheStepEdge_AndNullWhenNothingBlocks()
        {
            var fx = Build(o => o.StepHeight = 0.5, new[] { Wall(10, 2.0) });
            var hit = fx.Vertical.TerrainBlockPoint(fx.Hero, new Vec2(0, 0), new Vec2(20, 0));
            Assert.NotNull(hit);
            Assert.Equal(10.0, hit!.Value.X, 6);
            Assert.Null(fx.Vertical.TerrainBlockPoint(fx.Hero, new Vec2(0, 0), new Vec2(9, 0)));
            Assert.Null(fx.Vertical.TerrainBlockPoint(fx.Hero, new Vec2(20, 0), new Vec2(30, 0))); // 台阶面上继续走：无落差
        }

        [Fact]
        public void TerrainBlockPoint_SlopeIsWalkableBelowTheStepHeightPerSample_AndBlockingAbove()
        {
            // 每个采样点（默认间隔 0.1）的地面升高 = 斜率 × 0.1：斜率 1 → 0.1 < 0.5 可走；斜率 20 → 2 > 0.5 被挡。
            var gentle = Build(o => o.StepHeight = 0.5, new[] { new TerrainRegion(new Vec2(0, -100), new Vec2(100, 100), slopeX: 1.0) });
            Assert.Null(gentle.Vertical.TerrainBlockPoint(gentle.Hero, new Vec2(0, 0), new Vec2(50, 0)));
            var steep = Build(o => o.StepHeight = 0.5, new[] { new TerrainRegion(new Vec2(0, -100), new Vec2(100, 100), slopeX: 20.0) });
            Assert.NotNull(steep.Vertical.TerrainBlockPoint(steep.Hero, new Vec2(0, 0), new Vec2(50, 0)));
        }

        [Fact]
        public void TerrainBlockPoint_IsNullWithoutStepHeight_OrWithoutTerrain()
        {
            var noStep = Build(null, new[] { Wall(10, 2.0) });
            Assert.False(noStep.Vertical.StepBlockingActive);
            Assert.Null(noStep.Vertical.TerrainBlockPoint(noStep.Hero, new Vec2(0, 0), new Vec2(20, 0)));
            var none = Build();
            Assert.False(none.Vertical.TerrainActive);
            Assert.Null(none.Vertical.TerrainBlockPoint(none.Hero, new Vec2(0, 0), new Vec2(20, 0)));
        }

        // ================================================================== 击飞叠加

        [Fact]
        public void LaunchStack_Add_SumsTheCurrentVerticalSpeedAndTheNewInitialSpeed()
        {
            const double g = 24;
            var fx = Build(o => o.Gravity = g);
            fx.Vertical.LaunchToApex(HeroId, 2.0);
            for (var i = 0; i < 10; i++) fx.Vertical.Advance(Dt);
            var speedBefore = fx.Vertical.GetVerticalSpeed(HeroId);
            var heightBefore = fx.Hero.HeightOffset;

            fx.Vertical.BeginLaunch(HeroId, 1.0, LaunchStackMode.Add, 0.0);
            var expected = speedBefore + Math.Sqrt(2 * g * 1.0);
            Assert.Equal(expected, fx.Vertical.GetVerticalSpeed(HeroId), 9);
            fx.Vertical.Advance(Dt);
            Assert.Equal(heightBefore + expected * Dt - 0.5 * g * Dt * Dt, fx.Hero.HeightOffset, 9);
        }

        [Fact]
        public void LaunchStack_Restart_DiscardsTheCurrentSpeed_LikeTheTwoArgumentOverload()
        {
            const double g = 24;
            var a = Build(o => o.Gravity = g);
            var b = Build(o => o.Gravity = g);
            foreach (var fx in new[] { a, b })
            {
                fx.Vertical.LaunchToApex(HeroId, 2.0);
                for (var i = 0; i < 10; i++) fx.Vertical.Advance(Dt);
            }

            a.Vertical.BeginLaunch(HeroId, 1.0);
            b.Vertical.BeginLaunch(HeroId, 1.0, LaunchStackMode.Restart, 5.0);
            Assert.Equal(Math.Sqrt(2 * g * 1.0), a.Vertical.GetVerticalSpeed(HeroId), 9);
            Assert.Equal(a.Vertical.GetVerticalSpeed(HeroId), b.Vertical.GetVerticalSpeed(HeroId));
        }

        [Fact]
        public void LaunchStack_AddWithCap_LimitsTheStackedSpeedToTheCapApex()
        {
            const double g = 24;
            var fx = Build(o => o.Gravity = g);
            fx.Vertical.LaunchToApex(HeroId, 2.0);
            fx.Vertical.BeginLaunch(HeroId, 2.0, LaunchStackMode.Add, 3.0); // 叠加后 ≈ 2·sqrt(2·g·2)，上限 sqrt(2·g·3)
            Assert.Equal(Math.Sqrt(2 * g * 3.0), fx.Vertical.GetVerticalSpeed(HeroId), 9);
        }

        [Fact]
        public void LaunchStack_AddOnAGroundedUnit_IsAnOrdinaryLaunch()
        {
            const double g = 24;
            var fx = Build(o => o.Gravity = g);
            fx.Vertical.BeginLaunch(HeroId, 1.5, LaunchStackMode.Add, 0.5);
            Assert.True(fx.Vertical.IsAirborne(HeroId));
            Assert.Equal(Math.Sqrt(2 * g * 1.5), fx.Vertical.GetVerticalSpeed(HeroId), 9); // 地面起跳不受叠加上限约束
        }

        [Fact]
        public void LaunchStack_AddWhileFalling_CancelsPartOfTheFallSpeed()
        {
            const double g = 24;
            var fx = Build(o => o.Gravity = g);
            fx.Vertical.LaunchToApex(HeroId, 2.0);
            // 走到下落段。
            while (fx.Vertical.GetVerticalSpeed(HeroId) > -5.0) fx.Vertical.Advance(Dt);
            var fall = fx.Vertical.GetVerticalSpeed(HeroId);
            Assert.True(fall < 0);
            fx.Vertical.BeginLaunch(HeroId, 0.5, LaunchStackMode.Add, 0.0);
            Assert.Equal(fall + Math.Sqrt(2 * g * 0.5), fx.Vertical.GetVerticalSpeed(HeroId), 9);
        }

        // ================================================================== 腾空查询 / 默认成员

        [Fact]
        public void AirborneUnits_ListsFlightsInIdOrder_AndAirborneQueryAgrees()
        {
            var fx = Build();
            Assert.Empty(fx.Vertical.AirborneUnits());
            fx.Vertical.Launch(OtherId, 5.0);
            fx.Vertical.Launch(HeroId, 5.0);
            var list = fx.Vertical.AirborneUnits();
            Assert.Equal(new[] { HeroId, OtherId }, list);
            IAirborneQuery query = fx.Vertical;
            Assert.True(query.IsAirborne(HeroId));
            fx.Vertical.Advance(10.0);
            Assert.Empty(fx.Vertical.AirborneUnits());
            Assert.False(query.IsAirborne(HeroId));
        }

        private sealed class MinimalVertical : IVerticalMotion
        {
            public bool IsAirborne(Id unitId) => false;
            public double GetVerticalSpeed(Id unitId) => 0.0;
            public bool Launch(Id unitId, double initialSpeed) => false;
            public bool LaunchToApex(Id unitId, double apexHeight) => false;
            public bool Jump(Id unitId) => false;
        }

        [Fact]
        public void DefaultInterfaceMembers_KeepExistingImplementationsCompiling()
        {
            IVerticalMotion motion = new MinimalVertical();
            Assert.Equal(0, motion.AirJumpsUsed(HeroId));
            Assert.Equal(1.0, motion.GetAirControl(HeroId));
            Assert.Empty(motion.AirborneUnits());
        }

        private sealed class OldLaunchSink : ILaunchSink
        {
            public readonly List<double> Apexes = new List<double>();
            public void BeginLaunch(Id unitId, double apexHeightWorld) => Apexes.Add(apexHeightWorld);
        }

        [Fact]
        public void LaunchSinkDefaultOverload_FallsBackToTheTwoArgumentLaunch()
        {
            var sink = new OldLaunchSink();
            ILaunchSink asInterface = sink;
            asInterface.BeginLaunch(HeroId, 1.25, LaunchStackMode.Add, 9.0);
            Assert.Equal(new[] { 1.25 }, sink.Apexes);
        }

        // ================================================================== 选项校验

        [Fact]
        public void Options_RejectNonsenseValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalMotionHost(Build().World, new VerticalAxisOptions { AirControl = -0.1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalMotionHost(Build().World, new VerticalAxisOptions { AirControl = double.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalMotionHost(Build().World, new VerticalAxisOptions { MaxAirJumps = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalMotionHost(Build().World, new VerticalAxisOptions { StepHeight = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalMotionHost(Build().World, new VerticalAxisOptions { StepSampleDistance = 0 }));
        }
    }
}
