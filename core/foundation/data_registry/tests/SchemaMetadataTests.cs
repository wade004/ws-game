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

        // -----------------------------------------------------------------
        // 消费方反馈第 46 条（04 第 3.4 节勘误"字段废弃元数据"）：FieldSchema.WithDeprecated 行为、
        // 重复设置防护、格式校验，以及 ReplacedBy 存在性校验的两个装配时机
        // （TableSchema 顶层 Fields / FieldSchema 构造函数 fields 参数）。
        // -----------------------------------------------------------------

        [Fact]
        public void WithDeprecated_SetsAllProperties()
        {
            var field = new FieldSchema("old_field", FieldKind.Number, required: false)
                .WithDeprecated("1.31.0", "new_field", note: "补充说明");

            Assert.True(field.IsDeprecated);
            Assert.Equal("1.31.0", field.DeprecatedSince);
            Assert.Equal("new_field", field.ReplacedBy);
            Assert.Equal("补充说明", field.DeprecationNote);
        }

        [Fact]
        public void WithDeprecated_Undeprecated_DefaultsToFalseAndNulls()
        {
            var field = new FieldSchema("plain_field", FieldKind.Number, required: false);

            Assert.False(field.IsDeprecated);
            Assert.Null(field.DeprecatedSince);
            Assert.Null(field.ReplacedBy);
            Assert.Null(field.DeprecationNote);
        }

        [Fact]
        public void WithDeprecated_ReplacedByNull_MeansNoReplacement()
        {
            // 同 StatHostOptions.EnableRatingConversion 一类历史案例（升级指南附录 C："无替代——
            // 换算层始终启用"）：ReplacedBy 传 null 是合法的、显式的"无同表替代字段"声明。
            var field = new FieldSchema("old_flag", FieldKind.Bool, required: false)
                .WithDeprecated("1.31.0", null);

            Assert.True(field.IsDeprecated);
            Assert.Null(field.ReplacedBy);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.2")]
        [InlineData("1.2.3.4")]
        [InlineData("v1.2.3")]
        [InlineData("1.2.x")]
        public void WithDeprecated_InvalidSinceVersionFormat_Throws(string? sinceVersion)
        {
            var field = new FieldSchema("old_field", FieldKind.Number, required: false);
            Assert.Throws<System.ArgumentException>(() => field.WithDeprecated(sinceVersion!, null));
        }

        [Fact]
        public void WithDeprecated_BlankReplacedBy_Throws()
        {
            var field = new FieldSchema("old_field", FieldKind.Number, required: false);
            Assert.Throws<System.ArgumentException>(() => field.WithDeprecated("1.31.0", "   "));
        }

        [Fact]
        public void WithDeprecated_ReplacedBySelf_Throws()
        {
            var field = new FieldSchema("old_field", FieldKind.Number, required: false);
            Assert.Throws<System.ArgumentException>(() => field.WithDeprecated("1.31.0", "old_field"));
        }

        [Fact]
        public void WithDeprecated_CalledTwice_Throws()
        {
            var field = new FieldSchema("old_field", FieldKind.Number, required: false)
                .WithDeprecated("1.31.0", null);
            Assert.Throws<System.InvalidOperationException>(() => field.WithDeprecated("1.32.0", null));
        }

        [Fact]
        public void TableSchema_TopLevelReplacedByExists_ConstructsWithoutThrowing()
        {
            var table = new TableSchema("test.deprecated_top_ok", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("old_field", FieldKind.Number, required: false).WithDeprecated("1.31.0", "new_field"),
                new FieldSchema("new_field", FieldKind.Number, required: false),
            });

            Assert.True(table.GetField("old_field")!.IsDeprecated);
        }

        [Fact]
        public void TableSchema_TopLevelReplacedByMissing_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new TableSchema("test.deprecated_top_missing", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("old_field", FieldKind.Number, required: false).WithDeprecated("1.31.0", "does_not_exist"),
            }));
        }

        [Fact]
        public void FieldSchemaFields_NestedReplacedByExists_ConstructsWithoutThrowing()
        {
            var parent = new FieldSchema("container", FieldKind.Object, required: false, fields: new[]
            {
                new FieldSchema("old_child", FieldKind.Number, required: false).WithDeprecated("1.31.0", "new_child"),
                new FieldSchema("new_child", FieldKind.Number, required: false),
            });

            Assert.True(parent.Fields![0].IsDeprecated);
        }

        [Fact]
        public void FieldSchemaFields_NestedReplacedByMissing_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new FieldSchema("container", FieldKind.Object, required: false, fields: new[]
            {
                new FieldSchema("old_child", FieldKind.Number, required: false).WithDeprecated("1.31.0", "does_not_exist"),
            }));
        }

        [Fact]
        public void FieldSchemaFields_NestedReplacedByOnlyExistsAtParentTable_StillThrows()
        {
            // ReplacedBy 只在"同一份兄弟字段清单"里查找——嵌套子结构里的字段不能声明 ReplacedBy 指向
            // 表顶层的字段（两者不是同一级），即便顶层确实有一个同名字段。
            Assert.Throws<System.ArgumentException>(() => new TableSchema("test.deprecated_nested_cannot_reach_top", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("sibling_at_top", FieldKind.Number, required: false),
                new FieldSchema("container", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("old_child", FieldKind.Number, required: false).WithDeprecated("1.31.0", "sibling_at_top"),
                }),
            }));
        }

        // -----------------------------------------------------------------
        // 消费方反馈第 60 条（04 第 4 节勘误"元素数量约束"）：FieldSchema.WithItemCount 行为、种类限制、
        // 参数校验、重复设置防护。
        // -----------------------------------------------------------------

        [Fact]
        public void WithItemCount_OnArray_SetsMinAndMax()
        {
            var field = new FieldSchema("objectives", FieldKind.Array, required: true)
                .WithItemCount(1, 5);

            Assert.Equal(1, field.MinItems);
            Assert.Equal(5, field.MaxItems);
        }

        [Fact]
        public void WithItemCount_OnIdList_SetsMinOnly_MaxNull()
        {
            var field = new FieldSchema("refs", FieldKind.IdList, required: false)
                .WithItemCount(2);

            Assert.Equal(2, field.MinItems);
            Assert.Null(field.MaxItems);
        }

        [Fact]
        public void WithItemCount_Undeclared_DefaultsToNullNull()
        {
            var field = new FieldSchema("plain_array", FieldKind.Array, required: false);

            Assert.Null(field.MinItems);
            Assert.Null(field.MaxItems);
        }

        [Theory]
        [InlineData(FieldKind.Number)]
        [InlineData(FieldKind.String)]
        [InlineData(FieldKind.Object)]
        [InlineData(FieldKind.Id)]
        [InlineData(FieldKind.Reference)]
        public void WithItemCount_OnNonCollectionKind_Throws(FieldKind kind)
        {
            var field = new FieldSchema("scalar", kind, required: false,
                referenceTable: kind == FieldKind.Reference ? "test.target" : null);

            Assert.Throws<System.ArgumentException>(() => field.WithItemCount(1));
        }

        [Fact]
        public void WithItemCount_NegativeMin_Throws()
        {
            var field = new FieldSchema("items", FieldKind.Array, required: false);
            Assert.Throws<System.ArgumentException>(() => field.WithItemCount(-1));
        }

        [Fact]
        public void WithItemCount_MaxLessThanMin_Throws()
        {
            var field = new FieldSchema("items", FieldKind.Array, required: false);
            Assert.Throws<System.ArgumentException>(() => field.WithItemCount(5, 3));
        }

        [Fact]
        public void WithItemCount_MaxEqualsMin_DoesNotThrow()
        {
            var field = new FieldSchema("items", FieldKind.Array, required: false).WithItemCount(3, 3);
            Assert.Equal(3, field.MinItems);
            Assert.Equal(3, field.MaxItems);
        }

        [Fact]
        public void WithItemCount_CalledTwice_Throws()
        {
            var field = new FieldSchema("items", FieldKind.Array, required: false).WithItemCount(1);
            Assert.Throws<System.InvalidOperationException>(() => field.WithItemCount(2));
        }
    }
}
