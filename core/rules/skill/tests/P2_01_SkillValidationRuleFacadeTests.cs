#pragma warning disable CS0618 // 有意调用 [Obsolete] 的 1.12 兼容 façade，验证 ABI/API 兼容行为。
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// P2-01 ABI/API 兼容回归测试（外部审计 audit-c9ff301-20260909）：ADR-0019/F1a 退役的三个
    /// public 规则类型（<see cref="EffectKindRegisteredRule"/>/<see cref="CostEntryShapeRule"/>/
    /// <see cref="ChargesShapeRule"/>）必须仍然存在、可显式注册、行为与 1.12 完全一致。
    /// </summary>
    public sealed class P2_01_SkillValidationRuleFacadeTests
    {
        [Fact]
        public void EffectKindRegisteredRule_FlagsUnknownEffectKind()
        {
            var ok = new SkillWorldBuilder()
                .SkillDef(J.O(
                    ("id", J.S("skill.p2_01_ok")), ("school", J.S("skill.school.p2_01")), ("kind", J.S("active")),
                    ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(true)),
                    ("target_shape_ref", J.S("target.chain.p2_01")),
                    ("effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O()))))))
                .ValidationRule(new EffectKindRegisteredRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new SkillWorldBuilder()
                .SkillDef(J.O(
                    ("id", J.S("skill.p2_01_bad")), ("school", J.S("skill.school.p2_01")), ("kind", J.S("active")),
                    ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(true)),
                    ("target_shape_ref", J.S("target.chain.p2_01")),
                    ("effects", J.A(J.O(("kind", J.S("not_a_real_kind")), ("params", J.O()))))))
                .ValidationRule(new EffectKindRegisteredRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "unknown_effect_kind");
        }

        [Fact]
        public void ChargesShapeRule_FlagsMissingRechargeTime()
        {
            var bad = new SkillWorldBuilder()
                .SkillDef(J.O(
                    ("id", J.S("skill.p2_01_charges_bad")), ("school", J.S("skill.school.p2_01")), ("kind", J.S("active")),
                    ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(true)),
                    ("target_shape_ref", J.S("target.chain.p2_01")),
                    ("charges", J.O(("max", J.N(1)))),
                    ("effects", J.A())))
                .ValidationRule(new ChargesShapeRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "charges_recharge_time_missing");
        }

        [Fact]
        public void CostEntryShapeRule_FlagsMissingAmount()
        {
            var bad = new SkillWorldBuilder()
                .SkillDef(J.O(
                    ("id", J.S("skill.p2_01_cost_bad")), ("school", J.S("skill.school.p2_01")), ("kind", J.S("active")),
                    ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(true)),
                    ("target_shape_ref", J.S("target.chain.p2_01")),
                    ("cost", J.A(J.O(("power_type", J.S("power.p2_01"))))),
                    ("effects", J.A())))
                .ValidationRule(new CostEntryShapeRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "cost_amount_invalid");
        }
    }
}
#pragma warning restore CS0618
