using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Rng;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 消费方反馈第 3 条（2026-09-20，[ADR-0048](../../../../architecture/adr/0048-任务起始方式补与场景物件交互取值.md)）：
    /// <c>skill.def.name_key</c> 落地验收——覆盖 <see cref="SkillDefCache"/> 解析、<see cref="SkillHost.GetSkillNameKey"/>
    /// 转发、有/无该字段两种既有+新增数据行的行为均不受影响。见 <c>core/rules/skill/README.md</c>
    /// 同名判断记录。
    /// </summary>
    public sealed class SkillDefNameKeyTests
    {
        private static readonly Id SkillWithNameKey = new Id("skill.name_key_test.with_key");
        private static readonly Id SkillWithoutNameKey = new Id("skill.name_key_test.without_key");
        private static readonly Id ChainId = new Id("target.chain.name_key_test");

        private static JsonObject Def(Id id, string? nameKey)
        {
            var builder = new JsonObjectBuilder();
            builder.Add("id", J.S(id.Value));
            if (nameKey != null)
            {
                builder.Add("name_key", J.S(nameKey));
            }
            builder.Add("school", J.S("skill.school.name_key_test"));
            builder.Add("kind", J.S("active"));
            builder.Add("range", J.N(0));
            builder.Add("cast_time", J.N(0));
            builder.Add("respects_gcd", J.B(false));
            builder.Add("target_shape_ref", J.S(ChainId.Value));
            builder.Add("effects", J.A(J.O(
                ("kind", J.S("school_damage")),
                ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0)))))));
            return builder.Build();
        }

        private static string Envelope(params JsonObject[] rows) =>
            JsonWriter.Write(J.O(("table", J.S("skill.def")), ("schema_version", J.N(1)), ("rows", J.A(rows))));

        private static string EmptyTableEnvelope(string table) =>
            JsonWriter.Write(J.O(("table", J.S(table)), ("schema_version", J.N(1)), ("rows", J.A())));

        private sealed class Harness
        {
            public IDataRegistry Registry = default!;
            public SkillHost Host = default!;
        }

        private static Harness Build()
        {
            var source = new InMemoryDataSource()
                .Add("skill.def", Envelope(
                    Def(SkillWithNameKey, nameKey: "l10n.skill.name_key_test.with_key.name"),
                    Def(SkillWithoutNameKey, nameKey: null)))
                .Add("stat.definition", EmptyTableEnvelope("stat.definition"));

            var bus = SkillWorldBuilder.CreateBus();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(SkillSchemas.Def);
            registry.RegisterSchema(SkillSchemas.AuraDef);
            registry.RegisterSchema(SkillSchemas.ProcDef);
            registry.RegisterSchema(SkillSchemas.SpellModDef);
            registry.RegisterSchema(SkillSchemas.Book);
            registry.RegisterSchema(StatSchemas.Definition);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var statsBus = bus;
            var stats = new StatHost(registry, statsBus);
            var powers = new PowerHost(Array.Empty<PowerTypeDefinition>(), statsBus);
            var rng = new RngHost(1);
            var units = new FakeUnitAccess();
            var combat = new FakeCombatHost();
            var targets = new FakeTargetHost();
            var exprs = new FakeExprHostFactory();
            var diagnostics = new InMemorySkillDiagnostics();

            var host = new SkillHost(
                registry, statsBus, units, stats, powers, rng, combat, targets, exprs, spatialQuery: null,
                diagnostics: diagnostics);

            return new Harness { Registry = registry, Host = host };
        }

        [Fact]
        public void SkillDefCache_ParsesNameKey_WhenFieldPresent()
        {
            var world = Build();
            var cache = new SkillDefCache(world.Registry);

            Assert.True(cache.TryGetSkillDef(SkillWithNameKey, out var def));
            Assert.Equal(new Id("l10n.skill.name_key_test.with_key.name"), def.NameKey);
        }

        [Fact]
        public void SkillDefCache_NameKeyIsNull_WhenFieldAbsent()
        {
            var world = Build();
            var cache = new SkillDefCache(world.Registry);

            Assert.True(cache.TryGetSkillDef(SkillWithoutNameKey, out var def));
            Assert.Null(def.NameKey);
        }

        [Fact]
        public void SkillHost_GetSkillNameKey_ForwardsParsedValue()
        {
            var world = Build();

            Assert.Equal(new Id("l10n.skill.name_key_test.with_key.name"), world.Host.GetSkillNameKey(SkillWithNameKey));
            Assert.Null(world.Host.GetSkillNameKey(SkillWithoutNameKey));
        }

        [Fact]
        public void SkillHost_GetSkillNameKey_UnknownSkill_ReturnsNull()
        {
            var world = Build();

            Assert.Null(world.Host.GetSkillNameKey(new Id("skill.name_key_test.does_not_exist")));
        }
    }
}
