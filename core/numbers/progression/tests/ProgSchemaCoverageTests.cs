using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Numbers.Progression
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Numbers.Progression.ProgSchemas.LevelCurve"/> 的
    /// <c>entries</c> 子结构登记（<c>Item</c>；<c>growth</c> 自 ADR-0024 第二批登记起为
    /// <c>MapSchema.ReferenceKeyTable("stat.definition", ...)</c>，见下方用例同时装配
    /// <c>stat.definition</c> 表），覆盖范围：子结构命中/坏形状各一例，
    /// <c>ProgLevelCurveValidationRule</c>（连续性业务判断）不因本次登记双报。
    /// </summary>
    public sealed class ProgSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void LevelCurveEntries_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"prog.level_curve.cov_sample\",\"max_level\":2,\"entries\":[" +
                "{\"level\":1,\"xp_to_next\":100,\"growth\":{\"stat.cov_sample\":1}}," +
                "{\"level\":2,\"xp_to_next\":200}]}]";
            var statRows = "[{\"id\":\"stat.cov_sample\",\"name_key\":\"l10n.stat.cov_sample.name\",\"group\":\"primary\"}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, statRows))
                .Add(Core.Numbers.Progression.ProgSchemas.LevelCurve.Name,
                    Envelope(Core.Numbers.Progression.ProgSchemas.LevelCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.LevelCurve);
            registry.RegisterValidationRule(new Core.Numbers.Progression.ProgLevelCurveValidationRule());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void LevelCurveEntries_MissingXpToNext_ReportsRequiredField_NotDoubleReportedByBusinessRule()
        {
            var rows = "[{\"id\":\"prog.level_curve.cov_bad\",\"max_level\":1,\"entries\":[{\"level\":1}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.LevelCurve.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.LevelCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.LevelCurve);
            registry.RegisterValidationRule(new Core.Numbers.Progression.ProgLevelCurveValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "entries[0].xp_to_next");
        }

        // -----------------------------------------------------------------
        // T-N4-1（ADR-0033 决策 2/3；06 第 2.5 节）：prog.level_curve.talent_points、
        // prog.xp_source 新字段（kind/base_curve_ref/level_diff_ref/once_key）、
        // prog.xp_base_curve 表的 schema 覆盖 + 旧字段兼容。
        // -----------------------------------------------------------------

        [Fact]
        public void LevelCurve_TalentPoints_Optional_DefaultsAbsent_LoadsWithoutErrors()
        {
            // talent_points 缺省不填：验收标准"旧字段兼容"的同类场景（纯新增可选字段，旧数据不受影响）。
            var rows = "[{\"id\":\"prog.level_curve.tp_absent\",\"max_level\":1,\"entries\":[" +
                "{\"level\":1,\"xp_to_next\":0}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.LevelCurve.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.LevelCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.LevelCurve);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void LevelCurve_TalentPoints_NonNegative_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"prog.level_curve.tp_ok\",\"max_level\":2,\"entries\":[" +
                "{\"level\":1,\"xp_to_next\":100,\"talent_points\":0}," +
                "{\"level\":2,\"xp_to_next\":0,\"talent_points\":1}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.LevelCurve.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.LevelCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.LevelCurve);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void LevelCurve_TalentPoints_Negative_ReportsFieldRangeError()
        {
            var rows = "[{\"id\":\"prog.level_curve.tp_bad\",\"max_level\":1,\"entries\":[" +
                "{\"level\":1,\"xp_to_next\":0,\"talent_points\":-1}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.LevelCurve.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.LevelCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.LevelCurve);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "entries[0].talent_points");
        }

        [Fact]
        public void XpSource_OnlyLegacyFields_LoadsWithoutErrors()
        {
            // 验收标准"旧字段兼容 1 组"：只有 base_xp/weight（无 kind/base_curve_ref/level_diff_ref/
            // once_key）的既有来源必须仍能 0 error 加载——data/_sample/prog/prog.xp_source.json 现存
            // 的 prog.xp.kill_sample 行即此形状。
            var rows = "[{\"id\":\"prog.xp.legacy_sample\",\"base_xp\":50,\"weight\":1}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.XpSource.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.XpSource.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpSource);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void XpSource_WithBaseCurveRef_ValidReference_LoadsWithoutErrors()
        {
            var curveRows = "[{\"id\":\"prog.xp_base_curve.cov_sample\",\"entries\":[" +
                "{\"x\":1,\"y\":10},{\"x\":10,\"y\":100}]}]";
            var sourceRows = "[{\"id\":\"prog.xp.kill_cov\",\"kind\":\"kill\",\"base_xp\":50,\"weight\":1," +
                "\"base_curve_ref\":\"prog.xp_base_curve.cov_sample\"}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.Progression.ProgSchemas.XpBaseCurve.Name,
                    Envelope(Core.Numbers.Progression.ProgSchemas.XpBaseCurve.Name, curveRows))
                .Add(Core.Numbers.Progression.ProgSchemas.XpSource.Name,
                    Envelope(Core.Numbers.Progression.ProgSchemas.XpSource.Name, sourceRows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpBaseCurve);
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpSource);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void XpSource_WithBaseCurveRef_DanglingReference_ReportsReferenceIntegrityError()
        {
            var sourceRows = "[{\"id\":\"prog.xp.kill_dangling\",\"base_xp\":50," +
                "\"base_curve_ref\":\"prog.xp_base_curve.does_not_exist\"}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.XpSource.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.XpSource.Name, sourceRows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpBaseCurve);
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpSource);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "base_curve_ref");
        }

        [Fact]
        public void XpSource_WithLevelDiffRef_ValidReference_LoadsWithoutErrors()
        {
            // combat.level_diff_table 归 Core.Rules（本项目 Tests.Numbers 不引用该程序集，见
            // Tests.Numbers.csproj 只 ProjectReference 到 Core.Numbers/Adapters.Stub），这里用一张
            // 同名同主键的最小占位 TableSchema 站位——reference_integrity 校验只关心"目标表是否
            // 注册、目标行是否存在"，不关心目标表内部字段，真正的 combat.level_diff_table 字段
            // 语义由 combat 模块自己的测试覆盖（CombatLevelDiffTableSchemaTests.cs）。
            var levelDiffStub = new TableSchema(
                name: "combat.level_diff_table",
                primaryKey: "id",
                currentSchemaVersion: 1,
                fields: new[] { new FieldSchema("id", FieldKind.Id, required: true, description: "占位") });

            var levelDiffRows = "[{\"id\":\"combat.level_diff.cov_sample\"}]";
            var sourceRows = "[{\"id\":\"prog.xp.kill_leveldiff\",\"kind\":\"kill\",\"base_xp\":50," +
                "\"level_diff_ref\":\"combat.level_diff.cov_sample\"}]";

            var source = new InMemoryDataSource()
                .Add("combat.level_diff_table", Envelope("combat.level_diff_table", levelDiffRows))
                .Add(Core.Numbers.Progression.ProgSchemas.XpSource.Name,
                    Envelope(Core.Numbers.Progression.ProgSchemas.XpSource.Name, sourceRows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(levelDiffStub);
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpSource);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void XpSource_WithDiscoveryKindAndOnceKey_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"prog.xp.discovery_cov\",\"kind\":\"discovery\",\"base_xp\":10," +
                "\"once_key\":\"prog.explore.zone_1\"}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.XpSource.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.XpSource.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpSource);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void XpSource_KindOutsideEnumValues_ReportsFieldTypeError()
        {
            var rows = "[{\"id\":\"prog.xp.bad_kind\",\"kind\":\"boss\",\"base_xp\":50}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.XpSource.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.XpSource.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpSource);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "kind");
        }

        [Fact]
        public void XpBaseCurve_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"prog.xp_base_curve.cov_wellformed\",\"entries\":[" +
                "{\"x\":1,\"y\":10},{\"x\":10,\"y\":100},{\"x\":20,\"y\":250}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.XpBaseCurve.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.XpBaseCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpBaseCurve);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void XpBaseCurve_NegativeY_ReportsFieldRangeError()
        {
            var rows = "[{\"id\":\"prog.xp_base_curve.cov_negative\",\"entries\":[" +
                "{\"x\":1,\"y\":-5},{\"x\":10,\"y\":100}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.XpBaseCurve.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.XpBaseCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpBaseCurve);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range");
        }

        [Fact]
        public void XpBaseCurve_NonMonotonic_ReportsCurveMonotonicFiniteError()
        {
            // 04 第 3.6 节：全部登记为断点表形态的字段统一受 curve_monotonic_finite 约束，本表登记时
            // 自动受约束，不需要专属校验规则——这里显式注册该通用规则验证约束确实生效。
            var rows = "[{\"id\":\"prog.xp_base_curve.cov_nonmonotonic\",\"entries\":[" +
                "{\"x\":1,\"y\":100},{\"x\":10,\"y\":10}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Progression.ProgSchemas.XpBaseCurve.Name,
                Envelope(Core.Numbers.Progression.ProgSchemas.XpBaseCurve.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.XpBaseCurve);
            registry.RegisterValidationRule(new Core.Foundation.DataRegistry.CurveMonotonicFiniteRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "curve_monotonic_finite");
        }
    }
}
