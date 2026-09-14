using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 分阶段落地计划 T-N0-1 验收：<see cref="CurveSchema"/> 工厂生成的标准子结构（断点表 / 饱和）、
    /// <see cref="FieldSchema.WithCurve"/> 只能设置一次、<see cref="CurveSchema.ReadBreakpoints(DataRecord, string)"/>
    /// 解析与排序、加载期对断点元素复用既有 <c>required_field</c>/<c>field_type</c>/<c>field_range</c>
    /// 检查项（登记与校验同源，不新增平行检查名）。
    /// </summary>
    public sealed class CurveSchemaTests
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

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static TableSchema CurveTable(FieldSchema entries) => new TableSchema(
            "test.curve", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                entries,
            });

        private static DataRegistry LoadCurveTable(FieldSchema entries, string rowsJson, out ValidationReport report)
        {
            var source = new InMemoryDataSource().Add("test.curve", Envelope("test.curve", rowsJson));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(CurveTable(entries));
            report = registry.LoadAll();
            return registry;
        }

        [Fact]
        public void BreakpointsField_LevelAxis_BuildsArrayOfIntXNumberYWithCurveMarker()
        {
            var field = CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true, description: "等级曲线");

            Assert.Equal(FieldKind.Array, field.Kind);
            Assert.True(field.Required);
            Assert.NotNull(field.Curve);
            Assert.Equal(CurveShape.Breakpoints, field.Curve!.Shape);
            Assert.Equal(CurveAxis.Level, field.Curve.Axis);

            var item = field.Item;
            Assert.NotNull(item);
            Assert.Equal(FieldKind.Object, item!.Kind);
            Assert.NotNull(item.Fields);
            Assert.Equal(2, item.Fields!.Count);
            Assert.Equal(CurveSchema.XFieldName, item.Fields[0].Name);
            Assert.Equal(FieldKind.Int, item.Fields[0].Kind);
            Assert.True(item.Fields[0].Required);
            Assert.Equal(CurveSchema.YFieldName, item.Fields[1].Name);
            Assert.Equal(FieldKind.Number, item.Fields[1].Kind);
            Assert.True(item.Fields[1].Required);
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.False(string.IsNullOrWhiteSpace(item.Fields[0].Description));
            Assert.False(string.IsNullOrWhiteSpace(item.Fields[1].Description));
        }

        [Fact]
        public void BreakpointsField_ValueAxis_UsesNumberXAndAppliesOptionalRanges()
        {
            var field = CurveSchema.BreakpointsField("entries", CurveAxis.Value, required: false, description: "数值曲线",
                yRange: FieldRange.Range(min: 0, minExclusive: true));

            Assert.Equal(CurveAxis.Value, field.Curve!.Axis);
            Assert.Equal(FieldKind.Number, field.Item!.Fields![0].Kind);
            Assert.Null(field.Item.Fields[0].Range);
            Assert.NotNull(field.Item.Fields[1].Range);
        }

        [Fact]
        public void SaturationField_BuildsObjectWithRequiredKAndOptionalCap()
        {
            var field = CurveSchema.SaturationField("curve", required: true, description: "饱和曲线");

            Assert.Equal(FieldKind.Object, field.Kind);
            Assert.Equal(CurveShape.Saturation, field.Curve!.Shape);
            Assert.Equal(CurveAxis.Value, field.Curve.Axis);
            Assert.NotNull(field.Fields);
            Assert.Equal(CurveSchema.SaturationKFieldName, field.Fields![0].Name);
            Assert.True(field.Fields[0].Required);
            Assert.NotNull(field.Fields[0].Range);
            Assert.Equal(CurveSchema.SaturationCapFieldName, field.Fields[1].Name);
            Assert.False(field.Fields[1].Required);
            Assert.NotNull(field.Fields[1].Range);
        }

        [Fact]
        public void WithCurve_Twice_Throws()
        {
            var field = new FieldSchema("entries", FieldKind.Array, required: true, description: "x");
            field.WithCurve(CurveSchema.Breakpoints(CurveAxis.Level));

            Assert.Throws<InvalidOperationException>(() => field.WithCurve(CurveSchema.Saturation()));
            Assert.Throws<ArgumentNullException>(() => new FieldSchema("f", FieldKind.Array, true).WithCurve(null!));
        }

        [Fact]
        public void ReadBreakpoints_ParsesAndSortsUnsortedEntries()
        {
            var entries = CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true, description: "等级曲线");
            var rows = "[{\"id\": \"test.a\", \"entries\": [{\"x\": 20, \"y\": 400}, {\"x\": 1, \"y\": 10}, {\"x\": 10, \"y\": 100}]}]";
            var registry = LoadCurveTable(entries, rows, out var report);
            Assert.False(report.IsBlocking);

            var curve = CurveSchema.ReadBreakpoints(registry.Get("test.curve", "test.a")!, "entries");

            Assert.Equal(3, curve.Count);
            Assert.Equal(new CurvePoint(1, 10), curve.Points[0]);
            Assert.Equal(new CurvePoint(20, 400), curve.Points[2]);
            Assert.Equal(55, curve.Evaluate(5.5));
        }

        [Fact]
        public void ReadBreakpoints_ElementMissingY_ThrowsDataFieldExceptionWithElementPath()
        {
            var table = CurveTable(CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true, description: "等级曲线"));
            var raw = (JsonObject)JsonReader.Parse("{\"id\": \"test.a\", \"entries\": [{\"x\": 1, \"y\": 1}, {\"x\": 2}]}");
            var record = new DataRecord(table, "test.a", new Id("test.a"), raw);

            var ex = Assert.Throws<DataFieldException>(() => CurveSchema.ReadBreakpoints(record, "entries"));

            Assert.Equal("entries[1]", ex.Field);
            Assert.Equal("test.curve", ex.Table);
        }

        [Fact]
        public void TryReadBreakpoints_ReportsFirstBadIndexWithoutThrowing()
        {
            var raw = (JsonArray)JsonReader.Parse("[{\"x\": 1, \"y\": 1}, 5, {\"x\": 3}]");

            var ok = CurveSchema.TryReadBreakpoints(raw, out var curve, out var badIndex);

            Assert.False(ok);
            Assert.Equal(1, badIndex);
            Assert.Equal(0, curve.Count);

            var good = (JsonArray)JsonReader.Parse("[{\"x\": 2, \"y\": 20}, {\"x\": 1, \"y\": 10}]");
            Assert.True(CurveSchema.TryReadBreakpoints(good, out var parsed, out var none));
            Assert.Equal(-1, none);
            Assert.Equal(15, parsed.Evaluate(1.5));
        }

        [Fact]
        public void Load_EntryMissingY_ReportsRequiredFieldOnElementPath()
        {
            var entries = CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true, description: "等级曲线");
            var rows = "[{\"id\": \"test.a\", \"entries\": [{\"x\": 1, \"y\": 10}, {\"x\": 2}]}]";

            LoadCurveTable(entries, rows, out var report);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "entries[1].y");
        }

        [Fact]
        public void Load_LevelAxisXNotInteger_ReportsFieldType()
        {
            var entries = CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true, description: "等级曲线");
            var rows = "[{\"id\": \"test.a\", \"entries\": [{\"x\": 1.5, \"y\": 10}]}]";

            LoadCurveTable(entries, rows, out var report);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "entries[0].x");
        }

        [Fact]
        public void Load_YOutOfRegisteredRange_ReportsFieldRange()
        {
            var entries = CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true, description: "等级曲线",
                yRange: FieldRange.Range(min: 0, minExclusive: true));
            var rows = "[{\"id\": \"test.a\", \"entries\": [{\"x\": 1, \"y\": 0}]}]";

            LoadCurveTable(entries, rows, out var report);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "entries[0].y");
        }

        [Fact]
        public void Load_SaturationMissingK_ReportsRequiredField()
        {
            var curve = CurveSchema.SaturationField("curve", required: true, description: "饱和曲线");
            var rows = "[{\"id\": \"test.a\", \"curve\": {\"cap\": 0.75}}]";

            LoadCurveTable(curve, rows, out var report);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "curve.k");
        }
    }
}
