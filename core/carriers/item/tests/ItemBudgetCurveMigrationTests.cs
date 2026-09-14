using System.Collections.Generic;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 分阶段落地计划 T-N0-4 验收（<c>item.budget_curve</c> 侧）：v1 数据 <c>{item_level, budget}</c> 经
    /// 1→2 迁移加载后，<see cref="ItemBudgetCurve.ParseEntries"/>/<see cref="ItemBudgetCurve.Interpolate(IReadOnlyList{ValueTuple{int, double}}, int)"/>
    /// 的结果与迁移前的手写插值逐位一致（≥ 3 组）；v2 数据直接加载结果相同；未经迁移直接构造的记录
    /// 仍能按 v1 元素名解析（旧字段读取路径未删除）；迁移环节保留元素内其它键。
    /// </summary>
    public sealed class ItemBudgetCurveMigrationTests
    {
        private static IEventBus MakeBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private static string Envelope(int version, string rows) =>
            "{\"table\": \"item.budget_curve\", \"schema_version\": " + version + ", \"rows\": " + rows + "}";

        private const string V1Rows = "[{\"id\": \"item.budget.default\", \"entries\": [" +
            "{\"item_level\": 60, \"budget\": 1200}, {\"item_level\": 1, \"budget\": 20}, {\"item_level\": 10, \"budget\": 200}]}]";

        private const string V2Rows = "[{\"id\": \"item.budget.default\", \"entries\": [" +
            "{\"x\": 60, \"y\": 1200}, {\"x\": 1, \"y\": 20}, {\"x\": 10, \"y\": 200}]}]";

        /// <summary>迁移前 <c>ItemBudgetCurve.Interpolate</c> 的原式（逐字复刻，作为基准）。</summary>
        private static double LegacyInterpolate(IReadOnlyList<(int ItemLevel, double Budget)> entries, int itemLevel)
        {
            if (entries.Count == 0) return 0;
            if (itemLevel <= entries[0].ItemLevel) return entries[0].Budget;
            var last = entries[entries.Count - 1];
            if (itemLevel >= last.ItemLevel) return last.Budget;
            for (var i = 0; i < entries.Count - 1; i++)
            {
                var lo = entries[i];
                var hi = entries[i + 1];
                if (itemLevel >= lo.ItemLevel && itemLevel <= hi.ItemLevel)
                {
                    if (hi.ItemLevel == lo.ItemLevel) return lo.Budget;
                    var t = (double)(itemLevel - lo.ItemLevel) / (hi.ItemLevel - lo.ItemLevel);
                    return lo.Budget + t * (hi.Budget - lo.Budget);
                }
            }
            return last.Budget;
        }

        private static DataRecord Load(int version, string rows)
        {
            var source = new InMemoryDataSource().Add("item.budget_curve", Envelope(version, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(ItemSchemas.BudgetCurve);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry.Get("item.budget_curve", "item.budget.default")!;
        }

        [Fact]
        public void V1Data_MigratedTo2_ElementsRenamedToXY()
        {
            var record = Load(1, V1Rows);

            var entries = record.GetArray("entries");
            Assert.Equal(3, entries.Count);
            var first = (JsonObject)entries[0];
            Assert.True(first.ContainsKey("x"));
            Assert.True(first.ContainsKey("y"));
            Assert.False(first.ContainsKey("item_level"));
            Assert.False(first.ContainsKey("budget"));
        }

        [Fact]
        public void V1Data_ParseEntries_SortedTuplesSameAsBefore()
        {
            var entries = ItemBudgetCurve.ParseEntries(Load(1, V1Rows));

            Assert.Equal(new[] { (1, 20.0), (10, 200.0), (60, 1200.0) }, entries);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(37)]
        [InlineData(60)]
        [InlineData(99)]
        public void V1AndV2Data_Interpolate_MatchLegacyFormulaBitForBit(int itemLevel)
        {
            var legacyEntries = new List<(int, double)> { (1, 20.0), (10, 200.0), (60, 1200.0) };
            var expected = LegacyInterpolate(legacyEntries, itemLevel);

            var v1 = Load(1, V1Rows);
            var v2 = Load(2, V2Rows);

            Assert.Equal(expected, ItemBudgetCurve.Interpolate(ItemBudgetCurve.ParseEntries(v1), itemLevel));
            Assert.Equal(expected, ItemBudgetCurve.Interpolate(ItemBudgetCurve.ParseCurve(v1), itemLevel));
            Assert.Equal(expected, ItemBudgetCurve.Interpolate(ItemBudgetCurve.ParseCurve(v2), itemLevel));
        }

        [Fact]
        public void UnmigratedRecord_WithLegacyElementNames_StillParses()
        {
            var raw = (JsonObject)JsonReader.Parse("{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 20}, {\"item_level\": 10, \"budget\": 200}]}");
            var record = new DataRecord(ItemSchemas.BudgetCurve, "item.budget.default", new Id("item.budget.default"), raw);

            var curve = ItemBudgetCurve.ParseCurve(record);

            Assert.Equal(2, curve.Count);
            Assert.Equal(100, ItemBudgetCurve.Interpolate(curve, 5));
            Assert.Equal(100, ItemBudgetCurve.Interpolate(ItemBudgetCurve.ParseEntries(record), 5));
        }

        [Fact]
        public void UnmigratedRecord_MissingBothNamings_Throws()
        {
            var raw = (JsonObject)JsonReader.Parse("{\"id\": \"item.budget.default\", \"entries\": [{\"lvl\": 1, \"budget\": 20}]}");
            var record = new DataRecord(ItemSchemas.BudgetCurve, "item.budget.default", new Id("item.budget.default"), raw);

            Assert.Throws<System.ArgumentException>(() => ItemBudgetCurve.ParseCurve(record));
        }

        [Fact]
        public void Migration_PreservesExtraElementKeysAndNonObjectElements()
        {
            var row = (JsonObject)JsonReader.Parse("{\"id\": \"item.budget.default\", \"note\": \"n\", \"entries\": [{\"item_level\": 1, \"budget\": 20, \"tag\": \"a\"}, 7]}");

            var migrated = CurveSchema.MigrateBreakpointsFieldNames(row, "entries", "item_level", "budget");

            Assert.Equal("n", ((JsonString)migrated["note"]).Value);
            var entries = (JsonArray)migrated["entries"];
            var first = (JsonObject)entries[0];
            Assert.Equal(1, ((JsonNumber)first["x"]).Value);
            Assert.Equal(20, ((JsonNumber)first["y"]).Value);
            Assert.Equal("a", ((JsonString)first["tag"]).Value);
            Assert.Equal(7, ((JsonNumber)entries[1]).Value);
            // 输入未被修改。
            Assert.True(((JsonObject)((JsonArray)row["entries"])[0]).ContainsKey("item_level"));
        }
    }
}
