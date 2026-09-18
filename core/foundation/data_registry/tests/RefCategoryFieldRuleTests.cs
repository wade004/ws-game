using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// ADR-0038 决策 6 前半验收：<see cref="RefCategoryFieldRule"/>（检查名 <c>field_ref_category</c>）
    /// 正负例——顶层 Id 字段、IdList 逐元素、嵌套 Object.Fields、Map 值三种子结构路径、未登记该项的
    /// 表不受影响、未知类别前缀与已知但不在允许集合内两种失败分别报告、规则元数据。判断记录：本文件
    /// 直接构造 <see cref="DataRegistry"/> 并显式 <see cref="IDataRegistry.RegisterValidationRule"/>，
    /// 不经 <c>Presentation.Assembly.ContentValidationAssembly</c>（该入口默认不注册本规则，见
    /// <see cref="RefCategoryFieldRule"/> 类型判断记录"本规则当前不默认注册"），本文件因此能独立验证
    /// 规则本身的行为正确，不受该默认开关影响。
    /// </summary>
    public sealed class RefCategoryFieldRuleTests
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

        private static TableSchema TopLevelIdTable() => new TableSchema("test.vfx", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            new FieldSchema("resource_ref", FieldKind.Id, required: true, description: "测试用")
                .WithAllowedRefCategories("vfx"),
        });

        private static TableSchema IdListTable() => new TableSchema("test.sfx", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            new FieldSchema("variants", FieldKind.IdList, required: false, description: "测试用")
                .WithFreeIds("测试用")
                .WithAllowedRefCategories("sfx"),
        });

        private static TableSchema NestedMapOfObjectTable() => new TableSchema("test.anim_set", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            new FieldSchema("clips", FieldKind.Object, required: true, description: "测试用")
                .WithMap(MapSchema.FreeKeyed("测试用", new FieldSchema("<clip>", FieldKind.Object, required: true, fields: new[]
                {
                    new FieldSchema("resource_ref", FieldKind.Id, required: true, description: "测试用")
                        .WithAllowedRefCategories("anim", "sprite_anim"),
                }))),
        });

        private static TableSchema NestedMapOfIdTable() => new TableSchema("test.weapon_style", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            new FieldSchema("cast_anim_override", FieldKind.Object, required: false, description: "测试用")
                .WithMap(MapSchema.FreeKeyed("测试用",
                    new FieldSchema("<anim_clip_id>", FieldKind.Id, required: true, description: "测试用")
                        .WithAllowedRefCategories("anim", "sprite_anim"))),
        });

        private static TableSchema UnrelatedTable() => new TableSchema("test.unrelated", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            new FieldSchema("free_text", FieldKind.String, required: false, description: "不受本规则影响"),
        });

        private static ValidationReport Load(TableSchema schema, string rowsJson)
        {
            var source = new InMemoryDataSource().Add(schema.Name, Envelope(schema.Name, rowsJson));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);
            registry.RegisterValidationRule(new RefCategoryFieldRule());
            return registry.LoadAll();
        }

        [Fact]
        public void TopLevelId_LegalCategory_NoIssue()
        {
            var report = Load(TopLevelIdTable(), "[{\"id\": \"test.a\", \"resource_ref\": \"vfx.sample_cast_circle\"}]");

            Assert.False(report.IsBlocking);
            Assert.DoesNotContain(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
        }

        [Fact]
        public void TopLevelId_KnownButNotAllowedCategory_ReportsError()
        {
            var report = Load(TopLevelIdTable(), "[{\"id\": \"test.a\", \"resource_ref\": \"sfx.wrong_category\"}]");

            var issue = Assert.Single(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Equal("test.vfx", issue.Table);
            Assert.Equal("test.a", issue.RecordKey);
            Assert.Equal("resource_ref", issue.Field);
            Assert.Equal(nameof(RefCategoryFieldRule), issue.RuleId);
            Assert.Contains("sfx", issue.Message);
            Assert.Contains("vfx", issue.Message);
        }

        [Fact]
        public void TopLevelId_UnknownCategory_ReportsErrorWithLegalSet()
        {
            var report = Load(TopLevelIdTable(), "[{\"id\": \"test.a\", \"resource_ref\": \"bogus.thing\"}]");

            var issue = Assert.Single(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
            Assert.Contains("bogus", issue.Message);
            Assert.Contains("不合法", issue.Message);
        }

        [Fact]
        public void IdList_EachElementChecked_ReportsIndexedPath()
        {
            var report = Load(IdListTable(),
                "[{\"id\": \"test.a\", \"variants\": [\"sfx.hit_v0\", \"vfx.wrong_v1\"]}]");

            var issue = Assert.Single(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
            Assert.Equal("variants[1]", issue.Field);
        }

        [Fact]
        public void IdList_AllLegal_NoIssue()
        {
            var report = Load(IdListTable(),
                "[{\"id\": \"test.a\", \"variants\": [\"sfx.hit_v0\", \"sfx.hit_v1\"]}]");

            Assert.DoesNotContain(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
        }

        [Fact]
        public void NestedMapOfObject_ClipResourceRef_LegalEitherCategory()
        {
            var report = Load(NestedMapOfObjectTable(),
                "[{\"id\": \"test.a\", \"clips\": {" +
                "\"idle\": {\"resource_ref\": \"anim.idle\"}," +
                "\"attack\": {\"resource_ref\": \"sprite_anim.attack\"}" +
                "}}]");

            Assert.DoesNotContain(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
        }

        [Fact]
        public void NestedMapOfObject_ClipResourceRef_IllegalReportsMapKeyPath()
        {
            var report = Load(NestedMapOfObjectTable(),
                "[{\"id\": \"test.a\", \"clips\": {\"idle\": {\"resource_ref\": \"vfx.not_allowed\"}}}]");

            var issue = Assert.Single(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
            Assert.Equal("clips[idle].resource_ref", issue.Field);
        }

        [Fact]
        public void NestedMapOfId_CastAnimOverride_IllegalReportsMapKeyPath()
        {
            var report = Load(NestedMapOfIdTable(),
                "[{\"id\": \"test.a\", \"cast_anim_override\": {\"skill.sample_burn\": \"vfx.not_allowed\"}}]");

            var issue = Assert.Single(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
            Assert.Equal("cast_anim_override[skill.sample_burn]", issue.Field);
        }

        [Fact]
        public void NestedMapOfId_CastAnimOverride_Legal_NoIssue()
        {
            var report = Load(NestedMapOfIdTable(),
                "[{\"id\": \"test.a\", \"cast_anim_override\": {\"skill.sample_burn\": \"anim.cast\"}}]");

            Assert.DoesNotContain(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
        }

        [Fact]
        public void TableWithoutAnyRegisteredField_Skipped_EvenWithArbitraryText()
        {
            var report = Load(UnrelatedTable(), "[{\"id\": \"test.a\", \"free_text\": \"whatever.not.an.asset.ref\"}]");

            Assert.DoesNotContain(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
        }

        [Fact]
        public void MissingOptionalField_NoIssue()
        {
            var report = Load(IdListTable(), "[{\"id\": \"test.a\"}]");

            Assert.DoesNotContain(report.Issues, i => i.Check == RefCategoryFieldRule.CheckName);
        }

        [Fact]
        public void RuleMetadata_DefaultsMatchExpectations()
        {
            var rule = new RefCategoryFieldRule();
            Assert.Equal(nameof(RefCategoryFieldRule), rule.RuleId);
            Assert.Equal(ValidationSeverity.Error, rule.DefaultSeverity);
            Assert.False(rule.NonEscalatable);
            Assert.Equal("field_ref_category", RefCategoryFieldRule.CheckName);
        }
    }
}
