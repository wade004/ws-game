using System;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// 竖直运动服务（体积空间 / 横版二维能力包，手感设计/06 第 1.2 节）：解析式抛体积分、落地、再抛起、二段跳开关、
    /// 销毁实体、非法参数。每条行为有复现（高度/落地步数从 X 变到 Y，期望值由 <c>h = h0 + v0·t − g·t²/2</c> 算出）与不变量
    /// （没被抛起的单位高度恒不动；离散步不推进）。
    /// </summary>
    public class VerticalMotionHostTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id Faction = new Id("fac.vmh");
        private static readonly Id Archetype = new Id("arch.class.sample");
        private static readonly Id HeroId = new Id("unit.vmh_hero");
        private static readonly Id OtherId = new Id("unit.vmh_other");
        private const double Dt = 1.0 / 60.0;

        private sealed class Fx
        {
            public WorldSim World = null!;
            public PlayerUnit Hero = null!;
            public PlayerUnit Other = null!;
            public VerticalMotionHost Host = null!;
            public VerticalAxisOptions Options = null!;
        }

        private static Fx Build(Action<VerticalAxisOptions>? configure = null)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var hero = new PlayerUnit(HeroId, MapId, Faction, Archetype);
            var other = new PlayerUnit(OtherId, MapId, Faction, Archetype);
            world.AddEntity(hero);
            world.AddEntity(other);
            var options = new VerticalAxisOptions();
            configure?.Invoke(options);
            return new Fx { World = world, Hero = hero, Other = other, Options = options, Host = new VerticalMotionHost(world, options) };
        }

        private static double Analytic(double v0, double g, double t, double h0 = 0.0) => h0 + v0 * t - 0.5 * g * t * t;

        // ---------- 复现：抛起 → 高度曲线 → 落地 ----------

        [Fact]
        public void Launch_HeightFollowsAnalyticParabola_AndLandsOnTheComputedStep()
        {
            var fx = Build(o => o.Gravity = 24);
            const double v0 = 7.0;
            Assert.True(fx.Host.Launch(HeroId, v0));
            Assert.True(fx.Host.IsAirborne(HeroId));

            // 落地步数 = 使 h ≤ 0 的最小 n：n·dt ≥ 2·v0/g。
            var expectedSteps = (int)Math.Ceiling(2.0 * v0 / fx.Options.Gravity / Dt - 1e-9);
            for (var n = 1; n <= expectedSteps; n++)
            {
                fx.Host.Advance(Dt);
                var expected = Math.Max(0.0, Analytic(v0, fx.Options.Gravity, n * Dt));
                Assert.Equal(expected, fx.Hero.HeightOffset, 9);
                Assert.Equal(n < expectedSteps, fx.Host.IsAirborne(HeroId));
            }

            Assert.Equal(0.0, fx.Hero.HeightOffset);
            Assert.False(fx.Host.IsAirborne(HeroId));
            Assert.Equal(0.0, fx.Host.GetVerticalSpeed(HeroId));
        }

        [Fact]
        public void LaunchToApex_ReachesTheRequestedApex_ForAnyGravity()
        {
            foreach (var g in new[] { 10.0, 30.0, 61.0 })
            {
                var fx = Build(o => o.Gravity = g);
                const double apex = 1.7;
                fx.Host.LaunchToApex(HeroId, apex);
                var max = 0.0;
                for (var i = 0; i < 400 && fx.Host.IsAirborne(HeroId); i++)
                {
                    fx.Host.Advance(Dt);
                    max = Math.Max(max, fx.Hero.HeightOffset);
                }

                // 顶点由初速 sqrt(2·g·H) 决定；离散采样至多亏欠半步（g·(dt/2)²/2）。
                Assert.InRange(max, apex - g * Dt * Dt / 8.0 - 1e-9, apex + 1e-9);
            }
        }

        [Fact]
        public void GetVerticalSpeed_FallsLinearlyWithGravity()
        {
            var fx = Build(o => o.Gravity = 20);
            fx.Host.Launch(HeroId, 6.0);
            for (var n = 1; n <= 5; n++)
            {
                fx.Host.Advance(Dt);
                Assert.Equal(6.0 - 20.0 * n * Dt, fx.Host.GetVerticalSpeed(HeroId), 9);
            }
        }

        // ---------- 跳跃 ----------

        [Fact]
        public void Jump_UsesJumpHeightAsApex_AndIsRefusedMidAirUnlessAirJumpAllowed()
        {
            var fx = Build(o => { o.JumpHeight = 2.0; o.Gravity = 30; });
            Assert.True(fx.Host.Jump(HeroId));
            fx.Host.Advance(Dt);
            Assert.False(fx.Host.Jump(HeroId));

            var air = Build(o => { o.JumpHeight = 2.0; o.AllowAirJump = true; });
            Assert.True(air.Host.Jump(HeroId));
            for (var i = 0; i < 10; i++) air.Host.Advance(Dt);
            var before = air.Hero.HeightOffset;
            Assert.True(air.Host.Jump(HeroId));
            // 二段跳从当前高度重新起算（起点高度 = 当前高度，初速由顶点高度重算）。
            air.Host.Advance(Dt);
            var v0 = Math.Sqrt(2.0 * air.Options.Gravity * air.Options.JumpHeight);
            Assert.Equal(Analytic(v0, air.Options.Gravity, Dt, before), air.Hero.HeightOffset, 9);
        }

        // ---------- 不变量 ----------

        [Fact]
        public void UnitsThatWereNeverLaunched_KeepTheirStaticHeight_AndDoNotDisturbOthers()
        {
            var fx = Build();
            fx.Other.HeightOffset = 2.5;
            fx.Host.LaunchToApex(HeroId, 1.0);
            for (var i = 0; i < 100; i++) fx.Host.Advance(Dt);
            Assert.Equal(2.5, fx.Other.HeightOffset);
            Assert.Equal(0.0, fx.Hero.HeightOffset);
            Assert.Equal(0, fx.Host.AirborneCount);
        }

        [Fact]
        public void DestroyedEntity_DropsItsFlightSilently()
        {
            var fx = Build();
            fx.Host.Launch(HeroId, 5.0);
            fx.Host.Advance(Dt);
            fx.World.RemoveEntityImmediately(HeroId);
            fx.Host.Advance(Dt);
            Assert.False(fx.Host.IsAirborne(HeroId));
            Assert.Equal(0, fx.Host.AirborneCount);
            Assert.False(fx.Host.Launch(HeroId, 5.0));
        }

        [Fact]
        public void TickHandler_AdvancesOnlyOnContinuousSteps()
        {
            var fx = Build();
            var handler = new VerticalMotionTickHandler(fx.Host);
            fx.Host.Launch(HeroId, 5.0);
            handler.Execute(SimStep.Discrete(HeroId, StepPhase.Act), fx.World);
            Assert.Equal(0.0, fx.Hero.HeightOffset);
            handler.Execute(SimStep.Continuous(Dt), fx.World);
            Assert.Equal(Analytic(5.0, fx.Options.Gravity, Dt), fx.Hero.HeightOffset, 9);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NaN)]
        public void NonPositiveOrNonFiniteLaunch_Throws(double value)
        {
            var fx = Build();
            Assert.Throws<ArgumentOutOfRangeException>(() => fx.Host.Launch(HeroId, value));
            Assert.Throws<ArgumentOutOfRangeException>(() => fx.Host.LaunchToApex(HeroId, value));
        }

        [Theory]
        [InlineData(0.0, 1.5)]
        [InlineData(-3.0, 1.5)]
        [InlineData(30.0, 0.0)]
        [InlineData(30.0, double.NaN)]
        public void InvalidOptions_AreRejectedAtConstruction(double gravity, double jumpHeight)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            Assert.ThrowsAny<Exception>(() => new VerticalMotionHost(world, new VerticalAxisOptions { Gravity = gravity, JumpHeight = jumpHeight }));
        }
    }
}
