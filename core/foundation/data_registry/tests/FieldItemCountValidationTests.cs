using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 消费方反馈第 60 条（04 第 4 节勘误"元素数量约束"）：<see cref="FieldSchema.WithItemCount"/> 挂到
    /// 具体字段后，<c>DataRegistry.LoadAll</c> 加载期 <c>field_item_count</c> 检查项——Array/IdList 两种
    /// 种类、含/不含上界、顶层与嵌套路径、省略可选字段不报错、类型错误不重复报告。
    /// <see cref="FieldSchema.WithItemCount"/> 本身的挂载期校验（种类限制/参数校验/重复设置防护）见
    /// <c>SchemaMetadataTests</c>"消费方反馈第 60 条"分节，不在本文件重复。
    /// </summary>
    public sealed class FieldItemCountValidationTests
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
        // 顶层 Array 字段：min 单独登记、min+max 登记
        // -----------------------------------------------------------------

        private static TableSchema TopLevelArrayMinSchema() => new TableSchema(
            "test.item_count_array_min", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("tags", FieldKind.Array, required: false,
                    item: new FieldSchema("<tag>", FieldKind.String, required: true))
                    .WithItemCount(2),
            });

        [Fact]
        public void ArrayField_BelowMin_ReportsFieldItemCount()
        {
            var rows = "[{\"id\": \"test.a\", \"tags\": [\"only_one\"]}]";
            var source = new InMemoryDataSource().Add("test.item_count_array_min", Envelope("test.item_count_array_min", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(TopLevelArrayMinSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_item_count" && i.Field == "tags"
                && i.Message.Contains("1") && i.Message.Contains("[2, +∞)"));
        }

        [Fact]
        public void ArrayField_AtMin_Passes()
        {
            var rows = "[{\"id\": \"test.a\", \"tags\": [\"a\", \"b\"]}]";
            var source = new InMemoryDataSource().Add("test.item_count_array_min", Envelope("test.item_count_array_min", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(TopLevelArrayMinSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ArrayField_OmittedOptionalField_NoError()
        {
            var rows = "[{\"id\": \"test.a\"}]";
            var source = new InMemoryDataSource().Add("test.item_count_array_min", Envelope("test.item_count_array_min", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(TopLevelArrayMinSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        private static TableSchema TopLevelArrayMinMaxSchema() => new TableSchema(
            "test.item_count_array_range", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("slots", FieldKind.Array, required: true,
                    item: new FieldSchema("<slot>", FieldKind.Int, required: true))
                    .WithItemCount(1, 3),
            });

        [Theory]
        [InlineData("[]", false)]
        [InlineData("[1]", true)]
        [InlineData("[1,2,3]", true)]
        [InlineData("[1,2,3,4]", false)]
        public void ArrayField_MinMax_BoundaryBehavior(string itemsJson, bool expectPass)
        {
            var rows = "[{\"id\": \"test.a\", \"slots\": " + itemsJson + "}]";
            var source = new InMemoryDataSource().Add("test.item_count_array_range", Envelope("test.item_count_array_range", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(TopLevelArrayMinMaxSchema());

            var report = registry.LoadAll();

            if (expectPass)
            {
                Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            }
            else
            {
                Assert.Contains(report.Issues, i => i.Check == "field_item_count" && i.Field == "slots");
            }
        }

        /// <summary>类型错误优先于元素数量错误：值不是 Array 时只报 field_type，不会因为"无法数出元素数
        /// 参与区间比较"额外报 field_item_count（ValidateArrayField 先判类型，类型不对时直接返回，见
        /// DataRegistry 判断记录）。</summary>
        [Fact]
        public void ArrayField_WrongType_OnlyReportsFieldType_NotFieldItemCount()
        {
            var rows = "[{\"id\": \"test.a\", \"slots\": \"not_an_array\"}]";
            var source = new InMemoryDataSource().Add("test.item_count_array_range", Envelope("test.item_count_array_range", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(TopLevelArrayMinMaxSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "slots");
            Assert.DoesNotContain(report.Issues, i => i.Check == "field_item_count" && i.Field == "slots");
        }

        // -----------------------------------------------------------------
        // 顶层 IdList 字段
        // -----------------------------------------------------------------

        private static TableSchema TopLevelIdListSchema() => new TableSchema(
            "test.item_count_idlist", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("members", FieldKind.IdList, required: false)
                    .WithFreeIds("测试用自由 id 列表")
                    .WithItemCount(2, 4),
            });

        [Fact]
        public void IdListField_BelowMin_ReportsFieldItemCount()
        {
            var rows = "[{\"id\": \"test.a\", \"members\": [\"m.one\"]}]";
            var source = new InMemoryDataSource().Add("test.item_count_idlist", Envelope("test.item_count_idlist", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(TopLevelIdListSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_item_count" && i.Field == "members");
        }

        [Fact]
        public void IdListField_AboveMax_ReportsFieldItemCount()
        {
            var rows = "[{\"id\": \"test.a\", \"members\": [\"m.a\", \"m.b\", \"m.c\", \"m.d\", \"m.e\"]}]";
            var source = new InMemoryDataSource().Add("test.item_count_idlist", Envelope("test.item_count_idlist", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(TopLevelIdListSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_item_count" && i.Field == "members"
                && i.Message.Contains("[2, 4]"));
        }

        [Fact]
        public void IdListField_WithinRange_Passes()
        {
            var rows = "[{\"id\": \"test.a\", \"members\": [\"m.a\", \"m.b\", \"m.c\"]}]";
            var source = new InMemoryDataSource().Add("test.item_count_idlist", Envelope("test.item_count_idlist", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(TopLevelIdListSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // 嵌套路径（Object.Fields 内的 Array 字段）
        // -----------------------------------------------------------------

        [Fact]
        public void ObjectFields_NestedArrayItemCount_ReportsFieldItemCountWithNestedPath()
        {
            var schema = new TableSchema("test.item_count_nested", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("loadout", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("slots", FieldKind.Array, required: false,
                        item: new FieldSchema("<slot>", FieldKind.Int, required: true))
                        .WithItemCount(1),
                }),
            });
            var rows = "[{\"id\": \"test.a\", \"loadout\": {\"slots\": []}}]";
            var source = new InMemoryDataSource().Add("test.item_count_nested", Envelope("test.item_count_nested", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_item_count" && i.Field == "loadout.slots");
        }

        /// <summary>数组元素本身（Array.Item）内再套一层 Array 字段——路径记法沿用既有 "[]" 惯例，
        /// 与 <c>SchemaFieldItemCountExport</c>/<c>SchemaFieldRangeExport</c> 静态收集侧的路径记法
        /// 一致（本用例是运行期实际数据路径，含具体下标）。</summary>
        [Fact]
        public void ArrayItem_NestedIdListItemCount_ReportsFieldItemCountWithIndexedPath()
        {
            var groupItem = new FieldSchema("<group>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("members", FieldKind.IdList, required: true)
                    .WithFreeIds("测试用自由 id 列表")
                    .WithItemCount(2),
            });
            var schema = new TableSchema("test.item_count_array_item_nested", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("groups", FieldKind.Array, required: true, item: groupItem),
            });
            var rows = "[{\"id\": \"test.a\", \"groups\": [{\"members\": [\"m.only_one\"]}]}]";
            var source = new InMemoryDataSource().Add("test.item_count_array_item_nested", Envelope("test.item_count_array_item_nested", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_item_count" && i.Field == "groups[0].members");
        }
    }
}
