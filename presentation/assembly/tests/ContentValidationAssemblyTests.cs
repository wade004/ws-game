using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// ADR-0018 决策第 3 条（校验装配入口）验收测试：<see cref="ContentValidationAssembly"/> 是
    /// <c>toolchain/validator</c> 与编辑器基础套件共用的唯一装配入口，本测试覆盖任务书点名的四项：
    /// 默认选项下两条可选规则均未启用；显式接线后两条规则均启用且真实生效；
    /// <see cref="DataRegistryStrictness.WarningsBlock"/> 下警告也会置 <c>IsBlocking</c>；本入口与
    /// 直接调用 <see cref="PresentationSchemaCatalog.RegisterAll(IDataRegistry, Id?, ICreatureTemplateQuery?)"/>
    /// 得到的问题集合一致（同一份数据两条路径报告相同，防止本入口另行分叉出第二套装配顺序）。
    /// </summary>
    public class ContentValidationAssemblyTests
    {
        /// <summary>惯例同 <c>presentation/assembly/tests/PresentationAssemblyTests.cs</c>
        /// <c>AddMinimalGameplayTables</c>：<c>creature.template</c> 引用完整性要求的最小支撑表集合
        /// （<c>stat.definition</c>/<c>arch.power_type</c>/<c>prog.level_curve</c>/
        /// <c>creature.tier_definition</c>），不加载 <c>l10n.text</c>（两处 <c>name_key</c> 会各产出
        /// 一条 <c>text_key_exists</c> Warning，符合预期、不阻断）。</summary>
        private static InMemoryDataSource BuildCreatureTemplateSource()
        {
            var source = new InMemoryDataSource();
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.sample_max_health.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]}");
            source.Add("arch.power_type",
                "{\"table\": \"arch.power_type\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.sample_health.name\", " +
                "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}}" +
                "]}");
            source.Add("prog.level_curve",
                "{\"table\": \"prog.level_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"prog.sample_curve\", \"max_level\": 1, \"entries\": [" +
                "{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}" +
                "]}]}");
            source.Add("creature.tier_definition",
                "{\"table\": \"creature.tier_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"creature.tier.sample_normal\", \"name_key\": \"l10n.creature.tier.sample_normal.name\", " +
                "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}" +
                "]}");
            source.Add("creature.template",
                "{\"table\": \"creature.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"creature.sample_player\", \"name_key\": \"l10n.creature.sample_player.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.sample_normal\", " +
                "\"base_stats\": {\"stat.max_health\": 100}, \"stat_growth_ref\": \"prog.sample_curve\", " +
                "\"faction_id\": \"fac.sample_player\", \"display_ref\": \"display.sample_player\"}" +
                "]}");
            return source;
        }

        /// <summary>只用于满足 <c>ICreatureTemplateQuery</c> 类型要求、不会被真正调用——测试数据集
        /// 里 <c>spawn.table</c> 始终为空，<c>SpawnSummonOnlyCreatureRule.Validate</c>
        /// 对空表直接返回，不会调用本类型任何方法（见该规则源码判断记录）。</summary>
        private sealed class NeverCalledCreatureTemplateQuery : ICreatureTemplateQuery
        {
            public CreatureTemplate Get(Id templateId) =>
                throw new System.InvalidOperationException("测试夹具中不应被调用：spawn.table 为空");

            public bool HasFlag(Id templateId, NpcFlag flag) =>
                throw new System.InvalidOperationException("测试夹具中不应被调用：spawn.table 为空");
        }

        [Fact]
        public void OptionalRuleNames_IsFixedTwoEntryList()
        {
            Assert.Equal(
                new[] { "SpawnSummonOnlyCreatureRule", "DisplayMapCoverageRule" },
                ContentValidationAssembly.OptionalRuleNames);
        }

        [Fact]
        public void Run_DefaultOptions_BothOptionalRulesDisabled_NoneEnabled()
        {
            var source = new InMemoryDataSource();
            var options = new ContentValidationOptions { FailOnUnknownTable = false };

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, options);

            Assert.Equal(
                new[] { "SpawnSummonOnlyCreatureRule", "DisplayMapCoverageRule" },
                run.DisabledOptionalRules);
            Assert.Empty(run.EnabledOptionalRules);
            Assert.False(run.Report.IsBlocking, string.Join("; ", run.Report.Issues));
        }

        [Fact]
        public void Run_WithBothOptionalRuleDependencies_BothEnabled_AndDisplayMapCoverageRuleActuallyFires()
        {
            var source = BuildCreatureTemplateSource();
            // 判断记录：故意不加一条覆盖 creature.sample_player 的 display.map 行，验证
            // DisplayMapCoverageRule 真的接线生效（不是"Enabled 列表里有名字，但规则实际没跑"）。
            var options = new ContentValidationOptions
            {
                FailOnUnknownTable = false,
                CreatureTemplateQuery = new NeverCalledCreatureTemplateQuery(),
                DisplayMapCoverageSources = new[] { ("creature.template", "id") },
            };

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, options);

            Assert.Empty(run.DisabledOptionalRules);
            Assert.Equal(
                new[] { "SpawnSummonOnlyCreatureRule", "DisplayMapCoverageRule" },
                run.EnabledOptionalRules);

            Assert.Contains(run.Report.Issues, i =>
                i.Check == "display_map_coverage" &&
                i.Table == "creature.template" &&
                i.RecordKey == "creature.sample_player");
        }

        [Fact]
        public void Run_WarningsBlockStrictness_WarningOnlyReport_IsBlocking()
        {
            // stat.definition.name_key 引用一个 l10n.text 未加载的文本键 -> 产出恰好一条
            // text_key_exists Warning（见 DataRegistry.ValidateTextKeyField 判断记录），不含任何
            // Error；默认 WarningsAllowed 下不阻断，WarningsBlock 下应阻断。
            var source = new InMemoryDataSource();
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.sample_max_health.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]}");

            var allowedRun = ContentValidationAssembly.Run(
                new IDataSource[] { source },
                new ContentValidationOptions { FailOnUnknownTable = false, Strictness = DataRegistryStrictness.WarningsAllowed });
            Assert.Equal(0, allowedRun.Report.ErrorCount);
            Assert.True(allowedRun.Report.WarningCount > 0);
            Assert.False(allowedRun.Report.IsBlocking);

            var blockRun = ContentValidationAssembly.Run(
                new IDataSource[] { source },
                new ContentValidationOptions { FailOnUnknownTable = false, Strictness = DataRegistryStrictness.WarningsBlock });
            Assert.Equal(0, blockRun.Report.ErrorCount);
            Assert.True(blockRun.Report.WarningCount > 0);
            Assert.True(blockRun.Report.IsBlocking);
        }

        [Fact]
        public void Run_ProducesSameIssueSet_AsDirectPresentationSchemaCatalogRegisterAll()
        {
            var sourceForAssembly = BuildCreatureTemplateSource();
            var sourceForDirect = BuildCreatureTemplateSource();

            var run = ContentValidationAssembly.Run(
                new IDataSource[] { sourceForAssembly },
                new ContentValidationOptions { FailOnUnknownTable = false });

            var directOptions = PresentationSchemaCatalog.CreateOptions();
            directOptions.FailOnUnknownTable = false;
            var bus = new Core.Foundation.EventBus.EventBus(
                Core.Foundation.EventBus.EventCatalog.FromDefinitions(System.Array.Empty<Core.Foundation.EventBus.EventDefinition>()),
                new Core.Foundation.EventBus.EventBusOptions { StrictCatalog = false });
            var directRegistry = new DataRegistry(sourceForDirect, bus, directOptions);
            PresentationSchemaCatalog.RegisterAll(directRegistry);
            var directReport = directRegistry.LoadAll();

            var assemblyIssueStrings = run.Report.Issues.Select(i => i.ToString()).OrderBy(s => s, System.StringComparer.Ordinal).ToList();
            var directIssueStrings = directReport.Issues.Select(i => i.ToString()).OrderBy(s => s, System.StringComparer.Ordinal).ToList();

            Assert.Equal(directIssueStrings, assemblyIssueStrings);
            Assert.Equal(directReport.IsBlocking, run.Report.IsBlocking);
        }

        // ---------- 消费方反馈 E10 根治：IDataRegistryView.RecordCount ----------

        [Fact]
        public void Run_RecordCount_MatchesRegistryRecordCount_WhenNotBlocking()
        {
            // 非阻断态下，ContentValidationRun.RecordCount（Run 内部改用 registry.RecordCount，
            // 见该方法判断记录）应与直接读 run.Registry.RecordCount（新增的默认接口成员）完全一致
            // ——两者是同一份数据源。故意不接线两条可选规则（默认禁用，同
            // Run_DefaultOptions_BothOptionalRulesDisabled_NoneEnabled 用例），避免
            // DisplayMapCoverageRule 因测试数据没有覆盖 display.map 而报 Error 变成阻断态——
            // 本用例只关心非阻断路径下两个 RecordCount 来源是否一致，不是可选规则接线本身。
            var source = BuildCreatureTemplateSource();
            var options = new ContentValidationOptions { FailOnUnknownTable = false };

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, options);

            Assert.False(run.Report.IsBlocking, string.Join("; ", run.Report.Issues));
            Assert.Equal(5, run.RecordCount); // stat.definition/arch.power_type/prog.level_curve/creature.tier_definition/creature.template 各 1 条
            Assert.Equal(run.RecordCount, run.Registry.RecordCount);
        }

        [Fact]
        public void CreateRegistry_CallerControlledLoadAll_RecordCount_ReflectsLoadedRows()
        {
            // 消费方反馈 E10 复现场景：调用方自己持有 CreateRegistry 返回的 registry、自行决定何时
            // LoadAll——此前没有任何办法在这条路径上拿到记录总数，只能自己遍历 Tables/GetAll 求和。
            // IDataRegistryView.RecordCount 新增后，CreateRegistry 场景与 Run 场景使用同一个默认
            // 求和口径（本用例不经过 Run，验证独立成立）。
            var source = new InMemoryDataSource();
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.sample_max_health.name\", \"group\": \"primary\", \"default_base\": 0}," +
                "{\"id\": \"stat.max_mana\", \"name_key\": \"l10n.stat.sample_max_mana.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]}");
            var options = new ContentValidationOptions { FailOnUnknownTable = false };

            var registry = ContentValidationAssembly.CreateRegistry(source, options, out _);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Equal(2, registry.RecordCount);
        }

        [Fact]
        public void RecordCount_DefaultImplementation_SumsAcrossMultipleTables()
        {
            // 覆盖"多张表各自贡献若干条记录，RecordCount 应是全部表的总和"这一基本求和语义
            // （不是只统计第一张表、或漏算某张表）。
            var source = new InMemoryDataSource();
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.sample_max_health.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]}");
            source.Add("arch.power_type",
                "{\"table\": \"arch.power_type\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.sample_health.name\", " +
                "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}}," +
                "{\"id\": \"arch.power.mana\", \"name_key\": \"l10n.power.sample_mana.name\", " +
                "\"max_source\": {\"kind\": \"fixed\", \"value\": 100}}" +
                "]}");
            var options = new ContentValidationOptions { FailOnUnknownTable = false };

            var registry = ContentValidationAssembly.CreateRegistry(source, options, out _);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Equal(3, registry.RecordCount); // 1 + 2
        }
    }
}
