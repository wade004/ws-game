using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// R09 收口（外部审计 5e779c6，P2）：<c>SkillValidationRules.cs</c> 对 <c>skill.def</c>/
    /// <c>skill.aura_def</c> 的嵌套结构（<c>effects[]</c>/<c>charges</c>/<c>cost[]</c>）校验此前
    /// 存在缺口——能通过 <c>DataRegistry.LoadAll()</c>（0 error），但
    /// <c>SkillDefCache.ParseSkillDef</c>/<c>ParseAuraDef</c>（懒解析，只在这个技能/光环第一次真正
    /// 被解析——通常就是第一次被施放——时才跑）对这些嵌套结构做的是无防御直接类型转换，会抛
    /// <see cref="InvalidCastException"/>/<see cref="System.Collections.Generic.KeyNotFoundException"/>。
    /// 本文件的每个"未修复前会怎样"用例都先用 <see cref="SkillDefCache"/> 独立证实"这份数据确实会
    /// 在解析时崩溃"（不依赖假设，真实复现该异常），再证实修复后的校验规则能在加载阶段就拦下同一份
    /// 数据。
    /// </summary>
    public sealed class SkillValidationNestedGapTests
    {
        private static readonly Id SkillId = new Id("skill.svng_sample");

        // -----------------------------------------------------------------
        // 主复现：effects[] 条目缺少 "kind" 字段（EffectKindRegisteredRule 此前的判断条件
        // `effects[i] is JsonObject obj && obj.TryGetValue("kind", ...) && kindVal is JsonString`
        // 只要有一环不成立就整体短路为 false，不产生任何校验问题——缺 "kind" 正是这样一种"三环都
        // 不满足"的坏形状）。
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
        public void EffectMissingKind_ParsesSuccessfully_UnvalidatedRegistry_ButThrowsAtSkillDefCacheResolution()
        {
            // 不经过 RulesSchemaCatalog 的完整规则集——只走 SkillWorldBuilder.Validate() 默认注册的
            // schema 本身（无本条新增规则），证实这份数据确实能通过"只做 schema 层校验"这一关。
            var report = new SkillWorldBuilder().SkillDef(SkillWithEffectMissingKind()).Validate();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Severity == Core.Foundation.DataRegistry.ValidationSeverity.Error);

            // 真实复现：SkillDefCache 第一次解析这个技能（对应"第一次被施放"）时崩溃，不是假设。
            var world = new SkillWorldBuilder().SkillDef(SkillWithEffectMissingKind()).Build();
            world.AddUnit(new Id("unit.svng_caster"));
            world.Targets.SetChain(new Id("target.chain.svng_sample"), new Id("unit.svng_caster"));

            Assert.ThrowsAny<Exception>(() => world.Host.CastSkill(
                new Id("unit.svng_caster"), SkillId, Array.Empty<Id>()));
        }

        [Fact]
        public void EffectMissingKind_CaughtByValidationRule_WhenEffectKindRegisteredRuleRegistered()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(SkillWithEffectMissingKind())
                .ValidationRule(new EffectKindRegisteredRule())
                .Validate();

            Assert.True(report.IsBlocking);
            var issue = report.Issues.Single(i => i.Check == "effect_kind_missing");
            Assert.Equal(Core.Foundation.DataRegistry.ValidationSeverity.Error, issue.Severity);
            Assert.Equal("skill.def", issue.Table);
        }

        [Fact]
        public void EffectEntryNotObject_CaughtByValidationRule()
        {
            var skill = J.O(
                ("id", J.S(SkillId.Value)),
                ("school", J.S("skill.school_svng")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.svng_sample")),
                // 一个裸数字混进了 effects 数组——同样是"is JsonObject"就短路失败的坏形状。
                ("effects", J.A(J.N(123))));

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new EffectKindRegisteredRule())
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "effect_entry_not_object");
        }

        [Fact]
        public void EffectKindNotString_CaughtByValidationRule()
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

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new EffectKindRegisteredRule())
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "effect_kind_not_string");
        }

        [Fact]
        public void AuraEffectMissingKind_CaughtByValidationRule()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.svng_sample")),
                ("duration", J.N(6)),
                ("effects", J.A(J.O(("params", J.O(("base_value", J.N(3))))))));

            var report = new SkillWorldBuilder()
                .AuraDef(aura)
                .ValidationRule(new EffectKindRegisteredRule())
                .Validate();

            Assert.True(report.IsBlocking);
            var issue = report.Issues.Single(i => i.Check == "effect_kind_missing");
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

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new EffectKindRegisteredRule())
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // charges 子字段缺失/类型错误（见 ChargesShapeRule 判断记录）。
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
        public void ChargesMissingRechargeTime_PassesSchemaOnly_ButThrowsAtSkillDefCacheResolution()
        {
            var report = new SkillWorldBuilder().SkillDef(SkillWithChargesMissingRechargeTime()).Validate();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new SkillWorldBuilder().SkillDef(SkillWithChargesMissingRechargeTime()).Build();
            world.AddUnit(new Id("unit.svng_caster2"));
            world.Targets.SetChain(new Id("target.chain.svng_sample"), new Id("unit.svng_caster2"));

            Assert.ThrowsAny<Exception>(() => world.Host.CastSkill(
                new Id("unit.svng_caster2"), SkillId, Array.Empty<Id>()));
        }

        [Fact]
        public void ChargesMissingRechargeTime_CaughtByChargesShapeRule()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(SkillWithChargesMissingRechargeTime())
                .ValidationRule(new ChargesShapeRule())
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "charges_recharge_time_missing");
        }

        [Fact]
        public void ChargesMaxZero_CaughtByChargesShapeRule()
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

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new ChargesShapeRule())
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
                .ValidationRule(new ChargesShapeRule())
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // cost[] 条目缺失/类型错误（见 CostEntryShapeRule 判断记录）。
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
        public void CostMissingAmount_PassesSchemaOnly_ButThrowsAtSkillDefCacheResolution()
        {
            var report = new SkillWorldBuilder().SkillDef(SkillWithCostMissingAmount()).Validate();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new SkillWorldBuilder().SkillDef(SkillWithCostMissingAmount()).Build();
            world.AddUnit(new Id("unit.svng_caster3"));
            world.Targets.SetChain(new Id("target.chain.svng_sample"), new Id("unit.svng_caster3"));

            Assert.ThrowsAny<Exception>(() => world.Host.CastSkill(
                new Id("unit.svng_caster3"), SkillId, Array.Empty<Id>()));
        }

        [Fact]
        public void CostMissingAmount_CaughtByCostEntryShapeRule()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(SkillWithCostMissingAmount())
                .ValidationRule(new CostEntryShapeRule())
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "cost_amount_invalid");
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

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new CostEntryShapeRule())
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }
    }
}
