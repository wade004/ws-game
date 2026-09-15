using System;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Sim
{
    /// <summary>T-N6-2a：<see cref="Core.Sim.AnchorTable"/> 的类型化读取（任务书验收 6）。</summary>
    public sealed class AnchorTableTests
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

        private static DataRegistry BuildRegistryWithAnchor(string rowsJson)
        {
            var envelope = "{\"table\": \"sim.anchor\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";
            var source = new InMemoryDataSource().Add("sim.anchor", envelope);
            var registry = new DataRegistry(source, MakeBus());
            Core.Sim.SimSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        private const string ThreeLevelsJson =
            "[" +
            "{\"id\": \"sim.anchor.l1\", \"level\": 1, \"hp\": 100, \"dps\": 10, \"ttk_seconds\": 5, " +
            "\"ttd_seconds\": 8, \"expected_item_level\": 1, \"level_duration_seconds\": 300, " +
            "\"kill_interval_seconds\": 15, \"quest_share\": 0.3}," +
            "{\"id\": \"sim.anchor.l2\", \"level\": 2, \"hp\": 140, \"dps\": 14, \"ttk_seconds\": 5, " +
            "\"ttd_seconds\": 8, \"expected_item_level\": 2, \"level_duration_seconds\": 320, " +
            "\"kill_interval_seconds\": 15, \"quest_share\": 0.3}," +
            "{\"id\": \"sim.anchor.l3\", \"level\": 3, \"hp\": 190, \"dps\": 19, \"ttk_seconds\": 5, " +
            "\"ttd_seconds\": 8, \"expected_item_level\": 3, \"level_duration_seconds\": 340, " +
            "\"kill_interval_seconds\": 16, \"quest_share\": 0.3, \"note\": \"hello\"}" +
            "]";

        [Fact]
        public void MaxLevel_EqualsHighestRegisteredLevel()
        {
            var registry = BuildRegistryWithAnchor(ThreeLevelsJson);
            var table = new Core.Sim.AnchorTable(registry);

            Assert.Equal(3, table.MaxLevel);
        }

        [Fact]
        public void TryGet_KnownLevel_ReturnsRowWithParsedFields()
        {
            var registry = BuildRegistryWithAnchor(ThreeLevelsJson);
            var table = new Core.Sim.AnchorTable(registry);

            Assert.True(table.TryGet(2, out var row));
            Assert.Equal(2, row.Level);
            Assert.Equal(140, row.Hp);
            Assert.Equal(14, row.Dps);
            Assert.Equal(5, row.TtkSeconds);
            Assert.Equal(8, row.TtdSeconds);
            Assert.Equal(2, row.ExpectedItemLevel);
            Assert.Equal(320, row.LevelDurationSeconds);
            Assert.Equal(15, row.KillIntervalSeconds);
            Assert.Equal(0.3, row.QuestShare);
            Assert.Null(row.Note);
        }

        [Fact]
        public void TryGet_ReadsOptionalNoteWhenPresent()
        {
            var registry = BuildRegistryWithAnchor(ThreeLevelsJson);
            var table = new Core.Sim.AnchorTable(registry);

            Assert.True(table.TryGet(3, out var row));
            Assert.Equal("hello", row.Note);
        }

        [Fact]
        public void TryGet_UnknownLevel_ReturnsFalse()
        {
            var registry = BuildRegistryWithAnchor(ThreeLevelsJson);
            var table = new Core.Sim.AnchorTable(registry);

            Assert.False(table.TryGet(99, out _));
        }

        [Fact]
        public void Get_UnknownLevel_ThrowsArgumentOutOfRangeExceptionWithMaxLevelInMessage()
        {
            var registry = BuildRegistryWithAnchor(ThreeLevelsJson);
            var table = new Core.Sim.AnchorTable(registry);

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => table.Get(99));
            Assert.Contains("MaxLevel=3", ex.Message);
        }

        [Fact]
        public void Get_KnownLevel_ReturnsSameRowAsTryGet()
        {
            var registry = BuildRegistryWithAnchor(ThreeLevelsJson);
            var table = new Core.Sim.AnchorTable(registry);

            var row = table.Get(1);
            Assert.Equal(1, row.Level);
        }

        [Fact]
        public void EmptyTable_MaxLevelIsZero()
        {
            var registry = BuildRegistryWithAnchor("[]");
            var table = new Core.Sim.AnchorTable(registry);

            Assert.Equal(0, table.MaxLevel);
            Assert.False(table.TryGet(1, out _));
        }
    }
}
