using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 分阶段落地计划 T-N0-3 验收：<see cref="CurveMonotonicFiniteRule"/>（检查名 <c>curve_monotonic_finite</c>）
    /// 正负例——非单调、NaN、无穷、合法，另覆盖横轴重复、空断点表、嵌套（对象子字段 / 数组元素 / 映射值 /
    /// 变体分支）路径定位、形态不符不重复报、未登记曲线形态的表不受影响、规则元数据。
    /// </summary>
    public sealed class CurveMonotonicFiniteRuleTests
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

        private static TableSchema TopLevelCurveTable() => new TableSchema("test.curve", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true, description: "等级曲线"),
        });

        private static ValidationReport Load(TableSchema schema, string rowsJson)
        {
            var source = new InMemoryDataSource().Add(schema.Name, Envelope(schema.Name, rowsJson));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);
            registry.RegisterValidationRule(new CurveMonotonicFiniteRule());
            return registry.LoadAll();
        }

        private static PiecewiseCurve Curve(params (double X, double Y)[] points)
        {
            var list = new List<CurvePoint>();
            foreach (var p in points) list.Add(new CurvePoint(p.X, p.Y));
            return new PiecewiseCurve(list);
        }

        [Fact]
        public void Valid_MonotonicFiniteCurve_NoIssue()
        {
            var report = Load(TopLevelCurveTable(),
                "[{\"id\": \"test.a\", \"entries\": [{\"x\": 1, \"y\": 10}, {\"x\": 10, \"y\": 100}, {\"x\": 20, \"y\": 100}]}]");

            Assert.False(report.IsBlocking);
            Assert.DoesNotContain(report.Issues, i => i.Check == CurveMonotonicFiniteRule.CheckName);
            Assert.Contains(report.Rules, r => r.RuleId == nameof(CurveMonotonicFiniteRule) && r.HitCount == 0);
        }

        [Fact]
        public void NonMonotonic_DecreasingY_ReportsErrorOnField()
        {
            var report = Load(TopLevelCurveTable(),
                "[{\"id\": \"test.a\", \"entries\": [{\"x\": 1, \"y\": 10}, {\"x\": 10, \"y\": 5}]}]");

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == CurveMonotonicFiniteRule.CheckName);
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Equal("test.curve", issue.Table);
            Assert.Equal("test.a", issue.RecordKey);
            Assert.Equal("entries", issue.Field);
            Assert.Equal(nameof(CurveMonotonicFiniteRule), issue.RuleId);
            Assert.Contains("下降", issue.Message);
        }

        [Fact]
        public void NonMonotonic_UnsortedInputIsSortedBeforeCheck()
        {
            // 无序但排序后单调：合法。
            var report = Load(TopLevelCurveTable(),
                "[{\"id\": \"test.a\", \"entries\": [{\"x\": 10, \"y\": 100}, {\"x\": 1, \"y\": 10}]}]");

            Assert.DoesNotContain(report.Issues, i => i.Check == CurveMonotonicFiniteRule.CheckName);
        }

        [Fact]
        public void DuplicateX_ReportsError()
        {
            var report = Load(TopLevelCurveTable(),
                "[{\"id\": \"test.a\", \"entries\": [{\"x\": 1, \"y\": 10}, {\"x\": 1, \"y\": 20}]}]");

            var issue = Assert.Single(report.Issues, i => i.Check == CurveMonotonicFiniteRule.CheckName);
            Assert.Contains("重复", issue.Message);
        }

        [Fact]
        public void EmptyEntries_ReportsError()
        {
            var report = Load(TopLevelCurveTable(), "[{\"id\": \"test.a\", \"entries\": []}]");

            var issue = Assert.Single(report.Issues, i => i.Check == CurveMonotonicFiniteRule.CheckName);
            Assert.Contains("空", issue.Message);
        }

        [Fact]
        public void Inspect_NaN_ReportsNonFinite()
        {
            var problem = CurveMonotonicFiniteRule.Inspect(Curve((1, 1), (2, double.NaN)));

            Assert.NotNull(problem);
            Assert.Contains("非有限", problem);
        }

        [Fact]
        public void Inspect_Infinity_ReportsNonFinite()
        {
            Assert.Contains("非有限", CurveMonotonicFiniteRule.Inspect(Curve((1, 1), (2, double.PositiveInfinity))));
            Assert.Contains("非有限", CurveMonotonicFiniteRule.Inspect(Curve((double.NegativeInfinity, 1), (2, 2))));
        }

        [Fact]
        public void Inspect_Valid_ReturnsNull_SinglePointAllowed()
        {
            Assert.Null(CurveMonotonicFiniteRule.Inspect(Curve((1, 1), (2, 2), (3, 2))));
            Assert.Null(CurveMonotonicFiniteRule.Inspect(Curve((5, 0))));
        }

        [Fact]
        public void MalformedElements_SkippedByThisRule_FieldLevelChecksReportInstead()
        {
            var report = Load(TopLevelCurveTable(),
                "[{\"id\": \"test.a\", \"entries\": [{\"x\": 1, \"y\": 10}, {\"x\": 2}]}]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "entries[1].y");
            Assert.DoesNotContain(report.Issues, i => i.Check == CurveMonotonicFiniteRule.CheckName);
        }

        [Fact]
        public void TableWithoutCurveFields_NotAffected()
        {
            var schema = new TableSchema("test.plain", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                new FieldSchema("entries", FieldKind.Array, required: true, description: "普通数组",
                    item: new FieldSchema("<e>", FieldKind.Object, required: true, description: "元素", fields: new[]
                    {
                        new FieldSchema("x", FieldKind.Int, required: true, description: "x"),
                        new FieldSchema("y", FieldKind.Number, required: true, description: "y"),
                    })),
            });

            var report = Load(schema, "[{\"id\": \"test.a\", \"entries\": [{\"x\": 1, \"y\": 10}, {\"x\": 2, \"y\": 1}]}]");

            Assert.False(report.IsBlocking);
            Assert.DoesNotContain(report.Issues, i => i.Check == CurveMonotonicFiniteRule.CheckName);
        }

        [Fact]
        public void NestedCurves_ObjectField_ArrayItem_MapValue_VariantCase_PathsResolved()
        {
            var curveInObject = new FieldSchema("bundle", FieldKind.Object, required: false, description: "对象", fields: new[]
            {
                CurveSchema.BreakpointsField("curve", CurveAxis.Level, required: true, description: "对象内曲线"),
            });
            var curveInArray = new FieldSchema("list", FieldKind.Array, required: false, description: "数组",
                item: new FieldSchema("<e>", FieldKind.Object, required: true, description: "元素", fields: new[]
                {
                    CurveSchema.BreakpointsField("curve", CurveAxis.Value, required: true, description: "元素内曲线"),
                }));
            var curveInMap = new FieldSchema("by_key", FieldKind.Object, required: false, description: "映射")
                .WithMap(MapSchema.FreeKeyed("测试用自由键", CurveSchema.BreakpointsField("value", CurveAxis.Level, required: true, description: "映射值曲线")));
            var curveInVariant = new FieldSchema("shape", FieldKind.Object, required: false, description: "变体",
                variants: new VariantSchema("kind", new Dictionary<string, IReadOnlyList<FieldSchema>>
                {
                    ["curved"] = new[] { CurveSchema.BreakpointsField("curve", CurveAxis.Level, required: true, description: "变体内曲线") },
                    ["flat"] = new FieldSchema[0],
                }));
            var schema = new TableSchema("test.nested", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                curveInObject, curveInArray, curveInMap, curveInVariant,
            });

            const string bad = "[{\"x\": 1, \"y\": 2}, {\"x\": 2, \"y\": 1}]";
            const string good = "[{\"x\": 1, \"y\": 1}, {\"x\": 2, \"y\": 2}]";
            var rows = "[{\"id\": \"test.a\"," +
                " \"bundle\": {\"curve\": " + bad + "}," +
                " \"list\": [{\"curve\": " + good + "}, {\"curve\": " + bad + "}]," +
                " \"by_key\": {\"k1\": " + bad + "}," +
                " \"shape\": {\"kind\": \"curved\", \"curve\": " + bad + "}}]";

            var report = Load(schema, rows);

            var fields = new List<string>();
            foreach (var i in report.Issues)
            {
                if (i.Check == CurveMonotonicFiniteRule.CheckName) fields.Add(i.Field!);
            }
            fields.Sort(System.StringComparer.Ordinal);
            Assert.Equal(new[] { "bundle.curve", "by_key[k1]", "list[1].curve", "shape.curve" }, fields);
        }

        [Fact]
        public void Metadata_RuleIdCheckNameAndSeverity()
        {
            IValidationRule rule = new CurveMonotonicFiniteRule();

            Assert.Equal("CurveMonotonicFiniteRule", rule.RuleId);
            Assert.Equal(ValidationSeverity.Error, rule.DefaultSeverity);
            Assert.False(rule.NonEscalatable);
            Assert.Equal("curve_monotonic_finite", CurveMonotonicFiniteRule.CheckName);
        }
    }
}
