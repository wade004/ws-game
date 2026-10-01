using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Archetype;
using Xunit;

namespace Tests.Numbers.Archetype
{
    /// <summary>
    /// archetype 契约类型与校验规则元数据的直接用例（T-L4，2026-10-01 测试覆盖第四批）：
    /// <see cref="TalentNode.Cost"/>/<see cref="TalentNode.Grants"/> 的解析（含缺省值）、
    /// <see cref="AppliedArchetype"/> 的字段原样透传，以及三条 arch 规则的 <c>RuleId</c>/<c>DefaultSeverity</c>/<c>NonEscalatable</c>。
    /// </summary>
    public sealed class ArchetypeContractCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private const string StatDefinitionRows = @"[
            { ""id"": ""stat.strength"", ""name_key"": ""l10n.stat.strength.name"", ""group"": ""primary"" }
        ]";

        private const string ClassRows = @"[
            {
                ""id"": ""arch.class.sample_a"",
                ""name_key"": ""l10n.arch.class.sample_a.name"",
                ""primary_stat"": ""stat.strength"",
                ""base_stats"": { ""stat.strength"": 10 },
                ""power_types"": [],
                ""talent_tree_ref"": ""arch.talent_tree.sample_tree""
            }
        ]";

        private const string RaceRows = @"[
            { ""id"": ""arch.race.sample_x"", ""name_key"": ""l10n.arch.race.sample_x.name"", ""stat_mods"": {}, ""passive_auras"": [] }
        ]";

        private static ArchetypeRegistry BuildHost(string talentTreeRows)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
                new EventDefinition(ArchetypeEventKeys.Applied, "archetype", new[] { "unitId", "classId", "raceId" }),
            }));
            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.class", Envelope("arch.class", ClassRows))
                .Add("arch.race", Envelope("arch.race", RaceRows))
                .Add("arch.talent_tree", Envelope("arch.talent_tree", talentTreeRows));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(ArchSchemas.Class);
            registry.RegisterSchema(ArchSchemas.Race);
            registry.RegisterSchema(ArchSchemas.TalentTree);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return new ArchetypeRegistry(
                registry, bus, (u, s, v) => { }, (u, s, o, v, src) => { }, (u, p) => { });
        }

        // -----------------------------------------------------------------
        // TalentNode.Cost / Grants
        // -----------------------------------------------------------------

        [Fact]
        public void TalentNode_CostAndGrants_AreParsedFromTheRow()
        {
            var host = BuildHost(@"[
                {
                    ""id"": ""arch.talent_tree.sample_tree"",
                    ""nodes"": [
                        { ""id"": ""root"", ""prerequisites"": [], ""cost"": 3, ""grants"": { ""skill"": ""skill.sample"", ""bonus"": 2 } },
                        { ""id"": ""leaf"", ""prerequisites"": [""root""], ""cost"": 7, ""grants"": {} }
                    ]
                }
            ]");

            var tree = host.GetTalentTree(new Id("arch.talent_tree.sample_tree"));

            Assert.NotNull(tree);
            var root = tree!.Nodes[0];
            Assert.Equal(3, root.Cost);
            Assert.Equal(2, root.Grants.Count);
            Assert.True(root.Grants.TryGetValue("skill", out var skill));
            Assert.Equal("skill.sample", Assert.IsType<JsonString>(skill).Value);
            Assert.True(root.Grants.TryGetValue("bonus", out var bonus));
            Assert.True(Assert.IsType<JsonNumber>(bonus).TryGetInt64(out var bonusValue));
            Assert.Equal(2, bonusValue);

            var leaf = tree.Nodes[1];
            Assert.Equal(7, leaf.Cost);
            Assert.Empty(leaf.Grants);
        }

        [Fact]
        public void TalentNode_MissingCostAndGrants_DefaultToZeroAndEmpty()
        {
            var host = BuildHost(@"[
                {
                    ""id"": ""arch.talent_tree.sample_tree"",
                    ""nodes"": [ { ""id"": ""root"", ""prerequisites"": [] } ]
                }
            ]");

            var node = host.GetTalentTree(new Id("arch.talent_tree.sample_tree"))!.Nodes[0];

            Assert.Equal(0, node.Cost);
            Assert.NotNull(node.Grants);
            Assert.Empty(node.Grants);
        }

        [Fact]
        public void TalentNode_Constructor_ExposesFieldsVerbatim()
        {
            var grants = new JsonObjectBuilder().Build();
            var prerequisites = new List<string> { "a", "b" };

            var node = new TalentNode("n", prerequisites, 5, grants);

            Assert.Equal("n", node.NodeId);
            Assert.Same(prerequisites, node.Prerequisites);
            Assert.Equal(5, node.Cost);
            Assert.Same(grants, node.Grants);
        }

        // -----------------------------------------------------------------
        // AppliedArchetype
        // -----------------------------------------------------------------

        [Fact]
        public void AppliedArchetype_Constructor_ExposesIdsVerbatim()
        {
            var cls = new Id("arch.class.sample_a");
            var race = new Id("arch.race.sample_x");
            var curve = new Id("prog.curve.sample");

            var applied = new AppliedArchetype(cls, race, curve);

            Assert.Equal(cls, applied.ClassId);
            Assert.Equal(race, applied.RaceId);
            Assert.Equal(curve, applied.LevelCurveRef);
        }

        [Fact]
        public void AppliedArchetype_OptionalRaceAndCurve_StayNull()
        {
            var applied = new AppliedArchetype(new Id("arch.class.sample_a"), null, null);

            Assert.Null(applied.RaceId);
            Assert.Null(applied.LevelCurveRef);
        }

        [Fact]
        public void ApplyTo_WithoutRaceAndWithoutLevelCurve_ReturnsNullOptionals()
        {
            var host = BuildHost(@"[
                { ""id"": ""arch.talent_tree.sample_tree"", ""nodes"": [ { ""id"": ""root"", ""prerequisites"": [] } ] }
            ]");

            var applied = host.ApplyTo(new Id("unit.cov_l4"), new Id("arch.class.sample_a"), null);

            Assert.Equal("arch.class.sample_a", applied.ClassId.Value);
            Assert.Null(applied.RaceId);
            Assert.Null(applied.LevelCurveRef);
        }

        // -----------------------------------------------------------------
        // 规则元数据
        // -----------------------------------------------------------------

        [Fact]
        public void ArchRules_RuleId_IsTheConcreteTypeName()
        {
            Assert.Equal(nameof(ArchClassDerivationOverrideValidationRule), ((IValidationRule)new ArchClassDerivationOverrideValidationRule()).RuleId);
            Assert.Equal(nameof(ArchTalentTreeCycleValidationRule), ((IValidationRule)new ArchTalentTreeCycleValidationRule()).RuleId);
            Assert.Equal(nameof(TalentTreeIsolationRule), ((IValidationRule)new TalentTreeIsolationRule()).RuleId);
        }

        [Fact]
        public void ArchRules_DefaultSeverityAndEscalation_MatchTheirDocumentedGrade()
        {
            IValidationRule isolation = new TalentTreeIsolationRule();
            Assert.Equal(ValidationSeverity.Warning, isolation.DefaultSeverity);
            Assert.True(isolation.NonEscalatable);

            foreach (var rule in new IValidationRule[]
            {
                new ArchClassDerivationOverrideValidationRule(),
                new ArchTalentTreeCycleValidationRule(),
            })
            {
                Assert.Equal(ValidationSeverity.Error, rule.DefaultSeverity);
                Assert.False(rule.NonEscalatable);
            }
        }
    }
}
