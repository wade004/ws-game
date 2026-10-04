using System;
using Core.Carriers.Common;
using Core.Foundation.SceneRouter;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// 竖直轴的输入层补全（ADR-0143）：土狼时间的前提（走出平台边缘的自然下落可辨认，且按地面起跳处理、不占空中跳跃次数）与可变跳高（上升中截断速度）。
    /// 期望值由抛体公式（<c>v = sqrt(2 g H)</c>、<c>apex = h + v² / 2g</c>）在用例里算出。
    /// </summary>
    public class VerticalAxisLedgeJumpTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id HeroId = new Id("unit.ledge_hero");
        private const double Dt = 1.0 / 60.0;
        private const double Gravity = 30.0;
        private const double PlatformHeight = 3.0;

        private sealed class Fx
        {
            public WorldSim World = null!;
            public PlayerUnit Hero = null!;
            public VerticalMotionHost Vertical = null!;
        }

        private static Fx Build(Action<VerticalAxisOptions>? configure = null)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var hero = new PlayerUnit(HeroId, MapId, new Id("fac.ledge"), new Id("arch.class.sample")) { Position = Vec2.Zero };
            world.AddEntity(hero);

            var terrain = new MapTerrainHeights();
            terrain.SetRegions(MapId, new[] { new TerrainRegion(new Vec2(-100, -100), new Vec2(5, 100), ground: PlatformHeight) });

            var options = new VerticalAxisOptions { Gravity = Gravity, JumpHeight = 1.5, StepHeight = 0.5, Terrain = terrain };
            configure?.Invoke(options);
            return new Fx { World = world, Hero = hero, Vertical = new VerticalMotionHost(world, options, bus) };
        }

        /// <summary>站上平台再走出边缘：返回时单位处于零速自然下落。</summary>
        private static void WalkOffLedge(Fx fx)
        {
            fx.Hero.Position = new Vec2(2, 0);
            fx.Vertical.Advance(Dt);
            fx.Hero.Position = new Vec2(6, 0);
            fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.IsAirborne(HeroId));
        }

        // ---------------------------------------------------------------- 边缘下落与宽限起跳

        [Fact]
        public void LedgeFall_IsRecognised_AndJumpFromLedgeLaunchesAtFullJumpSpeedWithoutSpendingAnAirJump()
        {
            var fx = Build(); // AllowAirJump 缺省关：普通 Jump 在空中返回 false
            Assert.False(fx.Vertical.IsLedgeFall(HeroId));
            WalkOffLedge(fx);
            Assert.True(fx.Vertical.IsLedgeFall(HeroId));
            Assert.False(fx.Vertical.Jump(HeroId)); // 复现：不开空中跳跃时，边缘下落里按普通跳是起不来的

            fx.Vertical.Advance(Dt);
            var heightBefore = fx.Hero.HeightOffset;
            Assert.True(fx.Vertical.JumpFromLedge(HeroId));

            var expectedSpeed = Math.Sqrt(2.0 * Gravity * 1.5);
            Assert.Equal(expectedSpeed, fx.Vertical.GetVerticalSpeed(HeroId), 9);
            Assert.Equal(heightBefore, fx.Hero.HeightOffset, 12); // 从当前高度起跳
            Assert.Equal(0, fx.Vertical.AirJumpsUsed(HeroId)); // 按地面起跳，不占空中跳跃次数
            Assert.False(fx.Vertical.IsLedgeFall(HeroId)); // 起跳后不再是边缘下落：不能连续宽限起跳
            Assert.False(fx.Vertical.JumpFromLedge(HeroId));
        }

        [Fact]
        public void JumpFromLedge_Invariants_RefusedOnGroundInJumpFlightAndAfterALaunch_AndStateIsUntouched()
        {
            var fx = Build();
            Assert.False(fx.Vertical.JumpFromLedge(HeroId)); // 地面上
            Assert.False(fx.Vertical.IsAirborne(HeroId));

            // 地面起跳的飞行：不是边缘下落。
            fx.Hero.Position = new Vec2(2, 0);
            fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.Jump(HeroId));
            fx.Vertical.Advance(Dt);
            var speed = fx.Vertical.GetVerticalSpeed(HeroId);
            Assert.False(fx.Vertical.IsLedgeFall(HeroId));
            Assert.False(fx.Vertical.JumpFromLedge(HeroId));
            Assert.Equal(speed, fx.Vertical.GetVerticalSpeed(HeroId));

            // 边缘下落里被击飞：飞行被替换，不再是边缘下落。
            var other = Build();
            WalkOffLedge(other);
            Assert.True(other.Vertical.IsLedgeFall(HeroId));
            Assert.True(other.Vertical.LaunchToApex(HeroId, 1.0));
            Assert.False(other.Vertical.IsLedgeFall(HeroId));
            Assert.False(other.Vertical.JumpFromLedge(HeroId));
        }

        // ---------------------------------------------------------------- 可变跳高

        [Fact]
        public void CutAscent_ScalesTheCurrentSpeed_KeepsHeightAndAirJumpCount_AndTheNewApexFollowsTheProjectileFormula()
        {
            const double ratio = 0.4;
            var fx = Build(o => { o.AllowAirJump = true; o.MaxAirJumps = 1; });
            fx.Hero.Position = new Vec2(2, 0);
            fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.Jump(HeroId));
            for (var i = 0; i < 3; i++) fx.Vertical.Advance(Dt);

            var speedBefore = fx.Vertical.GetVerticalSpeed(HeroId);
            var heightBefore = fx.Hero.HeightOffset;
            Assert.True(speedBefore > 0);
            Assert.True(fx.Vertical.CutAscent(HeroId, ratio));

            Assert.Equal(speedBefore * ratio, fx.Vertical.GetVerticalSpeed(HeroId), 9);
            Assert.Equal(heightBefore, fx.Hero.HeightOffset, 12);
            Assert.Equal(0, fx.Vertical.AirJumpsUsed(HeroId));

            // 新的顶点高度 = 当前高度 + (v·ratio)² / 2g；离散积分下最高点不超过它。
            var apexExpected = heightBefore + Math.Pow(speedBefore * ratio, 2) / (2.0 * Gravity);
            var apex = heightBefore;
            for (var i = 0; i < 600 && fx.Vertical.IsAirborne(HeroId); i++)
            {
                fx.Vertical.Advance(Dt);
                apex = Math.Max(apex, fx.Hero.HeightOffset);
            }

            Assert.InRange(apex, apexExpected - 0.05, apexExpected + 1e-9);

            // 对照：不截断的同一跳，最高点高得多。
            var full = Build(o => { o.AllowAirJump = true; o.MaxAirJumps = 1; });
            full.Hero.Position = new Vec2(2, 0);
            full.Vertical.Advance(Dt);
            full.Vertical.Jump(HeroId);
            var fullApex = 0.0;
            for (var i = 0; i < 600 && full.Vertical.IsAirborne(HeroId); i++)
            {
                full.Vertical.Advance(Dt);
                fullApex = Math.Max(fullApex, full.Hero.HeightOffset);
            }

            Assert.True(fullApex > apex + 0.3);
        }

        [Fact]
        public void CutAscent_Invariants_RefusedOnGroundWhileDescendingAndForRatiosOutsideZeroToOne()
        {
            var fx = Build();
            Assert.False(fx.Vertical.CutAscent(HeroId, 0.5)); // 地面上

            fx.Hero.Position = new Vec2(2, 0);
            fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.Jump(HeroId));
            fx.Vertical.Advance(Dt);
            var speed = fx.Vertical.GetVerticalSpeed(HeroId);
            Assert.False(fx.Vertical.CutAscent(HeroId, 0.0));
            Assert.False(fx.Vertical.CutAscent(HeroId, 1.0));
            Assert.False(fx.Vertical.CutAscent(HeroId, 1.5));
            Assert.Equal(speed, fx.Vertical.GetVerticalSpeed(HeroId)); // 被拒绝的调用不改变状态

            // 过了顶点（下降中）：拒绝。
            for (var i = 0; i < 600 && fx.Vertical.GetVerticalSpeed(HeroId) > 0; i++) fx.Vertical.Advance(Dt);
            Assert.True(fx.Vertical.GetVerticalSpeed(HeroId) <= 0);
            Assert.False(fx.Vertical.CutAscent(HeroId, 0.5));
        }
    }
}
