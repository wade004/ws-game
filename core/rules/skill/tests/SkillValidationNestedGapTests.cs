using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0019 / F1a 收口：本文件此前覆盖的是 R09 遗留缺口（<c>effects[]</c>/<c>charges</c>/
    /// <c>cost[]</c> 嵌套结构此前只能靠额外注册 <c>EffectKindRegisteredRule</c>/
    /// <c>ChargesShapeRule</c>/<c>CostEntryShapeRule</c> 三条手写规则拦下，未注册时能通过
    /// <c>DataRegistry.LoadAll()</c>（0 error）、只在 <c>SkillDefCache</c> 懒解析（通常是这个
    /// 技能/光环第一次被施放）时才因无防御的直接类型转换崩溃）。三条规则的结构性检查已退役
    /// （见 <c>SkillValidationRules.cs</c> 顶部判断记录）——<c>SkillSchemas.Def</c>/<c>AuraDef</c>
    /// 现把这些嵌套结构登记为 <see cref="Core.Foundation.DataRegistry.FieldSchema.Fields"/>/
    /// <see cref="Core.Foundation.DataRegistry.FieldSchema.Variants"/>，同一批坏数据现在<b>只靠
    /// schema 本身</b>（<c>SkillWorldBuilder.Validate()</c> 默认注册的 <c>RegisterSchema</c>，不再
    /// 额外注册任何 <c>IValidationRule</c>）即可在加载阶段就被拦下，不需要调用方记得另外注册规则；
    /// 本文件因此从"证明缺口存在 + 证明手写规则能补上"改写为"证明缺口已经被登记本身关闭"。
    /// </summary>
    public sealed class SkillValidationNestedGapTests
    {
        private static readonly Id SkillId = new Id("skill.svng_sample");

        // -----------------------------------------------------------------
        // effects[] 条目缺少 "kind" / 条目不是对象 / kind 不是字符串 —— 全部改由
        // SkillSchemas.Def.effects 的 VariantSchema 递归校验拦下（variant_discriminator/field_type），
        // 不再需要注册任何额外规则。
        // -----------------------------------------------------------------

        private static JsonObject SkillWithEffectMissingKind() => J.O(
            ("id", J.S(SkillId.Value)),
            ("school", J.S("skill.school_svng")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S("target.chain.svng_sample")),
            // 缺 "kind"，只有 "params"——真实容易发生的数据作者疏漏（复制粘贴时漏了一行）。
            ("effects", J.A(J.O(("params", J.O(("base_value", J.N(5))))))));

        [Fact]
        public void EffectMissingKind_CaughtBySchemaAlone_NoExtraRuleNeeded()
        {
            var report = new SkillWorldBuilder().SkillDef(SkillWithEffectMissingKind()).Validate();

            Assert.True(report.IsBlocking);
            var issue = report.Issues.Single(i => i.Check == "variant_discriminator");
            Assert.Equal("skill.def", issue.Table);
        }

        /// <summary>与本文件改写前的核心区别：这份坏数据此前能完整通过
        /// <c>DataRegistry.LoadAll()</c>（0 error），只在第一次真正施放这个技能时才在
        /// <c>SkillDefCache</c> 懒解析里崩溃；登记本身关闭这个缺口后，<c>SkillWorldBuilder.Build()</c>
        /// （校验不通过即抛异常，见该方法实现）在构造阶段就会拒绝这份数据，<c>CastSkill</c> 从此
        /// 永远不会看到这份坏形状。</summary>
        [Fact]
        public void EffectMissingKind_NowRejectedAtBuildTime_NeverReachesCastSkill()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => new SkillWorldBuilder().SkillDef(SkillWithEffectMissingKind()).Build());
            Assert.Contains("variant_discriminator", ex.Message);
        }

        [Fact]
        public void EffectEntryNotObject_CaughtBySchemaAlone()
        {
            var skill = J.O(
                ("id", J.S(SkillId.Value)),
                ("school", J.S("skill.school_svng")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.svng_sample")),
                // 一个裸数字混进了 effects 数组——Array.Item 递归校验按 field_type 拦下。
                ("effects", J.A(J.N(123))));

            var report = new SkillWorldBuilder().SkillDef(skill).Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Table == "skill.def");
        }

        [Fact]
        public void EffectKindNotString_CaughtBySchemaAlone()
        {
            var skill = J.O(
                ("id", J.S(SkillId.Value)),
                ("school", J.S("skill.school_svng")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.svng_sample")),
                ("effects", J.A(J.O(("kind", J.N(1))))));

            var report = new SkillWorldBuilder().SkillDef(skill).Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator");
        }

        [Fact]
        public void AuraEffectMissingKind_CaughtBySchemaAlone()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.svng_sample")),
                ("duration", J.N(6)),
                ("effects", J.A(J.O(("params", J.O(("base_value", J.N(3))))))));

            var report = new SkillWorldBuilder().AuraDef(aura).Validate();

            Assert.True(report.IsBlocking);
            var issue = report.Issues.Single(i => i.Check == "variant_discriminator");
            Assert.Equal("skill.aura_def", issue.Table);
        }

        [Fact]
        public void KnownEffectKind_StillPasses_Regression()
        {
            var skill = J.O(
                ("id", J.S(SkillId.Value)),
                ("school", J.S("skill.school_svng")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.svng_sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var report = new SkillWorldBuilder().SkillDef(skill).Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // charges 子字段缺失/类型错误 —— 结构部分（缺字段/类型错）改由
        // SkillSchemas.Def.charges 的 Fields 递归校验拦下（required_field）；唯一保留的业务判断
        // （charges.max 必须 >= 1）仍需显式注册 ChargesMaxAtLeastOneRule（见该类型判断记录）。
        // -----------------------------------------------------------------

        private static JsonObject SkillWithChargesMissingRechargeTime() => J.O(
            ("id", J.S(SkillId.Value)),
            ("school", J.S("skill.school_svng")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("charges", J.O(("max", J.N(1)))), // 漏填 recharge_time。
            ("target_shape_ref", J.S("target.chain.svng_sample")),
            ("effects", J.A()));

        [Fact]
        public void ChargesMissingRechargeTime_CaughtBySchemaAlone()
        {
            var report = new SkillWorldBuilder().SkillDef(SkillWithChargesMissingRechargeTime()).Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "charges.recharge_time");
        }

        [Fact]
        public void ChargesMissingRechargeTime_NowRejectedAtBuildTime_NeverReachesCastSkill()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => new SkillWorldBuilder().SkillDef(SkillWithChargesMissingRechargeTime()).Build());
            Assert.Contains("required_field", ex.Message);
        }

        [Fact]
        public void ChargesMaxZero_CaughtByChargesMaxAtLeastOneRule()
        {
            var skill = J.O(
                ("id", J.S(SkillId.Value)),
                ("school", J.S("skill.school_svng")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("charges", J.O(("max", J.N(0)), ("recharge_time", J.N(5)))),
                ("target_shape_ref", J.S("target.chain.svng_sample")),
                ("effects", J.A()));

            // charges.max == 0 结构上仍是合法 Int（0/负数不违反 field_type），schema 本身不会报错——
            // 这条数值范围约束是登记表达不了的业务判断，须显式注册 ChargesMaxAtLeastOneRule。
            var schemaOnly = new SkillWorldBuilder().SkillDef(skill).Validate();
            Assert.False(schemaOnly.IsBlocking, string.Join("; ", schemaOnly.Issues));

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new ChargesMaxAtLeastOneRule())
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "charges_max_invalid");
        }

        [Fact]
        public void WellFormedCharges_StillPasses_Regression()
        {
            var skill = J.O(
                ("id", J.S(SkillId.Value)),
                ("school", J.S("skill.school_svng")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("charges", J.O(("max", J.N(2)), ("recharge_time", J.N(5)))),
                ("target_shape_ref", J.S("target.chain.svng_sample")),
                ("effects", J.A()));

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new ChargesMaxAtLeastOneRule())
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // cost[] 条目缺失/类型错误 —— 全部结构性，改由 SkillSchemas.Def.cost 的 Fields 递归校验
        // 拦下，CostEntryShapeRule 已整条退役，不再需要注册任何规则。
        // -----------------------------------------------------------------

        private static JsonObject SkillWithCostMissingAmount() => J.O(
            ("id", J.S(SkillId.Value)),
            ("school", J.S("skill.school_svng")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("cost", J.A(J.O(("power_type", J.S("arch.power.svng_mana"))))), // 漏填 amount。
            ("target_shape_ref", J.S("target.chain.svng_sample")),
            ("effects", J.A()));

        [Fact]
        public void CostMissingAmount_CaughtBySchemaAlone()
        {
            var report = new SkillWorldBuilder().SkillDef(SkillWithCostMissingAmount()).Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "cost[0].amount");
        }

        [Fact]
        public void CostMissingAmount_NowRejectedAtBuildTime_NeverReachesCastSkill()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => new SkillWorldBuilder().SkillDef(SkillWithCostMissingAmount()).Build());
            Assert.Contains("required_field", ex.Message);
        }

        [Fact]
        public void WellFormedCost_StillPasses_Regression()
        {
            var skill = J.O(
                ("id", J.S(SkillId.Value)),
                ("school", J.S("skill.school_svng")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("cost", J.A(J.O(("power_type", J.S("arch.power.svng_mana")), ("amount", J.N(10))))),
                ("target_shape_ref", J.S("target.chain.svng_sample")),
                ("effects", J.A()));

            var report = new SkillWorldBuilder().SkillDef(skill).Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }
    }
}
