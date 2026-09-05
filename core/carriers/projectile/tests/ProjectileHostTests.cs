using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;
using ProjectileEntity = Core.Carriers.Projectile.ProjectileEntity;
using ProjectileHost = Core.Carriers.Projectile.ProjectileHost;
using ProjectileOptions = Core.Carriers.Projectile.ProjectileOptions;
using ProjectileTickHandler = Core.Carriers.Projectile.ProjectileTickHandler;

namespace Tests.Carriers.Projectile
{
    /// <summary>
    /// <see cref="ProjectileHost"/> 端到端测试（05 第 1.4 节 <c>Projectile</c>、06 第 3.2 节
    /// <c>projectile</c> 效果原语、<c>core/carriers/projectile/README.md</c>）。
    /// </summary>
    public class ProjectileHostTests
    {
        private static readonly Id Source = new Id("unit.p_source");
        private static readonly Id Target = new Id("unit.p_target");
        private static readonly Id SkillId = new Id("skill.p_sample_bolt");
        private static readonly Id School = new Id("school.physical");

        private static EffectContext MakeContext(Id source, Id target, Core.Foundation.Common.Json.JsonObject @params) =>
            new EffectContext(source, target, SkillId, EffectKind.Projectile, School, 0, 0, @params);

        // -----------------------------------------------------------------
        // 1. 生成
        // -----------------------------------------------------------------

        [Fact]
        public void Spawn_CreatesProjectileEntity_WithExpectedFieldsAndVelocity()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(10, 0));

            var context = MakeContext(Source, Target, J.O(
                ("travel_mode", J.S("straight")),
                ("hit_behavior", J.S("impact_on_first")),
                ("speed", J.N(20))));

            w.Host.Spawn(context, w.Sink);

            Assert.Equal(1, w.Host.ActiveCount);
            var entity = w.World.QueryEntities(new EntityFilter(kind: EntityKinds.Projectile)).Single();
            var projectile = Assert.IsType<ProjectileEntity>(entity);

