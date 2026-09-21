using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 消费方反馈第 4 条（2026-09-21，ADR-0056）：<c>skill.aura_def.name_key</c> 落地验收——覆盖
    /// <see cref="SkillDefCache"/> 对该新增可选字段的解析（有/无该字段两种数据行均合法，不提
    /// schema_version），惯例同 <c>SkillDefNameKeyTests</c>（<c>skill.def.name_key</c>，ADR-0048）。
    /// <see cref="Core.Rules.Skill.AuraHost.GetActiveAuraSnapshots"/> 端到端转发该字段的验收见
    /// <c>AuraSnapshotTests.NameKey_ResolvesFromAuraDef_WhenRegistered_NullWhenFieldOmitted_NoDiagnosticEitherWay</c>，
    /// 两者各自负责一段，不重复验证同一件事。
    /// </summary>
    public sealed class AuraDefNameKeyTests
    {
        private static JsonObject Aura(string id, string? nameKey)
        {
            var builder = new JsonObjectBuilder();
            builder.Add("id", J.S(id));
            builder.Add("max_stacks", J.N(1));
            builder.Add("duration", J.N(5.0));
            if (nameKey != null)
            {
                builder.Add("name_key", J.S(nameKey));
            }
            builder.Add("effects", J.A());
            return builder.Build();
        }

        [Fact]
        public void SkillDefCache_ParsesAuraNameKey_WhenFieldPresent()
        {
            var world = new SkillWorldBuilder()
                .AuraDef(Aura("skill.aura_def.name_key_test.with_key", "l10n.aura.name_key_test.with_key.name"))
                .Build();
            var cache = new SkillDefCache(world.Registry);

            Assert.True(cache.TryGetAuraDef(new Id("skill.aura_def.name_key_test.with_key"), out var def));
            Assert.Equal(new Id("l10n.aura.name_key_test.with_key.name"), def.NameKey);
        }

        [Fact]
        public void SkillDefCache_AuraNameKeyIsNull_WhenFieldAbsent()
        {
            var world = new SkillWorldBuilder()
                .AuraDef(Aura("skill.aura_def.name_key_test.without_key", nameKey: null))
                .Build();
            var cache = new SkillDefCache(world.Registry);

            Assert.True(cache.TryGetAuraDef(new Id("skill.aura_def.name_key_test.without_key"), out var def));
            Assert.Null(def.NameKey);
        }
    }
}
