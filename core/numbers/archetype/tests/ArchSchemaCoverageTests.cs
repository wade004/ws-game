using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Numbers.Archetype
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Numbers.Archetype.ArchSchemas.TalentTree"/> 的 <c>nodes</c>
    /// 子结构登记（<c>Item</c>，<c>grants</c> 未被进一步解析不登记子结构）；ADR-0024 第二批登记起
    /// <see cref="Core.Numbers.Archetype.ArchSchemas.Class"/>/<see cref="Core.Numbers.Archetype.ArchSchemas.Race"/>
    /// 的 <c>base_stats</c>/<c>stat_mods</c> 改用 <c>MapSchema.ReferenceKeyTable("stat.definition", ...)</c>
    /// 登记，覆盖范围：子结构命中/坏形状各一例。
    /// </summary>
    public sealed class ArchSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private const string StatDefinitionRows = "[{\"id\":\"stat.cov_sample\",\"name_key\":\"l10n.stat.cov_sample.name\",\"group\":\"primary\"}]";

        // -----------------------------------------------------------------
        // ADR-0024 第二批登记：base_stats（arch.class） / stat_mods（arch.race）
        // -----------------------------------------------------------------

        [Fact]
        public void ClassBaseStats_KnownStatKey_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"arch.class.cov_sample\",\"name_key\":\"l10n.arch.class.cov_sample.name\"," +
                "\"primary_stat\":\"stat.cov_sample\",\"base_stats\":{\"stat.cov_sample\":10},\"power_types\":[]}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, StatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Class.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Class.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Class);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ClassBaseStats_UnknownStatKey_ReportsReferenceIntegrityWithBracketPath()
        {
            var rows = "[{\"id\":\"arch.class.cov_bad\",\"name_key\":\"l10n.arch.class.cov_bad.name\"," +
                "\"primary_stat\":\"stat.cov_sample\",\"base_stats\":{\"stat.no_such\":10},\"power_types\":[]}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, StatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Class.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Class.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Class);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "reference_integrity" && i.Field == "base_stats[stat.no_such]");
        }

        [Fact]
        public void RaceStatMods_KnownStatKey_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"arch.race.cov_sample\",\"name_key\":\"l10n.arch.race.cov_sample.name\"," +
                "\"stat_mods\":{\"stat.cov_sample\":1}}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, StatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Race.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Race.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Race);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void RaceStatMods_UnknownStatKey_ReportsReferenceIntegrityWithBracketPath()
        {
            var rows = "[{\"id\":\"arch.race.cov_bad\",\"name_key\":\"l10n.arch.race.cov_bad.name\"," +
                "\"stat_mods\":{\"stat.no_such\":1}}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, StatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Race.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Race.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Race);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "reference_integrity" && i.Field == "stat_mods[stat.no_such]");
        }

        [Fact]
        public void TalentTreeNodes_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"arch.talent_tree.cov_sample\",\"nodes\":[" +
                "{\"id\":\"n1\",\"cost\":1},{\"id\":\"n2\",\"prerequisites\":[\"n1\"],\"cost\":2,\"grants\":{}}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Archetype.ArchSchemas.TalentTree.Name,
                Envelope(Core.Numbers.Archetype.ArchSchemas.TalentTree.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.TalentTree);
            registry.RegisterValidationRule(new Core.Numbers.Archetype.ArchTalentTreeCycleValidationRule());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void TalentTreeNodes_MissingId_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"arch.talent_tree.cov_bad\",\"nodes\":[{\"cost\":1}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Numbers.Archetype.ArchSchemas.TalentTree.Name,
                Envelope(Core.Numbers.Archetype.ArchSchemas.TalentTree.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.TalentTree);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "nodes[0].id");
        }

        // -----------------------------------------------------------------
        // T-N1-4：arch.class.derivation_overrides 子结构（ADR-0030 决策 2）与
        // ArchClassDerivationOverrideValidationRule 正负例
        // -----------------------------------------------------------------

        private const string DerivedStatDefinitionRows =
            "[{\"id\":\"stat.cov_might\",\"name_key\":\"l10n.stat.cov_might.name\",\"category\":\"primary\"}," +
            "{\"id\":\"stat.cov_power\",\"name_key\":\"l10n.stat.cov_power.name\",\"category\":\"derived\"," +
            "\"derived_from\":[{\"stat\":\"stat.cov_might\",\"coefficient\":1.0}]}]";

        [Fact]
        public void ClassDerivationOverrides_PointingToExistingEdge_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"arch.class.cov_override_ok\",\"name_key\":\"l10n.arch.class.cov_override_ok.name\"," +
                "\"primary_stat\":\"stat.cov_might\",\"base_stats\":{\"stat.cov_might\":10},\"power_types\":[]," +
                "\"derivation_overrides\":[{\"stat\":\"stat.cov_power\",\"source\":\"stat.cov_might\",\"coefficient\":2.0}]}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, DerivedStatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Class.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Class.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Class);
            registry.RegisterValidationRule(new Core.Numbers.Archetype.ArchClassDerivationOverrideValidationRule());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ClassDerivationOverrides_PointingToNonExistentEdge_ReportsError()
        {
            // stat.cov_power 的 derived_from 里只登记了 stat.cov_might 这一条来源，这里覆盖一条
            // 不存在的来源 stat.cov_bogus——ArchClassDerivationOverrideValidationRule 应报错。
            var rows = "[{\"id\":\"arch.class.cov_override_bad\",\"name_key\":\"l10n.arch.class.cov_override_bad.name\"," +
                "\"primary_stat\":\"stat.cov_might\",\"base_stats\":{\"stat.cov_might\":10},\"power_types\":[]," +
                "\"derivation_overrides\":[{\"stat\":\"stat.cov_power\",\"source\":\"stat.cov_bogus\",\"coefficient\":2.0}]}]";
            var extraStatDefinitionRows =
                "[{\"id\":\"stat.cov_might\",\"name_key\":\"l10n.stat.cov_might.name\",\"category\":\"primary\"}," +
                "{\"id\":\"stat.cov_power\",\"name_key\":\"l10n.stat.cov_power.name\",\"category\":\"derived\"," +
                "\"derived_from\":[{\"stat\":\"stat.cov_might\",\"coefficient\":1.0}]}," +
                "{\"id\":\"stat.cov_bogus\",\"name_key\":\"l10n.stat.cov_bogus.name\",\"category\":\"primary\"}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, extraStatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Class.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Class.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Class);
            registry.RegisterValidationRule(new Core.Numbers.Archetype.ArchClassDerivationOverrideValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == Core.Numbers.Archetype.ArchClassDerivationOverrideValidationRule.CheckName);
        }

        /// <summary>
        /// 深度复审 A（测试覆盖缺口 #4）补测，复审整合项 4 更新（2026-09-16，设计层裁定：采纳）：
        /// 验证"<c>derivation_overrides[].source</c> 引用一个完全不存在的 <c>stat.definition</c>
        /// 记录"这一场景的正确行为。
        /// <para>
        /// 判断记录（从"如实记录现状"改为"锁定正确行为"）：本用例最初（深度复审 A）如实记录了一个
        /// 缺口——<see cref="Core.Numbers.Archetype.ArchClassDerivationOverrideValidationRule"/>
        /// 类型顶部判断记录声称"读不到 stat.definition 对应记录时……直接跳过该条目，留给
        /// reference_integrity 单独报"，但当时的 <c>Validate</c> 实现并没有对应的"跳过"分支，
        /// <c>source</c> 引用完全不存在的记录时 <c>reference_integrity</c> 与本规则会同时报错，与
        /// 文档描述不符。复审整合项 4 已在 <c>ArchClassDerivationOverrideValidationRule.Validate</c>
        /// 补上该跳过分支（<c>stat</c>/<c>source</c> 任一侧对应的 <c>stat.definition</c> 记录不存在
        /// 就 <c>continue</c>，不再重复报错）——这是把"错误行为的锁定"改成"正确行为的锁定"，不是
        /// 放宽既有断言：<c>reference_integrity</c> 依旧报错（该检查项本身未改动），只是本规则不再
        /// 对同一条数据凑一份重复噪音。
        /// </para>
        /// </summary>
        [Fact]
        public void ClassDerivationOverrides_SourceReferencesNonExistentStat_OnlyReferenceIntegrityReports()
        {
            var rows = "[{\"id\":\"arch.class.cov_override_unknown\",\"name_key\":\"l10n.arch.class.cov_override_unknown.name\"," +
                "\"primary_stat\":\"stat.cov_might2\",\"base_stats\":{\"stat.cov_might2\":10},\"power_types\":[]," +
                "\"derivation_overrides\":[{\"stat\":\"stat.cov_power2\",\"source\":\"stat.cov_totally_missing\",\"coefficient\":2.0}]}]";
            var extraStatDefinitionRows =
                "[{\"id\":\"stat.cov_might2\",\"name_key\":\"l10n.stat.cov_might2.name\",\"category\":\"primary\"}," +
                "{\"id\":\"stat.cov_power2\",\"name_key\":\"l10n.stat.cov_power2.name\",\"category\":\"derived\"," +
                "\"derived_from\":[{\"stat\":\"stat.cov_might2\",\"coefficient\":1.0}]}]";

            var source = new InMemoryDataSource()
                .Add(Core.Numbers.StatBlock.StatSchemas.Definition.Name,
                    Envelope(Core.Numbers.StatBlock.StatSchemas.Definition.Name, extraStatDefinitionRows))
                .Add(Core.Numbers.Archetype.ArchSchemas.Class.Name,
                    Envelope(Core.Numbers.Archetype.ArchSchemas.Class.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.Class);
            registry.RegisterValidationRule(new Core.Numbers.Archetype.ArchClassDerivationOverrideValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity"
                && i.Field == "derivation_overrides[0].source");
            // 修复后：本规则对"引用本身就不存在"的情形跳过，不再与 reference_integrity 重复报错。
            Assert.DoesNotContain(report.Issues, i =>
                i.Check == Core.Numbers.Archetype.ArchClassDerivationOverrideValidationRule.CheckName);
        }
    }
}
