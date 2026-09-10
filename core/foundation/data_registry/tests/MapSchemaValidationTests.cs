using System;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// ADR-0024（04 第 3.3 节"映射登记"）：<see cref="MapSchema"/>/<see cref="FieldSchema.WithMap"/>
    /// 的值级不变量，以及 <c>DataRegistry</c> 加载期对映射键（并入 <c>reference_integrity</c>）与
    /// 映射值（递归复用既有种类/范围/子结构校验）的处理。<c>SchemaAudit</c> 的 <c>field_map_kind</c>/
    /// <c>field_map_conflict</c> 自洽检查覆盖在 <c>presentation/assembly/tests/SchemaAuditTests.cs</c>
    /// （见该文件"ADR-0024"分节），不在本文件重复。
    /// </summary>
    public sealed class MapSchemaValidationTests
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
        // MapSchema 静态工厂：值级不变量（构造期立即拒绝）
        // -----------------------------------------------------------------

        [Fact]
        public void ReferenceKeyTable_NullOrEmpty_Throws()
        {
            var value = new FieldSchema("value", FieldKind.Number, required: true, description: "示例");
            Assert.Throws<ArgumentException>(() => MapSchema.ReferenceKeyTable(null!, value));
            Assert.Throws<ArgumentException>(() => MapSchema.ReferenceKeyTable("", value));
        }

        [Fact]
        public void ReferenceKeyDomain_NullOrEmpty_Throws()
        {
            var value = new FieldSchema("value", FieldKind.Number, required: true, description: "示例");
            Assert.Throws<ArgumentException>(() => MapSchema.ReferenceKeyDomain(null!, value));
            Assert.Throws<ArgumentException>(() => MapSchema.ReferenceKeyDomain("", value));
        }

        [Fact]
        public void FreeKeyed_NullOrWhitespaceReason_Throws()
        {
            var value = new FieldSchema("value", FieldKind.Number, required: true, description: "示例");
            Assert.Throws<ArgumentException>(() => MapSchema.FreeKeyed(null!, value));
            Assert.Throws<ArgumentException>(() => MapSchema.FreeKeyed("   ", value));
        }

        [Fact]
        public void AnyFactory_NullValueSchema_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MapSchema.ReferenceKeyTable("stat.definition", null!));
            Assert.Throws<ArgumentNullException>(() => MapSchema.ReferenceKeyDomain("stat", null!));
            Assert.Throws<ArgumentNullException>(() => MapSchema.FreeKeyed("理由", null!));
        }

        // -----------------------------------------------------------------
        // FieldSchema.WithMap：单次设置、链式返回
        // -----------------------------------------------------------------

        [Fact]
        public void WithMap_SetTwice_Throws()
        {
            var field = new FieldSchema("base_stats", FieldKind.Object, required: false, description: "示例");
            field.WithMap(MapSchema.FreeKeyed("理由", new FieldSchema("value", FieldKind.Number, required: true, description: "值")));

            Assert.Throws<InvalidOperationException>(() =>
                field.WithMap(MapSchema.FreeKeyed("理由2", new FieldSchema("value", FieldKind.Number, required: true, description: "值"))));
        }

        [Fact]
        public void WithMap_Null_Throws()
        {
            var field = new FieldSchema("base_stats", FieldKind.Object, required: false, description: "示例");
            Assert.Throws<ArgumentNullException>(() => field.WithMap(null!));
        }

        [Fact]
        public void WithMap_ReturnsSameInstance_ForChaining()
        {
            var field = new FieldSchema("base_stats", FieldKind.Object, required: false, description: "示例");
            var returned = field.WithMap(MapSchema.FreeKeyed("理由", new FieldSchema("value", FieldKind.Number, required: true, description: "值")));

            Assert.Same(field, returned);
            Assert.NotNull(field.Map);
        }

        /// <summary>WithMap 刻意不检查 Kind（同 WithRange 判断记录）：挂在 Array 字段上不抛异常，只由
        /// SchemaAudit 的 field_map_kind 事后报告——本用例只证明"不炸"。</summary>
        [Fact]
        public void WithMap_OnNonObjectKind_DoesNotThrow()
        {
            var field = new FieldSchema("value", FieldKind.Array, required: false, description: "示例");
            var returned = field.WithMap(MapSchema.FreeKeyed("理由", new FieldSchema("value", FieldKind.Number, required: true, description: "值")));

            Assert.Same(field, returned);
        }

        // -----------------------------------------------------------------
        // DataRegistry.LoadAll：ReferenceKeyTable
        // -----------------------------------------------------------------

        private static TableSchema StatTableSchema() => new TableSchema(
            "test.stat_def", "id", 1,
            new[] { new FieldSchema("id", FieldKind.Id, required: true) });

        private static TableSchema BaseStatsTableSchema() => new TableSchema(
            "test.base_stats_owner", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("base_stats", FieldKind.Object, required: false, description: "示例")
                    .WithMap(MapSchema.ReferenceKeyTable("test.stat_def",
                        new FieldSchema("value", FieldKind.Number, required: true, description: "属性基础值"))),
            });

        [Fact]
        public void ReferenceKeyTable_KeyExists_Passes()
        {
            var source = new InMemoryDataSource()
                .Add("test.stat_def", Envelope("test.stat_def", 1, "[{\"id\": \"test.strength\"}]"))
                .Add("test.base_stats_owner", Envelope("test.base_stats_owner", 1,
                    "[{\"id\": \"test.a\", \"base_stats\": {\"test.strength\": 10}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(StatTableSchema());
            registry.RegisterSchema(BaseStatsTableSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ReferenceKeyTable_KeyMissing_ReportsReferenceIntegrityWithBracketPath()
        {
            var source = new InMemoryDataSource()
                .Add("test.stat_def", Envelope("test.stat_def", 1, "[{\"id\": \"test.strength\"}]"))
                .Add("test.base_stats_owner", Envelope("test.base_stats_owner", 1,
                    "[{\"id\": \"test.a\", \"base_stats\": {\"test.no_such_stat\": 10}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(StatTableSchema());
            registry.RegisterSchema(BaseStatsTableSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "reference_integrity" && i.Field == "base_stats[test.no_such_stat]");
        }

        [Fact]
        public void ReferenceKeyTable_EmptyMap_NoIssues()
        {
            var source = new InMemoryDataSource()
                .Add("test.stat_def", Envelope("test.stat_def", 1, "[]"))
                .Add("test.base_stats_owner", Envelope("test.base_stats_owner", 1,
                    "[{\"id\": \"test.a\", \"base_stats\": {}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(StatTableSchema());
            registry.RegisterSchema(BaseStatsTableSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ReferenceKeyTable_ValueWrongType_ReportsFieldTypeWithBracketPath()
        {
            var source = new InMemoryDataSource()
                .Add("test.stat_def", Envelope("test.stat_def", 1, "[{\"id\": \"test.strength\"}]"))
                .Add("test.base_stats_owner", Envelope("test.base_stats_owner", 1,
                    "[{\"id\": \"test.a\", \"base_stats\": {\"test.strength\": \"not_a_number\"}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(StatTableSchema());
            registry.RegisterSchema(BaseStatsTableSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_type" && i.Field == "base_stats[test.strength]");
        }

        // -----------------------------------------------------------------
        // DataRegistry.LoadAll：ReferenceKeyDomain
        // -----------------------------------------------------------------

        private static TableSchema DomainOwnerSchema() => new TableSchema(
            "test.domain_owner", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("growth", FieldKind.Object, required: false, description: "示例")
                    .WithMap(MapSchema.ReferenceKeyDomain("test",
                        new FieldSchema("value", FieldKind.Number, required: true, description: "成长值"))),
            });

        [Fact]
        public void ReferenceKeyDomain_KeyExistsInDomain_Passes()
        {
            var source = new InMemoryDataSource()
                .Add("test.stat_def", Envelope("test.stat_def", 1, "[{\"id\": \"test.strength\"}]"))
                .Add("test.domain_owner", Envelope("test.domain_owner", 1,
                    "[{\"id\": \"test.a\", \"growth\": {\"test.strength\": 2}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(StatTableSchema());
            registry.RegisterSchema(DomainOwnerSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ReferenceKeyDomain_KeyDomainMismatch_ReportsReferenceIntegrity()
        {
            var source = new InMemoryDataSource()
                .Add("test.stat_def", Envelope("test.stat_def", 1, "[{\"id\": \"test.strength\"}]"))
                .Add("test.domain_owner", Envelope("test.domain_owner", 1,
                    "[{\"id\": \"test.a\", \"growth\": {\"other.strength\": 2}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(StatTableSchema());
            registry.RegisterSchema(DomainOwnerSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "reference_integrity" && i.Field == "growth[other.strength]");
        }

        [Fact]
        public void ReferenceKeyDomain_KeyNotFoundInAnyDomainTable_ReportsReferenceIntegrity()
        {
            var source = new InMemoryDataSource()
                .Add("test.stat_def", Envelope("test.stat_def", 1, "[{\"id\": \"test.strength\"}]"))
                .Add("test.domain_owner", Envelope("test.domain_owner", 1,
                    "[{\"id\": \"test.a\", \"growth\": {\"test.no_such_stat\": 2}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(StatTableSchema());
            registry.RegisterSchema(DomainOwnerSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "reference_integrity" && i.Field == "growth[test.no_such_stat]");
        }

        // -----------------------------------------------------------------
        // DataRegistry.LoadAll：FreeKeyed（不做键校验）
        // -----------------------------------------------------------------

        private static TableSchema FreeKeyedOwnerSchema() => new TableSchema(
            "test.free_keyed_owner", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("material_params", FieldKind.Object, required: false, description: "示例")
                    .WithMap(MapSchema.FreeKeyed("材质参数名，自由字符串",
                        new FieldSchema("value", FieldKind.Number, required: true, description: "参数值"))),
            });

        [Fact]
        public void FreeKeyed_AnyKey_NoReferenceCheck()
        {
            var source = new InMemoryDataSource()
                .Add("test.free_keyed_owner", Envelope("test.free_keyed_owner", 1,
                    "[{\"id\": \"test.a\", \"material_params\": {\"emission_strength\": 0.5, \"not.an.id.either\": 1}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(FreeKeyedOwnerSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void FreeKeyed_ValueOutOfRange_ReportsFieldRangeWithBracketPath()
        {
            var schema = new TableSchema(
                "test.free_keyed_range_owner", "id", 1,
                new[]
                {
                    new FieldSchema("id", FieldKind.Id, required: true),
                    new FieldSchema("material_params", FieldKind.Object, required: false, description: "示例")
                        .WithMap(MapSchema.FreeKeyed("材质参数名，自由字符串",
                            new FieldSchema("value", FieldKind.Number, required: true, description: "参数值")
                                .WithRange(FieldRange.Range(min: 0, max: 1)))),
                });
            var source = new InMemoryDataSource().Add("test.free_keyed_range_owner", Envelope("test.free_keyed_range_owner", 1,
                "[{\"id\": \"test.a\", \"material_params\": {\"emission_strength\": 5}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_range" && i.Field == "material_params[emission_strength]");
        }

        // -----------------------------------------------------------------
        // 映射值本身是 Object 子结构：递归复用既有必填/类型检查
        // -----------------------------------------------------------------

        [Fact]
        public void MapValue_ObjectSubstructure_MissingRequiredSubfield_ReportsNestedPath()
        {
            var schema = new TableSchema(
                "test.anchor_owner", "id", 1,
                new[]
                {
                    new FieldSchema("id", FieldKind.Id, required: true),
                    new FieldSchema("anchor_points", FieldKind.Object, required: false, description: "示例")
                        .WithMap(MapSchema.FreeKeyed("锚点名，自由字符串", new FieldSchema("<anchor>", FieldKind.Object, required: true, fields: new[]
                        {
                            new FieldSchema("parent_layer", FieldKind.String, required: true, description: "所属层名"),
                        }, description: "锚点定义"))),
                });
            var source = new InMemoryDataSource().Add("test.anchor_owner", Envelope("test.anchor_owner", 1,
                "[{\"id\": \"test.a\", \"anchor_points\": {\"weapon_hand\": {}}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "required_field" && i.Field == "anchor_points[weapon_hand].parent_layer");
        }

        // -----------------------------------------------------------------
        // Map 优先于 Fields/Variants（登记冲突场景下的运行期行为；SchemaAudit 静态门禁另行覆盖）
        // -----------------------------------------------------------------

        [Fact]
        public void MapAndFields_BothSet_MapTakesPriorityAtRuntime()
        {
            // FieldSchema 构造函数本身不允许同时传 fields 与 variants（互斥检查），但 Map 是事后经
            // WithMap 挂载的，物理上可以与 Fields 共存——DataRegistry.ValidateObjectField 按判断记录
            // 选择 Map 优先，这里验证的正是这条运行期行为（登记冲突本身由 SchemaAudit 报告）。
            var field = new FieldSchema("payload", FieldKind.Object, required: false, fields: new[]
            {
                new FieldSchema("known", FieldKind.String, required: true, description: "固定键"),
            }, description: "冲突登记场景").WithMap(MapSchema.FreeKeyed("理由",
                new FieldSchema("value", FieldKind.Number, required: true, description: "值")));

            var schema = new TableSchema("test.conflict_owner", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                field,
            });
            var source = new InMemoryDataSource().Add("test.conflict_owner", Envelope("test.conflict_owner", 1,
                "[{\"id\": \"test.a\", \"payload\": {\"anything\": 1}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            // Map 优先：没有因为 "known" 缺失报 required_field，值 1 是 Number 合法。
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }
    }
}
