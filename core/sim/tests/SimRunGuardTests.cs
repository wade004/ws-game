using System;
using System.Collections.Generic;
using System.Threading;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// 三个仿真入口（<see cref="ArenaSimulation"/>/<see cref="CoverageSimulation"/>/<see cref="GrowthSimulation"/>）
    /// 的 <c>Run</c> 参数守卫，以及 <see cref="ScenarioCatalog"/> 的解析错误路径
    /// （T-M5 / T-M15 sim 半 / T-M14 sim 半，2026-10-01 测试覆盖第四批）。
    /// <para>
    /// 判断记录：守卫全部发生在 <c>Run</c> 入口、先于任何世界装配，所以这里传入的 <c>dataSources</c> 可以是空列表、
    /// 锚点表可以是零行的 <see cref="AnchorTable"/>——被测的恰是"守卫先于一切"这条行为本身；若某守卫被挪到装配之后，
    /// 空数据源会改抛 <see cref="ArgumentException"/>（来自装配根）而不是预期的参数名，用例随之变红。
    /// </para>
    /// </summary>
    public sealed class SimRunGuardTests
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

        /// <summary>宽松的 sim.scenario schema：player/opponent 子对象不声明任何子字段。真 schema（<see cref="SimSchemas.Scenario"/>）
        /// 在加载期就把缺 class_id/level/creature_id、类型不符这类坏形状全部阻断，<see cref="ScenarioCatalog"/> 自己的
        /// <see cref="DataFieldException"/> 是"调用方绕过校验直接注入数据"时的防御，只有放宽 schema 才走得到。</summary>
        private static readonly TableSchema RelaxedScenario = new TableSchema(
            "sim.scenario", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: new[] { "arena", "growth", "coverage" }),
                new FieldSchema("player", FieldKind.Object, required: true),
                new FieldSchema("opponent", FieldKind.Object, required: true),
                new FieldSchema("runs", FieldKind.Int, required: true),
                new FieldSchema("base_seed", FieldKind.Int, required: true),
                new FieldSchema("max_ticks", FieldKind.Int, required: true),
            });

        private static DataRegistry BuildRegistry(string scenarioRowsJson, bool relaxed = false)
        {
            var source = new InMemoryDataSource()
                .Add("sim.scenario", Envelope("sim.scenario", scenarioRowsJson))
                .Add("arch.class", Envelope("arch.class", "[{\"id\": \"arch.class.a\"}]"))
                .Add("creature.template", Envelope("creature.template", "[{\"id\": \"creature.beast\"}]"))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", "[{\"id\": \"creature.tier.normal\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition", "[{\"id\": \"item.quality.rare\"}]"))
                .Add("arch.race", Envelope("arch.race", "[{\"id\": \"arch.race.human\"}]"));

            var registry = new DataRegistry(source, MakeBus());
            // 只登记 schema、不登记 SimScenarioValidationRule 等校验规则：Run 入口的守卫是"数据绕过内容校验直接
            // 注入"时的防御，用例需要构造出本应被规则阻断的场景（如 growth 缺 level_from）。
            registry.RegisterSchema(SimSchemas.Anchor);
            registry.RegisterSchema(relaxed ? RelaxedScenario : SimSchemas.Scenario);
            registry.RegisterSchema(StubTable("arch.class"));
            registry.RegisterSchema(StubTable("creature.template"));
            registry.RegisterSchema(StubTable("creature.tier_definition"));
            registry.RegisterSchema(StubTable("item.quality_definition"));
            registry.RegisterSchema(StubTable("arch.race"));
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        private static string Row(string id, string kind, string playerJson, string extra) =>
            "{\"id\": \"" + id + "\", \"kind\": \"" + kind + "\", \"player\": " + playerJson + ", " +
            "\"opponent\": {\"creature_id\": \"creature.beast\", \"level_offsets\": [0]}, " +
            extra + "\"runs\": 1, \"base_seed\": 1, \"max_ticks\": 10, \"bandwidths\": {}}";

        private const string PlayerWithQuality =
            "{\"class_id\": \"arch.class.a\", \"level\": 1, \"quality_id\": \"item.quality.rare\"}";

        private const string PlayerWithoutQuality = "{\"class_id\": \"arch.class.a\", \"level\": 1}";

        private sealed class Fixture
        {
            public readonly DataRegistry Registry;
            public readonly ScenarioCatalog Catalog;
            public readonly AnchorTable EmptyAnchors;
            public readonly IReadOnlyList<IDataSource> NoSources = new IDataSource[0];

            public Fixture()
            {
                var rows = "[" + string.Join(",", new[]
                {
                    Row("sim.scenario.arena_ok", "arena", PlayerWithQuality, "\"levels\": [1], "),
                    Row("sim.scenario.arena_no_quality", "arena", PlayerWithoutQuality, "\"levels\": [1], "),
                    Row("sim.scenario.coverage_ok", "coverage", PlayerWithQuality, "\"levels\": [1], "),
                    Row("sim.scenario.coverage_no_quality", "coverage", PlayerWithoutQuality, "\"levels\": [1], "),
                    Row("sim.scenario.growth_ok", "growth", PlayerWithQuality, "\"level_from\": 1, \"level_to\": 2, "),
                    Row("sim.scenario.growth_no_quality", "growth", PlayerWithoutQuality, "\"level_from\": 1, \"level_to\": 2, "),
                    Row("sim.scenario.growth_no_from", "growth", PlayerWithQuality, "\"level_to\": 2, "),
                    Row("sim.scenario.growth_no_to", "growth", PlayerWithQuality, "\"level_from\": 1, "),
                    Row("sim.scenario.growth_no_range", "growth", PlayerWithQuality, string.Empty),
                }) + "]";
                Registry = BuildRegistry(rows);
                Catalog = new ScenarioCatalog(Registry);
                EmptyAnchors = new AnchorTable(Registry);
            }

            public ScenarioDef Scenario(string suffix) => Catalog.Get(new Id("sim.scenario." + suffix));
        }

        // -----------------------------------------------------------------
        // 空参
        // -----------------------------------------------------------------

        [Fact]
        public void Arena_Run_NullArguments_ThrowArgumentNullExceptionNamingTheParameter()
        {
            var f = new Fixture();
            var scenario = f.Scenario("arena_ok");

            Assert.Equal("scenario", Assert.Throws<ArgumentNullException>(() => ArenaSimulation.Run(null!, f.EmptyAnchors, f.NoSources)).ParamName);
            Assert.Equal("anchors", Assert.Throws<ArgumentNullException>(() => ArenaSimulation.Run(scenario, null!, f.NoSources)).ParamName);
            Assert.Equal("dataSources", Assert.Throws<ArgumentNullException>(() => ArenaSimulation.Run(scenario, f.EmptyAnchors, null!)).ParamName);
        }

        [Fact]
        public void Coverage_Run_NullArguments_ThrowArgumentNullExceptionNamingTheParameter()
        {
            var f = new Fixture();
            var scenario = f.Scenario("coverage_ok");

            Assert.Equal("scenario", Assert.Throws<ArgumentNullException>(() => CoverageSimulation.Run(null!, f.EmptyAnchors, f.NoSources)).ParamName);
            Assert.Equal("anchors", Assert.Throws<ArgumentNullException>(() => CoverageSimulation.Run(scenario, null!, f.NoSources)).ParamName);
            Assert.Equal("dataSources", Assert.Throws<ArgumentNullException>(() => CoverageSimulation.Run(scenario, f.EmptyAnchors, null!)).ParamName);
        }

        [Fact]
        public void Growth_Run_NullArguments_ThrowArgumentNullExceptionNamingTheParameter()
        {
            var f = new Fixture();
            var scenario = f.Scenario("growth_ok");

            Assert.Equal("scenario", Assert.Throws<ArgumentNullException>(() => GrowthSimulation.Run(null!, f.EmptyAnchors, f.NoSources)).ParamName);
            Assert.Equal("anchors", Assert.Throws<ArgumentNullException>(() => GrowthSimulation.Run(scenario, null!, f.NoSources)).ParamName);
            Assert.Equal("dataSources", Assert.Throws<ArgumentNullException>(() => GrowthSimulation.Run(scenario, f.EmptyAnchors, null!)).ParamName);
        }

        // -----------------------------------------------------------------
        // kind 不符
        // -----------------------------------------------------------------

        [Fact]
        public void Arena_Run_WrongKind_ThrowsArgumentExceptionNamingActualKind()
        {
            var f = new Fixture();

            foreach (var suffix in new[] { "coverage_ok", "growth_ok" })
            {
                var scenario = f.Scenario(suffix);
                var ex = Assert.Throws<ArgumentException>(() => ArenaSimulation.Run(scenario, f.EmptyAnchors, f.NoSources));

                Assert.Equal("scenario", ex.ParamName);
                Assert.Contains(scenario.Kind.ToString(), ex.Message);
            }
        }

        [Fact]
        public void Coverage_Run_WrongKind_ThrowsArgumentExceptionNamingActualKind()
        {
            var f = new Fixture();

            foreach (var suffix in new[] { "arena_ok", "growth_ok" })
            {
                var scenario = f.Scenario(suffix);
                var ex = Assert.Throws<ArgumentException>(() => CoverageSimulation.Run(scenario, f.EmptyAnchors, f.NoSources));

                Assert.Equal("scenario", ex.ParamName);
                Assert.Contains(scenario.Kind.ToString(), ex.Message);
            }
        }

        [Fact]
        public void Growth_Run_WrongKind_ThrowsArgumentExceptionNamingActualKind()
        {
            var f = new Fixture();

            foreach (var suffix in new[] { "arena_ok", "coverage_ok" })
            {
                var scenario = f.Scenario(suffix);
                var ex = Assert.Throws<ArgumentException>(() => GrowthSimulation.Run(scenario, f.EmptyAnchors, f.NoSources));

                Assert.Equal("scenario", ex.ParamName);
                Assert.Contains(scenario.Kind.ToString(), ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Player.QualityId 为空
        // -----------------------------------------------------------------

        [Fact]
        public void AllRuns_ScenarioWithoutPlayerQuality_ThrowArgumentExceptionOnScenarioParameter()
        {
            var f = new Fixture();
            Assert.Null(f.Scenario("arena_no_quality").Player.QualityId);

            Assert.Equal("scenario", Assert.Throws<ArgumentException>(
                () => ArenaSimulation.Run(f.Scenario("arena_no_quality"), f.EmptyAnchors, f.NoSources)).ParamName);
            Assert.Equal("scenario", Assert.Throws<ArgumentException>(
                () => CoverageSimulation.Run(f.Scenario("coverage_no_quality"), f.EmptyAnchors, f.NoSources)).ParamName);
            Assert.Equal("scenario", Assert.Throws<ArgumentException>(
                () => GrowthSimulation.Run(f.Scenario("growth_no_quality"), f.EmptyAnchors, f.NoSources)).ParamName);
        }

        // -----------------------------------------------------------------
        // Growth：LevelFrom / LevelTo 为空
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("growth_no_from")]
        [InlineData("growth_no_to")]
        [InlineData("growth_no_range")]
        public void Growth_Run_MissingLevelFromOrTo_ThrowsArgumentException(string suffix)
        {
            var f = new Fixture();
            var scenario = f.Scenario(suffix);
            Assert.True(scenario.LevelFrom == null || scenario.LevelTo == null);

            var ex = Assert.Throws<ArgumentException>(() => GrowthSimulation.Run(scenario, f.EmptyAnchors, f.NoSources));

            Assert.Equal("scenario", ex.ParamName);
            Assert.Contains("LevelFrom", ex.Message);
        }

        // -----------------------------------------------------------------
        // 守卫先于取消检查
        // -----------------------------------------------------------------

        [Fact]
        public void Run_GuardsFireBeforeTheCancellationCheck()
        {
            var f = new Fixture();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // 参数错误优先于取消：已取消的令牌 + 错误的 kind 仍报 ArgumentException，而不是 OperationCanceledException。
            Assert.Throws<ArgumentException>(() => ArenaSimulation.Run(f.Scenario("growth_ok"), f.EmptyAnchors, f.NoSources, false, cts.Token));
            Assert.Throws<ArgumentException>(() => CoverageSimulation.Run(f.Scenario("arena_ok"), f.EmptyAnchors, f.NoSources, false, cts.Token));
            Assert.Throws<ArgumentException>(() => GrowthSimulation.Run(f.Scenario("coverage_ok"), f.EmptyAnchors, f.NoSources, false, cts.Token));
        }

        // -----------------------------------------------------------------
        // ScenarioCatalog / ScenarioDef 解析
        // -----------------------------------------------------------------

        [Fact]
        public void ScenarioCatalog_NullRegistry_Throws()
        {
            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => new ScenarioCatalog(null!)).ParamName);
        }

        [Fact]
        public void ScenarioCatalog_Get_UnknownId_NamesTheId()
        {
            var f = new Fixture();
            var id = new Id("sim.scenario.does_not_exist");

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => f.Catalog.Get(id));

            Assert.Contains(id.Value, ex.Message);
        }

        [Fact]
        public void ScenarioCatalog_PlayerWithoutClassId_ThrowsDataFieldExceptionNamingTheField()
        {
            var rows = "[" + Row("sim.scenario.bad_player", "arena", "{\"level\": 1}", "\"levels\": [1], ") + "]";
            var registry = BuildRegistry(rows, relaxed: true);

            var ex = Assert.Throws<DataFieldException>(() => new ScenarioCatalog(registry));

            Assert.Contains("class_id", ex.Message);
        }

        [Fact]
        public void ScenarioCatalog_PlayerWithoutLevel_ThrowsDataFieldExceptionNamingTheField()
        {
            var rows = "[" + Row("sim.scenario.bad_player", "arena", "{\"class_id\": \"arch.class.a\"}", "\"levels\": [1], ") + "]";
            var registry = BuildRegistry(rows, relaxed: true);

            var ex = Assert.Throws<DataFieldException>(() => new ScenarioCatalog(registry));

            Assert.Contains("level", ex.Message);
        }

        [Fact]
        public void ScenarioCatalog_PlayerFieldOfWrongType_ThrowsDataFieldException()
        {
            // class_id 不是字符串、level 不是数值，各自都按"字段缺失或类型不符"报错。
            var wrongClass = BuildRegistry("[" + Row("sim.scenario.bad_a", "arena", "{\"class_id\": 5, \"level\": 1}", "\"levels\": [1], ") + "]", relaxed: true);
            var wrongLevel = BuildRegistry("[" + Row("sim.scenario.bad_b", "arena", "{\"class_id\": \"arch.class.a\", \"level\": \"x\"}", "\"levels\": [1], ") + "]", relaxed: true);

            Assert.Throws<DataFieldException>(() => new ScenarioCatalog(wrongClass));
            Assert.Throws<DataFieldException>(() => new ScenarioCatalog(wrongLevel));
        }

        [Fact]
        public void ScenarioCatalog_OpponentWithoutCreatureId_ThrowsDataFieldException()
        {
            var rows = "[{\"id\": \"sim.scenario.bad_opp\", \"kind\": \"arena\", \"player\": " + PlayerWithQuality + ", " +
                "\"opponent\": {\"level_offsets\": [0]}, \"levels\": [1], \"runs\": 1, \"base_seed\": 1, \"max_ticks\": 10, \"bandwidths\": {}}]";
            var registry = BuildRegistry(rows, relaxed: true);

            var ex = Assert.Throws<DataFieldException>(() => new ScenarioCatalog(registry));

            Assert.Contains("creature_id", ex.Message);
        }

        [Fact]
        public void ScenarioCatalog_OptionalOpponentFieldsAbsent_AreNullOrEmpty()
        {
            var f = new Fixture();
            var opponent = f.Scenario("arena_ok").Opponent;

            Assert.Null(opponent.TierId);
            Assert.Null(opponent.Level);
            Assert.Equal(new[] { 0 }, opponent.LevelOffsets);
            Assert.Null(f.Scenario("arena_ok").AnchorRef);
            Assert.Null(f.Scenario("arena_ok").Note);
            Assert.Null(f.Scenario("arena_ok").Player.RaceId);
        }

        [Fact]
        public void ScenarioDef_WithRuns_ChangesOnlyRuns()
        {
            var f = new Fixture();
            var scenario = f.Scenario("growth_ok");

            var changed = scenario.WithRuns(scenario.Runs + 9);

            Assert.Equal(scenario.Runs + 9, changed.Runs);
            Assert.Equal(scenario.Id, changed.Id);
            Assert.Equal(scenario.Kind, changed.Kind);
            Assert.Equal(scenario.LevelFrom, changed.LevelFrom);
            Assert.Equal(scenario.LevelTo, changed.LevelTo);
            Assert.Equal(scenario.BaseSeed, changed.BaseSeed);
            Assert.Equal(scenario.MaxTicks, changed.MaxTicks);
            Assert.Equal(scenario.Player.ClassId, changed.Player.ClassId);
            Assert.Equal(scenario.Opponent.CreatureId, changed.Opponent.CreatureId);
        }

        [Fact]
        public void AnchorTable_NullRegistry_Throws_AndEmptyTableHasMaxLevelZero()
        {
            var f = new Fixture();

            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => new AnchorTable(null!)).ParamName);
            Assert.Equal(0, f.EmptyAnchors.MaxLevel);
            Assert.False(f.EmptyAnchors.TryGet(1, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.EmptyAnchors.Get(1));
        }
    }
}
