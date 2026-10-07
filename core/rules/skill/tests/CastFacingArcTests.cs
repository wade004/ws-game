using System;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 技能朝向要求（<c>skill.def.facing_arc</c>，ADR-0164，样板游戏 B 反馈）：声明后施法管线步骤 7 要求目标在施法者朝向的扇形内。
    /// 复现：目标在身后时同一技能从成功变为 <see cref="CastFailureReason.NotFacing"/>（期望夹角由坐标算出）；
    /// 不变量：未声明时永不触发；扇形边界上通过、略超失败；目标是施法者自身/重叠位置不触发；朝向取负角度/超过 π 的包角正确。
    /// </summary>
    public sealed class CastFacingArcTests
    {
        private static readonly Id CasterId = new Id("unit.caster");
        private static readonly Id TargetId = new Id("unit.target");

        private static Core.Foundation.Common.Json.JsonObject Skill(double? arc)
        {
            var fields = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S("skill.sample_bolt")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(20)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(10)), ("coefficient", J.N(0))))))),
            };
            if (arc.HasValue)
            {
                fields.Add(("facing_arc", J.N(arc.Value)));
            }

            return J.O(fields.ToArray());
        }

        private static CastResult Cast(double? arc, Vec2 targetPos, double casterFacing)
        {
            var world = new SkillWorldBuilder().SkillDef(Skill(arc)).Build();
            world.AddUnit(CasterId, new Vec2(0, 0));
            world.AddUnit(TargetId, targetPos);
            world.Units.SetFacing(CasterId, casterFacing);
            world.Targets.SetChain(new Id("target.chain.sample"), TargetId);
            return world.Host.CastSkill(CasterId, new Id("skill.sample_bolt"), Array.Empty<Id>());
        }

        [Fact]
        public void TargetBehindTheCaster_FailsNotFacing_WhileTargetInFrontSucceeds()
        {
            Assert.True(Cast(120, new Vec2(5, 0), 0).Success);
            var behind = Cast(120, new Vec2(-5, 0), 0);
            Assert.False(behind.Success);
            Assert.Equal(CastFailureReason.NotFacing, behind.Reason);
        }

        [Fact]
        public void NoFacingArcDeclared_NeverFails_EvenWhenTargetIsBehind()
        {
            Assert.True(Cast(null, new Vec2(-5, 0), 0).Success);
        }

        [Fact]
        public void ArcBoundary_HalfAngleExactlyPasses_SlightlyBeyondFails()
        {
            // 全角 90 度 = 半角 45 度：目标在 45 度方向上刚好通过，在 46 度方向上失败。
            var onEdge = Cast(90, new Vec2(Math.Cos(Math.PI / 4) * 5, Math.Sin(Math.PI / 4) * 5), 0);
            Assert.True(onEdge.Success);
            var beyond = Cast(90, new Vec2(Math.Cos(46 * Math.PI / 180) * 5, Math.Sin(46 * Math.PI / 180) * 5), 0);
            Assert.False(beyond.Success);
            Assert.Equal(CastFailureReason.NotFacing, beyond.Reason);
        }

        [Fact]
        public void AngleWrapsAroundPi_FacingNearMinusPiTargetNearPlusPi_Passes()
        {
            // 朝向 -179 度，目标在 +179 度方向：夹角 2 度（不是 358 度）。
            var facing = -179 * Math.PI / 180;
            var pos = new Vec2(Math.Cos(179 * Math.PI / 180) * 5, Math.Sin(179 * Math.PI / 180) * 5);
            Assert.True(Cast(60, pos, facing).Success);
        }

        [Fact]
        public void OverlappingTarget_IsNotRejected()
        {
            Assert.True(Cast(60, new Vec2(0, 0), 1.0).Success);
        }

        private static Core.Foundation.Common.Json.JsonObject WithField(string id, string key, Core.Foundation.Common.Json.JsonValue value) =>
            J.O(("id", J.S(id)), ("school", J.S("skill.school_sample")), ("kind", J.S("active")), ("range", J.N(0)), ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)), ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(J.O(("kind", J.S("heal")), ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0))))))), (key, value));

        [Theory]
        [InlineData(0.0)]
        [InlineData(-30.0)]
        [InlineData(361.0)]
        public void FacingArcRule_OutOfRange_ReportsError(double arc)
        {
            var bad = new SkillWorldBuilder().SkillDef(WithField("skill.sample_arc_bad", "facing_arc", J.N(arc))).ValidationRule(new Core.Rules.Skill.FacingArcRule());
            Assert.Contains(bad.Validate().Issues, i => i.Check == "facing_arc_range");
        }

        [Fact]
        public void FacingArcRule_ValidArc_Passes_AndTimelineConflict_ReportsError()
        {
            var ok = new SkillWorldBuilder().SkillDef(WithField("skill.sample_arc_ok", "facing_arc", J.N(120))).ValidationRule(new Core.Rules.Skill.FacingArcRule());
            Assert.DoesNotContain(ok.Validate().Issues, i => i.Check.StartsWith("facing_arc"));

            var both = J.O(("id", J.S("skill.sample_arc_tl")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")), ("range", J.N(0)), ("cast_time", J.N(1)),
                ("respects_gcd", J.B(false)), ("target_shape_ref", J.S("target.chain.sample")), ("facing_arc", J.N(90)),
                ("timeline", J.O(("startup_ms", J.N(500)), ("active_ms", J.N(100)), ("recovery_ms", J.N(400)))),
                ("effects", J.A(J.O(("kind", J.S("heal")), ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0))))))));
            var report = new SkillWorldBuilder().SkillDef(both).ValidationRule(new Core.Rules.Skill.FacingArcRule()).Validate();
            Assert.Contains(report.Issues, i => i.Check == "facing_arc_timeline_conflict");
        }
    }
}
