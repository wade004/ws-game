using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0021（04 第 4 节勘误"范围约束"，消费方反馈 2026-09-10"技能效果参数数值范围校验改进
    /// 建议"）验收：<c>skill.aura_def</c> 的 <c>periodic_damage</c>/<c>periodic_heal</c>.
    /// <c>params.interval</c>（依据 AuraHost.Update：折算后 <c>interval &lt;= 0</c> 静默 continue，
    /// 见该方法判断记录）与 <c>skill.def</c> 的 <c>apply_aura.params.duration_override</c>（依据
    /// EffectDispatcher.ApplyAuraEffectPrimitive 原样转发给 AuraHost.ApplyAura 作为剩余持续时间）
    /// 现经 <c>SkillSchemas.AuraDef</c>/<c>Def</c> 登记的 <c>field_range</c> 约束，在
    /// <see cref="SkillWorldBuilder.Validate"/>（内部 <c>RegisterSchema</c> + <c>DataRegistry.LoadAll</c>，
    /// 与 <c>Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll</c> 注册的是同一份
    /// <c>SkillSchemas.Def</c>/<c>AuraDef</c>）加载阶段就被阻断，不再需要真正施放技能/推进
    /// <c>AuraHost.Update</c> 才能发现内容错误。
    /// </summary>
    public sealed class SkillEffectParamRangeTests
    {
        private static readonly Id AuraId = new Id("skill.aura_def.svng_periodic");
        private static readonly Id SkillId = new Id("skill.svng_apply_aura");

        private static JsonObject AuraWithPeriodicDamage(JsonValue intervalValue) => J.O(
            ("id", J.S(AuraId.Value)),
            ("effects", J.A(J.O(
                ("kind", J.S("periodic_damage")),
                ("params", J.O(
                    ("interval", intervalValue),
                    ("school", J.S("skill.school_svng"))))))));

        private static JsonObject ValidAuraDef() => J.O(
            ("id", J.S(AuraId.Value)),
            ("effects", J.A()));

        private static JsonObject SkillWithApplyAura(JsonValue durationOverrideValue) => J.O(
            ("id", J.S(SkillId.Value)),
            ("school", J.S("skill.school_svng")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S("target.chain.svng_sample")),
            ("effects", J.A(J.O(
                ("kind", J.S("apply_aura")),
                ("params", J.O(
                    ("aura_def", J.S(AuraId.Value)),
                    ("duration_override", durationOverrideValue)))))));

        // -----------------------------------------------------------------
        // periodic_damage.params.interval
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void PeriodicDamageInterval_ZeroOrNegative_BlockedAtLoad(double interval)
        {
            var report = new SkillWorldBuilder()
                .AuraDef(AuraWithPeriodicDamage(J.N(interval)))
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_range" &&
                i.Field == "effects[0].params.interval");
        }

        [Fact]
        public void PeriodicDamageInterval_Positive_Passes()
        {
            var report = new SkillWorldBuilder()
                .AuraDef(AuraWithPeriodicDamage(J.N(1)))
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void PeriodicDamageInterval_WrongType_ReportsFieldType_NotFieldRange()
        {
            var report = new SkillWorldBuilder()
                .AuraDef(AuraWithPeriodicDamage(J.S("not_a_number")))
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "effects[0].params.interval");
            Assert.DoesNotContain(report.Issues, i => i.Check == "field_range" && i.Field == "effects[0].params.interval");
        }

        // -----------------------------------------------------------------
        // apply_aura.params.duration_override
        // -----------------------------------------------------------------

        [Fact]
        public void ApplyAuraDurationOverride_Negative_BlockedAtLoad()
        {
            var report = new SkillWorldBuilder()
                .AuraDef(ValidAuraDef())
                .SkillDef(SkillWithApplyAura(J.N(-1)))
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_range" &&
                i.Field == "effects[0].params.duration_override");
        }

        [Fact]
        public void ApplyAuraDurationOverride_Zero_Passes()
        {
            // 0 合法：代表极短/几乎立即到期的光环（见 SkillSchemas.cs duration_override 字段旁
            // 判断记录），不是内容错误。
            var report = new SkillWorldBuilder()
                .AuraDef(ValidAuraDef())
                .SkillDef(SkillWithApplyAura(J.N(0)))
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ApplyAuraDurationOverride_Omitted_Passes()
        {
            // 省略时沿用 aura_def.duration（可空=永久），登记范围不影响这条既有约定——可选字段缺失
            // 不触发 required_field，也不触发 field_range（见 DataRegistry.ValidateRecordField：
            // 字段不存在时直接 return，根本不会走到范围检查）。
            var skill = J.O(
                ("id", J.S(SkillId.Value)),
                ("school", J.S("skill.school_svng")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.svng_sample")),
                ("effects", J.A(J.O(
                    ("kind", J.S("apply_aura")),
                    ("params", J.O(("aura_def", J.S(AuraId.Value))))))));

            var report = new SkillWorldBuilder()
                .AuraDef(ValidAuraDef())
                .SkillDef(skill)
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }
    }
}
