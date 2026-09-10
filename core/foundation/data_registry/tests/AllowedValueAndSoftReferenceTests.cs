using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 消费方反馈第 28/29 条（04 第 3.4 节勘误"IdList/Id 固定取值登记""软引用元数据"）：
    /// <see cref="FieldSchema.WithAllowedValues"/>/<see cref="FieldSchema.WithSoftReference"/> 的
    /// With* 行为、重复设置防护，以及 <c>DataRegistry.LoadAll</c> 的 <c>field_allowed_value</c>
    /// 检查项（IdList 逐元素 + Id 单值）、SoftReference 不参与引用完整性校验。<c>SchemaAudit</c> 的
    /// <c>idlist_allowed_values_conflict</c>/<c>soft_reference_kind</c> 自洽检查覆盖见
    /// <c>presentation/assembly/tests/SchemaAuditTests.cs</c>"消费方反馈第 28/29 条"分节，不在本文件
    /// 重复。
    /// </summary>
    public sealed class AllowedValueAndSoftReferenceTests
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
        // FieldSchema.WithAllowedValues
        // -----------------------------------------------------------------

        [Fact]
        public void WithAllowedValues_SetsValues_ReturnsSameInstance()
        {
            var field = new FieldSchema("flags", FieldKind.IdList, required: false, description: "示例");
            var values = new[] { new Id("flag.a"), new Id("flag.b") };

            var returned = field.WithAllowedValues(values);

            Assert.Same(field, returned);
            Assert.Equal(values, field.AllowedValues);
        }

        [Fact]
        public void WithAllowedValues_Null_Throws()
        {
            var field = new FieldSchema("flags", FieldKind.IdList, required: false, description: "示例");
            Assert.Throws<ArgumentNullException>(() => field.WithAllowedValues(null!));
        }

        [Fact]
        public void WithAllowedValues_EmptyList_Throws()
        {
            var field = new FieldSchema("flags", FieldKind.IdList, required: false, description: "示例");
            Assert.Throws<ArgumentException>(() => field.WithAllowedValues(Array.Empty<Id>()));
        }

        [Fact]
        public void WithAllowedValues_CalledTwice_Throws()
        {
            var field = new FieldSchema("flags", FieldKind.IdList, required: false, description: "示例")
                .WithAllowedValues(new[] { new Id("flag.a") });
            Assert.Throws<InvalidOperationException>(() => field.WithAllowedValues(new[] { new Id("flag.b") }));
        }

        [Fact]
        public void WithAllowedValues_OnIdKind_DoesNotThrow()
        {
            var field = new FieldSchema("flag", FieldKind.Id, required: false, description: "示例");
            var returned = field.WithAllowedValues(new[] { new Id("flag.a") });
            Assert.Same(field, returned);
        }

        // -----------------------------------------------------------------
        // FieldSchema.WithSoftReference
        // -----------------------------------------------------------------

        [Fact]
        public void WithSoftReference_Table_SetsTable()
        {
            var field = new FieldSchema("target_ref", FieldKind.Id, required: false, description: "示例")
                .WithSoftReference(table: "ai.rotation");

            Assert.Equal("ai.rotation", field.SoftReferenceTable);
            Assert.Null(field.SoftReferenceDomain);
        }

        [Fact]
        public void WithSoftReference_Domain_SetsDomain()
        {
            var field = new FieldSchema("target_ref", FieldKind.Id, required: false, description: "示例")
                .WithSoftReference(domain: "ai");

            Assert.Equal("ai", field.SoftReferenceDomain);
            Assert.Null(field.SoftReferenceTable);
        }

        [Fact]
        public void WithSoftReference_NeitherTableNorDomain_Throws()
        {
            var field = new FieldSchema("target_ref", FieldKind.Id, required: false, description: "示例");
            Assert.Throws<ArgumentException>(() => field.WithSoftReference());
        }

        [Fact]
        public void WithSoftReference_BothTableAndDomain_Throws()
        {
            var field = new FieldSchema("target_ref", FieldKind.Id, required: false, description: "示例");
            Assert.Throws<ArgumentException>(() => field.WithSoftReference(table: "a.b", domain: "a"));
        }

        [Fact]
        public void WithSoftReference_CalledTwice_Throws()
        {
            var field = new FieldSchema("target_ref", FieldKind.Id, required: false, description: "示例")
                .WithSoftReference(table: "a.b");
            Assert.Throws<InvalidOperationException>(() => field.WithSoftReference(table: "c.d"));
        }

        // -----------------------------------------------------------------
        // DataRegistry.LoadAll：field_allowed_value（Id 单值）
        // -----------------------------------------------------------------

        private static TableSchema IdAllowedValueSchema() => new TableSchema(
            "test.id_allowed", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("flag", FieldKind.Id, required: false, description: "固定取值")
                    .WithAllowedValues(new[] { new Id("flag.a"), new Id("flag.b") }),
            });

        [Fact]
        public void IdField_AllowedValue_NoIssue()
        {
            var source = new InMemoryDataSource().Add("test.id_allowed",
                Envelope("test.id_allowed", 1, "[{\"id\": \"test.s1\", \"flag\": \"flag.a\"}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(IdAllowedValueSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void IdField_DisallowedValue_ReportsFieldAllowedValue()
        {
            var source = new InMemoryDataSource().Add("test.id_allowed",
                Envelope("test.id_allowed", 1, "[{\"id\": \"test.s1\", \"flag\": \"flag.unknown\"}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(IdAllowedValueSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_allowed_value" && i.Field == "flag" && i.Message.Contains("flag.unknown"));
        }

        [Fact]
        public void IdField_OmittedOptionalField_NoError()
        {
            var source = new InMemoryDataSource().Add("test.id_allowed",
                Envelope("test.id_allowed", 1, "[{\"id\": \"test.s1\"}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(IdAllowedValueSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // DataRegistry.LoadAll：field_allowed_value（IdList 逐元素）
        // -----------------------------------------------------------------

        private static TableSchema IdListAllowedValueSchema() => new TableSchema(
            "test.idlist_allowed", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("flags", FieldKind.IdList, required: false, description: "固定取值集合")
                    .WithAllowedValues(new[] { new Id("flag.a"), new Id("flag.b") }),
            });

        [Fact]
        public void IdListField_AllElementsAllowed_NoIssue()
        {
            var source = new InMemoryDataSource().Add("test.idlist_allowed",
                Envelope("test.idlist_allowed", 1, "[{\"id\": \"test.s1\", \"flags\": [\"flag.a\", \"flag.b\"]}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(IdListAllowedValueSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void IdListField_OneElementDisallowed_ReportsFieldAllowedValueWithElementPath()
        {
            var source = new InMemoryDataSource().Add("test.idlist_allowed",
                Envelope("test.idlist_allowed", 1, "[{\"id\": \"test.s1\", \"flags\": [\"flag.a\", \"flag.bogus\"]}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(IdListAllowedValueSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_allowed_value" && i.Field == "flags[1]" && i.Message.Contains("flag.bogus"));
            // 只报告非法元素本身，不误伤合法元素。
            Assert.DoesNotContain(report.Issues, i => i.Check == "field_allowed_value" && i.Field == "flags[0]");
        }

        [Fact]
        public void IdListField_EmptyArray_NoIssue()
        {
            var source = new InMemoryDataSource().Add("test.idlist_allowed",
                Envelope("test.idlist_allowed", 1, "[{\"id\": \"test.s1\", \"flags\": []}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(IdListAllowedValueSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // DataRegistry.LoadAll：SoftReference 不参与引用完整性校验（消费方反馈第 29 条）
        // -----------------------------------------------------------------

        [Fact]
        public void SoftReference_TargetTableNotLoaded_DoesNotBlock()
        {
            var schema = new TableSchema("test.soft_ref_source", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("target_ref", FieldKind.Id, required: false, description: "软引用，不参与加载期校验")
                    .WithSoftReference(table: "test.does_not_exist_and_is_not_loaded"),
            });
            var source = new InMemoryDataSource().Add("test.soft_ref_source",
                Envelope("test.soft_ref_source", 1, "[{\"id\": \"test.s1\", \"target_ref\": \"anything.goes\"}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == "reference_integrity");
        }
    }
}
