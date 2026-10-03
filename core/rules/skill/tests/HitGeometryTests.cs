using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Feel;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;
using static Tests.Rules.Skill.SpatialRig;
using static Tests.Rules.Skill.TimelineHarness;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 命中几何与时序（手感落地 M5-S2a，手感设计/03 第 2.2～2.4 节、05 第 3 节）：接触点落在目标受击圆面上、技能行 <c>feel_ref</c>/<c>ignores_invulnerability</c>、
    /// 命中标记的分段手感覆盖与蓄力手感缩放、地面落点技能的 <c>timeline.hit_anchor</c>、对应的加载期校验。
    /// 每个机制一条复现、一条不变量；没有声明新字段时与此前逐位一致由既有用例（SpatialHitTests 等）继续守着。
    /// </summary>
    public sealed class HitGeometryTests
    {
        private static readonly Id FoeB = new Id("unit.foe_b");

        private static Id Unit(SpatialRig rig, string name, Vec2 position)
        {
            var id = new Id(name);
            rig.H.World.AddUnit(id, position);
            return id;
        }

        private static JsonObject With(JsonObject row, params (string Key, JsonValue Value)[] extra)
        {
            var fields = new List<(string, JsonValue)>();
            for (var i = 0; i < row.Count; i++)
            {
                fields.Add((row[i].Key, row[i].Value));
            }

            fields.AddRange(extra.Select(e => (e.Key, e.Value)));
            return J.O(fields.ToArray());
        }

        // ------------------------------------------------------------------ 接触点落在目标受击圆面上

        [Fact]
        public void Contact_WithAHurtRadius_IsOnTheTargetCircleSurfaceFacingTheShape_NotInsideTheBody()
        {
            const double radius = 0.5;
            var rig = Create(
                new[] { SpSkill("skill.sample_spin", 0, 100, 100, Array.Empty<JsonValue>(), hitPolicy: "continuous") },
                Shape.Circle(Vec2.Zero, 1.0));
            rig.Shapes.ReportHitRadius = true;
            rig.Units.SetPosition(Foe, new Vec2(1.25, 0));
            rig.Shapes.SetRadius(Foe, radius);
            rig.H.CastInTick("skill.sample_spin");
            rig.RunToEnd();

            var hit = Assert.Single(rig.Hits);
            var target = new Vec2(1.25, 0);

            // 不变量：接触点到目标中心的距离恰为受击半径（在圆面上，不在体内）；法线是自目标中心指向接触点的单位向量。
            Assert.Equal(radius, (hit.ContactPoint - target).Length, 9);
            Assert.Equal(1.0, hit.ContactNormal.Length, 9);
            Assert.Equal(hit.ContactPoint.X, target.X + hit.ContactNormal.X * radius, 9);
            Assert.Equal(hit.ContactPoint.Y, target.Y + hit.ContactNormal.Y * radius, 9);

            // 复现：圆面上朝向形状（形状最近点在 (1,0)，即 -x 方向）的点 = (1.25 - 0.5, 0)。
            Assert.Equal(target.X - radius, hit.ContactPoint.X, 9);
            Assert.Equal(0.0, hit.ContactPoint.Y, 9);
            Assert.Equal(-1.0, hit.ContactNormal.X, 9);
        }

        [Fact]
        public void Contact_WithAHurtRadius_AndTheCenterInsideTheShape_FacesTheAttacker()
        {
            const double radius = 0.5;
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Shapes.ReportHitRadius = true;
            rig.Units.SetPosition(Foe, new Vec2(1.0, 0.0));
            rig.Shapes.SetRadius(Foe, radius);
            rig.H.CastInTick("skill.sample_cone");
            rig.RunToEnd();

            var hit = Assert.Single(rig.Hits);
            // 目标中心在形状内：接触点取圆面上朝向攻击方的点（攻击方在原点，目标在 +x，朝向攻击方 = -x）。
            Assert.Equal(radius, (hit.ContactPoint - new Vec2(1.0, 0.0)).Length, 9);
            Assert.Equal(0.5, hit.ContactPoint.X, 9);
            Assert.Equal(-1.0, hit.ContactNormal.X, 9);
        }

        [Fact]
        public void Contact_WithoutAHurtRadius_KeepsTheRegisteredPositionAndClosestPointPolicies()
        {
            // 命中半径来源没有接上（TargetHitRadius = 0）：marker 命中取目标登记位置，与 M5 之前逐位一致（即使旧式替身登记了半径）。
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(1.0, 0.0));
            rig.Shapes.SetRadius(Foe, 0.5); // ReportHitRadius 缺省 false
            rig.H.CastInTick("skill.sample_cone");
            rig.RunToEnd();

            var hit = Assert.Single(rig.Hits);
            Assert.Equal(new Vec2(1.0, 0.0), hit.ContactPoint);
        }

        // ------------------------------------------------------------------ 无敌前置检查与 ignores_invulnerability

        private static SpatialRig InvulnerableFoeRig(bool ignoresInvulnerability, out Dictionary<Id, double> hp)
        {
            var health = new Dictionary<Id, double> { [Foe] = 100 };
            hp = health;
            var attack = SpSkill("skill.sample_cone", 50, 300, 100, new[] { HitAt(100) });
            if (ignoresInvulnerability) attack = With(attack, ("ignores_invulnerability", J.B(true)));
            var rig = Create(
                new[]
                {
                    attack,
                    SelfSkill("skill.sample_dodge", 0, 500, 0, new[] { Marker("invuln_start", 0), Marker("invuln_end", 500) }),
                },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.H.World.Combat.ResolveFunc = ctx =>
            {
                if (ctx.Kind == EffectKind.SchoolDamage) health[ctx.TargetId] -= ctx.BaseValue;
                return new ResolveResult(HitResult.Hit, ctx.BaseValue, ctx.BaseValue, 0, immune: false, isHeal: ctx.Kind == EffectKind.Heal);
            };
            rig.Units.SetPosition(Foe, new Vec2(1, 0.2));
            rig.H.Tick(() => rig.H.World.Host.CastSkill(Foe, new Id("skill.sample_dodge"), Array.Empty<Id>()));
            Assert.True(rig.H.Query.IsInvulnerable(Foe));
            rig.H.CastInTick("skill.sample_cone");
            rig.RunToEnd(_ => rig.H.Clock.Advance(Foe));
            return rig;
        }

        [Fact]
        public void IgnoresInvulnerability_LetsTheTimelineHitLandOnAnInvulnerableTarget()
        {
            var normal = InvulnerableFoeRig(false, out var hpNormal);
            Assert.Equal(HitResult.Invulnerable, Assert.Single(normal.HitsOn(Foe)).HitResult);
            Assert.Equal(100.0, hpNormal[Foe]);

            // 同一场景、同一目标，技能声明 ignores_invulnerability：时间线路径不做无敌前置检查，伤害照常结算。
            var ignoring = InvulnerableFoeRig(true, out var hpIgnoring);
            var hit = Assert.Single(ignoring.HitsOn(Foe));
            Assert.Equal(HitResult.Hit, hit.HitResult);
            Assert.True(hpIgnoring[Foe] < 100.0);
            Assert.Empty(ignoring.H.Of<CombatAttackAvoidedEvent>());
        }

        [Fact]
        public void BlocksHitByInvulnerability_FollowsTheGateRules_PeriodicSelfAndIgnoringSkillsAreExempt()
        {
            var rig = InvulnerableFoeRig(false, out _);
            var host = rig.H.World.Host;
            var school = new Id("skill.school_sample");
            var skill = new Id("skill.sample_cone");

            // 复现：目标处于无敌窗口之内时，来自别人的伤害类结算被门拦住。
            rig.H.Tick(() => rig.H.World.Host.CastSkill(Foe, new Id("skill.sample_dodge"), Array.Empty<Id>()));
            Assert.True(rig.H.Query.IsInvulnerable(Foe));
            EffectContext Ctx(Id source, Id? aura = null, bool periodic = false, Id? skillId = null) =>
                new EffectContext(source, Foe, skillId ?? skill, EffectKind.SchoolDamage, school, 5, 0, null, aura, periodic, true, true, null, 0, null);
            Assert.True(host.BlocksHitByInvulnerability(Ctx(Actor)));

            // 不变量：周期性结算、光环来源、自伤不被拦；技能声明 ignores_invulnerability 也不被拦（此处用另一份声明了的技能表）。
            Assert.False(host.BlocksHitByInvulnerability(Ctx(Actor, periodic: true)));
            Assert.False(host.BlocksHitByInvulnerability(Ctx(Actor, aura: new Id("aura.inst_1"))));
            Assert.False(host.BlocksHitByInvulnerability(Ctx(Foe)));

            var ignoring = InvulnerableFoeRig(true, out _);
            ignoring.H.Tick(() => ignoring.H.World.Host.CastSkill(Foe, new Id("skill.sample_dodge"), Array.Empty<Id>()));
            Assert.True(ignoring.H.Query.IsInvulnerable(Foe));
            Assert.False(ignoring.H.World.Host.BlocksHitByInvulnerability(Ctx(Actor)));

            // 目标不在无敌窗口：恒不拦。
            for (var i = 0; i < Ticks(600); i++) rig.H.Tick(() => rig.H.Clock.Advance(Foe));
            Assert.False(rig.H.Query.IsInvulnerable(Foe));
            Assert.False(host.BlocksHitByInvulnerability(Ctx(Actor)));
        }

        // ------------------------------------------------------------------ 手感：分段覆盖、技能行 feel_ref、蓄力缩放

        private const string SkillRow = "feel.action.test_skill";
        private const string SegmentRow = "feel.action.test_segment";
        private const double SkillHitstop = 55;
        private const double SegmentHitstop = 77;

        private static void AddActionRows(FeelResolver feel)
        {
            var p = feel.Profiles;
            var rows = p.Presets.Concat(p.Archetypes).Concat(p.Weapons).Concat(p.Characters).Concat(p.Actions).Concat(p.TagMaps)
                .Append(FeelRow.Overlay(FeelTables.Action, SkillRow, new[] { new FeelWrite("attacker_hitstop_ms", FeelOp.Set, FeelValue.Of(SkillHitstop)) }))
                .Append(FeelRow.Overlay(FeelTables.Action, SegmentRow, new[] { new FeelWrite("attacker_hitstop_ms", FeelOp.Set, FeelValue.Of(SegmentHitstop)) }));
            feel.Reload(new FeelProfileSet(p.Fields, rows, p.MotionModeRules, p.Calibrations));
        }

        private static JsonValue HitWithFeel(double atMs, int segment, string feelRef) =>
            J.O(("name", J.S("hit")), ("at_ms", J.N(atMs)), ("args", J.O(("segment", J.N(segment)), ("feel_ref", J.S(feelRef)))));

        private static double AttackerHitstop(HitFeelInput input) => input.AttackerFeel!.GetNumber("attacker_hitstop_ms");

        [Fact]
        public void SegmentFeelRef_OverridesOnlyThatSegmentOnTopOfTheActionLayer_AndFlagsTheOverride()
        {
            var skill = With(
                SpSkill("skill.sample_two", 0, 300, 100, new[] { HitAt(100, 0), HitWithFeel(200, 1, SegmentRow) }),
                ("feel_ref", J.S(SkillRow)));
            var rig = Create(new[] { skill }, Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            AddActionRows(rig.H.Feel!);
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            rig.H.CastInTick("skill.sample_two");
            rig.RunToEnd();

            var inputs = rig.Arbiter!.Full;
            Assert.Equal(2, inputs.Count);

            // 第 0 段没有声明分段覆盖：视图是动作开始快照（技能行的动作层 55 ms），不标记覆盖，蓄力缩放为恒等。
            Assert.False(inputs[0].AttackerFeelOverrides);
            Assert.Equal(SkillHitstop, AttackerHitstop(inputs[0]));
            Assert.True(inputs[0].Scale.IsIdentity);

            // 第 1 段声明了 args.feel_ref：动作层之上再叠分段行（同层后写覆盖先写），并标记覆盖供落地阶段击退/击飞使用。
            Assert.True(inputs[1].AttackerFeelOverrides);
            Assert.Equal(SegmentHitstop, AttackerHitstop(inputs[1]));
        }

        [Fact]
        public void SkillFeelRef_WithoutASegmentOverride_IsTheActionLayerOfTheSnapshot_AndUndeclaredIsThePresetValue()
        {
            var declared = Create(
                new[] { With(SpSkill("skill.sample_one", 0, 200, 100, new[] { HitAt(100) }), ("feel_ref", J.S(SkillRow))) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            AddActionRows(declared.H.Feel!);
            declared.Units.SetPosition(Foe, new Vec2(1, 0));
            declared.H.CastInTick("skill.sample_one");
            declared.RunToEnd();

            var plain = Create(
                new[] { SpSkill("skill.sample_one", 0, 200, 100, new[] { HitAt(100) }) }, Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            AddActionRows(plain.H.Feel!);
            plain.Units.SetPosition(Foe, new Vec2(1, 0));
            plain.H.CastInTick("skill.sample_one");
            plain.RunToEnd();

            var preset = plain.H.Feel!.ResolveJudging(Actor).GetNumber("attacker_hitstop_ms");
            Assert.Equal(SkillHitstop, AttackerHitstop(Assert.Single(declared.Arbiter!.Full)));
            Assert.Equal(preset, AttackerHitstop(Assert.Single(plain.Arbiter!.Full))); // 未声明：按武器/角色，与此前一致
            Assert.NotEqual(SkillHitstop, preset);
        }

        private static JsonObject Charge(double minMs, double maxMs, JsonObject feelScale) =>
            J.O(("min_ms", J.N(minMs)), ("max_ms", J.N(maxMs)), ("feel_scale", feelScale));

        private static JsonObject Range(double min, double max) => J.O(("min", J.N(min)), ("max", J.N(max)));

        [Theory]
        [InlineData(0)]
        [InlineData(18)]
        [InlineData(60)]
        public void ChargeFeelScale_InterpolatesEachDeclaredFieldByTheChargeRatio_AndUndeclaredFieldsStayAtOne(int heldTicks)
        {
            const double minMs = 100;
            const double maxMs = 500;
            var scale = J.O(("attacker_hitstop_ms", Range(1, 3)), ("knockback_distance", Range(0.5, 2)));
            var rig = Create(
                new[] { SpSkill("skill.sample_charged", 50, 200, 100, new[] { HitAt(100) }, charge: Charge(minMs, maxMs, scale)) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            var r = rig.H.World.Host.CastSkillWithContext(Actor, new Id("skill.sample_charged"), Array.Empty<Id>(), new ActionCastContext(null, heldTicks));
            Assert.True(r.Success, r.Reason.ToString());
            rig.RunToEnd();

            var heldMs = heldTicks * Step * 1000.0;
            var ratio = Math.Min(1.0, Math.Max(0.0, (heldMs - minMs) / (maxMs - minMs)));
            var input = Assert.Single(rig.Arbiter!.Full);
            Assert.Equal(1 + (3 - 1) * ratio, input.Scale.AttackerHitstop, 9);
            Assert.Equal(0.5 + (2 - 0.5) * ratio, input.Scale.KnockbackDistance, 9);
            Assert.Equal(1.0, input.Scale.TargetHitstop);
            Assert.Equal(1.0, input.Scale.LaunchHeight);
            Assert.False(input.AttackerFeelOverrides); // 只缩放不覆盖：视图仍是动作开始快照
        }

        [Fact]
        public void ChargeWithoutFeelScale_GivesAnIdentityScale_SoDeclaredChargesAreTheOnlyOnesAffected()
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_charged", 50, 200, 100, new[] { HitAt(100) }, charge: J.O(("min_ms", J.N(100)), ("max_ms", J.N(500)))) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            Assert.True(rig.H.World.Host.CastSkillWithContext(Actor, new Id("skill.sample_charged"), Array.Empty<Id>(), new ActionCastContext(null, 60)).Success);
            rig.RunToEnd();
            Assert.True(Assert.Single(rig.Arbiter!.Full).Scale.IsIdentity);
        }

        // ------------------------------------------------------------------ 地面落点技能的 hit_anchor

        private static JsonObject GroundSkill(string id, string? anchor)
        {
            var row = SpSkill(id, 0, 200, 100, new[] { HitAt(100) });
            var timeline = (JsonObject)row["timeline"];
            var fields = new List<(string, JsonValue)>();
            for (var i = 0; i < timeline.Count; i++) fields.Add((timeline[i].Key, timeline[i].Value));
            if (anchor != null) fields.Add(("hit_anchor", J.S(anchor)));
            var rebuilt = new List<(string, JsonValue)>();
            for (var i = 0; i < row.Count; i++)
            {
                rebuilt.Add(row[i].Key == "timeline" ? ("timeline", J.O(fields.ToArray())) : (row[i].Key, row[i].Value));
            }

            rebuilt.Add(("ground_target", J.B(true)));
            return J.O(rebuilt.ToArray());
        }

        private static SpatialRig GroundRig(string? anchor, out Id near, out Id far)
        {
            var rig = Create(new[] { GroundSkill("skill.sample_ground", anchor) }, Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0));
            rig.Units.SetPosition(Foe, new Vec2(1, 0)); // 靠近施法者（朝向 +x）
            near = Foe;
            far = Unit(rig, FoeB.Value, new Vec2(0.3, 11)); // 靠近落点 (0, 10) 前方
            return rig;
        }

        private static void CastAtGround(SpatialRig rig, Vec2 point)
        {
            rig.H.Tick(() =>
            {
                var r = rig.H.World.Host.CastSkillAtGround(Actor, new Id("skill.sample_ground"), new GroundCastRequest(point));
                Assert.True(r.Success, r.Reason.ToString());
            });
            rig.RunToEnd();
        }

        [Fact]
        public void HitAnchor_GroundPoint_AnchorsTheShapeAtThePoint_FacingFromTheCasterToIt_AndCastSuccessCarriesThePoint()
        {
            var point = new Vec2(0, 10);
            var rig = GroundRig("ground_point", out var near, out var far);
            CastAtGround(rig, point);

            // 复现：形状以落点为锚点、朝向指向落点（+y）：只有落点前方的目标被命中，施法者身边的目标没有。
            Assert.Equal(new[] { far }, rig.Hits.Select(e => e.TargetId).ToArray());
            Assert.Empty(rig.HitsOn(near));
            // 不变量：cast_success 带落点（落点施放的既有口径；伤害类结算管线本身不消费落点，见 EffectContext.GroundPoint 判断记录）。
            Assert.Equal(point, rig.H.Of<SkillCastSuccessEvent>().Single().Event.GroundPoint);
        }

        [Fact]
        public void HitAnchor_Caster_AnchorsTheShapeAtTheCasterButCastSuccessStillCarriesThePoint()
        {
            var point = new Vec2(0, 10);
            var rig = GroundRig("caster", out var near, out _);
            CastAtGround(rig, point);

            Assert.Equal(new[] { near }, rig.Hits.Select(e => e.TargetId).ToArray());
            Assert.Equal(point, rig.H.Of<SkillCastSuccessEvent>().Single().Event.GroundPoint);
        }

        [Fact]
        public void HitAnchor_Undeclared_KeepsTheExistingGroundCastPath_TheTimelineIsIgnored()
        {
            var rig = GroundRig(null, out _, out _);
            rig.H.Tick(() =>
            {
                var r = rig.H.World.Host.CastSkillAtGround(Actor, new Id("skill.sample_ground"), new GroundCastRequest(new Vec2(0, 10)));
                Assert.True(r.Success, r.Reason.ToString());
            });
            rig.H.TickN(Ticks(1000));

            // 既有行为：不进入时间线模式（没有动作开始事件、没有 hit_confirmed）。
            Assert.Empty(rig.H.Of<ActionStartedEvent>());
            Assert.Empty(rig.Hits);
        }

        // ------------------------------------------------------------------ 加载期校验

        private static ValidationReport Validate(params JsonObject[] skills)
        {
            var builder = new SkillWorldBuilder().ValidationRule(new SkillTimelineRule());
            foreach (var s in skills) builder.SkillDef(s);
            return builder.Validate();
        }

        [Fact]
        public void Validation_GroundTargetWithHitAnchor_NoLongerWarnsTheTimelineIsIgnored_WithoutItStillDoes()
        {
            var anchored = Validate(GroundSkill("skill.sample_ground", "ground_point"));
            Assert.DoesNotContain(anchored.Issues, i => i.Check == "timeline_ground_target_unsupported");
            Assert.DoesNotContain(anchored.Issues, i => i.Severity == ValidationSeverity.Error);

            var plain = Validate(GroundSkill("skill.sample_ground", null));
            Assert.Contains(plain.Issues, i => i.Check == "timeline_ground_target_unsupported" && i.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void Validation_HitAnchorWithoutGroundTarget_IsAWarning()
        {
            var row = GroundSkill("skill.sample_ground", "ground_point");
            var noGround = J.O(Enumerable.Range(0, row.Count).Where(i => row[i].Key != "ground_target").Select(i => (row[i].Key, row[i].Value)).ToArray());
            var report = Validate(noGround);
            Assert.Contains(report.Issues, i => i.Check == "timeline_hit_anchor_without_ground_target" && i.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void Validation_SkillFeelRefAndTimelineFeelRefMustAgree()
        {
            JsonObject Build(string skillRef, string timelineRef)
            {
                var row = SpSkill("skill.sample_feel", 0, 200, 100, new[] { HitAt(100) });
                var timeline = (JsonObject)row["timeline"];
                var tl = new List<(string, JsonValue)>();
                for (var i = 0; i < timeline.Count; i++) tl.Add((timeline[i].Key, timeline[i].Value));
                tl.Add(("feel_ref", J.S(timelineRef)));
                var fields = new List<(string, JsonValue)>();
                for (var i = 0; i < row.Count; i++) fields.Add(row[i].Key == "timeline" ? ("timeline", J.O(tl.ToArray())) : (row[i].Key, row[i].Value));
                fields.Add(("feel_ref", J.S(skillRef)));
                return J.O(fields.ToArray());
            }

            Assert.DoesNotContain(Validate(Build("feel.action.a", "feel.action.a")).Issues, i => i.Check == "skill_feel_ref_conflict");
            var conflict = Validate(Build("feel.action.a", "feel.action.b"));
            Assert.Contains(conflict.Issues, i => i.Check == "skill_feel_ref_conflict" && i.Severity == ValidationSeverity.Error);
            Assert.True(conflict.IsBlocking);
        }

        [Fact]
        public void Validation_MarkerFeelRefOnANonHitMarker_IsAWarning_AndOnHitOrReleaseIsSilent()
        {
            var ok = Validate(SpSkill("skill.sample_a", 0, 200, 100, new[] { HitWithFeel(100, 0, SegmentRow) }));
            Assert.DoesNotContain(ok.Issues, i => i.Check == "timeline_marker_feel_ref_ignored");

            var bad = Validate(SpSkill(
                "skill.sample_b", 0, 200, 100,
                new[] { HitAt(100), J.O(("name", J.S("invuln_start")), ("at_ms", J.N(0)), ("args", J.O(("feel_ref", J.S(SegmentRow))))) }));
            Assert.Contains(bad.Issues, i => i.Check == "timeline_marker_feel_ref_ignored" && i.Severity == ValidationSeverity.Warning);
        }
    }
}
