using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;
using ProjectileEntity = Core.Carriers.Projectile.ProjectileEntity;
using ProjectileOptions = Core.Carriers.Projectile.ProjectileOptions;
using ProjectileTickHandler = Core.Carriers.Projectile.ProjectileTickHandler;

namespace Tests.Carriers.Projectile
{
    /// <summary>
    /// T-M19（测试覆盖剩余项 2026-10-01）：投射物飞行的几个边界——离散步的等效秒数换算、追踪目标失效后
    /// 沿最后方向直线飞行、<c>speed</c> 缺省取 <see cref="ProjectileOptions.DefaultSpeed"/>、<c>speed</c> 非正数的
    /// 生成拒绝（见 <c>core/carriers/projectile/README.md</c> 判断记录）。
    /// </summary>
    public class ProjectileFlightEdgeTests
    {
        private static readonly Id Source = new Id("unit.pfe_source");
        private static readonly Id Target = new Id("unit.pfe_target");
        private static readonly Id SkillId = new Id("skill.pfe_bolt");
        private static readonly Id School = new Id("school.physical");

        private static EffectContext MakeContext(Core.Foundation.Common.Json.JsonObject @params) =>
            new EffectContext(Source, Target, SkillId, EffectKind.Projectile, School, 0, 0, @params);

        private static ProjectileEntity OnlyProjectile(ProjectileWorld w) =>
            (ProjectileEntity)w.World.QueryEntities(new EntityFilter(kind: EntityKinds.Projectile)).Single();

        // -----------------------------------------------------------------
        // 离散步 / 连续步
        // -----------------------------------------------------------------

        [Fact]
        public void DiscreteStep_AdvancesByDiscreteTickEquivalentSeconds_NotByStepDt()
        {
            var options = new ProjectileOptions { DiscreteTickEquivalentSeconds = 0.25 };
            var w = ProjectileWorldBuilder.Build(options);
            w.World.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new ProjectileTickHandler(w.Host, options));
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(1000, 0)); // 远离射程，保证本用例只观察位移
            const double speed = 8.0;
            w.Host.Spawn(MakeContext(J.O(("speed", J.N(speed)), ("max_range", J.N(500)))), w.Sink);

            w.World.Tick(SimStep.Discrete(Source, StepPhase.Act));

            // 离散步 Dt 恒为 0，但仍按 DiscreteTickEquivalentSeconds 折算一次飞行。
            Assert.Equal(speed * options.DiscreteTickEquivalentSeconds, OnlyProjectile(w).Position.X, 6);
        }

        [Fact]
        public void ContinuousStep_UsesStepDt_IgnoringDiscreteEquivalent()
        {
            var options = new ProjectileOptions { DiscreteTickEquivalentSeconds = 100.0 };
            var w = ProjectileWorldBuilder.Build(options);
            w.World.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new ProjectileTickHandler(w.Host, options));
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(1000, 0));
            const double speed = 8.0;
            const double dt = 0.5;
            w.Host.Spawn(MakeContext(J.O(("speed", J.N(speed)), ("max_range", J.N(500)))), w.Sink);

            w.World.Tick(SimStep.Continuous(dt));

            Assert.Equal(speed * dt, OnlyProjectile(w).Position.X, 6);
        }

        [Fact]
        public void TickHandler_RejectsNullHost()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ProjectileTickHandler(null!));
        }

        // -----------------------------------------------------------------
        // 追踪目标失效
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(false)] // 目标死亡
        [InlineData(true)]  // 目标已不存在（Despawn）
        public void Homing_TargetGoneMidFlight_KeepsFlyingStraightAlongLastDirection(bool despawn)
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var targetId = w.AddUnit(Target.Value, new Vec2(10, 10));
            const double speed = 5.0;
            w.Host.Spawn(MakeContext(J.O(
                ("travel_mode", J.S("homing")), ("speed", J.N(speed)), ("max_range", J.N(1000)))), w.Sink);

            w.Host.Advance(0.1);
            var projectile = OnlyProjectile(w);
            var lastVelocity = projectile.Velocity;
            var lastFacing = projectile.Facing;
            Assert.True(lastVelocity.X > 0 && lastVelocity.Y > 0, "前置：已朝右上方目标瞄准");

            if (despawn)
            {
                w.World.MarkForDestruction(targetId);
                w.World.Tick(SimStep.Continuous(0.0));
            }
            else
            {
                w.Units.SetAlive(targetId, false);
            }

            var before = projectile.Position;
            w.Host.Advance(0.2);

            // 目标失效后不再重新瞄准：速度矢量与朝向保持，位置按原速度直线前进。
            Assert.Equal(lastVelocity, projectile.Velocity);
            Assert.Equal(lastFacing, projectile.Facing, 9);
            Assert.Equal(before.X + lastVelocity.X * 0.2, projectile.Position.X, 9);
            Assert.Equal(before.Y + lastVelocity.Y * 0.2, projectile.Position.Y, 9);
            Assert.Equal(1, w.Host.ActiveCount); // 没有凭空消失
        }

        [Fact]
        public void Homing_TargetGoneMidFlight_ThenReachesMaxRange_ExpiresWithoutEffect()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var targetId = w.AddUnit(Target.Value, new Vec2(100, 0));
            w.Host.Spawn(MakeContext(J.O(
                ("travel_mode", J.S("homing")), ("speed", J.N(10)), ("max_range", J.N(5)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(9))))))))), w.Sink);
            w.Units.SetAlive(targetId, false);

            w.Host.Advance(1.0);

            Assert.Equal(0, w.Host.ActiveCount);
            Assert.Empty(w.Sink.Applied);
        }

        // -----------------------------------------------------------------
        // speed 缺省 / 非正数
        // -----------------------------------------------------------------

        [Fact]
        public void SpeedOmitted_UsesOptionsDefaultSpeed()
        {
            var options = new ProjectileOptions { DefaultSpeed = 7.0 };
            var w = ProjectileWorldBuilder.Build(options);
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(50, 0));

            w.Host.Spawn(MakeContext(J.O(("max_range", J.N(100)))), w.Sink);

            Assert.Equal(options.DefaultSpeed, OnlyProjectile(w).Velocity.Length, 9);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-3.0)]
        public void NonPositiveSpeed_IsRejectedAtSpawn_WithWarning_NoZombieProjectile(double speed)
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(50, 0));

            w.Host.Spawn(MakeContext(J.O(("speed", J.N(speed)), ("max_range", J.N(100)))), w.Sink);

            Assert.Equal(0, w.Host.ActiveCount);
            Assert.True(w.Host.IsQuiescent);
            Assert.Empty(w.World.QueryEntities(new EntityFilter(kind: EntityKinds.Projectile)));
            Assert.Single(w.Diagnostics.Warnings);
        }

        [Fact]
        public void NonPositiveDefaultSpeed_WithSpeedOmitted_IsRejectedAtSpawn()
        {
            var options = new ProjectileOptions { DefaultSpeed = 0.0 };
            var w = ProjectileWorldBuilder.Build(options);
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(50, 0));

            w.Host.Spawn(MakeContext(J.O(("max_range", J.N(100)))), w.Sink);

            Assert.Equal(0, w.Host.ActiveCount);
            Assert.Single(w.Diagnostics.Warnings);
        }
    }
}
