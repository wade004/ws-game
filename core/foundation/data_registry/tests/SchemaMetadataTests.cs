using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// ADR-0022（04 第 3.4 节"表级归属元数据""字段分组元数据""时间字段单位与作用域""IdList 引用
    /// 目标"）：新增元数据的 With* 行为、默认值、重复设置防护，以及 IdList 引用完整性
    /// （<c>DataRegistry.LoadAll</c> 覆盖数组元素）、<see cref="TimeModelRules"/> 读取元数据的判定。
    /// <c>SchemaAudit</c> 的五项新自洽检查（table_ownership/field_group/time_scope_declared/
    /// idlist_reference_target/time_unit_missing）覆盖见
    /// <c>presentation/assembly/tests/SchemaAuditTests.cs</c>"ADR-0022"分节，不在本文件重复。
    /// </summary>
    public sealed class SchemaMetadataTests
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
        // TableSchema.WithOwnership / Domain 默认值 / WithDomain / WithTimeScope
        // -----------------------------------------------------------------

        [Fact]
        public void WithOwnership_SetsLayerAndModule()
        {
            var table = new TableSchema("found.sample", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            }).WithOwnership(SchemaLayer.Foundation, "foundation");

            Assert.Equal(SchemaLayer.Foundation, table.Layer);
            Assert.Equal("foundation", table.Module);
        }

        [Fact]
        public void WithOwnership_CalledTwice_Throws()
        {
            var table = new TableSchema("found.sample2", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            }).WithOwnership(SchemaLayer.Foundation, "foundation");

            Assert.Throws<System.InvalidOperationException>(() => table.WithOwnership(SchemaLayer.Numbers, "stats"));
        }

        [Fact]
        public void WithOwnership_BlankModule_Throws()
        {
            var table = new TableSchema("found.sample3", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            });

            Assert.Throws<System.ArgumentException>(() => table.WithOwnership(SchemaLayer.Foundation, "  "));
        }

        [Theory]
        [InlineData("found.sample", "found")]
        [InlineData("skill.def", "skill")]
        [InlineData("camera_profile", "camera_profile")] // 单段名：默认取整个表名（未 WithDomain 前）
        public void Domain_DefaultsToFirstSegment(string tableName, string expectedDomain)
        {
            var table = new TableSchema(tableName, "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            });

            Assert.Equal(expectedDomain, table.Domain);
        }

        [Fact]
        public void WithDomain_OverridesDefault()
        {
            var table = new TableSchema("camera_profile", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            }).WithDomain("camera");

            Assert.Equal("camera", table.Domain);
        }

        [Fact]
        public void WithDomain_CalledTwice_Throws()
        {
            var table = new TableSchema("camera_profile", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            }).WithDomain("camera");

            Assert.Throws<System.InvalidOperationException>(() => table.WithDomain("camera2"));
        }

        [Fact]
        public void TimeScope_DefaultsToNone_WithTimeScope_Overrides()
        {
            var table = new TableSchema("skill.def", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            });
            Assert.Equal(TimeScope.None, table.TimeScope);

            table.WithTimeScope(TimeScope.Combat);
            Assert.Equal(TimeScope.Combat, table.TimeScope);
        }

        [Fact]
        public void WithTimeScope_CalledTwice_Throws()
        {
            var table = new TableSchema("skill.def", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            }).WithTimeScope(TimeScope.Combat);

            Assert.Throws<System.InvalidOperationException>(() => table.WithTimeScope(TimeScope.Exploration));
        }

        // -----------------------------------------------------------------
        // FieldSchema.Group：计算默认值 + WithGroup 覆盖
        // -----------------------------------------------------------------

        [Fact]
        public void Group_PrimaryKey_DefaultsToBasic()
        {
            var field = new FieldSchema("id", FieldKind.Id, required: true);
            Assert.Equal(FieldGroup.Basic, field.Group);
        }

        [Fact]
        public void Group_TextKey_DefaultsToBasic()
        {
            var field = new FieldSchema("name_key", FieldKind.TextKey, required: true);
            Assert.Equal(FieldGroup.Basic, field.Group);
        }

        [Theory]
        [InlineData(FieldKind.Reference)]
        [InlineData(FieldKind.IdList)]
        [InlineData(FieldKind.Id)]
        public void Group_ReferenceLikeKinds_DefaultToReference(FieldKind kind)
        {
            var field = new FieldSchema("target_ref", kind, required: false);
            Assert.Equal(FieldGroup.Reference, field.Group);
        }

        [Theory]
        [InlineData(FieldKind.Number)]
        [InlineData(FieldKind.Int)]
        public void Group_NumericKinds_DefaultToNumeric(FieldKind kind)
        {
            var field = new FieldSchema("amount", kind, required: false);
            Assert.Equal(FieldGroup.Numeric, field.Group);
        }

        [Fact]
        public void Group_PresentationNameHint_DefaultsToPresentation()
        {
            var field = new FieldSchema("icon_id", FieldKind.String, required: false);
            Assert.Equal(FieldGroup.Presentation, field.Group);
        }

        [Fact]
        public void Group_NoHint_DefaultsToAdvanced()
        {
            var field = new FieldSchema("misc_flag", FieldKind.Bool, required: false);
            Assert.Equal(FieldGroup.Advanced, field.Group);
        }

        [Fact]
        public void WithGroup_OverridesDefault()
        {
            var field = new FieldSchema("amount", FieldKind.Number, required: false).WithGroup(FieldGroup.Advanced);
            Assert.Equal(FieldGroup.Advanced, field.Group);
        }

        [Fact]
        public void WithGroup_CalledTwice_Throws()
        {
            var field = new FieldSchema("amount", FieldKind.Number, required: false).WithGroup(FieldGroup.Advanced);
            Assert.Throws<System.InvalidOperationException>(() => field.WithGroup(FieldGroup.Basic));
        }

        // -----------------------------------------------------------------
        // FieldSchema.Unit / WithFreeIds
        // -----------------------------------------------------------------

        [Fact]
        public void Unit_DefaultsToNone_WithUnit_Overrides()
        {
            var field = new FieldSchema("duration", FieldKind.Number, required: false);
            Assert.Equal(FieldUnit.None, field.Unit);

            field.WithUnit(FieldUnit.Time);
            Assert.Equal(FieldUnit.Time, field.Unit);
        }

        [Fact]
        public void WithUnit_CalledTwice_Throws()
        {
            var field = new FieldSchema("duration", FieldKind.Number, required: false).WithUnit(FieldUnit.Time);
            Assert.Throws<System.InvalidOperationException>(() => field.WithUnit(FieldUnit.Percent));
        }

        [Fact]
        public void WithFreeIds_SetsFlagAndReason()
        {
            var field = new FieldSchema("tags", FieldKind.IdList, required: false).WithFreeIds("自由标签");
            Assert.True(field.FreeIds);
            Assert.Equal("自由标签", field.FreeIdsReason);
        }

        [Fact]
        public void WithFreeIds_BlankReason_Throws()
        {
            var field = new FieldSchema("tags", FieldKind.IdList, required: false);
            Assert.Throws<System.ArgumentException>(() => field.WithFreeIds(""));
        }

        [Fact]
        public void WithFreeIds_WhenReferenceTableAlreadySet_Throws()
        {
            var field = new FieldSchema("refs", FieldKind.IdList, required: false, referenceTable: "test.target");
            Assert.Throws<System.InvalidOperationException>(() => field.WithFreeIds("理由"));
        }

        [Fact]
        public void WithFreeIds_CalledTwice_Throws()
        {
            var field = new FieldSchema("tags", FieldKind.IdList, required: false).WithFreeIds("自由标签");
            Assert.Throws<System.InvalidOperationException>(() => field.WithFreeIds("再来一次"));
        }

        // -----------------------------------------------------------------
        // DataRegistry.LoadAll：IdList 元素的 reference_integrity 覆盖
        // -----------------------------------------------------------------

        private static TableSchema IdListTargetSchema() => new TableSchema(
            "test.idlist_target", "id", 1,
            new[] { new FieldSchema("id", FieldKind.Id, required: true) });

        private static TableSchema IdListSourceSchema() => new TableSchema(
            "test.idlist_source", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("refs", FieldKind.IdList, required: false, referenceTable: "test.idlist_target"),
            });

        [Fact]
        public void IdList_AllElementsExist_NoIssue()
        {
            var source = new InMemoryDataSource()
                .Add("test.idlist_target", Envelope("test.idlist_target", 1, "[{\"id\": \"test.a\"}, {\"id\": \"test.b\"}]"))
                .Add("test.idlist_source", Envelope("test.idlist_source", 1,
                    "[{\"id\": \"test.s1\", \"refs\": [\"test.a\", \"test.b\"]}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(IdListTargetSchema());
            registry.RegisterSchema(IdListSourceSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void IdList_OneElementMissing_ReportsReferenceIntegrityWithElementPath()
        {
            var source = new InMemoryDataSource()
                .Add("test.idlist_target", Envelope("test.idlist_target", 1, "[{\"id\": \"test.a\"}]"))
                .Add("test.idlist_source", Envelope("test.idlist_source", 1,
                    "[{\"id\": \"test.s1\", \"refs\": [\"test.a\", \"test.missing\"]}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(IdListTargetSchema());
            registry.RegisterSchema(IdListSourceSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "reference_integrity" && i.Field == "refs[1]" && i.Message.Contains("test.missing"));
        }

        [Fact]
        public void IdList_WithoutReferenceTarget_OnlyFormatCheckedNotExistence()
        {
            var noRefTarget = new TableSchema("test.idlist_free_source", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("tags", FieldKind.IdList, required: false),
            });
            var source = new InMemoryDataSource()
                .Add("test.idlist_free_source", Envelope("test.idlist_free_source", 1,
                    "[{\"id\": \"test.s1\", \"tags\": [\"tag.anything\"]}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(noRefTarget);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // TimeModelRules：读取元数据的公开判定入口
        // -----------------------------------------------------------------

        [Fact]
        public void TimeModelRules_GetTimeScope_ReadsTableMetadata()
        {
            var table = new TableSchema("skill.def", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            }).WithTimeScope(TimeScope.Combat);

            Assert.Equal(TimeScope.Combat, TimeModelRules.GetTimeScope(table));
        }

        [Fact]
        public void TimeModelRules_IsTimeField_ReadsFieldMetadata()
        {
            var timeField = new FieldSchema("duration", FieldKind.Number, required: false).WithUnit(FieldUnit.Time);
            var plainField = new FieldSchema("scale", FieldKind.Number, required: false);

            Assert.True(TimeModelRules.IsTimeField(timeField));
            Assert.False(TimeModelRules.IsTimeField(plainField));
        }
    }
}
