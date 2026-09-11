using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0019 / 04 第 5 节"变体表与原语注册集合一致"落地形式（见 <c>SkillSchemas.cs</c> 类型
    /// 顶部判断记录）：<c>SkillSchemas.Def.effects</c>/<c>AuraDef.effects</c> 两处 <c>VariantSchema</c>
    /// 的 <c>Cases</c> 键集合必须分别与 <see cref="EffectKind"/>/<see cref="AuraEffectKind"/> 的
    /// 全集一致——两者都在代码里（枚举 + 登记），运行期不需要额外校验规则，用测试锁死这条一致性
    /// 即可（新增/遗漏一种原语的变体分支会立即让本测试失败，而不是等到内容作者踩到才发现）。
    /// </summary>
    public sealed class SkillSchemaCoverageTests
    {
        [Fact]
        public void EffectVariantKeys_MatchEffectKindNamesFullSet()
        {
            var effectsField = SkillSchemas.Def.GetField("effects");
            Assert.NotNull(effectsField);
            var variants = effectsField!.Item!.Variants;
            Assert.NotNull(variants);

            var registered = new HashSet<string>(variants!.Cases.Keys, StringComparer.Ordinal);
            var expected = new HashSet<string>(
                ((EffectKind[])Enum.GetValues(typeof(EffectKind))).Select(EffectKindNames.ToText),
                StringComparer.Ordinal);

            Assert.Equal(expected, registered);
            Assert.Equal("kind", variants.Discriminator);
        }

        [Fact]
        public void AuraEffectVariantKeys_MatchAuraEffectKindNamesFullSet()
        {
            var effectsField = SkillSchemas.AuraDef.GetField("effects");
            Assert.NotNull(effectsField);
            var variants = effectsField!.Item!.Variants;
            Assert.NotNull(variants);

            var registered = new HashSet<string>(variants!.Cases.Keys, StringComparer.Ordinal);
            var expected = new HashSet<string>(
                ((AuraEffectKind[])Enum.GetValues(typeof(AuraEffectKind))).Select(AuraEffectKindNames.ToText),
                StringComparer.Ordinal);

            Assert.Equal(expected, registered);
            Assert.Equal("kind", variants.Discriminator);
        }

        /// <summary><c>on_hit_effects</c>（<c>projectile</c> 案例内）与顶层 <c>effects</c> 复用同一份
        /// <see cref="SkillSchemas.EffectsItemSchema"/> 实例（见 04 第 3.2 节"登记一次、多处复用"），
        /// 惰性求值只在首次访问时求值一次并缓存（见 <c>FieldSchema.Item</c> 判断记录）。</summary>
        [Fact]
        public void ProjectileOnHitEffects_ReusesSameEffectsItemSchemaInstance()
        {
            var effectsField = SkillSchemas.Def.GetField("effects");
            var projectileParams = effectsField!.Item!.Variants!.Cases[EffectKindNames.ToText(EffectKind.Projectile)]
                .Single(f => f.Name == "params").Fields!
                .Single(f => f.Name == "on_hit_effects");

            Assert.Same(SkillSchemas.EffectsItemSchema, projectileParams.Item);
        }

        /// <summary>P3-04 根治：<c>periodic_damage</c>/<c>periodic_heal</c> 的 <c>params.scaling_stat</c>
        /// 登记为 <c>Reference(stat.definition)</c>、非必填——与非周期的 <c>school_damage</c>/
        /// <c>heal</c>（<c>SkillSchemas.cs</c> 里的 <c>DamageOrHealParams</c>）保持一致，两者共用
        /// 同一条 <c>EffectDispatcher.ApplyDamageOrHeal</c> 结算路径（见 <see
        /// cref="Tests.Rules.Skill.AuraEffectTests.PeriodicDamage_WithScalingStat_ScalesBySourceStat"/>
        /// 端到端验证运行期确实消费这个字段）。</summary>
        [Theory]
        [InlineData("periodic_damage")]
        [InlineData("periodic_heal")]
        public void PeriodicEffectParams_DeclareOptionalScalingStatReference(string kind)
        {
            var effectsField = SkillSchemas.AuraDef.GetField("effects");
            var paramsFields = effectsField!.Item!.Variants!.Cases[kind].Single(f => f.Name == "params").Fields!;

            var scalingStat = paramsFields.SingleOrDefault(f => f.Name == "scaling_stat");

            Assert.NotNull(scalingStat);
            Assert.False(scalingStat!.Required);
            Assert.Equal(FieldKind.Reference, scalingStat.Kind);
            Assert.Equal("stat.definition", scalingStat.ReferenceTable);
        }

        [Fact]
        public void AllEffectKindValues_ContainsExactly19Names()
        {
            Assert.Equal(19, SkillSchemas.AllEffectKindValues.Length);
            Assert.Equal(19, SkillSchemas.AllEffectKindValues.Distinct(StringComparer.Ordinal).Count());
        }

        /// <summary>消费方反馈第 37 条：<c>skill.proc_def.trigger_skill</c> 此前只在
        /// <c>RulesSchemaCatalog.DeclareKnownReferences</c> 登记了加载期硬校验（
        /// <c>DeclareReference("skill.proc_def", "trigger_skill", "skill.def")</c>），字段元数据一侧
        /// 未同步登记 <see cref="FieldSchema.SoftReferenceTable"/>，导致内容工具（编辑器 P3.1 引用图）
        /// 只能按值弱推断该字段是否是引用。本测试锁死字段元数据一侧已同步补齐（同 29/30 号反馈同类
        /// 修复的验收口径）。</summary>
        [Fact]
        public void ProcDef_TriggerSkill_DeclaresSoftReferenceToSkillDef()
        {
            var field = SkillSchemas.ProcDef.GetField("trigger_skill");
            Assert.NotNull(field);
            Assert.Equal(FieldKind.Id, field!.Kind);
            Assert.Equal("skill.def", field.SoftReferenceTable);
            Assert.Null(field.SoftReferenceDomain);
        }
    }
}
