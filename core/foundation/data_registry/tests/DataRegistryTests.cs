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

        /// <summary>判断记录（数据目录框架/游戏分层任务）：<c>found.event_catalog</c>/
        /// <c>found.input_action</c> 两张表已从 <c>data/_sample/found/</c> 搬到
        /// <c>data/_framework/found/</c>（见 <c>data/README.md</c>"框架级数据表与游戏数据目录
        /// 并列加载"一节——这两张表的行分别被 <c>EventKeys.g.cs</c>/Unity 适配层引导代码硬引用，
        /// 判定为框架级）。真实数据下的加载测试因此改成同一个 <see cref="StubFileSystem"/> 上的
        /// 两个 <see cref="FileSystemDataSource"/>（<c>data/_framework</c> + <c>data/_sample</c>），
        /// 用新增的 <see cref="IDataRegistry.LoadAll(IReadOnlyList{IDataSource})"/> 多根加载合并
        /// 校验——这本身就是"多根加载在真实数据上跑通"的端到端验证，不是单独为了绕开文件搬家。</summary>
        private static (FileSystemDataSource Framework, FileSystemDataSource Sample) BuildRealFrameworkAndSampleSources(out StubFileSystem fs)
        {
            var repoRoot = FindRepoRoot();
            // 判断记录：out 参数不能被本地函数/lambda 捕获（CS1628），下面用局部变量 fsLocal
            // 承接实际实例，本地函数捕获 fsLocal 而不是 out 参数 fs 本身，函数末尾再把
            // fsLocal 赋回 out 参数。
            var fsLocal = new StubFileSystem();
            fs = fsLocal;

            FileSystemDataSource BuildOne(string datasetDirName)
            {
                var root = Path.Combine(repoRoot, "data", datasetDirName);
                foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(root.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    fsLocal.WriteTextAtomic("data/" + datasetDirName + "/" + rel, File.ReadAllText(file));
                }
                return new FileSystemDataSource(fsLocal, "data/" + datasetDirName);
            }

            var framework = BuildOne("_framework");
            var sample = BuildOne("_sample");
            return (framework, sample);
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

        /// <summary>FND-03 回归用：一个可持有多张表、且每张表文本可在测试中途独立改写的
        /// <see cref="IDataSource"/>（<see cref="MutableSingleTableSource"/> 只支持单表，这里的场景
        /// 需要"一张表始终是坏 JSON，另一张表可被独立修复/重载"）。</summary>
        private sealed class MutableMultiTableSource : IDataSource
        {
            private readonly Dictionary<string, string> _json = new Dictionary<string, string>(StringComparer.Ordinal);

            public MutableMultiTableSource Set(string tableName, string json)
            {
                _json[tableName] = json;
                return this;
            }

            public IReadOnlyList<DataTableSource> ListTables()
            {
                var result = new List<DataTableSource>();
                foreach (var kv in _json)
                {
                    var name = kv.Key;
                    result.Add(new DataTableSource(name, "memory://" + name, () => _json[name]));
                }
                return result;
            }
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

        // 判断记录（T2-1~T2-3 之后的计数断言）：data/_sample 现由 L0（l10n）与 L1 五个
        // 数值模块（stat/arch/prog/fac）共同贡献表；本类不属于任何一个模块，无法随每次模块给
        // data/_sample 增补示例表而同步改动。为避免这两条计数断言随后续任务持续失真，
        // TableCount/RecordCount 改为"至少达到本次改动时的实际值"（>=）而不是精确相等——放宽
        // 方向选取原因：后续任务只会新增表/新增记录，不会删除已有 L0 表，`>=` 天然兼容"只增不减"
        // 的演进方向，不需要每次改动都回来同步这两个数字；仍然用精确值锁定的
        // `found.event_catalog`（87，2026-09-05 事件命名勘误后：改名 3 行 + 新增 7 行）/
        // `found.input_action`（缺口收敛 G3：由 7 行补齐到 13 行——新增 6 个战斗动作）两张 L0
        // 自有表不受本任务影响，继续保持精确断言（它们的行数变化理应触发本文件的显式复核）。
        // RecordCount 下限随 input_action +6 行同步从 113 上调到 119。
        //
        // 判断记录（数据目录框架/游戏分层任务）：`found.event_catalog`/`found.input_action`
        // 已从 `data/_sample/found/` 搬到 `data/_framework/found/`（见 `data/README.md`），
        // 本测试因此从单根 `LoadAll()` 改为 `LoadAll(new[] { framework, sample })` 多根加载——
        // 这两张表仍然在合并后的注册表里可查，计数断言不变，同时验证了"框架级数据表 + 示例数据
        // 合并加载 0 错误 0 警告"这条真实端到端路径。
        [Fact]
        public void LoadAll_RealFrameworkAndSampleData_ZeroIssues_AndPublishesLoadCompleted()
        {
            var (frameworkSource, sampleSource) = BuildRealFrameworkAndSampleSources(out _);
            var bus = MakeBus();
            DataLoadCompletedEvent? received = null;
            bus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, e => received = e);

            // 判断记录：data/_sample 现含 L1 五个数值模块（stat/arch/prog/fac）贡献的表，本类是
            // L0 data_registry 自己的测试，不引用任何 L1 模块类型注册对应 schema（L0 不依赖
            // L1，见 01_分层与依赖.md）；这些表在本测试里改用 FailOnUnknownTable=false 以
            // "无 schema 表"方式加载（只做信封与主键格式检查，不做字段级校验——字段级校验由
            // core/numbers/tests/L1SampleDataTests.cs 用真实 L1 schema 覆盖）。
            var registry = new DataRegistry(frameworkSource, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            RegisterBuiltins(registry);

            var report = registry.LoadAll(new IDataSource[] { frameworkSource, sampleSource });

            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(0, report.WarningCount);
            Assert.False(report.IsBlocking);

            Assert.NotNull(registry.Get("stat.definition", new Id("stat.strength")));
            Assert.Equal(88, registry.GetAll("found.event_catalog").Count);
            // H4 新增 input.action.end_turn（离散时间模型结束回合意图，见该表判断记录），13 -> 14。
            Assert.Equal(14, registry.GetAll("found.input_action").Count);

            // 只读诊断：found.event_catalog 只应来自 data/_framework 这一个根。
            var locations = registry.GetTableSourceLocations("found.event_catalog");
            Assert.Single(locations);
            Assert.Contains("data/_framework", locations[0]);

            Assert.NotNull(received);
            Assert.True(received!.TableCount >= 14, $"期望合并后至少 14 张表，实际 {received.TableCount}");
            Assert.True(received.RecordCount >= 119, $"期望合并后至少 119 条记录，实际 {received.RecordCount}");
            Assert.Equal(0, received.ErrorCount);
            Assert.Equal(0, received.WarningCount);
        }

        [Fact]
        public void FileSystemDataSource_WithStubFileSystem_ListsAllFrameworkAndSampleTables()
        {
            var (frameworkSource, sampleSource) = BuildRealFrameworkAndSampleSources(out _);

            var frameworkNames = new HashSet<string>();
            foreach (var t in frameworkSource.ListTables()) frameworkNames.Add(t.TableName);
            Assert.Contains("found.event_catalog", frameworkNames);
            Assert.Contains("found.input_action", frameworkNames);

            var tables = sampleSource.ListTables();
            Assert.True(tables.Count >= 13, $"期望 data/_sample 至少 13 张表，实际 {tables.Count}");
            var names = new HashSet<string>();
            foreach (var t in tables) names.Add(t.TableName);
            // found.event_catalog/found.input_action 已搬到 data/_framework，不应再出现在 _sample。
            Assert.DoesNotContain("found.event_catalog", names);
            Assert.DoesNotContain("found.input_action", names);
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

        // -----------------------------------------------------------------
        // 16a. FND-03 收口回归：加载阶段错误按表持久保留，直到该表成功重载
        // -----------------------------------------------------------------

        [Fact]
        public void Validate_AfterBadTableLoad_StaysBlocked_EvenWithoutTouchingBadTable()
        {
            var source = new MutableMultiTableSource()
                .Set("test.widget", "{ not valid json")
                .Set("test.owner", Envelope("test.owner", 1, "[{\"id\": \"test.owner.a\", \"name\": \"Ann\"}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.RegisterSchema(OwnerSchema());

            var initial = registry.LoadAll();
            Assert.True(initial.IsBlocking);
            Assert.True(initial.ErrorCount >= 1);
            Assert.Throws<InvalidOperationException>(() => registry.GetAll("test.widget"));

            // 复现前置条件（对应 FND-03"触发"描述）：坏表首次加载后，无参 Validate() 不应该
            // 凭空清空这条加载期错误——它此前只活在 LoadAllCore 局部的 issues 列表里，Validate()
            // 自己起一个全新的空列表，字段级校验循环也天然看不到从未进入 _tables 的坏表。
            var afterBareValidate = registry.Validate();
            Assert.True(afterBareValidate.IsBlocking);
            Assert.True(afterBareValidate.ErrorCount >= 1);
            Assert.Contains(afterBareValidate.Issues, i => i.Table == "test.widget" && i.Check == "envelope");
            Assert.Throws<InvalidOperationException>(() => registry.GetAll("test.widget"));

            // 重载一张完全无关的好表（test.owner）：不应该以任何方式解除坏表造成的阻断。
            var afterUnrelatedReload = registry.Reload("test.owner");
            Assert.True(afterUnrelatedReload.IsBlocking);
            Assert.Contains(afterUnrelatedReload.Issues, i => i.Table == "test.widget" && i.Check == "envelope");

            // 只有真正修复并重载坏表本身，阻断才解除；报告里不再出现 test.widget 的加载错误。
            source.Set("test.widget", Envelope("test.widget", 1, "[{\"id\": \"test.widget.a\", \"name\": \"Fixed\", \"count\": 1}]"));
            var afterFixReload = registry.Reload("test.widget");
            Assert.False(afterFixReload.IsBlocking);
            Assert.DoesNotContain(afterFixReload.Issues, i => i.Table == "test.widget");
            Assert.Equal("Fixed", registry.Get("test.widget", "test.widget.a")!.GetString("name"));

            // 修复生效后，后续无参 Validate() 应保持不阻断——不残留任何已经修好的历史错误。
            var afterFinalValidate = registry.Validate();
            Assert.False(afterFinalValidate.IsBlocking);
            Assert.DoesNotContain(afterFinalValidate.Issues, i => i.Table == "test.widget");
        }

        // -----------------------------------------------------------------
        // 17. 多根加载（数据目录框架/游戏分层任务新增：LoadAll(IReadOnlyList<IDataSource>)）
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_Parameterless_IsEquivalentToSingleElementSourcesList()
        {
            // 向后兼容：单根既有用法（LoadAll() 无参）行为不因新重载的存在而改变。
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            Assert.Single(registry.GetAll("test.widget"));
            Assert.Equal(new[] { "memory://test.widget" }, registry.GetTableSourceLocations("test.widget"));
        }

        [Fact]
        public void LoadAll_MultiRoot_DisjointTables_UnionsTableSet()
        {
            var rootA = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1}]"));
            var rootB = new InMemoryDataSource().Add("test.owner", Envelope("test.owner", 1,
                "[{\"id\": \"test.owner.a\", \"name\": \"Owner A\"}]"));

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.RegisterSchema(OwnerSchema());

            var report = registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.Equal(0, report.ErrorCount);
            Assert.Single(registry.GetAll("test.widget"));
            Assert.Single(registry.GetAll("test.owner"));
        }

        [Fact]
        public void LoadAll_MultiRoot_SameTableDifferentKeys_MergesRowsFromBothRoots()
        {
            var rootA = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1}]"), location: "root_a/test.widget.json");
            var rootB = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.b\", \"name\": \"B\", \"count\": 2}]"), location: "root_b/test.widget.json");

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.Equal(0, report.ErrorCount);
            var all = registry.GetAll("test.widget");
            Assert.Equal(2, all.Count);
            Assert.NotNull(registry.Get("test.widget", "test.widget.a"));
            Assert.NotNull(registry.Get("test.widget", "test.widget.b"));

            var locations = registry.GetTableSourceLocations("test.widget");
            Assert.Equal(2, locations.Count);
            Assert.Contains("root_a/test.widget.json", locations);
            Assert.Contains("root_b/test.widget.json", locations);
        }

        [Fact]
        public void LoadAll_MultiRoot_SameTableSamePrimaryKey_ReportsPrimaryKeyErrorNamingBothRoots()
        {
            var rootA = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1}]"), location: "root_a/test.widget.json");
            var rootB = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"A2\", \"count\": 9}]"), location: "root_b/test.widget.json");

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == "primary_key" && i.RecordKey == "test.widget.a");
            Assert.Contains("root_a/test.widget.json", issue.Message);
            Assert.Contains("root_b/test.widget.json", issue.Message);
            Assert.Throws<InvalidOperationException>(() => registry.GetAll("test.widget"));
        }

        [Fact]
        public void LoadAll_MultiRoot_SameTableDifferentSchemaVersion_ReportsSchemaVersionErrorNamingBothRoots()
        {
            // 判断记录：要制造"两个根各自都能独立通过校验、但信封 schema_version 原始值不同"
            // 的场景，需要一张有迁移链的表——若两个根的原始版本都直接不合法（如其中一个超过
            // CurrentSchemaVersion），会在单根解析阶段（LoadOneTablePartial）就先报出"schema_version
            // 超过当前代码期望版本"这类逐文件错误并提前返回，根本走不到跨根一致性检查这一步。
            // 这里复用"6~8. schema 版本与迁移"一节同款迁移链模式：CurrentSchemaVersion=2，
            // 1→2 环节把 old_name 重命名为 name；root A 给版本 1（会被迁移到 2）、root B 直接给
            // 版本 2（无需迁移）——两个根单独看都能正常解析成功，但原始 schema_version 不同
            // （1 vs 2），应判定为跨根不一致。
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

            var rootA = new InMemoryDataSource().Add("test.legacy",
                Envelope("test.legacy", 1, "[{\"id\": \"test.legacy.a\", \"old_name\": \"A\"}]"),
                location: "root_a/test.legacy.json");
            var rootB = new InMemoryDataSource().Add("test.legacy",
                Envelope("test.legacy", 2, "[{\"id\": \"test.legacy.b\", \"name\": \"B\"}]"),
                location: "root_b/test.legacy.json");

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Table == "test.legacy" && i.Check == "schema_version");
            Assert.Contains("root_a/test.legacy.json", issue.Message);
            Assert.Contains("root_b/test.legacy.json", issue.Message);
            Assert.Throws<InvalidOperationException>(() => registry.GetAll("test.legacy"));
        }

        [Fact]
        public void Reload_MultiRootTable_RereadsAllContributingRootsAndReMerges()
        {
            var rootA = new MutableSingleTableSource("test.widget",
                Envelope("test.widget", 1, "[{\"id\": \"test.widget.a\", \"name\": \"Old A\", \"count\": 1}]"));
            var rootB = new MutableSingleTableSource("test.widget",
                Envelope("test.widget", 1, "[{\"id\": \"test.widget.b\", \"name\": \"Old B\", \"count\": 2}]"));

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.Equal("Old A", registry.Get("test.widget", "test.widget.a")!.GetString("name"));
            Assert.Equal("Old B", registry.Get("test.widget", "test.widget.b")!.GetString("name"));

            rootA.Json = Envelope("test.widget", 1, "[{\"id\": \"test.widget.a\", \"name\": \"New A\", \"count\": 9}]");
            var report = registry.Reload("test.widget");

            Assert.Equal(0, report.ErrorCount);
            Assert.Equal("New A", registry.Get("test.widget", "test.widget.a")!.GetString("name"));
            Assert.Equal("Old B", registry.Get("test.widget", "test.widget.b")!.GetString("name")); // 未改动的根保持不变。
        }

        // -----------------------------------------------------------------
        // 18. 覆盖语义（数据行覆盖语义任务新增：DataRegistryOptions.AllowOverride、
        //     行级 "override"/"final" 字段、OverrideDiagnostic）
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_MultiRoot_NoOverrideDeclared_SameKey_StillBlocks_DefaultBehaviorUnchanged()
        {
            // 回归基线：两个字段都不声明时，行为必须与改动前完全一致——跨根同主键重复即阻断，
            // 不受本任务新增的 AllowOverride 默认 true 影响。
            var rootA = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1}]"), location: "root_a/test.widget.json");
            var rootB = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"A2\", \"count\": 9}]"), location: "root_b/test.widget.json");

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            var report = registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.True(report.IsBlocking);
            Assert.Empty(registry.GetOverrideDiagnostics());
        }

        [Fact]
        public void LoadAll_MultiRoot_LaterRootDeclaresOverride_ReplacesEarlierRow_NoBlockingError()
        {
            var rootA = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"Framework\", \"count\": 1}]"), location: "data/_framework/test.widget.json");
            var rootB = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"Game\", \"count\": 9, \"override\": true}]"), location: "data/_sample/test.widget.json");

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            var report = registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(0, report.WarningCount);
            Assert.False(report.IsBlocking);

            // 整行替换：后层（_sample）行的全部字段值胜出，不是逐字段合并。
            var record = registry.Get("test.widget", "test.widget.a")!;
            Assert.Equal("Game", record.GetString("name"));
            Assert.Equal(9, record.GetInt("count"));
            Assert.Single(registry.GetAll("test.widget")); // 覆盖不产生重复行。
        }

        [Fact]
        public void LoadAll_MultiRoot_OverrideSucceeds_RecordsOverrideDiagnosticWithTableKeyAndBothLocations()
        {
            var rootA = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"Framework\", \"count\": 1}]"), location: "data/_framework/test.widget.json");
            var rootB = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"Game\", \"count\": 9, \"override\": true}]"), location: "data/_sample/test.widget.json");

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.LoadAll(new IDataSource[] { rootA, rootB });

            var diagnostics = registry.GetOverrideDiagnostics();
            var diag = Assert.Single(diagnostics);
            Assert.Equal("test.widget", diag.Table);
            Assert.Equal("test.widget.a", diag.RecordKey);
            Assert.Equal("data/_sample/test.widget.json", diag.OverridingLocation);
            Assert.Equal("data/_framework/test.widget.json", diag.OverriddenLocation);
        }

        [Fact]
        public void LoadAll_MultiRoot_EarlierRowDeclaresFinal_RejectsOverride_ReportsBlockingErrorNamingBothRootsAndNoDiagnostic()
        {
            var rootA = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"Framework\", \"count\": 1, \"final\": true}]"), location: "data/_framework/test.widget.json");
            var rootB = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"Game\", \"count\": 9, \"override\": true}]"), location: "data/_sample/test.widget.json");

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            var report = registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == "primary_key" && i.RecordKey == "test.widget.a");
            Assert.Contains("data/_framework/test.widget.json", issue.Message);
            Assert.Contains("data/_sample/test.widget.json", issue.Message);
            Assert.Contains("final", issue.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(registry.GetOverrideDiagnostics()); // 拒绝覆盖不产生覆盖诊断。
            Assert.Throws<InvalidOperationException>(() => registry.GetAll("test.widget"));
        }

        [Fact]
        public void LoadAll_MultiRoot_AllowOverrideFalse_OverrideFieldIgnored_StillBlocks()
        {
            var rootA = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"Framework\", \"count\": 1}]"), location: "data/_framework/test.widget.json");
            var rootB = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"Game\", \"count\": 9, \"override\": true}]"), location: "data/_sample/test.widget.json");

            var registry = new DataRegistry(rootA, MakeBus(), new DataRegistryOptions { AllowOverride = false });
            registry.RegisterSchema(WidgetSchema());
            var report = registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.True(report.IsBlocking);
            Assert.Empty(registry.GetOverrideDiagnostics());
        }

        [Fact]
        public void LoadAll_SingleRoot_OverrideFieldTrue_WarnsAndIsIgnored()
        {
            // 单根加载（LoadAll() 无参）：override 字段没有第二个根可覆盖，判定为 Warning 并忽略，
            // 不影响该行本身正常加载。
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"override\": true}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows), location: "only_root/test.widget.json");
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(1, report.WarningCount);
            Assert.False(report.IsBlocking); // 默认 WarningsAllowed，Warning 不阻断读取。
            var warning = Assert.Single(report.Issues);
            Assert.Equal(ValidationSeverity.Warning, warning.Severity);
            Assert.Equal("override", warning.Field);
            Assert.Equal("test.widget.a", warning.RecordKey);
            Assert.Equal("A", registry.Get("test.widget", "test.widget.a")!.GetString("name"));
            Assert.Empty(registry.GetOverrideDiagnostics());
        }

        [Fact]
        public void LoadAll_SingleRoot_FinalFieldTrue_WarnsAndIsIgnored()
        {
            var rows = "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"final\": true}]";
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1, rows), location: "only_root/test.widget.json");
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(1, report.WarningCount);
            var warning = Assert.Single(report.Issues);
            Assert.Equal("final", warning.Field);
        }

        [Fact]
        public void LoadAll_MultiRoot_TableOnlyInOneOfSeveralRoots_OverrideFieldStillWarns()
        {
            // 判断记录：本次 LoadAll 传了多个根，但 test.widget 这张表本身只出现在其中一个根
            // （rootB 只贡献 test.owner）——对这张表而言并未发生任何合并，override 同样不生效，
            // 与"整次加载是不是单根"无关，只看"这张表这次是不是只来自一个根"。
            var rootA = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"A\", \"count\": 1, \"override\": true}]"), location: "root_a/test.widget.json");
            var rootB = new InMemoryDataSource().Add("test.owner", Envelope("test.owner", 1,
                "[{\"id\": \"test.owner.a\", \"name\": \"Owner A\"}]"));

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.RegisterSchema(OwnerSchema());
            var report = registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.Equal(0, report.ErrorCount);
            var warning = Assert.Single(report.Issues, i => i.Table == "test.widget");
            Assert.Equal(ValidationSeverity.Warning, warning.Severity);
            Assert.Equal("override", warning.Field);
        }

        [Fact]
        public void LoadAll_MultiRoot_ThreeRoots_SecondOverridesFirst_ThirdPlainDuplicate_ErrorNamesCurrentWinnerLocation()
        {
            // 三根链式覆盖：root2 用 override 顶掉 root1，成为当前"胜出"的行；root3 未声明 override，
            // 与 root2 撞键——错误消息应点出"当前实际生效"的位置（root2），而不是最初的 root1，
            // 验证合并循环里 mergedLocationByKey 会随每次成功覆盖同步更新。
            var root1 = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"R1\", \"count\": 1}]"), location: "root1/test.widget.json");
            var root2 = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"R2\", \"count\": 2, \"override\": true}]"), location: "root2/test.widget.json");
            var root3 = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", 1,
                "[{\"id\": \"test.widget.a\", \"name\": \"R3\", \"count\": 3}]"), location: "root3/test.widget.json");

            var registry = new DataRegistry(root1, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            var report = registry.LoadAll(new IDataSource[] { root1, root2, root3 });

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == "primary_key" && i.RecordKey == "test.widget.a");
            Assert.Contains("root2/test.widget.json", issue.Message); // 当前胜出者，不是最初的 root1。
            Assert.Contains("root3/test.widget.json", issue.Message);
            Assert.DoesNotContain("root1/test.widget.json", issue.Message);

            var diag = Assert.Single(registry.GetOverrideDiagnostics());
            Assert.Equal("root2/test.widget.json", diag.OverridingLocation);
            Assert.Equal("root1/test.widget.json", diag.OverriddenLocation);
        }

        [Fact]
        public void Reload_MultiRootTable_OverrideDiagnostics_UpdatedAfterRootStopsDeclaringOverride()
        {
            var rootA = new MutableSingleTableSource("test.widget",
                Envelope("test.widget", 1, "[{\"id\": \"test.widget.a\", \"name\": \"Framework\", \"count\": 1}]"));
            var rootB = new MutableSingleTableSource("test.widget",
                Envelope("test.widget", 1, "[{\"id\": \"test.widget.a\", \"name\": \"Game\", \"count\": 9, \"override\": true}]"));

            var registry = new DataRegistry(rootA, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.LoadAll(new IDataSource[] { rootA, rootB });

            Assert.Single(registry.GetOverrideDiagnostics());
            Assert.Equal("Game", registry.Get("test.widget", "test.widget.a")!.GetString("name"));

            // rootB 不再声明 override：重载后应恢复"跨根同主键即阻断"，且旧的覆盖诊断被清空
            // （不是继续累积一条过期条目）。
            rootB.Json = Envelope("test.widget", 1, "[{\"id\": \"test.widget.a\", \"name\": \"Game2\", \"count\": 10}]");
            var report = registry.Reload("test.widget");

            Assert.True(report.IsBlocking);
            Assert.Empty(registry.GetOverrideDiagnostics());
        }

        // W2 收边补齐（A1 审计第 7 节，测试完备性缺口）：GetSchema 此前只有生产代码内部调用点
        // （DataRegistry 自身解析谓词文本、CombatValidationRules），没有一处测试把它作为断言主语
        // 直接调用。

        [Fact]
        public void GetSchema_RegisteredTable_ReturnsSameSchemaInstance()
        {
            var registry = new DataRegistry(new InMemoryDataSource(), MakeBus());
            var schema = WidgetSchema();
            registry.RegisterSchema(schema);

            var result = registry.GetSchema("test.widget");

            Assert.Same(schema, result);
        }

        [Fact]
        public void GetSchema_UnregisteredTable_ReturnsNull()
        {
            var registry = new DataRegistry(new InMemoryDataSource(), MakeBus());

            Assert.Null(registry.GetSchema("test.never_registered"));
        }
    }
}
