using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Numbers.Faction;
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
        // RC-09（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-09）：
        // 整步命中判定必须先按剩余射程截断线段，再查单位/终点，不能等整个 tick 的位移都走完、
        // 命中判定用了越界的那一段之后才检查是否超过 max_range。
        // -----------------------------------------------------------------

        [Fact]
        public void LargeStep_TargetBeyondMaxRangeButWithinRawTickMovement_DoesNotHit()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            // 目标在 max_range(15) 之外，但在"整个 tick 未截断的原始位移"(speed 100 × dt 1.0 = 100)
            // 以内——修复前 TryResolveUnitHits 用的线段是 (0,0)→(100,0)，会在这条线段上查到并命中
            // 这个本不该够到的目标。
            var farTarget = w.AddUnit(Target.Value, new Vec2(50, 0));

            var context = MakeContext(Source, Target, J.O(
                ("speed", J.N(100)),
                ("max_range", J.N(15)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(99)))))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Empty(w.Sink.Applied); // 修复前会命中 farTarget 并回灌一次 school_damage。
            Assert.Equal(0, w.Host.ActiveCount);
        }

        [Fact]
        public void LargeSingleStep_AndManySmallSteps_ProduceTheSameOutcome()
        {
            // "大步与拆步一致"：同一发射程/速度/目标配置，一次 Advance(1.0) 与十次 Advance(0.1)
            // 应该得到相同结果——修复前小步推进天然每步都会先触达 max_range 再销毁（每步位移小，
            // 目标位置又在射程外，从不会被单独一小步的线段查到），只有大步一次推进整段位移时才
            // 会命中，这正是"大步与拆步不一致"的体现（见外部审计 RC-09）。
            var farTarget = new Vec2(50, 0);

            var wLarge = ProjectileWorldBuilder.Build();
            wLarge.AddUnit(Source.Value, new Vec2(0, 0));
            wLarge.AddUnit(Target.Value, farTarget);
            var largeContext = MakeContext(Source, Target, J.O(
                ("speed", J.N(100)),
                ("max_range", J.N(15)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(99)))))))));
            wLarge.Host.Spawn(largeContext, wLarge.Sink);
            wLarge.Host.Advance(1.0);

            var wSmall = ProjectileWorldBuilder.Build();
            wSmall.AddUnit(Source.Value, new Vec2(0, 0));
            wSmall.AddUnit(Target.Value, farTarget);
            var smallContext = MakeContext(Source, Target, J.O(
                ("speed", J.N(100)),
                ("max_range", J.N(15)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(99)))))))));
            wSmall.Host.Spawn(smallContext, wSmall.Sink);
            for (var i = 0; i < 10; i++)
            {
                wSmall.Host.Advance(0.1);
            }

            Assert.Empty(wLarge.Sink.Applied);
            Assert.Empty(wSmall.Sink.Applied);
            Assert.Equal(0, wLarge.Host.ActiveCount);
            Assert.Equal(0, wSmall.Host.ActiveCount);
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

        [Fact]
        public void ImpactOnExpiry_LargeStepOvershootingRawMovement_BlastCenterIsRangeClippedPoint()
        {
            // RC-09"过射程爆炸中心为射程终点"：speed(100)×dt(1.0) 的原始整步位移终点在 (100,0)，
            // 但 max_range 只有 20——爆炸圆心必须是射程终点 (20,0)，不能是未截断的整步终点
            // (100,0)。unit.p_aoe_at_range 恰好放在射程终点周围（impact_radius 内），若圆心用了
            // 错误的 (100,0)，两者相距 80，必然查不到，断言据此判定修复前后行为差异。
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(500, 0)); // 只用于瞄准方向（沿 +X），本身远在爆炸半径外。
            var atRangeEnd = w.AddUnit("unit.p_aoe_at_range", new Vec2(20.5, 0));

            var context = MakeContext(Source, Target, J.O(
                ("hit_behavior", J.S("impact_on_expiry")),
                ("speed", J.N(100)),
                ("max_range", J.N(20)),
                ("impact_radius", J.N(2.0)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(8)))))))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            var hitTargets = w.Sink.Applied.Select(c => c.TargetId).ToList();
            Assert.Contains(atRangeEnd, hitTargets); // 修复前：圆心在 (100,0)，本断言会失败。
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

        // -----------------------------------------------------------------
        // 9. ADR-0028：敌友关系策略（relation_policy/pierce_order）
        // -----------------------------------------------------------------
        //
        // 消费方反馈最小复现场景（M-C10 第 3 节）：施法者 (0,0)、锁定目标（敌方候选）(6,0)、
        // 友方候选 (2,0)（比目标更靠近施法者，impact_on_first 下会先经过它）、一堵不影响本场景的
        // 墙（放在射程之外，只用来覆盖"关系策略不绕过地形阻挡"这一点）。

        private static readonly Id CasterFaction = new Id("fac.p_caster");
        private static readonly Id HostileFaction = new Id("fac.p_hostile");
        private static readonly Id FriendlyFaction = new Id("fac.p_friendly");

        /// <summary>见类型顶部"9. ADR-0028"注释：搭好最小复现场景的三个单位 + 阵营矩阵桩，不生成
        /// 投射物（由各测试自行按需生成，取值不同）。</summary>
        private static (ProjectileWorld World, Id HostileId, Id FriendlyId, StubFactionMatrix Factions) SetupRelationScenario()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0), CasterFaction);
            var hostileId = w.AddUnit("unit.p_hostile", new Vec2(6, 0), HostileFaction);
            var friendlyId = w.AddUnit("unit.p_friendly", new Vec2(2, 0), FriendlyFaction);
            // 墙放在射程之外（本场景 max_range 均 ≤ 10），只覆盖"关系策略不绕过既有地形阻挡判定"
            // 这一点，不参与命中候选断言。
            w.Navigation.SetBlocking(ProjectileWorld.MapId, new[]
            {
                new Core.Foundation.Common.Rect(new Vec2(20, -1), new Vec2(21, 1)),
            });

            var factions = new StubFactionMatrix();
            factions.Set(CasterFaction, HostileFaction, Reaction.Hostile);
            factions.Set(CasterFaction, FriendlyFaction, Reaction.Friendly);
            w.Host.Factions = factions;

            return (w, hostileId, friendlyId, factions);
        }

        /// <summary>单条 <c>{kind: school_damage, params: {base_value}}</c> 效果条目——调用方自行
        /// 用 <see cref="J.A"/> 包成数组传给 <c>on_hit_effects</c>（同既有测试 helper 惯例 <see cref="J"/>，
        /// 不在本方法内预先包一层数组，避免出现 <c>[[{...}]]</c> 双重嵌套）。</summary>
        private static Core.Foundation.Common.Json.JsonValue OnHitEffectItem(double baseValue) =>
            J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(baseValue)))));

        [Fact]
        public void RelationPolicy_Default_HitsNearerFriendlyCandidate_CompatibleWithPreAdrBehavior()
        {
            // ADR-0028 之前的既有行为（消费方诊断复现的正是这一现象）：relation_policy 缺省
            // default 时不做任何阵营过滤，impact_on_first 只看"谁在路径上离起点最近"——友方候选
            // (2,0) 比敌方目标 (6,0) 更近，会被优先命中，即使瞄准的是敌方目标。
            var (w, hostileId, friendlyId, _) = SetupRelationScenario();

            var context = MakeContext(Source, hostileId, J.O(
                ("hit_behavior", J.S("impact_on_first")),
                ("speed", J.N(20)),
                ("max_range", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Single(w.Sink.Applied);
            Assert.Equal(friendlyId, w.Sink.Applied[0].TargetId); // 兼容既有行为：友方候选先被命中。
            Assert.DoesNotContain(w.Sink.Applied, c => c.TargetId.Equals(hostileId));
        }

        [Fact]
        public void RelationPolicy_HostileOnly_SkipsFriendlyCandidate_HitsHostileBehindIt()
        {
            var (w, hostileId, friendlyId, _) = SetupRelationScenario();

            var context = MakeContext(Source, hostileId, J.O(
                ("hit_behavior", J.S("impact_on_first")),
                ("relation_policy", J.S("hostile_only")),
                ("speed", J.N(20)),
                ("max_range", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Single(w.Sink.Applied);
            Assert.Equal(hostileId, w.Sink.Applied[0].TargetId);
            Assert.DoesNotContain(w.Sink.Applied, c => c.TargetId.Equals(friendlyId));
        }

        [Fact]
        public void RelationPolicy_FriendlyOnly_HitsOnlyFriendlyCandidate_SkipsHostile()
        {
            var (w, hostileId, friendlyId, _) = SetupRelationScenario();

            // pierce + 不限穿透次数：即使友方候选被跳过，敌方候选也不会被算进 Applied，穿透会一路
            // 飞到 max_range 才销毁（该场景内已无其它候选）。
            var context = MakeContext(Source, hostileId, J.O(
                ("hit_behavior", J.S("pierce")),
                ("relation_policy", J.S("friendly_only")),
                ("speed", J.N(20)),
                ("max_range", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Single(w.Sink.Applied);
            Assert.Equal(friendlyId, w.Sink.Applied[0].TargetId);
            Assert.DoesNotContain(w.Sink.Applied, c => c.TargetId.Equals(hostileId));
            Assert.Equal(0, w.Host.ActiveCount); // 飞到 max_range 后销毁（无更多候选）。
        }

        [Fact]
        public void RelationPolicy_LockedTargetOnly_Pierce_HitsOnlyLockedTarget_SkipsOthersInPath()
        {
            var (w, hostileId, friendlyId, _) = SetupRelationScenario();

            var context = MakeContext(Source, hostileId, J.O(
                ("hit_behavior", J.S("pierce")),
                ("relation_policy", J.S("locked_target_only")),
                ("speed", J.N(20)),
                ("max_range", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            // 只有锁定目标（hostileId）被命中一次；友方候选即使在路径上也被跳过。
            Assert.Single(w.Sink.Applied);
            Assert.Equal(hostileId, w.Sink.Applied[0].TargetId);
            Assert.DoesNotContain(w.Sink.Applied, c => c.TargetId.Equals(friendlyId));
        }

        [Fact]
        public void RelationPolicy_LockedTargetOnly_WithoutTarget_HitsNothing()
        {
            // 自由瞄准发射（Spawn 的 context.TargetId 等于 SourceId，Spawn 判为"无目标"）：
            // locked_target_only 语义上恒不命中任何候选。
            var (w, hostileId, friendlyId, _) = SetupRelationScenario();

            var context = MakeContext(Source, Source, J.O(
                ("hit_behavior", J.S("pierce")),
                ("relation_policy", J.S("locked_target_only")),
                ("speed", J.N(20)),
                ("max_range", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Empty(w.Sink.Applied);
        }

        [Fact]
        public void PierceOrder_HostileFirst_ProcessesFartherHostileBeforeNearerFriendly()
        {
            var (w, hostileId, friendlyId, _) = SetupRelationScenario();

            // relation_policy 保持 default（两者都是合法候选），pierce_order=hostile_first：
            // 敌方候选 (6,0) 虽然比友方候选 (2,0) 更远，仍应先被处理。
            var context = MakeContext(Source, hostileId, J.O(
                ("hit_behavior", J.S("pierce")),
                ("pierce_order", J.S("hostile_first")),
                ("speed", J.N(20)),
                ("max_range", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Equal(2, w.Sink.Applied.Count);
            Assert.Equal(hostileId, w.Sink.Applied[0].TargetId); // Hostile 优先，尽管距离更远。
            Assert.Equal(friendlyId, w.Sink.Applied[1].TargetId);
        }

        [Fact]
        public void RelationPolicy_HostileOnly_WithoutFactionsInjected_DegradesToDefault_AndWarns()
        {
            var (w, hostileId, friendlyId, _) = SetupRelationScenario();
            w.Host.Factions = null; // 显式不注入阵营矩阵（覆盖 SetupRelationScenario 的桩）。

            var context = MakeContext(Source, hostileId, J.O(
                ("hit_behavior", J.S("impact_on_first")),
                ("relation_policy", J.S("hostile_only")),
                ("speed", J.N(20)),
                ("max_range", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            // 退化为 default：友方候选（更近）仍被命中，不是"整条策略判不通过、什么都不命中"。
            Assert.Single(w.Sink.Applied);
            Assert.Equal(friendlyId, w.Sink.Applied[0].TargetId);
            Assert.Contains(w.Diagnostics.Warnings, msg => msg.Contains("relation_policy") && msg.Contains("退化"));
        }

        [Fact]
        public void PierceOrder_HostileFirst_WithoutFactionsInjected_DegradesToNearest_AndWarns()
        {
            var (w, hostileId, friendlyId, _) = SetupRelationScenario();
            w.Host.Factions = null;

            var context = MakeContext(Source, hostileId, J.O(
                ("hit_behavior", J.S("pierce")),
                ("pierce_order", J.S("hostile_first")),
                ("speed", J.N(20)),
                ("max_range", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            // 退化为 nearest：友方候选（更近，2,0）先于敌方候选（6,0）被处理。
            Assert.Equal(2, w.Sink.Applied.Count);
            Assert.Equal(friendlyId, w.Sink.Applied[0].TargetId);
            Assert.Equal(hostileId, w.Sink.Applied[1].TargetId);
            Assert.Contains(w.Diagnostics.Warnings, msg => msg.Contains("pierce_order") && msg.Contains("退化"));
        }

        [Fact]
        public void RelationPolicy_HostileOnly_StillBlockedByWall_DoesNotBypassTerrainBlocking()
        {
            // 关系策略只影响"候选筛选"，不影响既有的地形阻挡判定——墙紧贴在友方候选与敌方目标
            // 之间，hostile_only 跳过友方候选之后，飞行仍应在墙前停下，不产生任何命中后效果。
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0), CasterFaction);
            var hostileId = w.AddUnit("unit.p_hostile_wall", new Vec2(6, 0), HostileFaction);
            w.AddUnit("unit.p_friendly_wall", new Vec2(2, 0), FriendlyFaction);
            w.Navigation.SetBlocking(ProjectileWorld.MapId, new[]
            {
                new Core.Foundation.Common.Rect(new Vec2(3.5, -1), new Vec2(4.5, 1)),
            });

            var factions = new StubFactionMatrix();
            factions.Set(CasterFaction, HostileFaction, Reaction.Hostile);
            factions.Set(CasterFaction, FriendlyFaction, Reaction.Friendly);
            w.Host.Factions = factions;

            var context = MakeContext(Source, hostileId, J.O(
                ("hit_behavior", J.S("impact_on_first")),
                ("relation_policy", J.S("hostile_only")),
                ("speed", J.N(20)),
                ("max_range", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Empty(w.Sink.Applied); // 撞墙，敌方目标在墙后面，够不到。
            Assert.Equal(0, w.Host.ActiveCount);
        }

        [Fact]
        public void ExcludeCaster_ImpactOnExpiry_SourceNeverHit_EvenWithinImpactRadius()
        {
            // 施法者本人恰好落在到期爆炸半径内（射程很短、爆炸半径很大）：即使 relation_policy=
            // default（不做关系过滤），施法者排除仍是无条件规则，不应该把自己算作命中候选。
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var hostileId = w.AddUnit("unit.p_hostile_selfhit", new Vec2(1, 0));

            var context = MakeContext(Source, hostileId, J.O(
                ("hit_behavior", J.S("impact_on_expiry")),
                ("speed", J.N(1)),
                ("max_range", J.N(0.5)),
                ("impact_radius", J.N(10)),
                ("on_hit_effects", J.A(OnHitEffectItem(10)))));
            w.Host.Spawn(context, w.Sink);

            w.Host.Advance(1.0);

            Assert.Single(w.Sink.Applied);
            Assert.Equal(hostileId, w.Sink.Applied[0].TargetId);
            Assert.DoesNotContain(w.Sink.Applied, c => c.TargetId.Equals(Source));
        }

        // -----------------------------------------------------------------
        // 10. ADR-0028：ActiveCount/IsQuiescent/ClearAll 契约
        // -----------------------------------------------------------------

        private static ProjectileWorld SpawnOneActiveProjectile(out Id targetId)
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            targetId = w.AddUnit(Target.Value, new Vec2(100, 0)); // 远超射程，保证不会自己飞完命中/到期。

            var context = MakeContext(Source, Target, J.O(("speed", J.N(1)), ("max_range", J.N(1000))));
            w.Host.Spawn(context, w.Sink);
            Assert.Equal(1, w.Host.ActiveCount);
            return w;
        }

        [Fact]
        public void ClearAll_ImmediatelyZeroesActiveCount_NoAdvanceCall()
        {
            // "暂停"窗口：调用 ClearAll 之后完全不驱动 Advance，ActiveCount/IsQuiescent 应立即反映
            // "已清空"，不需要等待任何一次 tick。
            var w = SpawnOneActiveProjectile(out _);

            w.World.ClearAll();
            w.Host.ClearAll();

            Assert.Equal(0, w.Host.ActiveCount);
            Assert.True(w.Host.IsQuiescent);
            Assert.Empty(w.Sink.Applied);
        }

        [Fact]
        public void ClearAll_ThenAdvanceZeroDt_StaysZero_NoGhostDamage()
        {
            var w = SpawnOneActiveProjectile(out _);

            w.World.ClearAll();
            w.Host.ClearAll();
            w.Host.Advance(0.0); // 零 dt 窗口：Advance 本身对非正数 dt 是空操作，不应改变结果。

            Assert.Equal(0, w.Host.ActiveCount);
            Assert.True(w.Host.IsQuiescent);
            Assert.Empty(w.Sink.Applied);
        }

        [Fact]
        public void ClearAll_ThenAdvancePositiveDt_StaysZero_NoGhostDamage()
        {
            var w = SpawnOneActiveProjectile(out _);

            w.World.ClearAll();
            w.Host.ClearAll();
            w.Host.Advance(1.0); // 正 dt 窗口：清空之后的正常推进不应凭空冒出命中效果。

            Assert.Equal(0, w.Host.ActiveCount);
            Assert.True(w.Host.IsQuiescent);
            Assert.Empty(w.Sink.Applied);
        }

        [Fact]
        public void ClearAll_ThenSpawnNewProjectileInSameHost_WorksNormally_NoStaleStateInterference()
        {
            // "新世界"窗口：ClearAll 之后本类应可以正常继续工作（不是进入某种半损坏状态），新生成
            // 的投射物命中判定与此前完全独立。
            var w = SpawnOneActiveProjectile(out _);
            w.World.ClearAll();
            w.Host.ClearAll();
            Assert.Equal(0, w.Host.ActiveCount);

            w.AddUnit("unit.p_after_clear_source", new Vec2(0, 0));
            var freshTargetId = w.AddUnit("unit.p_after_clear_target", new Vec2(5, 0));
            var context = MakeContext(new Id("unit.p_after_clear_source"), new Id("unit.p_after_clear_target"), J.O(
                ("speed", J.N(20)),
                ("on_hit_effects", J.A(OnHitEffectItem(7)))));
            w.Host.Spawn(context, w.Sink);
            Assert.Equal(1, w.Host.ActiveCount);

            w.Host.Advance(1.0);

            Assert.Equal(0, w.Host.ActiveCount);
            Assert.True(w.Host.IsQuiescent);
            Assert.Single(w.Sink.Applied);
            Assert.Equal(freshTargetId, w.Sink.Applied[0].TargetId);
        }

        [Fact]
        public void WorldClearAll_WithoutHostClearAll_LeavesActiveCountStale_UntilNextPositiveDtAdvance_ThenSelfHealsWithoutGhostDamage()
        {
            // 契约回归：只调用 IWorldSim.ClearAll（不调用 ProjectileHost.ClearAll）时，ActiveCount
            // 按文档约定在下一次正 dt 的 Advance 之前保持"清空前"的数值（见 ProjectileHost.ClearAll
            // 判断记录、README"ActiveCount/IsQuiescent/ClearAll 契约"一节），但这段窗口期内不会产生
            // 幽灵伤害；下一次正 dt 推进后自愈归零。
            var w = SpawnOneActiveProjectile(out _);

            w.World.ClearAll(); // 不调用 w.Host.ClearAll()。

            Assert.Equal(1, w.Host.ActiveCount); // 契约明确的滞后窗口：尚未归零。
            Assert.False(w.Host.IsQuiescent);
            Assert.Empty(w.Sink.Applied); // 滞后窗口期内不产生幽灵伤害。

            w.Host.Advance(1.0); // 下一次正 dt：Advance 发现实体已从世界移除，防御性清理簿记。

            Assert.Equal(0, w.Host.ActiveCount); // 自愈归零。
            Assert.True(w.Host.IsQuiescent);
            Assert.Empty(w.Sink.Applied); // 全程未产生任何命中效果。
        }
    }
}