            Assert.Equal(EntityKinds.Projectile, projectile.Kind);
            Assert.Equal(Source, projectile.SourceUnitId);
            Assert.Equal(SkillId, projectile.SkillContextId);
            Assert.Equal("straight", projectile.TravelMode);
            Assert.Equal("impact_on_first", projectile.HitBehavior);
            Assert.Equal(new Vec2(0, 0), projectile.Position);
            Assert.Equal(20.0, projectile.Velocity.Length, 6);
            Assert.Equal(0.0, projectile.Velocity.Y, 6); // 正对 +X 方向的目标，速度矢量应沿 +X。
        }

        [Fact]
        public void Spawn_WithDisplayRef_SetsTemplateIdForDisplayResolution()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(10, 0));

            var context = MakeContext(Source, Target, J.O(("display_ref", J.S("projectile.sample_bolt"))));
            w.Host.Spawn(context, w.Sink);

            var projectile = (ProjectileEntity)w.World.QueryEntities(new EntityFilter(kind: EntityKinds.Projectile)).Single();
            Assert.Equal(new Id("projectile.sample_bolt"), projectile.TemplateId);
        }

        // -----------------------------------------------------------------
        // 2. 直线飞行命中 + 效果回灌
        // -----------------------------------------------------------------

        [Fact]
        public void StraightFlight_HitsTargetInPath_AppliesOnHitEffectAndDestroysProjectile()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var targetId = w.AddUnit(Target.Value, new Vec2(10, 0));

            var context = MakeContext(Source, Target, J.O(
                ("travel_mode", J.S("straight")),
                ("hit_behavior", J.S("impact_on_first")),
                ("speed", J.N(20)),
                ("max_range", J.N(30)),
                ("on_hit_effects", J.A(J.O(
                    ("kind", J.S("school_damage")),
                    ("params", J.O(("base_value", J.N(12)), ("coefficient", J.N(1.0)))))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0); // 速度 20 × 1s = 20，越过目标（距离 10），本 tick 内应命中。

            // ProjectileHost.Advance 只 MarkForDestruction（真正从 WorldSim 集合移除要等下一次
            // 完整 Tick 的生命周期清理阶段，见 IWorldSim.MarkForDestruction 注释），本测试直接调用
            // Advance（不经完整 world.Tick），因此用 ActiveCount（ProjectileHost 自己的簿记，
            // Destroy 时立即从内部字典移除）判断"投射物已被销毁"，而不是查 WorldSim 集合。
            Assert.Equal(0, w.Host.ActiveCount);

            Assert.Single(w.Sink.Applied);
            var applied = w.Sink.Applied[0];
            Assert.Equal(Source, applied.SourceId);
            Assert.Equal(targetId, applied.TargetId);
            Assert.Equal(SkillId, applied.SkillId);
            Assert.Equal(EffectKind.SchoolDamage, applied.Kind);
            Assert.Equal(12.0, applied.BaseValue, 6);
            Assert.Equal(1.0, applied.Coefficient, 6);
        }

        // -----------------------------------------------------------------
        // 3. 未命中超射程销毁
        // -----------------------------------------------------------------

        [Fact]
        public void NoTargetInPath_ExceedsMaxRange_DestroysWithoutAnyEffect()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(100, 0)); // 远超 max_range，飞行方向对但打不到。

            var context = MakeContext(Source, Target, J.O(
                ("speed", J.N(20)),
                ("max_range", J.N(15)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(99)))))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0); // 飞行 20，超过 max_range=15，未命中任何单位。

            Assert.Equal(0, w.Host.ActiveCount);
            Assert.Empty(w.Sink.Applied); // 未命中不回灌任何效果。
        }

        // -----------------------------------------------------------------
        // 4. 穿透
        // -----------------------------------------------------------------

        [Fact]
        public void Pierce_HitsMultipleUnitsInPath_AppliesEffectToEachAndKeepsFlying()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var t1 = w.AddUnit("unit.p_pierce_1", new Vec2(5, 0));
            var t2 = w.AddUnit("unit.p_pierce_2", new Vec2(10, 0));

            var context = MakeContext(Source, t1, J.O(
                ("travel_mode", J.S("straight")),
                ("hit_behavior", J.S("pierce")),
                ("speed", J.N(20)),
                ("max_range", J.N(30)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(5)))))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0); // 位移 20：依次经过 t1（5）与 t2（10），两者都应命中。

            Assert.Equal(2, w.Sink.Applied.Count);
            Assert.Equal(t1, w.Sink.Applied[0].TargetId);
            Assert.Equal(t2, w.Sink.Applied[1].TargetId);
            // 未超过 max_range=30（本 tick 只飞了 20），穿透命中不销毁，投射物应仍存活。
            Assert.Equal(1, w.Host.ActiveCount);
        }

        [Fact]
        public void Pierce_RespectsMaxPierceCount_DestroysAfterLimitReached()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var t1 = w.AddUnit("unit.p_pierce_cap_1", new Vec2(5, 0));
            var t2 = w.AddUnit("unit.p_pierce_cap_2", new Vec2(10, 0));

            var context = MakeContext(Source, t1, J.O(
                ("hit_behavior", J.S("pierce")),
                ("speed", J.N(20)),
                ("max_range", J.N(30)),
                ("max_pierce_count", J.N(1)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(5)))))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            // 只允许穿透命中 1 个（t1），命中后立即销毁，不会再命中 t2。
            Assert.Single(w.Sink.Applied);
            Assert.Equal(t1, w.Sink.Applied[0].TargetId);
            Assert.Equal(0, w.Host.ActiveCount);
        }

        // -----------------------------------------------------------------
        // 5. 范围（impact_on_expiry：到期按 impact_radius 做一次范围判定）
        // -----------------------------------------------------------------

        [Fact]
        public void ImpactOnExpiry_AtMaxRange_HitsAllUnitsWithinImpactRadius()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var near1 = w.AddUnit("unit.p_aoe_1", new Vec2(20.5, 0));
            var near2 = w.AddUnit("unit.p_aoe_2", new Vec2(19.5, 0.5));
            var farAway = w.AddUnit("unit.p_aoe_far", new Vec2(50, 0));

            var context = MakeContext(Source, near1, J.O(
                ("hit_behavior", J.S("impact_on_expiry")),
                ("speed", J.N(20)),
                ("max_range", J.N(20)),
                ("impact_radius", J.N(2.0)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(8)))))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0); // 飞行 20，恰好到期。

            var hitTargets = w.Sink.Applied.Select(c => c.TargetId).ToList();
            Assert.Contains(near1, hitTargets);
            Assert.Contains(near2, hitTargets);
            Assert.DoesNotContain(farAway, hitTargets);
            Assert.Equal(0, w.Host.ActiveCount);
        }

        [Fact]
        public void ImpactOnExpiry_NoUnitsInRadius_DestroysWithoutAnyEffect()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(500, 0)); // 远离落点。

            var context = MakeContext(Source, Target, J.O(
                ("hit_behavior", J.S("impact_on_expiry")),
                ("speed", J.N(20)),
                ("max_range", J.N(20)),
                ("impact_radius", J.N(1.0))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Empty(w.Sink.Applied);
            Assert.Equal(0, w.Host.ActiveCount);
        }

        // -----------------------------------------------------------------
        // 6. 效果回灌（字段正确性，含默认学派回退与多条效果）
        // -----------------------------------------------------------------

        [Fact]
        public void OnHitEffects_MultipleEntries_AllAppliedInOrder_WithCorrectFields()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var targetId = w.AddUnit(Target.Value, new Vec2(5, 0));

            var context = MakeContext(Source, Target, J.O(
                ("speed", J.N(20)),
                ("max_range", J.N(30)),
                ("on_hit_effects", J.A(
                    J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(10)), ("coefficient", J.N(0.5))))),
                    J.O(("kind", J.S("energize")), ("params", J.O(("power_type", J.S("arch.power.mana")), ("amount", J.N(5)))))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Equal(2, w.Sink.Applied.Count);
            Assert.Equal(EffectKind.SchoolDamage, w.Sink.Applied[0].Kind);
            Assert.Equal(10.0, w.Sink.Applied[0].BaseValue, 6);
            Assert.Equal(0.5, w.Sink.Applied[0].Coefficient, 6);
            Assert.Equal(School, w.Sink.Applied[0].School); // 未在 params 里覆盖 school，回退到 projectile 的 School（即技能 school）。
            Assert.Equal(EffectKind.Energize, w.Sink.Applied[1].Kind);
            Assert.Equal(targetId, w.Sink.Applied[1].TargetId);
        }

        [Fact]
        public void OnHitEffects_UnknownKind_WarnsAndSkips_DoesNotThrow()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(5, 0));

            var context = MakeContext(Source, Target, J.O(
                ("speed", J.N(20)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("not_a_real_effect_kind")))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Empty(w.Sink.Applied);
            Assert.NotEmpty(w.Diagnostics.Warnings);
        }

        // -----------------------------------------------------------------
        // 7. 事件流（entity.created / entity.destroyed，经完整 tick 驱动）
        // -----------------------------------------------------------------

        [Fact]
        public void FullTickPipeline_ViaProjectileTickHandler_FiresEntityCreatedAndDestroyed()
        {
            var w = ProjectileWorldBuilder.Build();
            w.World.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new ProjectileTickHandler(w.Host, w.Options));
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(10, 0));

            var context = MakeContext(Source, Target, J.O(("speed", J.N(20)), ("max_range", J.N(30))));
            w.Host.Spawn(context, w.Sink); // AddEntity 内部 Enqueue entity.created（尚未派发）。

            Assert.Empty(w.Events); // 生成时只 Enqueue，派发在下一次 DispatchPending/Tick。

            w.Tick(1.0); // 经 ProjectileTickHandler 推进并命中，随后 WorldSim 生命周期清理阶段真正移除。

            var created = w.Events.OfType<EntityCreatedEvent>().ToList();
            Assert.Contains(created, e => e.Kind == EntityKinds.Projectile);

            var destroyed = w.Events.OfType<EntityDestroyedEvent>().ToList();
            Assert.NotEmpty(destroyed);
        }

        // -----------------------------------------------------------------
        // 8. 存档不持久化投射物（05 第 1.4 节"进存档：否"）
        // -----------------------------------------------------------------

        [Fact]
        public void ProjectileHostAndEntity_DoNotImplementIPersistable()
        {
            Assert.False(typeof(Core.Foundation.SaveSystem.IPersistable).IsAssignableFrom(typeof(ProjectileHost)));
            Assert.False(typeof(Core.Foundation.SaveSystem.IPersistable).IsAssignableFrom(typeof(ProjectileEntity)));
        }

        // -----------------------------------------------------------------
        // 额外覆盖：追踪飞行方式、地形阻挡、命中判定排除自身
        // -----------------------------------------------------------------

        [Fact]
        public void Homing_RetargetsTowardCurrentTargetPosition_AcrossTicks()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var targetId = w.AddUnit(Target.Value, new Vec2(10, 0));

            var context = MakeContext(Source, Target, J.O(
                ("travel_mode", J.S("homing")),
                ("speed", J.N(5)),
                ("max_range", J.N(100))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(0.1); // 小步推进，避免一步就越过目标。

            // 目标突然上移：下一步应重新朝新位置调整方向（速度矢量的 Y 分量应变为正）。
            w.Units.SetPosition(targetId, new Vec2(10, 10));
            w.Host.Advance(0.1);

            var entity = (ProjectileEntity)w.World.QueryEntities(new EntityFilter(kind: EntityKinds.Projectile)).Single();
            Assert.True(entity.Velocity.Y > 0, "homing 投射物应在下一 tick 重新瞄准目标新位置");
        }

        [Fact]
        public void TerrainBlock_DestroysWithoutAnyEffect()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(10, 0));
            w.Navigation.SetBlocking(ProjectileWorld.MapId, new[]
            {
                new Core.Foundation.Common.Rect(new Vec2(4, -1), new Vec2(5, 1)),
            });

            var context = MakeContext(Source, Target, J.O(
                ("speed", J.N(20)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(99)))))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Empty(w.Sink.Applied); // 撞墙不产生命中后效果。
            Assert.Equal(0, w.Host.ActiveCount);
        }

        [Fact]
        public void HitCandidates_ExcludeSourceUnit_EvenWhenSourceSitsInPath()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var targetId = w.AddUnit(Target.Value, new Vec2(10, 0));

            var context = MakeContext(Source, Target, J.O(("speed", J.N(20)), ("max_range", J.N(30))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            // 命中候选应只有 target，不应该把发射者自己算作命中（发射者与投射物初始同一位置）。
            Assert.DoesNotContain(w.Sink.Applied, c => c.TargetId.Equals(Source));
        }
    }
}
