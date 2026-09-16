using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-2a：<c>sim.anchor</c>/<c>sim.scenario</c> 的 schema/校验规则正反例（任务书验收 6）。
    /// <para>
    /// 判断记录（跨表引用用最小桩 schema，不复用生产 <c>ArchSchemas</c>/<c>CreatureSchemas</c>）：
    /// 本测试只关心 <c>sim.*</c> 自身的字段登记与 <see cref="SimAnchorValidationRule"/>/
    /// <see cref="SimScenarioValidationRule"/> 行为，不关心 <c>arch.class</c>/<c>creature.template</c>
    /// 等目标表自身的完整字段契约——这些表在本文件里只登记一个 <c>id</c> 字段的最小占位 schema，
    /// 足以驱动真实的 <c>reference_integrity</c> 检查（值是否命中已加载记录），不需要为了测试
    /// <c>sim.scenario</c> 而搭一整套合法的 <c>arch.class</c>/<c>creature.template</c> 测试数据
    /// （那属于各自模块自己的测试职责，见 <c>SubstructureValidationTests</c> 同款"最小桩 schema"
    /// 惯例）。
    /// </para>
    /// </summary>
    public sealed class SimSchemaTests
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

        /// <summary>最小占位 schema：只登记 <c>id</c>，够 <c>reference_integrity</c> 判定存在性即可。</summary>
        private static TableSchema StubTable(string name) =>
            new TableSchema(name, "id", 1, new[] { new FieldSchema("id", FieldKind.Id, required: true) });

        private static DataRegistry BuildRegistry(
            DataRegistryStrictness strictness = DataRegistryStrictness.WarningsAllowed,
            string? anchorRowsJson = null,
            string? scenarioRowsJson = null,
            bool includeCrossTableStubs = true)
        {
            var source = new InMemoryDataSource();
            if (anchorRowsJson != null)
            {
                source.Add("sim.anchor", Envelope("sim.anchor", anchorRowsJson));
            }
            if (scenarioRowsJson != null)
            {
                source.Add("sim.scenario", Envelope("sim.scenario", scenarioRowsJson));
            }
            if (includeCrossTableStubs)
            {
                source.Add("arch.class", Envelope("arch.class", "[{\"id\": \"arch.class.t\"}]"));
                source.Add("creature.template", Envelope("creature.template", "[{\"id\": \"creature.t\"}]"));
                source.Add("creature.tier_definition", Envelope("creature.tier_definition", "[{\"id\": \"creature.tier.t\"}]"));
                source.Add("item.quality_definition", Envelope("item.quality_definition", "[{\"id\": \"item.quality.t\"}]"));
                source.Add("arch.race", Envelope("arch.race", "[{\"id\": \"arch.race.t\"}]"));
            }

            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { Strictness = strictness });
            Core.Sim.SimSchemaCatalog.RegisterAll(registry);
            if (includeCrossTableStubs)
            {
                registry.RegisterSchema(StubTable("arch.class"));
                registry.RegisterSchema(StubTable("creature.template"));
                registry.RegisterSchema(StubTable("creature.tier_definition"));
                registry.RegisterSchema(StubTable("item.quality_definition"));
                registry.RegisterSchema(StubTable("arch.race"));
            }
            return registry;
        }

        private const string ValidAnchorRow =
            "{\"id\": \"sim.anchor.l1\", \"level\": 1, \"hp\": 100, \"dps\": 10, \"ttk_seconds\": 5, " +
            "\"ttd_seconds\": 8, \"expected_item_level\": 1, \"level_duration_seconds\": 300, " +
            "\"kill_interval_seconds\": 15, \"quest_share\": 0.3}";

        private const string ValidScenarioRow =
            "{\"id\": \"sim.scenario.t\", \"kind\": \"arena\", " +
            "\"player\": {\"class_id\": \"arch.class.t\", \"level\": 1}, " +
            "\"opponent\": {\"creature_id\": \"creature.t\"}, " +
            "\"levels\": [1], \"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
            "\"bandwidths\": {\"dps\": 0.1}}";

        // -----------------------------------------------------------------
        // 正例
        // -----------------------------------------------------------------

        [Fact]
        public void ValidAnchorAndScenario_LoadsWithoutIssues()
        {
            var registry = BuildRegistry(anchorRowsJson: "[" + ValidAnchorRow + "]", scenarioRowsJson: "[" + ValidScenarioRow + "]");
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Empty(report.Issues);
        }

        // -----------------------------------------------------------------
        // sim.anchor 反例
        // -----------------------------------------------------------------

        [Fact]
        public void Anchor_LevelGap_ReportsLevelContinuityError()
        {
            var rows = "[" + ValidAnchorRow + ", " +
                "{\"id\": \"sim.anchor.l3\", \"level\": 3, \"hp\": 190, \"dps\": 19, \"ttk_seconds\": 5, " +
                "\"ttd_seconds\": 8, \"expected_item_level\": 3, \"level_duration_seconds\": 340, " +
                "\"kill_interval_seconds\": 16, \"quest_share\": 0.3}]";
            var registry = BuildRegistry(anchorRowsJson: rows, includeCrossTableStubs: false);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == Core.Sim.SimAnchorValidationRule.LevelContinuityCheck && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void Anchor_DuplicateLevel_ReportsLevelContinuityError()
        {
            var duplicate = "{\"id\": \"sim.anchor.l1b\", \"level\": 1, \"hp\": 100, \"dps\": 10, \"ttk_seconds\": 5, " +
                "\"ttd_seconds\": 8, \"expected_item_level\": 1, \"level_duration_seconds\": 300, " +
                "\"kill_interval_seconds\": 15, \"quest_share\": 0.3}";
            var rows = "[" + ValidAnchorRow + ", " + duplicate + "]";
            var registry = BuildRegistry(anchorRowsJson: rows, includeCrossTableStubs: false);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == Core.Sim.SimAnchorValidationRule.LevelContinuityCheck && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void Anchor_ExpectedItemLevelDecreases_ReportsWarningNotBlocking()
        {
            var l2 = "{\"id\": \"sim.anchor.l2\", \"level\": 2, \"hp\": 140, \"dps\": 14, \"ttk_seconds\": 5, " +
                "\"ttd_seconds\": 8, \"expected_item_level\": 0, \"level_duration_seconds\": 320, " +
                "\"kill_interval_seconds\": 15, \"quest_share\": 0.3}";
            var rows = "[" + ValidAnchorRow + ", " + l2 + "]";

            // Strictness=WarningsBlock：验证该警告登记为不可提升，即便整体严格级别设为"警告也阻断"
            // 仍不阻断（04 第 5 节"警告级抓意图不抓手滑"同一惯例，见 SimAnchorValidationRule 判断记录）。
            var registry = BuildRegistry(strictness: DataRegistryStrictness.WarningsBlock, anchorRowsJson: rows, includeCrossTableStubs: false);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Contains(report.Issues, i =>
                i.Check == Core.Sim.SimAnchorValidationRule.ExpectedItemLevelMonotonicCheck && i.Severity == ValidationSeverity.Warning);
        }

        // -----------------------------------------------------------------
        // sim.scenario 反例
        // -----------------------------------------------------------------

        [Fact]
        public void Scenario_InvalidKind_ReportsBlockingError()
        {
            var row = "{\"id\": \"sim.scenario.t\", \"kind\": \"not_a_kind\", " +
                "\"player\": {\"class_id\": \"arch.class.t\", \"level\": 1}, " +
                "\"opponent\": {\"creature_id\": \"creature.t\"}, " +
                "\"levels\": [1], \"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
                "\"bandwidths\": {\"dps\": 0.1}}";
            var registry = BuildRegistry(scenarioRowsJson: "[" + row + "]");
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void Scenario_BandwidthOutOfRange_ReportsBlockingError()
        {
            var row = "{\"id\": \"sim.scenario.t\", \"kind\": \"arena\", " +
                "\"player\": {\"class_id\": \"arch.class.t\", \"level\": 1}, " +
                "\"opponent\": {\"creature_id\": \"creature.t\"}, " +
                "\"levels\": [1], \"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
                "\"bandwidths\": {\"dps\": 1.5}}";
            var registry = BuildRegistry(scenarioRowsJson: "[" + row + "]");
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void Scenario_UnknownClassReference_ReportsReferenceIntegrityError()
        {
            var row = "{\"id\": \"sim.scenario.t\", \"kind\": \"arena\", " +
                "\"player\": {\"class_id\": \"arch.class.unknown\", \"level\": 1}, " +
                "\"opponent\": {\"creature_id\": \"creature.t\"}, " +
                "\"levels\": [1], \"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
                "\"bandwidths\": {\"dps\": 0.1}}";
            var registry = BuildRegistry(scenarioRowsJson: "[" + row + "]");
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void Scenario_ArenaKindWithoutLevels_ReportsLevelCoverageError()
        {
            var row = "{\"id\": \"sim.scenario.t\", \"kind\": \"arena\", " +
                "\"player\": {\"class_id\": \"arch.class.t\", \"level\": 1}, " +
                "\"opponent\": {\"creature_id\": \"creature.t\"}, " +
                "\"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
                "\"bandwidths\": {\"dps\": 0.1}}";
            var registry = BuildRegistry(scenarioRowsJson: "[" + row + "]");
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == Core.Sim.SimScenarioValidationRule.LevelCoverageCheck);
        }

        [Fact]
        public void Scenario_GrowthKindMissingLevelRange_ReportsLevelCoverageError()
        {
            var row = "{\"id\": \"sim.scenario.t\", \"kind\": \"growth\", " +
                "\"player\": {\"class_id\": \"arch.class.t\", \"level\": 1}, " +
                "\"opponent\": {\"creature_id\": \"creature.t\"}, " +
                "\"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
                "\"bandwidths\": {\"dps\": 0.1}}";
            var registry = BuildRegistry(scenarioRowsJson: "[" + row + "]");
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == Core.Sim.SimScenarioValidationRule.LevelCoverageCheck);
        }

        [Fact]
        public void Scenario_GrowthKindWithValidRange_LoadsWithoutIssues()
        {
            var row = "{\"id\": \"sim.scenario.t\", \"kind\": \"growth\", " +
                "\"player\": {\"class_id\": \"arch.class.t\", \"level\": 1}, " +
                "\"opponent\": {\"creature_id\": \"creature.t\"}, " +
                "\"level_from\": 1, \"level_to\": 10, " +
                "\"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
                "\"bandwidths\": {\"dps\": 0.1}}";
            var registry = BuildRegistry(scenarioRowsJson: "[" + row + "]");
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Scenario_OpponentLevelAndOffsetsBothSet_ReportsLevelCoverageError()
        {
            var row = "{\"id\": \"sim.scenario.t\", \"kind\": \"arena\", " +
                "\"player\": {\"class_id\": \"arch.class.t\", \"level\": 1}, " +
                "\"opponent\": {\"creature_id\": \"creature.t\", \"level\": 1, \"level_offsets\": [-1, 1]}, " +
                "\"levels\": [1], \"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
                "\"bandwidths\": {\"dps\": 0.1}}";
            var registry = BuildRegistry(scenarioRowsJson: "[" + row + "]");
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == Core.Sim.SimScenarioValidationRule.LevelCoverageCheck);
        }

        // -----------------------------------------------------------------
        // sim.scenario.bandwidths 键名反例（深度复审 E-S2：拼写错误全链路静默失效，无任何诊断）
        // -----------------------------------------------------------------

        /// <summary>反例：拼写错误的带宽键（<c>leve_duration</c> 少打一个 <c>l</c>）此前全链路静默
        /// 忽略——本用例锁死修复后的行为：产出一条不阻断的警告，而不是继续悄无声息。</summary>
        [Fact]
        public void Scenario_MisspelledBandwidthKey_ReportsWarningNotBlocking()
        {
            var row = "{\"id\": \"sim.scenario.t\", \"kind\": \"arena\", " +
                "\"player\": {\"class_id\": \"arch.class.t\", \"level\": 1}, " +
                "\"opponent\": {\"creature_id\": \"creature.t\"}, " +
                "\"levels\": [1], \"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
                "\"bandwidths\": {\"dps\": 0.1, \"leve_duration\": 0.2}}";
            var registry = BuildRegistry(scenarioRowsJson: "[" + row + "]");
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Contains(report.Issues, i =>
                i.Check == Core.Sim.SimScenarioValidationRule.BandwidthKeyUnknownCheck &&
                i.Severity == ValidationSeverity.Warning &&
                i.Message.Contains("leve_duration"));
        }

        /// <summary>正例对照：全部已知带宽键（<c>dps</c>/<c>hp</c>/<c>ttk</c>/<c>ttd</c>/
        /// <c>hit_rate</c>/<c>level_duration</c>/<c>item_level</c>/<c>gold</c>/<c>win_rate</c>，见
        /// <see cref="Core.Sim.SimBandwidthKeys.KnownKeys"/>）不触发 <see
        /// cref="Core.Sim.SimScenarioValidationRule.BandwidthKeyUnknownCheck"/>——防止修复本身矫枉过正
        /// 把合法键也判成拼写错误。</summary>
        [Fact]
        public void Scenario_AllKnownBandwidthKeys_NoUnknownBandwidthKeyWarning()
        {
            var row = "{\"id\": \"sim.scenario.t\", \"kind\": \"arena\", " +
                "\"player\": {\"class_id\": \"arch.class.t\", \"level\": 1}, " +
                "\"opponent\": {\"creature_id\": \"creature.t\"}, " +
                "\"levels\": [1], \"runs\": 5, \"base_seed\": 1, \"max_ticks\": 100, " +
                "\"bandwidths\": {\"dps\": 0.1, \"hp\": 0.1, \"ttk\": 0.1, \"ttd\": 0.1, " +
                "\"hit_rate\": 0.1, \"level_duration\": 0.1, \"item_level\": 0.1, \"gold\": 0.1, " +
                "\"win_rate\": 0.1}}";
            var registry = BuildRegistry(scenarioRowsJson: "[" + row + "]");
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == Core.Sim.SimScenarioValidationRule.BandwidthKeyUnknownCheck);
        }
    }
}
