using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>SpellMod 修正聚合顺序（先 Σflat 再 ×(1+Σpct)）与 <c>affects</c> 过滤三维度（见落地
    /// 方案 T2-6 行"法术修饰叠加顺序……affects 过滤"、06 第 3.5 节）。直接构造
    /// <see cref="SkillDefCache"/>/<see cref="AuraHost"/>/<see cref="SpellModResolver"/> 三者
    /// （而不经 <see cref="SkillHost"/>），因为 <see cref="SpellModResolver.Apply"/> 的
    /// <c>tags</c> 参数是本模块唯一能测试标签过滤维度的入口（见 README 判断记录 13："经
    /// <c>EffectContext</c> 落地的调用点不传标签"）。</summary>
    public sealed class SpellModTests
    {
        private static Core.Foundation.Common.Json.JsonObject SpellMod(
            string id, string dimension, string op, double value,
            string[]? schools = null, string[]? tags = null, string[]? skillIds = null)
        {
            var fields = new List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("target_dimension", J.S(dimension)),
                ("op", J.S(op)),
                ("value", J.N(value)),
            };

            var affectsFields = new List<(string, Core.Foundation.Common.Json.JsonValue)>();
            if (schools != null) affectsFields.Add(("schools", J.Ids(schools)));
            if (tags != null) affectsFields.Add(("tags", J.Ids(tags)));
            if (skillIds != null) affectsFields.Add(("skill_ids", J.Ids(skillIds)));
            if (affectsFields.Count > 0)
            {
                fields.Add(("affects", J.O(affectsFields.ToArray())));
            }

            return J.O(fields.ToArray());
        }

        private static Core.Foundation.Common.Json.JsonObject AuraWithMods(string id, params string[] spellModRefs)
        {
            var effects = new Core.Foundation.Common.Json.JsonValue[spellModRefs.Length];
            for (var i = 0; i < spellModRefs.Length; i++)
            {
                effects[i] = J.O(("kind", J.S("spell_mod")), ("params", J.O(("spell_mod_ref", J.S(spellModRefs[i])))));
            }

            return J.O(("id", J.S(id)), ("duration", J.N(30)), ("effects", J.A(effects)));
        }

        private static SpellModResolver BuildResolver(SkillWorld world, out AuraHost auraHost)
        {
            var defs = new SkillDefCache(world.Registry);
            auraHost = new AuraHost(defs, world.Stats, world.Bus, world.Options, world.Diagnostics);
            return new SpellModResolver(defs, auraHost);
        }

        [Theory]
        [InlineData("cast_time")]
        [InlineData("cost")]
        [InlineData("cooldown")]
        [InlineData("effect_value")]
        public void FlatThenPct_MatchesHandCalculation(string dimensionText)
        {
            var dimension = dimensionText switch
            {
                "cast_time" => SpellModDimension.CastTime,
                "cost" => SpellModDimension.Cost,
                "cooldown" => SpellModDimension.Cooldown,
                _ => SpellModDimension.EffectValue,
            };

            var flatMod = SpellMod("skill.spell_mod_def.sample_flat", dimensionText, "flat", -2);
            var pctMod = SpellMod("skill.spell_mod_def.sample_pct", dimensionText, "pct", 0.5);
            var aura = AuraWithMods("skill.aura_def.sample_mods", "skill.spell_mod_def.sample_flat", "skill.spell_mod_def.sample_pct");

            var world = new SkillWorldBuilder().SpellModDef(flatMod).SpellModDef(pctMod).AuraDef(aura).Build();
            world.AddUnit(new Id("unit.caster"));

            var resolver = BuildResolver(world, out var auraHost);
            auraHost.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_mods"), new Id("unit.caster"), null);

            var result = resolver.Apply(new Id("unit.caster"), dimension, new Id("skill.sample_bolt"), new Id("skill.school_sample"),
                System.Array.Empty<Id>(), baseValue: 10);

            // 手算：先 Σflat = 10 + (-2) = 8；再 ×(1+Σpct) = 8 × 1.5 = 12。
            Assert.Equal(12, result, 6);
        }

        [Fact]
        public void Affects_FiltersBySchool()
        {
            AssertAffectsFilter(schools: new[] { "skill.school_sample" }, matchingSchool: "skill.school_sample", expectMatch: true);
            AssertAffectsFilter(schools: new[] { "skill.school_other" }, matchingSchool: "skill.school_sample", expectMatch: false);
        }

        [Fact]
        public void Affects_FiltersByTag()
        {
            AssertAffectsFilter(tags: new[] { "skill.tag.sample_burn" }, matchingTags: new[] { new Id("skill.tag.sample_burn") }, expectMatch: true);
            AssertAffectsFilter(tags: new[] { "skill.tag.sample_burn" }, matchingTags: new[] { new Id("skill.tag.sample_other") }, expectMatch: false);
        }

        [Fact]
        public void Affects_FiltersBySkillId()
        {
            AssertAffectsFilter(skillIds: new[] { "skill.sample_bolt" }, matchingSkillId: "skill.sample_bolt", expectMatch: true);
            AssertAffectsFilter(skillIds: new[] { "skill.sample_bolt" }, matchingSkillId: "skill.sample_other", expectMatch: false);
        }

        private static void AssertAffectsFilter(
            string[]? schools = null, string[]? tags = null, string[]? skillIds = null,
            string matchingSchool = "skill.school_unrelated", string matchingSkillId = "skill.sample_unrelated",
            IReadOnlyList<Id>? matchingTags = null, bool expectMatch = true)
        {
            var mod = SpellMod("skill.spell_mod_def.sample_filter", "effect_value", "flat", 100, schools, tags, skillIds);
            var aura = AuraWithMods("skill.aura_def.sample_filter_aura", "skill.spell_mod_def.sample_filter");

            var world = new SkillWorldBuilder().SpellModDef(mod).AuraDef(aura).Build();
            world.AddUnit(new Id("unit.caster"));

            var resolver = BuildResolver(world, out var auraHost);
            auraHost.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_filter_aura"), new Id("unit.caster"), null);

            var result = resolver.Apply(
                new Id("unit.caster"), SpellModDimension.EffectValue, new Id(matchingSkillId), new Id(matchingSchool),
                matchingTags ?? System.Array.Empty<Id>(), baseValue: 10);

            Assert.Equal(expectMatch ? 110 : 10, result, 6);
        }
    }
}
