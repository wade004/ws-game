using System.Linq;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>五条校验规则（见 schema/README.md"校验规则"表）各一条正例、一条反例（见落地方案
    /// 综合验收"校验规则正反各一"）。</summary>
    public sealed class SkillValidationRuleTests
    {
        private static Core.Foundation.Common.Json.JsonObject SkillWithEffects(string id, int effectCount, string kind = "active", double castTime = 0, double channelTime = 0)
        {
            var effects = new Core.Foundation.Common.Json.JsonValue[effectCount];
            for (var i = 0; i < effectCount; i++)
            {
                effects[i] = J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0)))));
            }

            var fields = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S(kind)),
                ("range", J.N(0)),
                ("cast_time", J.N(castTime)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(effects)),
            };

            if (channelTime != 0)
            {
                fields.Add(("channel_time", J.N(channelTime)));
            }

            return J.O(fields.ToArray());
        }

        [Fact]
        public void MaxEffectsPerSkill_PassesWithinLimit_FailsOverLimit()
        {
            var within = new SkillWorldBuilder().SkillDef(SkillWithEffects("skill.sample_ok", 3))
                .ValidationRule(new MaxEffectsPerSkillRule(8));
            Assert.False(within.Validate().IsBlocking);

            var over = new SkillWorldBuilder().SkillDef(SkillWithEffects("skill.sample_over", 9))
                .ValidationRule(new MaxEffectsPerSkillRule(8));
            var report = over.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "max_effects_per_skill");
        }

        [Fact]
        public void StackCategoryConflict_PassesWhenConsistent_FailsWhenMixed()
        {
            var consistentA = J.O(("id", J.S("skill.aura_def.sample_a")), ("stack_category", J.S("skill.category.sample")), ("max_stacks", J.N(3)), ("effects", J.A()));
            var consistentB = J.O(("id", J.S("skill.aura_def.sample_b")), ("stack_category", J.S("skill.category.sample")), ("max_stacks", J.N(5)), ("effects", J.A()));
            var ok = new SkillWorldBuilder().AuraDef(consistentA).AuraDef(consistentB).ValidationRule(new StackCategoryConflictRule());
            Assert.False(ok.Validate().IsBlocking);

            var stackable = J.O(("id", J.S("skill.aura_def.sample_c")), ("stack_category", J.S("skill.category.mixed")), ("max_stacks", J.N(3)), ("effects", J.A()));
            var nonStackable = J.O(("id", J.S("skill.aura_def.sample_d")), ("stack_category", J.S("skill.category.mixed")), ("max_stacks", J.N(1)), ("effects", J.A()));
            var bad = new SkillWorldBuilder().AuraDef(stackable).AuraDef(nonStackable).ValidationRule(new StackCategoryConflictRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "stack_category_conflict");
        }

        /// <summary>ADR-0019 / F1a：<c>EffectKindRegisteredRule</c> 已退役（见
        /// <c>SkillValidationRules.cs</c> 顶部判断记录）——<c>effects[].kind</c> 是否已登记现由
        /// <c>SkillSchemas.Def</c> 把 <c>effects</c> 登记为按 <c>kind</c> 分派的
        /// <c>VariantSchema</c> 覆盖，不需要额外注册规则，检查名也从 <c>unknown_effect_kind</c>
        /// 改为共用的 <c>variant_discriminator</c>（本方法不再调用 <c>.ValidationRule(...)</c>）。</summary>
        [Fact]
        public void EffectKindRegistered_PassesForKnownKind_FailsForUnknown()
        {
            var known = J.O(("id", J.S("skill.sample_known")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")),
                ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(false)), ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(J.O(("kind", J.S("heal")), ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0))))))));
            var ok = new SkillWorldBuilder().SkillDef(known);
            Assert.False(ok.Validate().IsBlocking);

            var unknown = J.O(("id", J.S("skill.sample_unknown_kind")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")),
                ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(false)), ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(J.O(("kind", J.S("not_a_real_effect")), ("params", J.O())))));
            var bad = new SkillWorldBuilder().SkillDef(unknown);
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator");
        }

        [Fact]
        public void CastTimeChannelTimeExclusive_PassesWhenOnlyOneSet_FailsWhenBothSet()
        {
            var ok = new SkillWorldBuilder().SkillDef(SkillWithEffects("skill.sample_castonly", 1, castTime: 2))
                .ValidationRule(new CastTimeChannelTimeExclusiveRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new SkillWorldBuilder().SkillDef(SkillWithEffects("skill.sample_both", 1, castTime: 2, channelTime: 3))
                .ValidationRule(new CastTimeChannelTimeExclusiveRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "cast_time_channel_time_exclusive");
        }

        [Fact]
        public void PassiveSkillNoCastTime_PassesWhenZero_FailsWhenNonZero()
        {
            var ok = new SkillWorldBuilder().SkillDef(SkillWithEffects("skill.sample_passive_ok", 1, kind: "passive", castTime: 0))
                .ValidationRule(new PassiveSkillNoCastTimeRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new SkillWorldBuilder().SkillDef(SkillWithEffects("skill.sample_passive_bad", 1, kind: "passive", castTime: 1.5))
                .ValidationRule(new PassiveSkillNoCastTimeRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "passive_skill_no_cast_time");
        }
    }
}
