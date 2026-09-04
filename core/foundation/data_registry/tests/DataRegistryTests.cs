using System;
using System.Collections.Generic;
using System.IO;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Xunit;

namespace Tests.Foundation.Data
{
    public class DataRegistryTests
    {
        // -----------------------------------------------------------------
        // 公共夹具
        // -----------------------------------------------------------------

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

        private static TableSchema StatDefinitionSchema() => new TableSchema(
            name: "stat.definition",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("group", FieldKind.Enum, required: true, enumValues: new[] { "primary", "secondary", "derived" }),
            });

        // 判断记录（T1-7b1）：l10n.locale/l10n.text 的 TableSchema 已从 BuiltinSchemas 搬到
        // Core.Foundation.Localization.L10nSchemas（见 data_registry/core/BuiltinSchemas.cs 顶部
        // 判断记录）；found.input_action 是本次一并新增的 data/_sample 表（见
        // core/foundation/input_map/schema/found.input_action.md），real-sample 测试遍历整个
        // data/_sample 目录会自动发现这张新表，这里同步注册其 schema，否则会被判定为未知表。
        private static void RegisterBuiltins(IDataRegistry registry)
        {
            foreach (var schema in BuiltinSchemas.All)
            {
                registry.RegisterSchema(schema);
            }
            registry.RegisterSchema(StatDefinitionSchema());
            registry.RegisterSchema(L10nSchemas.Locale);
            registry.RegisterSchema(L10nSchemas.Text);
            registry.RegisterSchema(InputActionSchema.Table);
        }

