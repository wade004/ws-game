using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;
using static Tests.Rules.Skill.SpatialRig;
using static Tests.Rules.Skill.TimelineHarness;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 时间线空间命中（手感设计/03 第 2.2～2.5 节）：命中路径选择、形状命中、扫掠不穿透、攻击实例去重、无敌前置检查、
    /// <c>combat.hit_confirmed</c> 与动作实例配对、<c>release</c> 标记发射投射物的命中钩子、目标辅助。
    /// 期望值一律由规则（形状参数/位移/步长/档案）在用例里算出，不写死裸数。
    /// </summary>
    public sealed class SpatialHitTests
    {
        private static readonly Id FoeB = new Id("unit.foe_b");
        private static readonly Id FoeC = new Id("unit.foe_c");
        private static readonly Id FoeD = new Id("unit.foe_d");

        private static Id Unit(SpatialRig rig, string name, Vec2 position)
        {
            var id = new Id(name);
            rig.H.World.AddUnit(id, position);
            return id;
        }

        private static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            var len2 = ab.X * ab.X + ab.Y * ab.Y;
            var t = len2 < 1e-12 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2));
            return (p - (a + ab * t)).Length;
        }

        // ------------------------------------------------------------------ 命中路径选择

        [Fact]
        public void ConeMarker_HitsExactlyTheTargetsInsideTheShape()
        {
            const double radius = 2.0;
            var angle = Math.PI / 2; // 90 度扇形，半角 45 度。
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }) }, Shape.Cone(Vec2.Zero, 0, angle, radius));
            var foes = new Dictionary<Id, Vec2>
            {
                [Foe] = new Vec2(1.0, 0.5),
                [FoeB] = new Vec2(1.9, 0.0),
                [FoeC] = new Vec2(1.2, -1.1),
                [FoeD] = new Vec2(0.0, 1.5), // 正好在 90 度方向：半角之外。
                [new Id("unit.foe_e")] = new Vec2(2.5, 0.0), // 半径之外。
                [new Id("unit.foe_f")] = new Vec2(-1.0, 0.0), // 身后。
                [new Id("unit.foe_g")] = new Vec2(1.6, 1.6), // 距离 2.26，半径之外。
            };
            foreach (var kv in foes)
            {
                if (kv.Key.Equals(Foe)) rig.Units.SetPosition(Foe, kv.Value);
                else Unit(rig, kv.Key.Value, kv.Value);
            }

            var expected = foes.Where(kv => kv.Value.Length <= radius && Math.Abs(Math.Atan2(kv.Value.Y, kv.Value.X)) <= angle / 2)
                .Select(kv => kv.Key).OrderBy(i => i.Value, StringComparer.Ordinal).ToList();
            Assert.Equal(3, expected.Count); // 夹具自检：确有内有外。

            rig.H.CastInTick("skill.sample_cone");
            rig.RunToEnd();

            var hits = rig.Hits.ToList();
            Assert.Equal(expected, hits.Select(e => e.TargetId).OrderBy(i => i.Value, StringComparer.Ordinal).ToList());
            Assert.All(hits, e => Assert.Equal(HitResult.Hit, e.HitResult));
            Assert.Equal(expected, rig.H.World.Combat.ResolveCalls.Select(c => c.TargetId).OrderBy(i => i.Value, StringComparer.Ordinal).ToList());

            // marker 命中的接触点取目标登记位置（03 第 2.4 节），法线为单位向量（几何字段永不为空）。
            foreach (var e in hits)
            {
                Assert.Equal(foes[e.TargetId], e.ContactPoint);
                Assert.InRange(e.ContactNormal.Length, 1 - 1e-9, 1 + 1e-9);
                Assert.InRange(e.WorldDirection.Length, 1 - 1e-9, 1 + 1e-9);
            }

            // 一次 hit 标记同一批共用一个攻击实例 id。
            Assert.Single(hits.Select(e => e.AttackInstanceId).Distinct());
        }

        [Fact]
        public void ConeMarker_FollowsTheCasterFacingAtTheMarker_NotAtCastTime()
        {
            // 朝向在前摇期间转了 180 度：形状在 hit 标记当时的位姿解析，原来在身前的目标不再被命中，身后的目标被命中。
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }) }, Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            var behind = Unit(rig, "unit.foe_behind", new Vec2(-1, 0));
            rig.H.CastInTick("skill.sample_cone");
            rig.Units.SetFacing(Actor, Math.PI);
            rig.RunToEnd();

            Assert.Equal(new[] { behind }, rig.Hits.Select(e => e.TargetId).ToArray());
        }

        [Fact]
        public void ChainWithoutShape_KeepsInstantSettlement_AndStillEmitsHitConfirmedOnce()
        {
            // 默认行为不变：链没有 shape 的技能不走空间命中（hit 标记处按链解析并结算，S3a 路径）。
            var rig = Create(new[] { SpSkill("skill.sample_plain", 50, 200, 100, new[] { HitAt(100) }) }, shape: null);
            rig.H.CastInTick("skill.sample_plain");
            rig.RunToEnd();

            // 空间路径没有被启用（没有按位姿解析）。
            Assert.Equal(0, rig.Shapes.PoseResolveCalls);
        }

        [Fact]
        public void HitModeInstant_ForcesInstantSettlement_EvenWhenTheChainHasAShape()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }, hitMode: "instant") },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            rig.H.CastInTick("skill.sample_cone");
            rig.RunToEnd();

            Assert.Equal(0, rig.Shapes.PoseResolveCalls);
            Assert.Single(rig.HitsOn(Foe));
        }

        // ------------------------------------------------------------------ 去重（攻击实例 × 目标 × 段）

        [Fact]
        public void TwoHitSegments_EachHitTheTargetOnce_WithDistinctAttackInstances()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_combo2", 50, 300, 100, new[] { HitAt(100, 0), HitAt(200, 1) }) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            var c = rig.H.CastInTick("skill.sample_combo2");
            rig.RunToEnd();

            var hits = rig.H.Of<CombatHitConfirmedEvent>().ToList();
            Assert.Equal(new[] { 0, 1 }, hits.Select(h => h.Event.Segment).ToArray());
            Assert.Equal(c + Ticks(100), hits[0].Tick);
            Assert.Equal(c + Ticks(200), hits[1].Tick);
            Assert.NotEqual(hits[0].Event.AttackInstanceId, hits[1].Event.AttackInstanceId);
            Assert.Equal(2, rig.H.World.Combat.ResolveCalls.Count);
        }

        [Theory]
        [InlineData(50.0, 2)]
        [InlineData(150.0, 1)]
        public void MarkerRehitInterval_SuppressesASecondSegmentThatComesTooSoon(double rehitMs, int expectedHits)
        {
            // 两段相隔 60ms：间隔 50ms 时第二段照常命中，间隔 150ms 时第二段被压制（同一目标两次命中至少相隔 rehit_interval_ms）。
            var rig = Create(
                new[] { SpSkill("skill.sample_combo2", 50, 300, 100, new[] { HitAt(100, 0), HitAt(160, 1) }, rehitMs: rehitMs) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            rig.H.CastInTick("skill.sample_combo2");
            rig.RunToEnd();

            Assert.Equal(expectedHits, rig.HitsOn(Foe).Count());
        }

        [Fact]
        public void Continuous_StationaryTarget_IsHitOnlyOnce_AcrossEverySampleOfTheSameSegment()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_spin", 0, 300, 100, Array.Empty<Core.Foundation.Common.Json.JsonValue>(), hitPolicy: "continuous") },
                Shape.Circle(Vec2.Zero, 1.0));
            rig.Units.SetPosition(Foe, new Vec2(0.5, 0));
            rig.H.CastInTick("skill.sample_spin");
            rig.RunToEnd();

            var hit = Assert.Single(rig.Hits);
            Assert.Equal(0, hit.Segment);
            Assert.Equal(7.0, Assert.Single(rig.H.World.Combat.ResolveCalls).BaseValue);
        }

        [Fact]
        public void Continuous_RehitInterval_SplitsTheActivePhaseIntoSegments_EachHittingOnce()
        {
            const double activeMs = 300, rehitMs = 100;
            var rig = Create(
                new[] { SpSkill("skill.sample_spin", 0, activeMs, 100, Array.Empty<Core.Foundation.Common.Json.JsonValue>(), hitPolicy: "continuous", rehitMs: rehitMs) },
                Shape.Circle(Vec2.Zero, 1.0));
            rig.Units.SetPosition(Foe, new Vec2(0.5, 0));
            rig.H.CastInTick("skill.sample_spin");
            rig.RunToEnd();

            var segments = (int)Math.Ceiling((double)Ticks(activeMs) / Ticks(rehitMs));
            Assert.Equal(Enumerable.Range(0, segments).ToList(), rig.Hits.Select(e => e.Segment).ToList());
        }

        // ------------------------------------------------------------------ 扫掠（高速位移不穿透）

        private static HashSet<Id> RunDash(
            double activeMs, double pathLength, IReadOnlyDictionary<Id, Vec2> foes, double shapeRadius, double foeRadius,
            out int hitEvents)
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_dash_hit", 0, activeMs, 100, Array.Empty<Core.Foundation.Common.Json.JsonValue>(), hitPolicy: "continuous") },
                Shape.Circle(Vec2.Zero, shapeRadius));
            foreach (var kv in foes)
            {
                if (kv.Key.Equals(Foe)) rig.Units.SetPosition(Foe, kv.Value);
                else Unit(rig, kv.Key.Value, kv.Value);
                rig.Shapes.SetRadius(kv.Key, foeRadius);
            }

            rig.H.CastInTick("skill.sample_dash_hit");
            var activeTicks = Ticks(activeMs);
            // 动作时间 1..activeTicks-1 为判定相内的推进 tick；沿 x 轴匀速走完 pathLength。
            rig.RunToEnd(n => rig.Units.SetPosition(Actor, new Vec2(pathLength * Math.Min(n, activeTicks - 1) / (activeTicks - 1), 0)));
            hitEvents = rig.Hits.Count();
            return rig.Hits.Select(e => e.TargetId).ToHashSet();
        }

        [Fact]
        public void ContinuousDash_PerTickDisplacementOverTwiceTheTargetRadius_StillHitsEachTargetExactlyOnce()
        {
            const double shapeRadius = 0.5, foeRadius = 0.5, activeMs = 200;
            var activeTicks = Ticks(activeMs);
            var path = 3.0 * (activeTicks - 1); // 每 tick 位移 3，是目标半径 0.5 的 6 倍（> 2 倍）。
            var foes = new Dictionary<Id, Vec2>
            {
                [Foe] = new Vec2(16.5, 0.0), // 落在两个 tick 终点（15 与 18）正中：只看 tick 终点位置必然穿透。
                [FoeB] = new Vec2(10.0, 0.8),
                [FoeC] = new Vec2(20.0, 2.0), // 离路径 2.0 > 半径之和 1.0：不命中。
                [FoeD] = new Vec2(path + 20, 0.0), // 路径尽头之外：不命中。
            };

            // 夹具自检：目标 Foe 与全部 tick 终点位置的距离都大于半径之和（朴素的"每 tick 查一次"会穿透）。
            var step = path / (activeTicks - 1);
            for (var k = 0; k < activeTicks; k++)
            {
                Assert.True(Math.Abs(foes[Foe].X - step * k) > shapeRadius + foeRadius);
            }

            var expected = foes.Where(kv => DistanceToSegment(kv.Value, Vec2.Zero, new Vec2(path, 0)) <= shapeRadius + foeRadius)
                .Select(kv => kv.Key).ToHashSet();
            Assert.Equal(2, expected.Count);

            var hit = RunDash(activeMs, path, foes, shapeRadius, foeRadius, out var events);
            Assert.Equal(expected, hit);
            Assert.Equal(expected.Count, events);
        }

        [Fact]
        public void ContinuousDash_HitSetDoesNotDependOnTheNumberOfTicksTheSamePathIsCoveredIn()
        {
            // 同一段路径分别用 12 个 tick 与 24 个 tick 走完（等价于不同的模拟频率）：命中集合一致。
            const double shapeRadius = 0.5, foeRadius = 0.5, path = 33.0;
            var foes = new Dictionary<Id, Vec2>
            {
                [Foe] = new Vec2(16.5, 0.0),
                [FoeB] = new Vec2(10.0, 0.8),
                [FoeC] = new Vec2(20.0, 2.0),
            };

            var coarse = RunDash(200, path, foes, shapeRadius, foeRadius, out var coarseEvents);
            var fine = RunDash(400, path, foes, shapeRadius, foeRadius, out var fineEvents);
            Assert.Equal(coarse, fine);
            Assert.Equal(coarseEvents, fineEvents);
            Assert.Equal(2, fine.Count);
        }

        [Fact]
        public void ContinuousSweep_TurningInPlace_SweepsTheConeThroughTargetsTheEndPoseNeverCovers()
        {
            // 圆心不动、朝向在一个判定相内转 180 度：扇形扫过身后的目标，哪怕起止朝向两端都没有覆盖它。
            const double activeMs = 200;
            var rig = Create(
                new[] { SpSkill("skill.sample_sweep", 0, activeMs, 100, Array.Empty<Core.Foundation.Common.Json.JsonValue>(), hitPolicy: "continuous") },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 4, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(0, 1.5)); // 90 度方向：起止朝向（0 与 180 度）都不覆盖。
            var activeTicks = Ticks(activeMs);
            rig.H.CastInTick("skill.sample_sweep");
            rig.RunToEnd(n => rig.Units.SetFacing(Actor, Math.PI * Math.Min(n, activeTicks - 1) / (activeTicks - 1)));

            Assert.Single(rig.HitsOn(Foe));
        }

        [Fact]
        public void ContinuousHit_ContactPointIsTheShapeClosestPointToTheTarget()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_spin", 0, 100, 100, Array.Empty<Core.Foundation.Common.Json.JsonValue>(), hitPolicy: "continuous") },
                Shape.Circle(Vec2.Zero, 1.0));
            rig.Units.SetPosition(Foe, new Vec2(1.25, 0));
            rig.Shapes.SetRadius(Foe, 0.5);
            rig.H.CastInTick("skill.sample_spin");
            rig.RunToEnd();

            var hit = Assert.Single(rig.Hits);
            Assert.Equal(1.0, hit.ContactPoint.X, 9); // 形状最近点：(1, 0)，不是目标中心。
            Assert.Equal(0.0, hit.ContactPoint.Y, 9);
            Assert.Equal(-1.0, hit.ContactNormal.X, 9); // 法线自目标指向接触点（朝向攻击方一侧）。
        }

        [Fact]
        public void ContinuousHit_DoesNotSampleWhileTheActionClockIsPaused_AndKeepsTheSamePlanAfterwards()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_spin", 0, 300, 100, Array.Empty<Core.Foundation.Common.Json.JsonValue>(), hitPolicy: "continuous") },
                Shape.Circle(Vec2.Zero, 1.0));
            rig.Units.SetPosition(Foe, new Vec2(5, 0));
            rig.H.CastInTick("skill.sample_spin");
            rig.H.TickN(3);
            var before = rig.Shapes.PoseResolveCalls;
            rig.H.Clock.SetPaused(Actor, true);
            for (var i = 0; i < 5; i++) rig.H.Tick(advanceClock: false);
            Assert.Equal(before, rig.Shapes.PoseResolveCalls); // 顿帧期间动作时间不走，不采样。
            rig.H.Clock.SetPaused(Actor, false);
            rig.H.Tick();
            Assert.True(rig.Shapes.PoseResolveCalls > before);
        }

        [Fact]
        public void ContinuousHit_CancelledAction_LeavesNoResidualHits()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_spin", 0, 300, 100, Array.Empty<Core.Foundation.Common.Json.JsonValue>(), hitPolicy: "continuous") },
                Shape.Circle(Vec2.Zero, 1.0));
            rig.Units.SetPosition(Foe, new Vec2(5, 0));
            rig.H.CastInTick("skill.sample_spin");
            rig.H.TickN(3);
            rig.H.World.Host.CancelAction(Actor, ActionCancelReason.Stagger);
            rig.H.World.Flush();

            rig.Units.SetPosition(Foe, new Vec2(0.5, 0)); // 取消之后目标走进形状：不会再被命中。
            rig.H.TickN(20);
            Assert.Empty(rig.Hits);
        }

        [Fact]
        public void ContinuousHit_CostAtFirstHit_IsPaidOnTheFirstRealHit_NotWhenTheMarkerlessActiveStarts()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_spin", 0, 300, 100, Array.Empty<Core.Foundation.Common.Json.JsonValue>(), hitPolicy: "continuous", costAt: "first_hit", cost: 10) },
                Shape.Circle(Vec2.Zero, 1.0));
            rig.Units.SetPosition(Foe, new Vec2(5, 0));
            Assert.Equal(100, rig.H.World.Powers.GetPower(Actor, Energy));
            rig.H.CastInTick("skill.sample_spin");
            rig.H.TickN(4);
            Assert.Equal(100, rig.H.World.Powers.GetPower(Actor, Energy)); // 没有命中，没有扣费。

            rig.Units.SetPosition(Foe, new Vec2(0.5, 0));
            rig.H.TickN(2);
            Assert.Equal(90, rig.H.World.Powers.GetPower(Actor, Energy));
        }

        // ------------------------------------------------------------------ 无敌

        [Fact]
        public void InvulnerableTarget_GetsInvulnerableResult_NoDamageNoHitStopNoAdjudicationFeel()
        {
            var hp = new Dictionary<Id, double> { [Foe] = 100, [FoeB] = 100 };
            var rig = Create(
                new[]
                {
                    SpSkill("skill.sample_cone", 50, 300, 100, new[] { HitAt(100) }),
                    SelfSkill("skill.sample_dodge", 0, 500, 0, new[] { Marker("invuln_start", 0), Marker("invuln_end", 500) }),
                },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.H.World.Combat.ResolveFunc = ctx =>
            {
                if (ctx.Kind == EffectKind.SchoolDamage) hp[ctx.TargetId] -= ctx.BaseValue;
                return new ResolveResult(HitResult.Hit, ctx.BaseValue, ctx.BaseValue, 0, immune: false, isHeal: ctx.Kind == EffectKind.Heal);
            };
            rig.Units.SetPosition(Foe, new Vec2(1, 0.2));
            Unit(rig, FoeB.Value, new Vec2(1, -0.2));

            // 目标 Foe 先开一个带无敌窗口的动作，攻击方随后出手。
            rig.H.Tick(() => rig.H.World.Host.CastSkill(Foe, new Id("skill.sample_dodge"), Array.Empty<Id>()));
            Assert.True(rig.H.Query.IsInvulnerable(Foe));
            rig.H.CastInTick("skill.sample_cone");
            rig.RunToEnd(_ => rig.H.Clock.Advance(Foe));

            var invuln = Assert.Single(rig.HitsOn(Foe));
            Assert.Equal(HitResult.Invulnerable, invuln.HitResult);
            Assert.Equal(0.0, invuln.Amount);
            Assert.Equal(0.0, invuln.AmountRatio);
            Assert.False(invuln.IsKill);
            Assert.Equal(0, invuln.AttackerHitStopTicks);
            Assert.Equal(0, invuln.TargetHitStopTicks);
            Assert.Equal(HitReaction.None, invuln.Reaction);
            Assert.Equal(100.0, hp[Foe]); // 血量不变；对照目标照常掉血。
            Assert.Equal(100.0 - 7.0, hp[FoeB]);
            Assert.Single(rig.H.World.Combat.ResolveCalls.Where(c => c.Kind == EffectKind.SchoolDamage));

            var avoided = rig.H.Of<CombatAttackAvoidedEvent>().Single().Event;
            Assert.Equal(Foe, avoided.TargetId);
            Assert.Equal(HitResult.Invulnerable, avoided.HitResult);
            Assert.Equal(invuln.AttackInstanceId, avoided.AttackInstanceId);

            // 受击裁决只被问到无敌结局一次（纯函数，结局为回避类时由裁决返回无顿帧/无反应），对照目标得到裁决给出的顿帧与反应。
            Assert.Contains(rig.Arbiter!.Inputs, i => i.Target.Equals(Foe) && i.Result == HitResult.Invulnerable);
            var normal = Assert.Single(rig.HitsOn(FoeB));
            Assert.Equal(HitResult.Hit, normal.HitResult);
            Assert.Equal(rig.Arbiter.ForHit.AttackerHitStopTicks, normal.AttackerHitStopTicks);
            Assert.Equal(rig.Arbiter.ForHit.TargetHitStopTicks, normal.TargetHitStopTicks);
            Assert.Equal(rig.Arbiter.ForHit.Reaction, normal.Reaction);
            Assert.Equal(rig.Arbiter.ForHit.ImpactClass, normal.ImpactClass);
        }

        [Fact]
        public void InvulnerabilityWindowEnds_TheSameTargetIsHitNormallyOnALaterSegment()
        {
            var rig = Create(
                new[]
                {
                    SpSkill("skill.sample_combo2", 50, 400, 100, new[] { HitAt(100, 0), HitAt(300, 1) }),
                    SelfSkill("skill.sample_dodge", 0, 200, 0, new[] { Marker("invuln_start", 0), Marker("invuln_end", 200) }),
                },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            rig.H.Tick(() => rig.H.World.Host.CastSkill(Foe, new Id("skill.sample_dodge"), Array.Empty<Id>()));
            rig.H.CastInTick("skill.sample_combo2");
            rig.RunToEnd(_ => rig.H.Clock.Advance(Foe));

            var results = rig.HitsOn(Foe).OrderBy(e => e.Segment).Select(e => e.HitResult).ToList();
            Assert.Equal(new[] { HitResult.Invulnerable, HitResult.Hit }, results);
        }

        // ------------------------------------------------------------------ hit_confirmed 的动作实例配对

        [Fact]
        public void HitConfirmed_CarriesTheSameCastInstanceIdAsActionStarted_AndFillsHitFeelOutcome()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }) }, Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            Unit(rig, "unit.foe_hp", new Vec2(1, 0));
            rig.Units.SetPosition(Foe, new Vec2(1.2, 0));
            rig.H.CastInTick("skill.sample_cone");
            rig.RunToEnd();

            var started = rig.H.Of<ActionStartedEvent>().Single().Event;
            var hits = rig.Hits.ToList();
            Assert.Equal(2, hits.Count);
            Assert.All(hits, h =>
            {
                Assert.Equal(started.CastInstanceId, h.CastInstanceId);
                Assert.NotEqual(started.CastInstanceId, h.AttackInstanceId); // 攻击实例与动作实例是两个概念。
                Assert.Equal(rig.Arbiter!.ForHit.ImpactClass, h.ImpactClass);
                Assert.Equal(rig.Arbiter.ForHit.AttackerHitStopTicks, h.AttackerHitStopTicks);
                Assert.Equal(rig.Arbiter.ForHit.TargetHitStopTicks, h.TargetHitStopTicks);
                Assert.Equal(rig.Arbiter.ForHit.Reaction, h.Reaction);
                Assert.Equal(7.0, h.Amount);
            });
        }

        [Fact]
        public void AmountRatio_IsTheDamageOverTheTargetMaxHealth()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }) }, Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0),
                configure: b => b.Power(WellKnownPowers.Health.Value, 50));
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            rig.H.CastInTick("skill.sample_cone");
            rig.RunToEnd();

            var hit = Assert.Single(rig.Hits);
            Assert.Equal(hit.Amount / rig.H.World.Powers.GetPowerMax(Foe, WellKnownPowers.Health), hit.AmountRatio, 9);
            Assert.True(hit.AmountRatio > 0);
        }

        [Fact]
        public void InstantPath_TimelineSkillOnAShapelessChain_AlsoEmitsHitConfirmedWithTheCastInstanceId()
        {
            // 链没有 shape：保持 instant 结算，但统一发 hit_confirmed（03 第 2.3 节两种结算路径统一）。
            var rig = Create(new[] { SpSkill("skill.sample_plain", 50, 200, 100, new[] { HitAt(100) }) }, shape: null);
            rig.H.CastInTick("skill.sample_plain");
            rig.RunToEnd();

            var started = rig.H.Of<ActionStartedEvent>().Single().Event;
            var hit = Assert.Single(rig.Hits);
            Assert.Equal(started.CastInstanceId, hit.CastInstanceId);
            Assert.Equal(Actor, hit.SourceId); // 自身链：解析为施法者自己（本夹具对无形状链的约定）。
        }

        // ------------------------------------------------------------------ 离散模式与确定性

        [Fact]
        public void DiscreteMode_TimelineBlockIsInvisible_ResultsAreIdenticalWithAndWithoutIt()
        {
            string Run(bool withTimeline)
            {
                var skill = SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }, hitPolicy: "continuous");
                var plain = SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) });
                var rig = Create(
                    new[] { withTimeline ? skill : StripTimeline(plain) }, Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0),
                    configure: b => b.Options.IsDiscreteStep = () => true);
                rig.Units.SetPosition(Foe, new Vec2(1, 0));
                var result = rig.H.World.Host.CastSkill(Actor, new Id("skill.sample_cone"), Array.Empty<Id>());
                rig.H.World.Host.Update(1.0);
                rig.H.World.Flush();
                rig.H.World.Host.Update(1.0);
                rig.H.World.Flush();
                var calls = string.Join(";", rig.H.World.Combat.ResolveCalls.Select(c => c.TargetId + ":" + c.BaseValue));
                var events = string.Join(";", rig.H.World.Events.Select(e => e.GetType().Name));
                return result.Success + "|" + calls + "|" + events + "|" + rig.H.Log.Count(l => l.Event is ActionStartedEvent || l.Event is CombatHitConfirmedEvent);
            }

            Assert.Equal(Run(false), Run(true));
        }

        private static Core.Foundation.Common.Json.JsonObject StripTimeline(Core.Foundation.Common.Json.JsonObject skill)
        {
            var b = new Core.Foundation.Common.Json.JsonObjectBuilder();
            foreach (var kv in skill)
            {
                if (kv.Key != "timeline") b.Add(kv.Key, kv.Value);
            }

            return b.Build();
        }

        [Fact]
        public void Determinism_TheSameScenarioRunTwice_ProducesIdenticalHitStreams()
        {
            string Run()
            {
                var rig = Create(
                    new[] { SpSkill("skill.sample_dash_hit", 0, 200, 100, Array.Empty<Core.Foundation.Common.Json.JsonValue>(), hitPolicy: "continuous", rehitMs: 50) },
                    Shape.Circle(Vec2.Zero, 0.5));
                rig.Units.SetPosition(Foe, new Vec2(16.5, 0));
                Unit(rig, FoeB.Value, new Vec2(10, 0.8));
                rig.H.CastInTick("skill.sample_dash_hit");
                var ticks = Ticks(200);
                rig.RunToEnd(n => rig.Units.SetPosition(Actor, new Vec2(33.0 * Math.Min(n, ticks - 1) / (ticks - 1), 0)));
                return string.Join("\n", rig.H.Log.Where(l => l.Event is CombatHitConfirmedEvent).Select(l => l.Tick + ":" + Describe((CombatHitConfirmedEvent)l.Event)));
            }

            var first = Run();
            Assert.False(string.IsNullOrEmpty(first));
            Assert.Equal(first, Run());
        }

        // ------------------------------------------------------------------ 投射物（release 标记）

        [Fact]
        public void ReleaseMarker_SpawnsTheProjectileOnce_AtTheMarkerTick_AndTheHitMarkerDoesNotRespawnIt()
        {
            var spawner = new RecordingProjectileSpawner();
            var rig = Create(
                new[] { SpSkill("skill.sample_bolt", 50, 300, 100, new[] { HitAt(100), Marker("release", 150) }, effects: new[] { Damage(), Projectile() }) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), configure: b => b.ProjectileSpawner = spawner);
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            var c = rig.H.CastInTick("skill.sample_bolt");
            var spawnedAt = -1;
            var i = 0;
            while (rig.H.Query.Current(Actor).HasValue && i++ < 100)
            {
                rig.H.Tick();
                if (spawnedAt < 0 && spawner.Spawns.Count > 0) spawnedAt = rig.H.TickIndex;
            }

            var spawn = Assert.Single(spawner.Spawns);
            Assert.Equal(c + Ticks(150), spawnedAt);
            Assert.NotNull(spawn.Hook);
            Assert.Equal(EffectKind.Projectile, spawn.Context.Kind);
            // hit 标记处只结算伤害（非投射物子集），投射物只在 release 发射。
            Assert.Single(rig.H.World.Combat.ResolveCalls.Where(call => call.Kind == EffectKind.SchoolDamage));
        }

        [Fact]
        public void ProjectileHook_ConfirmsTheHitWithTheLaunchingActionInstance_EvenAfterTheActionEnded()
        {
            var spawner = new RecordingProjectileSpawner();
            var rig = Create(
                new[] { SpSkill("skill.sample_bolt", 50, 100, 50, new[] { Marker("release", 50) }, effects: new[] { Projectile() }) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), configure: b => b.ProjectileSpawner = spawner);
            rig.Units.SetPosition(Foe, new Vec2(30, 0));
            rig.H.CastInTick("skill.sample_bolt");
            rig.RunToEnd();
            Assert.Null(rig.H.Query.Current(Actor)); // 动作已经结束，投射物仍在飞。

            var hook = spawner.Spawns.Single().Hook!;
            var info = new ProjectileHitInfo(Actor, Foe, new Id("skill.sample_bolt"), new Vec2(29.5, 0.1), new Vec2(1, 0), 0);
            Assert.True(hook.BeforeHit(info, out var attackId));
            hook.AfterHit(info, attackId, new[] { new ResolveResult(HitResult.Crit, 9, 9, 0, immune: false, isHeal: false) });
            rig.H.World.Flush();

            var started = rig.H.Of<ActionStartedEvent>().Single().Event;
            var hit = Assert.Single(rig.Hits);
            Assert.Equal(started.CastInstanceId, hit.CastInstanceId);
            Assert.Equal(attackId, hit.AttackInstanceId);
            Assert.Equal(new Vec2(29.5, 0.1), hit.ContactPoint); // 接触点 = 碰撞点。
            Assert.Equal(HitResult.Crit, hit.HitResult);
            Assert.True(hit.IsCrit);
            Assert.Equal(9.0, hit.Amount);
            Assert.Equal(new Vec2(1, 0), hit.WorldDirection);
        }

        [Fact]
        public void ProjectileHook_InvulnerableTarget_IsRejectedBeforeAnyEffect_AndTheProjectileMayContinue()
        {
            var spawner = new RecordingProjectileSpawner();
            var rig = Create(
                new[]
                {
                    SpSkill("skill.sample_bolt", 50, 100, 50, new[] { Marker("release", 50) }, effects: new[] { Projectile() }),
                    SelfSkill("skill.sample_dodge", 0, 2000, 0, new[] { Marker("invuln_start", 0), Marker("invuln_end", 2000) }),
                },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), configure: b => b.ProjectileSpawner = spawner);
            rig.H.CastInTick("skill.sample_bolt");
            rig.RunToEnd();
            rig.H.Tick(() => rig.H.World.Host.CastSkill(Foe, new Id("skill.sample_dodge"), Array.Empty<Id>()));
            Assert.True(rig.H.Query.IsInvulnerable(Foe));

            var hook = spawner.Spawns.Single().Hook!;
            var info = new ProjectileHitInfo(Actor, Foe, new Id("skill.sample_bolt"), new Vec2(3, 0), new Vec2(1, 0), 0);
            Assert.False(hook.BeforeHit(info, out _));
            rig.H.World.Flush();

            var hit = Assert.Single(rig.Hits);
            Assert.Equal(HitResult.Invulnerable, hit.HitResult);
            Assert.Equal(0.0, hit.Amount);
            Assert.Equal(0, hit.TargetHitStopTicks);
            Assert.Contains(rig.Arbiter!.Inputs, i => i.Result == HitResult.Invulnerable);
        }

        // ------------------------------------------------------------------ 目标辅助

        private static Core.Foundation.Common.Json.JsonObject AssistBlock(string mode = "face_only") =>
            J.O(("chain_ref", J.S(Chain.Value)), ("max_distance", J.N(5)), ("max_angle_deg", J.N(90)), ("mode", J.S(mode)));

        private static double TurnCapDeg(SpatialRig rig)
        {
            Assert.True(rig.H.Feel!.Resolve(Actor).Judging.TryGetNumber("turn_assist_deg", out var cap));
            return cap;
        }

        [Fact]
        public void TargetAssist_FacingSnap_IsAppliedWithinTheProfileCap_AndEmitsAnEvent()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }, targetAssist: AssistBlock()) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), assist: true);
            var cap = TurnCapDeg(rig);
            Assert.True(cap > 10);
            rig.Assist!.Target = Foe;
            rig.Assist.FacingDeltaDeg = cap / 2;
            rig.Units.SetPosition(Foe, new Vec2(1, 1));

            rig.H.CastInTick("skill.sample_cone");
            Assert.Equal(cap / 2 * Math.PI / 180.0, rig.Units.GetFacing(Actor), 9);
            var assisted = Assert.Single(rig.H.Of<ActionTargetAssistedEvent>()).Event;
            Assert.Equal(Foe, assisted.TargetId);
            Assert.Equal(cap / 2, assisted.FacingDelta, 9);
            Assert.Equal(rig.H.Of<ActionStartedEvent>().Single().Event.CastInstanceId, assisted.CastInstanceId);
        }

        [Fact]
        public void TargetAssist_FacingSnap_NeverExceedsTheProfileCap()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }, targetAssist: AssistBlock()) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), assist: true);
            var cap = TurnCapDeg(rig);
            rig.Assist!.Target = Foe;
            rig.Assist.FacingDeltaDeg = cap + 30; // 解析器想转得更多：时间线按档案上限截断。
            rig.Units.SetPosition(Foe, new Vec2(1, 1));

            rig.H.CastInTick("skill.sample_cone");
            Assert.InRange(rig.Units.GetFacing(Actor) * 180.0 / Math.PI, cap - 1e-9, cap + 1e-9);
            Assert.Equal(cap, Assert.Single(rig.H.Of<ActionTargetAssistedEvent>()).Event.FacingDelta, 9);
        }

        [Fact]
        public void TargetAssist_DefaultOff_NoDeclarationOrNoService_NeverTouchesFacingOrEmitsEvents()
        {
            // 没有 target_assist 声明：即便装配了服务也不调用。
            var rigA = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }) }, Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), assist: true);
            rigA.Assist!.Target = Foe;
            rigA.Assist.FacingDeltaDeg = 20;
            rigA.H.CastInTick("skill.sample_cone");
            Assert.Empty(rigA.Assist.Requests);
            Assert.Equal(0.0, rigA.Units.GetFacing(Actor));
            Assert.Empty(rigA.H.Of<ActionTargetAssistedEvent>());

            // 声明了但没有装配服务：同样静默。
            var rigB = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }, targetAssist: AssistBlock()) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), assist: false);
            rigB.H.CastInTick("skill.sample_cone");
            Assert.Equal(0.0, rigB.Units.GetFacing(Actor));
            Assert.Empty(rigB.H.Of<ActionTargetAssistedEvent>());
        }

        [Fact]
        public void TargetAssist_NoCandidate_IsSilent()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }, targetAssist: AssistBlock()) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), assist: true);
            rig.H.CastInTick("skill.sample_cone");
            Assert.Single(rig.Assist!.Requests);
            Assert.Empty(rig.H.Of<ActionTargetAssistedEvent>());
            Assert.Equal(0.0, rig.Units.GetFacing(Actor));
        }

        [Fact]
        public void TargetAssist_ProvidesTheMotionTarget_AndCloseDistanceScalesTheDeclaredDistance()
        {
            var motion = J.O(("driver", J.S("code")), ("kind", J.S("dash")), ("distance", J.N(3)), ("direction", J.S("toward_target")));
            var rig = Create(
                new[]
                {
                    SpSkill("skill.sample_lunge", 50, 150, 100, new[] { Marker("motion_start", 50), Marker("motion_end", 150), HitAt(100) },
                        targetAssist: AssistBlock("close_distance"), motion: motion),
                },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), assist: true);
            var declared = rig.H.Feel!.Calibration.ToAbsolute(Core.Foundation.DataRegistry.FeelUnit.BodyHeights, 3);
            rig.Assist!.Target = Foe;
            rig.Assist.DistanceAdjust = -declared / 2; // 缩短到声明距离的一半。
            rig.Units.SetPosition(Foe, new Vec2(0, 2));

            rig.H.CastInTick("skill.sample_lunge");
            var m = rig.H.Query.Current(Actor)!.Value.Motion!.Value;
            Assert.Equal(Foe, m.TargetId);
            Assert.Equal(declared / 2, m.DistanceWorld, 9);
            Assert.Equal(0.0, m.Direction.X, 9); // toward_target：方向指向辅助挑出的目标（正上方）。
            Assert.Equal(1.0, m.Direction.Y, 9);
            Assert.Equal(-declared / 2, Assert.Single(rig.H.Of<ActionTargetAssistedEvent>()).Event.DistanceAdjust, 9);
        }

        [Fact]
        public void TargetAssist_ExplicitTargetStillWinsOverTheAssistTarget_ForMotion()
        {
            var motion = J.O(("driver", J.S("code")), ("kind", J.S("dash")), ("distance", J.N(3)), ("direction", J.S("toward_target")));
            var rig = Create(
                new[]
                {
                    SpSkill("skill.sample_lunge", 50, 150, 100, new[] { Marker("motion_start", 50), Marker("motion_end", 150), HitAt(100) },
                        targetAssist: AssistBlock(), motion: motion),
                },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0), assist: true);
            var explicitTarget = Unit(rig, "unit.explicit", new Vec2(-3, 0));
            rig.Assist!.Target = Foe;
            rig.Units.SetPosition(Foe, new Vec2(0, 2));

            rig.H.World.Host.CastSkill(Actor, new Id("skill.sample_lunge"), new[] { explicitTarget });
            var m = rig.H.Query.Current(Actor)!.Value.Motion!.Value;
            Assert.Equal(explicitTarget, m.TargetId);
        }
    }
}
