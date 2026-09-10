using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// ADR-0021（04 第 4 节勘误"范围约束"，消费方反馈 2026-09-10"技能效果参数数值范围校验改进
    /// 建议"）：<see cref="FieldRange"/>/<see cref="FieldSchema.WithRange"/> 的值级不变量，以及
    /// <c>DataRegistry</c> 加载期 <c>field_range</c> 检查项——含/不含端点、Int/Number 两种种类、
    /// 子结构与变体内字段路径、省略可选字段不报错、类型错误优先于范围错误。<c>SchemaAudit</c> 的
    /// <c>field_range_kind</c> 自洽检查覆盖在 <c>presentation/assembly/tests/SchemaAuditTests.cs</c>
    /// （见该文件"ADR-0021"分节），不在本文件重复。
    /// </summary>
    public sealed class FieldRangeValidationTests
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

        private static string Envelope(string table, int schemaVersion, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": " + schemaVersion + ", \"rows\": " + rowsJson + "}";

        // -----------------------------------------------------------------
        // FieldRange.Range：值级不变量（构造期立即拒绝，不依赖挂在哪个字段上）
        // -----------------------------------------------------------------

        [Fact]
        public void Range_MinAndMaxBothNull_Throws()
        {
            Assert.Throws<ArgumentException>(() => FieldRange.Range());
        }

        [Fact]
        public void Range_MinGreaterThanMax_Throws()
        {
            Assert.Throws<ArgumentException>(() => FieldRange.Range(min: 5, max: 1));
        }

        [Fact]
        public void Range_MinEqualsMaxWithExclusiveEndpoint_Throws()
        {
            Assert.Throws<ArgumentException>(() => FieldRange.Range(min: 1, minExclusive: true, max: 1));
            Assert.Throws<ArgumentException>(() => FieldRange.Range(min: 1, max: 1, maxExclusive: true));
        }

        [Fact]
        public void Range_MinEqualsMaxBothInclusive_DoesNotThrow_SingleValueRange()
        {
            var range = FieldRange.Range(min: 1, max: 1);
            Assert.True(range.Contains(1));
            Assert.False(range.Contains(0.999));
            Assert.False(range.Contains(1.001));
        }

        [Fact]
        public void Range_OnlyMinProvided_Describe_ShowsOpenUpperBound()
        {
            var range = FieldRange.Range(min: 0, minExclusive: true);
            Assert.Equal("(0, +∞)", range.Describe());
        }

        [Fact]
        public void Range_MinAndMaxInclusive_Describe_ShowsClosedInterval()
        {
            var range = FieldRange.Range(min: 0, max: 1);
            Assert.Equal("[0, 1]", range.Describe());
        }

        // -----------------------------------------------------------------
        // F-02 根治（2026-09-10 codex 第十六轮 schema 审计，schema-findings.md）：Min/Max 端点
        // 拒绝 NaN/±Infinity（无界一侧统一用 null 表达），Contains(NaN) 恒为 false。
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Range_MinIsNonFinite_Throws(double min)
        {
            Assert.Throws<ArgumentException>(() => FieldRange.Range(min: min));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Range_MaxIsNonFinite_Throws(double max)
        {
            Assert.Throws<ArgumentException>(() => FieldRange.Range(max: max));
        }

        [Fact]
        public void Range_MinIsPositiveInfinity_WithFiniteMax_StillThrows()
        {
            // 独立 consumer 复现（schema-findings.md F-02）：Range(min: +Infinity) 此前能成功构造。
            Assert.Throws<ArgumentException>(() => FieldRange.Range(min: double.PositiveInfinity, max: 10));
        }

        /// <summary>F-02 复现原文：<c>Range(min: NaN)</c> 此前构造成功，<c>Describe()</c> 输出
        /// <c>"[NaN, +∞)"</c>，且 <c>Contains(0)</c> 反常为 <c>true</c>（NaN 参与比较恒为 false，
        /// 两处"小于 Min 就拒绝"的判断都不成立，等价于没有下界）。构造入口现在直接拒绝，
        /// 不再可能构造出这种范围。</summary>
        [Fact]
        public void Range_NaNMin_NoLongerConstructible()
        {
            Assert.Throws<ArgumentException>(() => FieldRange.Range(min: double.NaN));
        }

        [Fact]
        public void Contains_NaNValue_AlwaysFalse_UnboundedAbove()
        {
            var range = FieldRange.Range(min: 0); // 无上界（null），只有下界
            Assert.False(range.Contains(double.NaN));
        }

        [Fact]
        public void Contains_NaNValue_AlwaysFalse_FullyBounded()
        {
            var range = FieldRange.Range(min: 0, max: 10);
            Assert.False(range.Contains(double.NaN));
        }

        [Fact]
        public void Contains_NaNValue_AlwaysFalse_UnboundedBelow()
        {
            var range = FieldRange.Range(max: 10); // 无下界（null），只有上界
            Assert.False(range.Contains(double.NaN));
        }

        /// <summary>Describe 不受本次改动影响（任务约束"Describe 不变"）：仍用 null 表示无界，
        /// 输出 <c>"(-∞"</c>/<c>"+∞)"</c>，与改动前逐字节一致。</summary>
        [Fact]
        public void Describe_UnboundedSide_UnaffectedByFiniteEndpointCheck()
        {
            Assert.Equal("[0, +∞)", FieldRange.Range(min: 0).Describe());
            Assert.Equal("(-∞, 0]", FieldRange.Range(max: 0).Describe());
        }

        // -----------------------------------------------------------------
        // FieldSchema.WithRange：单次设置、链式返回
        // -----------------------------------------------------------------

        [Fact]
        public void WithRange_SetTwice_Throws()
        {
            var field = new FieldSchema("value", FieldKind.Number, required: false, description: "示例");
            field.WithRange(FieldRange.Range(min: 0));

            Assert.Throws<InvalidOperationException>(() => field.WithRange(FieldRange.Range(min: 1)));
        }

        [Fact]
        public void WithRange_NullRange_Throws()
        {
            var field = new FieldSchema("value", FieldKind.Number, required: false, description: "示例");
            Assert.Throws<ArgumentNullException>(() => field.WithRange(null!));
        }

        [Fact]
        public void WithRange_ReturnsSameInstance_ForChaining()
        {
            var field = new FieldSchema("value", FieldKind.Number, required: false, description: "示例");
            var returned = field.WithRange(FieldRange.Range(min: 0));

            Assert.Same(field, returned);
            Assert.NotNull(field.Range);
        }

        /// <summary>WithRange 刻意不检查 Kind（见 FieldRange 类型顶部判断记录）：挂在 String 字段上
        /// 不抛异常，只由 SchemaAudit 的 field_range_kind 事后报告——本用例只证明"不炸"，
        /// SchemaAudit 那一半覆盖见 SchemaAuditTests.FieldRangeKind_RangeOnStringField_ReportsError。</summary>
        [Fact]
        public void WithRange_OnNonNumericKind_DoesNotThrow()
        {
            var field = new FieldSchema("value", FieldKind.String, required: false, description: "示例");
            var returned = field.WithRange(FieldRange.Range(min: 0));

            Assert.Same(field, returned);
        }

        // -----------------------------------------------------------------
        // DataRegistry.LoadAll：顶层字段，含/不含端点，Int/Number
        // -----------------------------------------------------------------

        private static TableSchema TopLevelNumberRangeSchema(FieldRange range) => new TableSchema(
            "test.range_number", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("value", FieldKind.Number, required: false, description: "示例数值")
                    .WithRange(range),
            });

        [Theory]
        [InlineData(0.0, true)] // 含端点：等于 min 通过
        [InlineData(-0.001, false)]
        [InlineData(1.0, true)]
        [InlineData(1.001, false)]
        public void NumberField_MinMaxInclusive_BoundaryBehavior(double value, bool expectPass)
        {
            var schema = TopLevelNumberRangeSchema(FieldRange.Range(min: 0, max: 1));
            var rows = $"[{{\"id\": \"test.a\", \"value\": {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}]";
            var source = new InMemoryDataSource().Add("test.range_number", Envelope("test.range_number", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            if (expectPass)
            {
                Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            }
            else
            {
                Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "value");
            }
        }

        [Theory]
        [InlineData(0.0, false)] // 不含端点（> 0）：等于 min 不通过
        [InlineData(0.001, true)]
        public void NumberField_MinExclusive_BoundaryBehavior(double value, bool expectPass)
        {
            var schema = TopLevelNumberRangeSchema(FieldRange.Range(min: 0, minExclusive: true));
            var rows = $"[{{\"id\": \"test.a\", \"value\": {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}]";
            var source = new InMemoryDataSource().Add("test.range_number", Envelope("test.range_number", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            if (expectPass)
            {
                Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            }
            else
            {
                Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "value");
                Assert.Contains(report.Issues, i => i.Message.Contains("(0, +∞)"));
            }
        }

        [Fact]
        public void NumberField_MaxExclusive_EqualToMax_Fails()
        {
            var schema = TopLevelNumberRangeSchema(FieldRange.Range(max: 1, maxExclusive: true));
            var rows = "[{\"id\": \"test.a\", \"value\": 1}]";
            var source = new InMemoryDataSource().Add("test.range_number", Envelope("test.range_number", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "value");
        }

        [Fact]
        public void IntField_BelowMin_ReportsFieldRange()
        {
            var schema = new TableSchema("test.range_int", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("count", FieldKind.Int, required: true, description: "示例整数")
                    .WithRange(FieldRange.Range(min: 1)),
            });
            var rows = "[{\"id\": \"test.a\", \"count\": 0}]";
            var source = new InMemoryDataSource().Add("test.range_int", Envelope("test.range_int", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "count");
        }

        [Fact]
        public void IntField_AtMin_Passes()
        {
            var schema = new TableSchema("test.range_int", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("count", FieldKind.Int, required: true, description: "示例整数")
                    .WithRange(FieldRange.Range(min: 1)),
            });
            var rows = "[{\"id\": \"test.a\", \"count\": 1}]";
            var source = new InMemoryDataSource().Add("test.range_int", Envelope("test.range_int", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        /// <summary>省略未登记 Range 的可选字段/省略登记了 Range 的可选字段均不报错——Range 只在
        /// 字段"存在"时才检查数值，不改变 required 语义（同 ADR-0019 子结构未登记时的向后兼容
        /// 惯例）。</summary>
        [Fact]
        public void NumberField_WithRange_OmittedOptionalField_NoError()
        {
            var schema = TopLevelNumberRangeSchema(FieldRange.Range(min: 0, max: 1));
            var rows = "[{\"id\": \"test.a\"}]";
            var source = new InMemoryDataSource().Add("test.range_number", Envelope("test.range_number", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        /// <summary>类型错误优先于范围错误：值不是 Number 时只报 field_type，不会因为"无法转换成
        /// 数字参与范围比较"额外报 field_range（ValidateFieldValue 的 Int/Number 分支先判类型，
        /// 类型不对时 ValidateFieldRange 根本不会被调用，见 DataRegistry 判断记录）。</summary>
        [Fact]
        public void NumberField_WrongType_OnlyReportsFieldType_NotFieldRange()
        {
            var schema = TopLevelNumberRangeSchema(FieldRange.Range(min: 0, max: 1));
            var rows = "[{\"id\": \"test.a\", \"value\": \"not_a_number\"}]";
            var source = new InMemoryDataSource().Add("test.range_number", Envelope("test.range_number", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "value");
            Assert.DoesNotContain(report.Issues, i => i.Check == "field_range" && i.Field == "value");
        }

        // -----------------------------------------------------------------
        // 子结构（Object.Fields）与变体（Variants）内字段路径
        // -----------------------------------------------------------------

        [Fact]
        public void ObjectFields_NestedRangeField_ReportsFieldRangeWithNestedPath()
        {
            var schema = new TableSchema("test.range_nested", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("info", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("age", FieldKind.Int, required: false, description: "年龄")
                        .WithRange(FieldRange.Range(min: 0)),
                }),
            });
            var rows = "[{\"id\": \"test.a\", \"info\": {\"age\": -1}}]";
            var source = new InMemoryDataSource().Add("test.range_nested", Envelope("test.range_nested", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "info.age");
        }

        [Fact]
        public void Variants_CaseFieldRange_ReportsFieldRangeWithVariantPath()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                ["periodic"] = new[]
                {
                    new FieldSchema("interval", FieldKind.Number, required: true, description: "周期间隔")
                        .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
                },
            };
            var schema = new TableSchema("test.range_variant", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("entry", FieldKind.Object, required: true,
                    variants: new VariantSchema("kind", cases)),
            });
            var rows = "[{\"id\": \"test.a\", \"entry\": {\"kind\": \"periodic\", \"interval\": 0}}]";
            var source = new InMemoryDataSource().Add("test.range_variant", Envelope("test.range_variant", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "entry.interval");
        }

        [Fact]
        public void Variants_CaseFieldRange_ValidValue_Passes()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                ["periodic"] = new[]
                {
                    new FieldSchema("interval", FieldKind.Number, required: true, description: "周期间隔")
                        .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
                },
            };
            var schema = new TableSchema("test.range_variant", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("entry", FieldKind.Object, required: true,
                    variants: new VariantSchema("kind", cases)),
            });
            var rows = "[{\"id\": \"test.a\", \"entry\": {\"kind\": \"periodic\", \"interval\": 1}}]";
            var source = new InMemoryDataSource().Add("test.range_variant", Envelope("test.range_variant", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }
    }
}
