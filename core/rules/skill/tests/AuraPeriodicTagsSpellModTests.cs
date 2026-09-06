using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// W1 收边补齐（A3 审计 #9）：<c>AuraHost.ApplyAura</c> 新增可选 <c>tags</c> 参数
    /// （非 <see cref="IEffectSink"/> 接口本身——扩大公开契约会波及 <c>core/gameplay</c> 等其它实现方，
    /// 见 <see cref="IEffectSink"/> 判断记录；标签透传只发生在
    /// <c>EffectDispatcher.ApplyAuraEffectPrimitive</c> 直接调用 <c>AuraHost.ApplyAura</c> 具体类型
    /// 重载这一条内部路径），周期性光环效果（<c>periodic_damage</c>）触发时构造的
    /// <see cref="EffectContext"/> 会带上施加该光环实例时的 <c>skill.def.tags</c>——此前恒不传
    /// （默认空列表），<c>effect_value</c>/<c>crit_chance</c> 维度的 <c>SpellMod</c> 标签过滤对周期
    /// 效果永远命中不到。本文件经真实施法路径（<c>CastSkill</c> → <c>apply_aura</c> 效果原语）验证。
    /// </summary>
    public sealed class AuraPeriodicTagsSpellModTests
    {
        private static readonly Id Source = new Id("unit.source");
        private static readonly Id Target = new Id("unit.target");
        private static readonly Id BurnTag = new Id("skill.tag.sample_burn");
        private static readonly Id DotAuraDef = new Id("skill.aura_def.sample_dot");

        private static Core.Foundation.Common.Json.JsonObject DotAuraDefJson() => J.O(
            ("id", J.S(DotAuraDef.Value)),
            ("duration", J.N(6)),
            ("effects", J.A(
                J.O(("kind", J.S("periodic_damage")),
                    ("params", J.O(
                        ("interval", J.N(2)),
                        ("base_value", J.N(3)),
                        ("coefficient", J.N(0)),
                        ("school", J.S("skill.school_sample"))))))));

        /// <summary>施放本技能会经 <c>apply_aura</c> 效果原语把 <see cref="DotAuraDef"/> 施加到目标身上，
        /// 技能自身声明 <paramref name="tags"/>（06 第 3.1 节 <c>skill.def.tags</c>），
        /// <c>CastPipeline.ExecuteEffectsOnly</c> 会把它塞进 <see cref="EffectContext.Tags"/>。</summary>
        private static Core.Foundation.Common.Json.JsonObject DotBoltSkillJson(params string[] tags) => J.O(
            ("id", J.S("skill.sample_dot_bolt")),
            ("school", J.S("skill.school_sample")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("tags", J.Ids(tags)),
            ("target_shape_ref", J.S("target.chain.sample")),
            ("effects", J.A(
                J.O(("kind", J.S("apply_aura")),
                    ("params", J.O(("aura_def", J.S(DotAuraDef.Value))))))));

        private static Core.Foundation.Common.Json.JsonObject TagFilteredSpellMod() => J.O(
            ("id", J.S("skill.spell_mod_def.tag_boost")),
            ("target_dimension", J.S("effect_value")),
            ("op", J.S("flat")),
            ("value", J.N(100)),
            ("affects", J.O(("tags", J.Ids(BurnTag.Value)))));

        private static Core.Foundation.Common.Json.JsonObject SpellModGrantingAura() => J.O(
            ("id", J.S("skill.aura_def.sample_mods")),
            ("duration", J.N(30)),
            ("effects", J.A(
                J.O(("kind", J.S("spell_mod")),
                    ("params", J.O(("spell_mod_ref", J.S("skill.spell_mod_def.tag_boost"))))))));

        [Fact]
        public void PeriodicTick_FromSkillWithMatchingTag_AppliesTagFilteredSpellMod()
        {
            var world = new SkillWorldBuilder()
                .SkillDef(DotBoltSkillJson(BurnTag.Value))
                .AuraDef(DotAuraDefJson())
                .AuraDef(SpellModGrantingAura())
                .SpellModDef(TagFilteredSpellMod())
                .Build();

            world.AddUnit(Source);
            world.AddUnit(Target);
            world.Targets.SetChain(new Id("target.chain.sample"), Target);

            // SpellMod 挂在施法者（伤害来源）身上——SpellModResolver.Apply 按 context.SourceId 查询。
            world.Host.EffectSink.ApplyAura(Source, new Id("skill.aura_def.sample_mods"), Source);

            var cast = world.Host.CastSkill(Source, new Id("skill.sample_dot_bolt"), System.Array.Empty<Id>());
            Assert.True(cast.Success);

            world.Host.Update(2.0);

            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.True(call.IsPeriodic);
            // 未修饰基础值 3 + flat 100 = 103（(3+100)*(1+0)）。
            Assert.Equal(103, call.BaseValue, 6);
            Assert.Contains(BurnTag, call.Tags.ToList());
        }

        [Fact]
        public void PeriodicTick_FromSkillWithoutTags_SpellModDoesNotApply_DegradesToUnmodifiedValue()
        {
            var world = new SkillWorldBuilder()
                .SkillDef(DotBoltSkillJson()) // 不带任何 tags。
                .AuraDef(DotAuraDefJson())
                .AuraDef(SpellModGrantingAura())
                .SpellModDef(TagFilteredSpellMod())
                .Build();

            world.AddUnit(Source);
            world.AddUnit(Target);
            world.Targets.SetChain(new Id("target.chain.sample"), Target);

            world.Host.EffectSink.ApplyAura(Source, new Id("skill.aura_def.sample_mods"), Source);

            var cast = world.Host.CastSkill(Source, new Id("skill.sample_dot_bolt"), System.Array.Empty<Id>());
            Assert.True(cast.Success);

            world.Host.Update(2.0);

            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(3, call.BaseValue, 6);
        }

        [Fact]
        public void PeriodicTick_ViaIEffectSinkApplyAura_StillDegradesToEmptyTags()
        {
            // 对照组：经 IEffectSink.ApplyAura（接口本身，不带 tags 参数，见该接口判断记录——
            // 装备特效授予/种族被动光环等场景走这条路径）施加的周期性光环，行为与本次改动之前
            // 完全一致——恒空标签，标签过滤的 SpellMod 不生效。
            var world = new SkillWorldBuilder()
                .AuraDef(DotAuraDefJson())
                .AuraDef(SpellModGrantingAura())
                .SpellModDef(TagFilteredSpellMod())
                .Build();

            world.AddUnit(Source);
            world.AddUnit(Target);

            world.Host.EffectSink.ApplyAura(Source, new Id("skill.aura_def.sample_mods"), Source);
            world.Host.EffectSink.ApplyAura(Target, DotAuraDef, Source); // 经接口本身，不带 tags。

            world.Host.Update(2.0);

            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(3, call.BaseValue, 6);
            Assert.Empty(call.Tags);
        }
    }
}