        private static TableSchema OwnerSchema() => new TableSchema(
            "test.owner", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("name", FieldKind.String, required: true),
            });

        private static TableSchema WidgetSchema() => new TableSchema(
            "test.widget", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("name", FieldKind.String, required: true),
                new FieldSchema("count", FieldKind.Int, required: true),
                new FieldSchema("kind", FieldKind.Enum, required: false, enumValues: new[] { "alpha", "beta" }),
                new FieldSchema("owner", FieldKind.Reference, required: false, referenceTable: "test.owner"),
                new FieldSchema("linked_owner", FieldKind.String, required: false),
                new FieldSchema("label", FieldKind.TextKey, required: false),
                new FieldSchema("rule", FieldKind.Expr, required: false),
                new FieldSchema("tags", FieldKind.IdList, required: false),
            });

        private static string Envelope(string table, int schemaVersion, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": " + schemaVersion + ", \"rows\": " + rowsJson + "}";

        // 判断记录：不用 AppContext.BaseDirectory 向上找——测试项目按任务书要求用
        // --artifacts-path 输出到仓库外的 scratch 目录（见任务书"你的所有 dotnet 命令一律加
        // --artifacts-path ..."），运行期的程序集所在目录因此完全不在仓库树下，向上查找永远
        // 找不到 data/_sample。改用 [CallerFilePath] 拿到"本源文件"在磁盘上的绝对路径——
        // 这是编译期由编译器填入的字面量，永远指向仓库内的真实源码位置，与构建产物落在哪里
        // 无关；本文件路径固定是 <repoRoot>/core/foundation/data_registry/tests/DataRegistryTests.cs，
        // 向上 4 级（tests → data_registry → foundation → core）即仓库根。
        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (int i = 0; i < 4; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }

        private static FileSystemDataSource BuildRealSampleSource(out StubFileSystem fs)
        {
            var repoRoot = FindRepoRoot();
            var sampleRoot = Path.Combine(repoRoot, "data", "_sample");
            fs = new StubFileSystem();

            foreach (var file in Directory.GetFiles(sampleRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(sampleRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic("data/_sample/" + rel, File.ReadAllText(file));
            }

            return new FileSystemDataSource(fs, "data/_sample");
        }

        private sealed class MutableSingleTableSource : IDataSource
        {
            private readonly string _tableName;

            public string Json;

            public MutableSingleTableSource(string tableName, string initialJson)
            {
                _tableName = tableName;
                Json = initialJson;
            }

            public IReadOnlyList<DataTableSource> ListTables() =>
                new[] { new DataTableSource(_tableName, "memory://" + _tableName, () => Json) };
        }

        private sealed class AlwaysWarnRule : IValidationRule
        {
            public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
            {
                yield return new ValidationIssue(ValidationSeverity.Warning, "test.widget", "custom_rule", "自定义规则命中");
            }
        }

        // -----------------------------------------------------------------
        // 1. 正向：真实 data/_sample 五表（T1-7b1 新增 found.input_action）
        // -----------------------------------------------------------------

        // 判断记录（T2-1~T2-3 之后的计数断言）：data/_sample 现由 L0（found/l10n）与 L1 五个
        // 数值模块（stat/arch/prog/fac）共同贡献表；本类不属于任何一个模块，无法随每次模块给
        // data/_sample 增补示例表而同步改动。为避免这两条计数断言随后续任务持续失真，
        // TableCount/RecordCount 改为"至少达到本次改动时的实际值"（>=）而不是精确相等——放宽
        // 方向选取原因：后续任务只会新增表/新增记录，不会删除已有 L0 表，`>=` 天然兼容"只增不减"
        // 的演进方向，不需要每次改动都回来同步这两个数字；仍然用精确值锁定的
        // `found.event_catalog`（80）/`found.input_action`（7）两张 L0 自有表不受本任务影响，
        // 继续保持精确断言（它们的行数变化理应触发本文件的显式复核）。
        [Fact]
        public void LoadAll_RealSampleData_ZeroIssues_AndPublishesLoadCompleted()
        {
            var source = BuildRealSampleSource(out _);
            var bus = MakeBus();
            DataLoadCompletedEvent? received = null;
            bus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, e => received = e);

            // 判断记录：data/_sample 现含 L1 五个数值模块（stat/arch/prog/fac）贡献的表，本类是
            // L0 data_registry 自己的测试，不引用任何 L1 模块类型注册对应 schema（L0 不依赖
            // L1，见 01_分层与依赖.md）；这些表在本测试里改用 FailOnUnknownTable=false 以
            // "无 schema 表"方式加载（只做信封与主键格式检查，不做字段级校验——字段级校验由
            // core/numbers/tests/L1SampleDataTests.cs 用真实 L1 schema 覆盖）。
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            RegisterBuiltins(registry);

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(0, report.WarningCount);
            Assert.False(report.IsBlocking);

            Assert.NotNull(registry.Get("stat.definition", new Id("stat.strength")));
            Assert.Equal(80, registry.GetAll("found.event_catalog").Count);
            Assert.Equal(7, registry.GetAll("found.input_action").Count);

            Assert.NotNull(received);
            Assert.True(received!.TableCount >= 14, $"期望 data/_sample 至少 14 张表，实际 {received.TableCount}");
            Assert.True(received.RecordCount >= 113, $"期望 data/_sample 至少 113 条记录，实际 {received.RecordCount}");
            Assert.Equal(0, received.ErrorCount);
            Assert.Equal(0, received.WarningCount);
        }

        [Fact]
        public void FileSystemDataSource_WithStubFileSystem_ListsAllSampleTables()
        {
            var source = BuildRealSampleSource(out _);
            var tables = source.ListTables();

            Assert.True(tables.Count >= 14, $"期望 data/_sample 至少 14 张表，实际 {tables.Count}");
            var names = new HashSet<string>();
            foreach (var t in tables) names.Add(t.TableName);
            Assert.Contains("found.event_catalog", names);
            Assert.Contains("found.input_action", names);
            Assert.Contains("l10n.locale", names);
            Assert.Contains("l10n.text", names);
            Assert.Contains("stat.definition", names);
            Assert.Contains("stat.rating_conversion", names);
            Assert.Contains("arch.power_type", names);
            Assert.Contains("arch.class", names);
            Assert.Contains("arch.race", names);
            Assert.Contains("arch.talent_tree", names);
            Assert.Contains("prog.level_curve", names);
            Assert.Contains("prog.xp_source", names);
            Assert.Contains("fac.faction", names);
            Assert.Contains("fac.reaction_matrix", names);
        }

        // -----------------------------------------------------------------
        // 2~3. 信封检查
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_TableFieldMismatchesFileName_ReportsEnvelopeError()
        {
            var source = new InMemoryDataSource()
                .Add("test.widget", Envelope("test.other_name", 1, "[]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.True(report.ErrorCount > 0);
            Assert.Contains(report.Issues, i => i.Check == "envelope" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void LoadAll_MissingRows_ReportsEnvelopeError()
        {
            var source = new InMemoryDataSource()
                .Add("test.widget", "{\"table\": \"test.widget\", \"schema_version\": 1}");
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "envelope" && i.Severity == ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------
        // 4~5. 未知表
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_UnknownTable_FailOnUnknownTableTrue_ReportsError()
        {
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, "[]"));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { FailOnUnknownTable = true });
            // 故意不 RegisterSchema。

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Table == "test.widget" && i.Check == "envelope" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void LoadAll_UnknownTable_FailOnUnknownTableFalse_LoadsEnvelopeOnlyAndIsReadable()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"anything\": 1}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { FailOnUnknownTable = false });

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking);
            var all = registry.GetAll("test.widget");
            Assert.Single(all);
            Assert.Equal("test.widget.a", all[0].Key);
        }

        // -----------------------------------------------------------------
        // 6~8. schema 版本与迁移
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_LowerSchemaVersion_AppliesMigrationChainAndRenamesField()
        {
            MigrateDelegate migrate = row =>
            {
                var builder = new JsonObjectBuilder();
                foreach (var kv in row)
                {
                    builder.Add(kv.Key == "old_name" ? "name" : kv.Key, kv.Value);
                }
                return builder.Build();
            };

            var schema = new TableSchema(
                "test.legacy", "id", 2,
                new[]
                {
                    new FieldSchema("id", FieldKind.Id, required: true),
                    new FieldSchema("name", FieldKind.String, required: true),
                },
                migrations: new[] { new TableMigration(1, 2, migrate) });

            var rows = "[{\"id\": \"test.legacy.a\", \"old_name\": \"Alpha\"}]";
            var source = new InMemoryDataSource().Add("test.legacy", Envelope("test.legacy", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            var record = registry.Get("test.legacy", "test.legacy.a");
            Assert.NotNull(record);
            Assert.Equal("Alpha", record!.GetString("name"));
        }

        [Fact]
        public void LoadAll_MissingMigrationLink_ReportsSchemaVersionError()
        {
            var schema = new TableSchema(
                "test.legacy", "id", 3,
                new[] { new FieldSchema("id", FieldKind.Id, required: true) },
                migrations: new[] { new TableMigration(1, 2, row => row) }); // 缺 2 -> 3 环节

            var rows = "[{\"id\": \"test.legacy.a\"}]";
            var source = new InMemoryDataSource().Add("test.legacy", Envelope("test.legacy", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "schema_version" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void LoadAll_SchemaVersionAheadOfCurrent_ReportsSchemaVersionError()
        {
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 99, "[]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "schema_version" && i.Severity == ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------
        // 9. 主键
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_DuplicatePrimaryKey_ReportsError()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1}, " +
                       "{\"id\": \"test.widget.a\", \"name\": \"B\", \"count\": 2}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "primary_key" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void LoadAll_ContentTableIdDomainMismatch_ReportsError()
        {
            var rows = "[{\"id\": \"other.widget_a\", \"name\": \"A\", \"count\": 1}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "primary_key" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void LoadAll_IllegalIdFormat_ReportsError()
        {
            var rows = "[{\"id\": \"Test.Widget.A\", \"name\": \"A\", \"count\": 1}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "primary_key" && i.Severity == ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------
        // 10. 必填 / 类型 / 枚举
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_RequiredFieldMissing_ReportsError()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"count\": 1}]"; // 缺 name
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "name" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void LoadAll_FieldTypeMismatch_ReportsError()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": \"not-a-number\"}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "count" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void LoadAll_InvalidEnumValue_ReportsError()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"kind\": \"gamma\"}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "kind" && i.Severity == ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------
        // 11. 引用完整性 / 文本键 / DeclareReference
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_TextKeyMissingFromL10nText_ReportsTextKeyExistsError()
        {
            var widgetRows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"label\": \"l10n.test.widget.a.title\"}]";
            var l10nTextRows = "[{\"key\": \"l10n.other.unrelated\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}]";

            var source = new InMemoryDataSource()
                .Add("test.widget", Envelope("test.widget", 1, widgetRows))
                .Add("l10n.text", Envelope("l10n.text", 1, l10nTextRows));

            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.RegisterSchema(L10nSchemas.Text);

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "text_key_exists" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void LoadAll_ReferenceFieldPointsToMissingTarget_ReportsReferenceIntegrityError()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"owner\": \"test.owner.ghost\"}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            // 故意不加载 test.owner 表。
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.RegisterSchema(OwnerSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "owner" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void DeclareReference_DynamicallyDeclaredReference_IsEnforced()
        {
            var widgetRowsOk = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"linked_owner\": \"test.owner.a\"}]";
            var ownerRows = "[{\"id\": \"test.owner.a\", \"name\": \"Owner A\"}]";

            var sourceOk = new InMemoryDataSource()
                .Add("test.widget", Envelope("test.widget", 1, widgetRowsOk))
                .Add("test.owner", Envelope("test.owner", 1, ownerRows));
            var registryOk = new DataRegistry(sourceOk, MakeBus());
            registryOk.RegisterSchema(WidgetSchema());
            registryOk.RegisterSchema(OwnerSchema());
            registryOk.DeclareReference("test.widget", "linked_owner", "test.owner");

            var reportOk = registryOk.LoadAll();
            Assert.DoesNotContain(reportOk.Issues, i => i.Check == "reference_integrity");

            var widgetRowsBad = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"linked_owner\": \"test.owner.ghost\"}]";
            var sourceBad = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, widgetRowsBad));
            var registryBad = new DataRegistry(sourceBad, MakeBus());
            registryBad.RegisterSchema(WidgetSchema());
            registryBad.DeclareReference("test.widget", "linked_owner", "test.owner");

            var reportBad = registryBad.LoadAll();
            Assert.Contains(reportBad.Issues, i => i.Check == "reference_integrity" && i.Field == "linked_owner");
        }

        // -----------------------------------------------------------------
        // 12. Expr 字段
        // -----------------------------------------------------------------

        private static ExprSchema WorldFlagExprSchema() =>
            new ExprSchema().Register("world", "some_flag", ExprValueKind.Bool, ExprValueKind.Id);

        [Fact]
        public void ExprField_ValidExpression_NoIssue()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"rule\": \"world.some_flag(item.town_key)\"}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { ExprSchema = WorldFlagExprSchema() });
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.DoesNotContain(report.Issues, i => i.Check == "expr_parsable");
        }

        [Fact]
        public void ExprField_SyntaxError_ReportsError()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"rule\": \"(\"}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { ExprSchema = WorldFlagExprSchema() });
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void ExprField_SuspiciousReferenceSpelling_ReportsWarningOnly()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"rule\": \"world.typo_flag\"}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { ExprSchema = WorldFlagExprSchema() });
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void ExprField_NoExprSchemaConfigured_ReportsWarningAndSkipsParsing()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"rule\": \"this is not even close to valid( ) syntax +++\"}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { ExprSchema = null });
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Severity == ValidationSeverity.Warning);
        }

        // -----------------------------------------------------------------
        // 13. 严格级别
        // -----------------------------------------------------------------

        [Fact]
        public void Strictness_WarningsAllowed_OnlyWarnings_StaysReadable()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"rule\": \"world.typo_flag\"}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions
            {
                ExprSchema = WorldFlagExprSchema(),
                Strictness = DataRegistryStrictness.WarningsAllowed,
            });
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            Assert.True(report.WarningCount > 0);
            Assert.False(report.IsBlocking);
            Assert.Single(registry.GetAll("test.widget"));
        }

        [Fact]
        public void Strictness_WarningsBlock_OnlyWarnings_BlocksAndPublishesValidationFailed()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"rule\": \"world.typo_flag\"}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var bus = MakeBus();
            var failedFired = false;
            bus.Subscribe(DataRegistryEventKeys.ValidationFailed, _ => failedFired = true);

            var registry = new DataRegistry(source, bus, new DataRegistryOptions
            {
                ExprSchema = WorldFlagExprSchema(),
                Strictness = DataRegistryStrictness.WarningsBlock,
            });
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            Assert.True(report.WarningCount > 0);
            Assert.True(report.IsBlocking);
            Assert.True(failedFired);
            Assert.Throws<InvalidOperationException>(() => { registry.GetAll("test.widget"); });
        }

        [Fact]
        public void Strictness_ErrorPresent_AlwaysBlocksRegardlessOfStrictness()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"count\": 1}]"; // 缺 name -> Error
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions
            {
                Strictness = DataRegistryStrictness.WarningsAllowed,
            });
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.True(report.ErrorCount > 0);
            Assert.True(report.IsBlocking);
            Assert.Throws<InvalidOperationException>(() => { registry.Get("test.widget", "test.widget.a"); });
        }

        // -----------------------------------------------------------------
        // 14. Query
        // -----------------------------------------------------------------

        private static IDataRegistry LoadStatDefinitionFixture(out TableSchema schema)
        {
            schema = StatDefinitionSchema();
            var rows = "[" +
                       "{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength.name\", \"group\": \"primary\"}," +
                       "{\"id\": \"stat.armor\", \"name_key\": \"l10n.stat.armor.name\", \"group\": \"secondary\"}" +
                       "]";
            var l10nTextRows = "[" +
                                "{\"key\": \"l10n.stat.strength.name\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"力量\"}," +
                                "{\"key\": \"l10n.stat.armor.name\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"护甲\"}" +
                                "]";
            var l10nLocaleRows = "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]";

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", 1, rows))
                .Add("l10n.text", Envelope("l10n.text", 1, l10nTextRows))
                .Add("l10n.locale", Envelope("l10n.locale", 1, l10nLocaleRows));

            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);
            registry.RegisterSchema(L10nSchemas.Text);
            registry.RegisterSchema(L10nSchemas.Locale);

            var report = registry.LoadAll();
            Assert.Equal(0, report.ErrorCount);
            return registry;
        }

        [Fact]
        public void Query_WithExprNode_ReturnsMatchingRecords()
        {
            var registry = LoadStatDefinitionFixture(out var schema);
            var exprSchema = RecordExprSchema.For(schema);
            var node = ExprParser.Parse("self.group == \"primary\"", exprSchema);

            var result = registry.Query("stat.definition", node);

            Assert.Single(result);
            Assert.Equal("stat.strength", result[0].Key);
        }

        [Fact]
        public void Query_WithPredicateText_ReturnsMatchingRecords()
        {
            var registry = LoadStatDefinitionFixture(out _);

            var result = registry.Query("stat.definition", "self.group == \"primary\"");

            Assert.Single(result);
            Assert.Equal("stat.strength", result[0].Key);
        }

        [Fact]
        public void Query_PredicateReferencingUndeclaredField_DoesNotThrow()
        {
            var registry = LoadStatDefinitionFixture(out _);

            var exception = Record.Exception(() => registry.Query("stat.definition", "self.missing_field"));

            Assert.Null(exception);
        }

        // -----------------------------------------------------------------
        // 15. 自定义 IValidationRule
        // -----------------------------------------------------------------

        [Fact]
        public void RegisterValidationRule_CustomIssueIncludedInReport()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.RegisterValidationRule(new AlwaysWarnRule());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "custom_rule" && i.Severity == ValidationSeverity.Warning);
        }

        // -----------------------------------------------------------------
        // 16. Reload
        // -----------------------------------------------------------------

        [Fact]
        public void Reload_ReplacesSingleTableRecordsInPlace()
        {
            var source = new MutableSingleTableSource("test.widget",
                Envelope("test.widget", 1, "[{\"id\": \"test.widget.a\", \"name\": \"Old\", \"count\": 1}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            registry.LoadAll();
            Assert.Equal("Old", registry.Get("test.widget", "test.widget.a")!.GetString("name"));

            source.Json = Envelope("test.widget", 1, "[{\"id\": \"test.widget.a\", \"name\": \"New\", \"count\": 2}]");
            var report = registry.Reload("test.widget");

            Assert.Equal(0, report.ErrorCount);
            Assert.Equal("New", registry.Get("test.widget", "test.widget.a")!.GetString("name"));
        }
    }
}
