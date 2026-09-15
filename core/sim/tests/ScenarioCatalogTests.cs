using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Sim
{
    /// <summary>T-N6-2a：<see cref="Core.Sim.ScenarioCatalog"/> 的类型化读取（任务书验收 6）。</summary>
    public sealed class ScenarioCatalogTests
    {
        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        private static TableSchema StubTable(string name) =>
            new TableSchema(name, "id", 1, new[] { new FieldSchema("id", FieldKind.Id, required: true) });

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static DataRegistry BuildRegistryWithScenarios(string scenarioRowsJson)
        {
            var source = new InMemoryDataSource()
                .Add("sim.scenario", Envelope("sim.scenario", scenarioRowsJson))
                .Add("arch.class", Envelope("arch.class", "[{\"id\": \"arch.class.a\"}]"))
                .Add("creature.template", Envelope("creature.template", "[{\"id\": \"creature.beast\"}]"))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", "[{\"id\": \"creature.tier.normal\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition", "[{\"id\": \"item.quality.rare\"}]"))
                .Add("arch.race", Envelope("arch.race", "[{\"id\": \"arch.race.human\"}]"));

            var registry = new DataRegistry(source, MakeBus());
            Core.Sim.SimSchemaCatalog.RegisterAll(registry);
            registry.RegisterSchema(StubTable("arch.class"));
            registry.RegisterSchema(StubTable("creature.template"));
            registry.RegisterSchema(StubTable("creature.tier_definition"));
            registry.RegisterSchema(StubTable("item.quality_definition"));
            registry.RegisterSchema(StubTable("arch.race"));

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        private const string ArenaRow =
            "{\"id\": \"sim.scenario.arena_sample\", \"kind\": \"arena\", " +
            "\"player\": {\"class_id\": \"arch.class.a\", \"level\": 10, \"quality_id\": \"item.quality.rare\", \"race_id\": \"arch.race.human\"}, " +
            "\"opponent\": {\"creature_id\": \"creature.beast\", \"tier_id\": \"creature.tier.normal\", \"level_offsets\": [-2, 0, 2]}, " +
            "\"levels\": [8, 10, 12], \"runs\": 20, \"base_seed\": 12345, \"max_ticks\": 1000, " +
            "\"bandwidths\": {\"dps\": 0.1, \"hp\": 0.15}, \"note\": \"sample\"}";

        private const string GrowthRow =
            "{\"id\": \"sim.scenario.growth_sample\", \"kind\": \"growth\", " +
            "\"player\": {\"class_id\": \"arch.class.a\", \"level\": 1}, " +
            "\"opponent\": {\"creature_id\": \"creature.beast\"}, " +
            "\"level_from\": 1, \"level_to\": 60, " +
            "\"runs\": 3, \"base_seed\": 7, \"max_ticks\": 5000, " +
            "\"bandwidths\": {\"level_duration\": 0.2}}";

        [Fact]
        public void All_ListsEveryScenarioInLoadOrder()
        {
            var registry = BuildRegistryWithScenarios("[" + ArenaRow + ", " + GrowthRow + "]");
            var catalog = new Core.Sim.ScenarioCatalog(registry);

            Assert.Equal(2, catalog.All.Count);
            Assert.Equal("sim.scenario.arena_sample", catalog.All[0].Id.Value);
            Assert.Equal("sim.scenario.growth_sample", catalog.All[1].Id.Value);
        }

        [Fact]
        public void TryGet_KnownId_ReturnsFullyParsedScenario()
        {
            var registry = BuildRegistryWithScenarios("[" + ArenaRow + "]");
            var catalog = new Core.Sim.ScenarioCatalog(registry);

            Assert.True(catalog.TryGet(new Id("sim.scenario.arena_sample"), out var scenario));
            Assert.Equal(Core.Sim.ScenarioKind.Arena, scenario.Kind);
            Assert.Equal("arch.class.a", scenario.Player.ClassId.Value);
            Assert.Equal(10, scenario.Player.Level);
            Assert.Equal("item.quality.rare", scenario.Player.QualityId!.Value.Value);
            Assert.Equal("arch.race.human", scenario.Player.RaceId!.Value.Value);
            Assert.Equal("creature.beast", scenario.Opponent.CreatureId.Value);
            Assert.Equal("creature.tier.normal", scenario.Opponent.TierId!.Value.Value);
            Assert.Null(scenario.Opponent.Level);
            Assert.Equal(new[] { -2, 0, 2 }, scenario.Opponent.LevelOffsets);
            Assert.Equal(new[] { 8, 10, 12 }, scenario.Levels);
            Assert.Null(scenario.LevelFrom);
            Assert.Null(scenario.LevelTo);
            Assert.Equal(20, scenario.Runs);
            Assert.Equal(12345UL, scenario.BaseSeed);
            Assert.Equal(1000, scenario.MaxTicks);
            Assert.Equal(0.1, scenario.Bandwidths["dps"]);
            Assert.Equal(0.15, scenario.Bandwidths["hp"]);
            Assert.Equal("sample", scenario.Note);
        }

        [Fact]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            var registry = BuildRegistryWithScenarios("[" + ArenaRow + "]");
            var catalog = new Core.Sim.ScenarioCatalog(registry);

            Assert.False(catalog.TryGet(new Id("sim.scenario.does_not_exist"), out _));
        }

        [Fact]
        public void Get_UnknownId_Throws()
        {
            var registry = BuildRegistryWithScenarios("[" + ArenaRow + "]");
            var catalog = new Core.Sim.ScenarioCatalog(registry);

            Assert.Throws<ArgumentOutOfRangeException>(() => catalog.Get(new Id("sim.scenario.does_not_exist")));
        }

        [Fact]
        public void ByKind_FiltersToMatchingScenariosOnly()
        {
            var registry = BuildRegistryWithScenarios("[" + ArenaRow + ", " + GrowthRow + "]");
            var catalog = new Core.Sim.ScenarioCatalog(registry);

            var arenaOnly = catalog.ByKind(Core.Sim.ScenarioKind.Arena);
            Assert.Single(arenaOnly);
            Assert.Equal("sim.scenario.arena_sample", arenaOnly[0].Id.Value);

            var growthOnly = catalog.ByKind(Core.Sim.ScenarioKind.Growth);
            Assert.Single(growthOnly);
            Assert.Equal(1, growthOnly[0].LevelFrom);
            Assert.Equal(60, growthOnly[0].LevelTo);

            Assert.Empty(catalog.ByKind(Core.Sim.ScenarioKind.Coverage));
        }

        [Fact]
        public void GrowthScenario_WithoutOptionalPlayerFields_LeavesThemNull()
        {
            var registry = BuildRegistryWithScenarios("[" + GrowthRow + "]");
            var catalog = new Core.Sim.ScenarioCatalog(registry);

            var scenario = catalog.All.Single();
            Assert.Null(scenario.Player.QualityId);
            Assert.Null(scenario.Player.RaceId);
            Assert.Null(scenario.Opponent.TierId);
            Assert.Null(scenario.Opponent.Level);
            Assert.Empty(scenario.Opponent.LevelOffsets);
            Assert.Empty(scenario.Levels);
            Assert.Null(scenario.Note);
        }
    }
}
