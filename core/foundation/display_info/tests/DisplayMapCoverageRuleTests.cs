using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    public class DisplayMapCoverageRuleTests
    {
        private const string CheckName = "display_map_coverage";

        /// <summary>测试用的最小内容表 schema：只有一个 <c>id</c> 字段，模拟"技能/生物/物品"
        /// 一类逻辑表——本模块不预设具体内容表结构，见 <see cref="DisplayMapCoverageRule"/>
        /// 类型注释。</summary>
        private static TableSchema MinimalContentSchema(string tableName) => new TableSchema(
            tableName, "id", 1,
            new[] { new FieldSchema("id", FieldKind.Id, required: true) });

        private static IReadOnlyList<ValidationIssue> CoverageIssues(ValidationReport report) =>
            report.Issues.Where(i => i.Check == CheckName).ToArray();

        // ---------------------------------------------------------------
        // 正例
        // ---------------------------------------------------------------

        [Fact]
        public void AllReferencedLogicalIds_HaveDisplayMapRows_NoCoverageIssues()
        {
            var source = new InMemoryDataSource()
                .Add("display.map", Envelope("display.map", "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "]"))
                .Add("creature.template", Envelope("creature.template", "[{\"id\": \"creature.grey_wolf\"}]"));

            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, DisplayInfoTestSupport.CreateBus());
            foreach (var schema in DisplaySchemas.All) registry.RegisterSchema(schema);
            registry.RegisterSchema(MinimalContentSchema("creature.template"));
            registry.RegisterValidationRule(new DisplayMapCoverageRule(new[] { ("creature.template", "id") }));

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking);
            Assert.Empty(CoverageIssues(report));
        }

        [Fact]
        public void MultipleSourceTables_AllCovered_NoCoverageIssues()
        {
            var source = new InMemoryDataSource()
                .Add("display.map", Envelope("display.map",
                    "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "," + DisplayInfoTestSupport.StoneGolemModelRow + "]"))
                .Add("display.anim_set", Envelope("display.anim_set", "[" + DisplayInfoTestSupport.StoneGolemAnimSetRow + "]"))
                .Add("creature.template", Envelope("creature.template",
                    "[{\"id\": \"creature.grey_wolf\"}, {\"id\": \"creature.stone_golem\"}]"))
                .Add("item.template", Envelope("item.template", "[]"));

            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, DisplayInfoTestSupport.CreateBus());
            foreach (var schema in DisplaySchemas.All) registry.RegisterSchema(schema);
            registry.RegisterSchema(MinimalContentSchema("creature.template"));
            registry.RegisterSchema(MinimalContentSchema("item.template"));
            registry.RegisterValidationRule(new DisplayMapCoverageRule(new[]
            {
                ("creature.template", "id"),
                ("item.template", "id"),
            }));

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking);
            Assert.Empty(CoverageIssues(report));
        }

        // ---------------------------------------------------------------
        // 反例
        // ---------------------------------------------------------------

        [Fact]
        public void LogicalIdNotInDisplayMap_ReportsCoverageIssue()
        {
            var source = new InMemoryDataSource()
                .Add("display.map", Envelope("display.map", "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "]"))
                .Add("creature.template", Envelope("creature.template",
                    "[{\"id\": \"creature.grey_wolf\"}, {\"id\": \"creature.uncovered_boar\"}]"));

            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, DisplayInfoTestSupport.CreateBus());
            foreach (var schema in DisplaySchemas.All) registry.RegisterSchema(schema);
            registry.RegisterSchema(MinimalContentSchema("creature.template"));
            registry.RegisterValidationRule(new DisplayMapCoverageRule(new[] { ("creature.template", "id") }));

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            var issues = CoverageIssues(report);
            Assert.Single(issues);
            Assert.Equal("creature.uncovered_boar", issues[0].RecordKey);
            Assert.Equal("creature.template", issues[0].Table);
        }

        [Fact]
        public void EmptyDisplayMap_AllSourceRecordsUncovered_ReportsOneIssuePerRecord()
        {
            var source = new InMemoryDataSource()
                .Add("display.map", Envelope("display.map", "[]"))
                .Add("creature.template", Envelope("creature.template",
                    "[{\"id\": \"creature.a\"}, {\"id\": \"creature.b\"}]"));

            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, DisplayInfoTestSupport.CreateBus());
            foreach (var schema in DisplaySchemas.All) registry.RegisterSchema(schema);
            registry.RegisterSchema(MinimalContentSchema("creature.template"));
            registry.RegisterValidationRule(new DisplayMapCoverageRule(new[] { ("creature.template", "id") }));

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            var issues = CoverageIssues(report);
            Assert.Equal(2, issues.Count);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";
    }
}
