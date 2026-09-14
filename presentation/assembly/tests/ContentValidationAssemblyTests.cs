using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Gameplay.Spawn;
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

        /// <summary>消费方反馈第 43 条验收：只用来让 <c>SpawnSummonOnlyCreatureRule</c> 真的产出一条
        /// Error 诊断（不关心具体模板 id，本类型对任意模板一律返回 <c>true</c>）。</summary>
        private sealed class AlwaysSummonOnlyCreatureTemplateQuery : ICreatureTemplateQuery
        {
            public CreatureTemplate Get(Id templateId) =>
                throw new System.NotSupportedException("测试夹具未实现 Get：本用例只用到 HasFlag");

            public bool HasFlag(Id templateId, NpcFlag flag) => true;
        }

        [Fact]
        public void OptionalRuleNames_IsFixedTwoEntryList()
        {
            Assert.Equal(
                new[] { "SpawnSummonOnlyCreatureRule", "DisplayMapCoverageRule" },
                ContentValidationAssembly.OptionalRuleNames);
        }

        /// <summary>消费方反馈第 43 条：<see cref="ContentValidationAssembly.OptionalRuleNames"/> 现由
        /// <see cref="ContentValidationAssembly.OptionalRules"/> 投影得到，两者逐项相等（顺序不变）。</summary>
        [Fact]
        public void OptionalRuleNames_EqualsOptionalRules_ProjectedByRuleName()
        {
            Assert.Equal(
                ContentValidationAssembly.OptionalRules.Select(d => d.RuleName).ToList(),
                ContentValidationAssembly.OptionalRuleNames);
        }

        /// <summary>消费方反馈第 43 条核心诉求：<see cref="OptionalRuleDescriptor.CheckName"/> 必须等于
        /// 各规则实际产出诊断的 <see cref="ValidationIssue.Check"/>——不是比对两个字面量字符串是否恰好
        /// 拼写一致，而是让规则真的跑一遍、拿真实诊断的 <c>Check</c> 出来比对，防止"描述符抄错/规则改名
        /// 但描述符没跟着改"这类静态比对发现不了的问题。</summary>
        [Fact]
        public void OptionalRules_CheckName_MatchesActualRuleIssueCheck_ViaRun()
        {
            var source = BuildCreatureTemplateSource();
            source.Add(SpawnSchemas.Table.Name,
                "{\"table\": \"spawn.table\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"spawn.sample_x\", \"map_id\": \"world.sample_field\", \"content_ref\": \"creature.sample_player\", " +
                "\"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0, \"respawn_policy\": \"never\"}" +
                "]}");

            var options = new ContentValidationOptions
            {
                FailOnUnknownTable = false,
                CreatureTemplateQuery = new AlwaysSummonOnlyCreatureTemplateQuery(),
                // 判断记录：本用例只关心 SpawnSummonOnlyCreatureRule 产出的诊断，显式关闭
                // DisplayMapCoverageRule（同类其它用例判断记录），避免 creature.sample_player 没有
                // display.map 覆盖行触发的 display_map_coverage 诊断掩盖本用例要断言的那一条。
                DisplayMapCoverageSources = System.Array.Empty<(string, string)>(),
            };

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, options);

            var spawnIssue = Assert.Single(run.Report.Issues, i => i.Table == SpawnSchemas.Table.Name);
            Assert.Equal(SpawnSummonOnlyCreatureRule.CheckName, spawnIssue.Check);
            Assert.True(ContentValidationAssembly.TryGetOptionalRuleByCheck(spawnIssue.Check, out var spawnDescriptor));
            Assert.Equal("SpawnSummonOnlyCreatureRule", spawnDescriptor.RuleName);

            // 同一次 Run 里 DisplayMapCoverageRule 关了（sources 为空），换一次单独的调用验证它的
            // CheckName 同样对得上真实诊断（用 Run_DefaultOptions_DisplayMapCoverageRule_FiresForUncoveredCreatureTemplate
            // 同款夹具，默认开启）。
            var displayRun = ContentValidationAssembly.Run(
                new IDataSource[] { BuildCreatureTemplateSource() },
                new ContentValidationOptions { FailOnUnknownTable = false });
            var displayIssue = Assert.Single(displayRun.Report.Issues, i => i.Check == DisplayMapCoverageRule.CheckName);
            Assert.True(ContentValidationAssembly.TryGetOptionalRuleByCheck(displayIssue.Check, out var displayDescriptor));
            Assert.Equal("DisplayMapCoverageRule", displayDescriptor.RuleName);
        }

        /// <summary>消费方反馈第 43 条：<see cref="ContentValidationAssembly.TryGetOptionalRuleByCheck"/>
        /// 命中/未命中两个分支。</summary>
        [Fact]
        public void TryGetOptionalRuleByCheck_HitAndMiss()
        {
            Assert.True(ContentValidationAssembly.TryGetOptionalRuleByCheck(DisplayMapCoverageRule.CheckName, out var hit));
            Assert.Equal("DisplayMapCoverageRule", hit.RuleName);
            Assert.Equal(DisplayMapCoverageRule.CheckName, hit.CheckName);

            Assert.False(ContentValidationAssembly.TryGetOptionalRuleByCheck("not_a_registered_check", out var miss));
            Assert.Null(miss);
        }

        /// <summary>消费方反馈第 43 条：<see cref="ContentValidationRun.EnabledOptionalRuleChecks"/>/
        /// <see cref="ContentValidationRun.DisabledOptionalRuleChecks"/> 与
        /// <see cref="ContentValidationRun.EnabledOptionalRules"/>/<see cref="ContentValidationRun.DisabledOptionalRules"/>
        /// 一一对应。**行为变更（消费方反馈第 44 条根治，2026-09-14）**：本用例此前断言默认选项下
        /// 只有 <c>DisplayMapCoverageRule</c> 启用、<c>SpawnSummonOnlyCreatureRule</c> 因未接线
        /// <c>CreatureTemplateQuery</c> 而禁用；现在 <see cref="ContentValidationOptions.CreatureTemplateQuery"/>
        /// 未提供时默认改用 <see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery"/>（见该
        /// 属性判断记录），两条可选规则默认均启用，<see cref="ContentValidationRun.DisabledOptionalRules"/>
        /// 恒为空——不是放宽断言，是真实的默认行为变更。</summary>
        [Fact]
        public void Run_EnabledDisabledOptionalRuleChecks_CorrespondToRuleNameLists()
        {
            var source = new InMemoryDataSource();
            var options = new ContentValidationOptions { FailOnUnknownTable = false };

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, options);

            Assert.Empty(run.DisabledOptionalRules);
            Assert.Empty(run.DisabledOptionalRuleChecks);
            Assert.Equal(new[] { "SpawnSummonOnlyCreatureRule", "DisplayMapCoverageRule" }, run.EnabledOptionalRules);
            Assert.Equal(
                new[] { SpawnSummonOnlyCreatureRule.CheckName, DisplayMapCoverageRule.CheckName },
                run.EnabledOptionalRuleChecks);
        }

        /// <summary>消费方反馈第 34 条：<see cref="ContentValidationOptions.DisplayMapCoverageSources"/>
        /// 未指定（<c>null</c>）时不再等价于"该可选规则禁用"，改为默认使用
        /// <see cref="PresentationSchemaCatalog.DefaultDisplayMapCoverageSources"/>。**行为变更
        /// （消费方反馈第 44 条根治，2026-09-14，比照本条第 34 条先例）**：本用例此前断言
        /// <c>SpawnSummonOnlyCreatureRule</c>（没有框架级默认 <c>ICreatureTemplateQuery</c> 可用）
        /// 仍然默认禁用；现在 <see cref="ContentValidationOptions.CreatureTemplateQuery"/> 未提供时
        /// 默认改用 <see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery"/>，该规则也默认
        /// 启用——不是放宽断言，是真实的默认行为变更，方法名同步改掉"OnlySpawnSummonRuleDisabled"这
        /// 半句已不再成立的描述。测试数据集里 5 张默认覆盖表均未注册任何数据（<c>InMemoryDataSource</c>
        /// 未 <c>Add</c> 过它们），两条规则对空表/空 <c>spawn.table</c> 均不产出任何问题，因此本用例
        /// 仍应保持不阻断。</summary>
        [Fact]
        public void Run_DefaultOptions_BothOptionalRulesEnabledByDefault()
        {
            var source = new InMemoryDataSource();
            var options = new ContentValidationOptions { FailOnUnknownTable = false };

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, options);

            Assert.Empty(run.DisabledOptionalRules);
            Assert.Equal(new[] { "SpawnSummonOnlyCreatureRule", "DisplayMapCoverageRule" }, run.EnabledOptionalRules);
            Assert.False(run.Report.IsBlocking, string.Join("; ", run.Report.Issues));
        }

        /// <summary>消费方反馈第 34 条：默认覆盖清单真的接线生效——<see cref="BuildCreatureTemplateSource"/>
        /// 的 <c>creature.sample_player</c> 没有配对的 <c>display.map</c> 行，未显式覆盖
        /// <see cref="ContentValidationOptions.DisplayMapCoverageSources"/> 时应按
        /// <see cref="PresentationSchemaCatalog.DefaultDisplayMapCoverageSources"/>（含
        /// <c>creature.template</c>）对它报 <c>display_map_coverage</c> 错误，不是"清单存在但
        /// 没真的接线"。</summary>
        [Fact]
        public void Run_DefaultOptions_DisplayMapCoverageRule_FiresForUncoveredCreatureTemplate()
        {
            var source = BuildCreatureTemplateSource();
            var options = new ContentValidationOptions { FailOnUnknownTable = false };

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, options);

            Assert.Contains(run.Report.Issues, i =>
                i.Check == "display_map_coverage" &&
                i.Table == "creature.template" &&
                i.RecordKey == "creature.sample_player");
            Assert.True(run.Report.IsBlocking);
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

            // 消费方反馈第 34 条：直接调用 PresentationSchemaCatalog.RegisterAll 的路径从不注册
            // DisplayMapCoverageRule（见该方法判断记录），而 ContentValidationAssembly.Run 默认选项
            // 现在会启用它（DisplayMapCoverageSources 未指定时按 DefaultDisplayMapCoverageSources
            // 生效，见 Run_DefaultOptions_DisplayMapCoverageRule_FiresForUncoveredCreatureTemplate）；
            // 本用例只关心"两条路径对其余规则报告的问题集合是否一致"，显式传空列表关闭该规则，避免
            // 两条路径的差异被这条本用例范围外的规则掩盖。
            var run = ContentValidationAssembly.Run(
                new IDataSource[] { sourceForAssembly },
                new ContentValidationOptions
                {
                    FailOnUnknownTable = false,
                    DisplayMapCoverageSources = System.Array.Empty<(string, string)>(),
                });

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
            // ——两者是同一份数据源。消费方反馈第 34 条：DisplayMapCoverageRule 现在默认启用
            // （DisplayMapCoverageSources 未指定时按 DefaultDisplayMapCoverageSources 生效），
            // 测试数据里 creature.sample_player 没有配对的 display.map 行会被判定为阻断——本用例
            // 只关心非阻断路径下两个 RecordCount 来源是否一致，不是可选规则接线本身，显式传空列表
            // 关闭该规则（同 Run_ProducesSameIssueSet_AsDirectPresentationSchemaCatalogRegisterAll
            // 判断记录）。
            var source = BuildCreatureTemplateSource();
            var options = new ContentValidationOptions
            {
                FailOnUnknownTable = false,
                DisplayMapCoverageSources = System.Array.Empty<(string, string)>(),
            };

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

        // ---------- 消费方反馈 第 17 条根治：阻断态下 RecordCount 不抛异常 ----------

        private sealed class MutableTableSource : IDataSource
        {
            private readonly string _tableName;

            public string Json;

            public MutableTableSource(string tableName, string json)
            {
                _tableName = tableName;
                Json = json;
            }

            public IReadOnlyList<DataTableSource> ListTables() =>
                new[] { new DataTableSource(_tableName, "memory://" + _tableName, () => Json) };
        }

        [Fact]
        public void Run_BlockingReport_RecordCountDoesNotThrow_MatchesRegistryRecordCount_AndDataLoadCompletedEvent()
        {
            // stat.definition.name_key 引用一个 l10n.text 未加载的文本键 -> 一条 text_key_exists
            // Warning（同 Run_WarningsBlockStrictness_WarningOnlyReport_IsBlocking 用例），
            // WarningsBlock 严格级别下报告阻断，但该表本身已成功加载（1 条记录）——这正是消费方
            // 反馈第 17 条描述的场景："数据集有阻断级问题、注册中心处于阻断态，但编辑器仍需要显示
            // 记录数"。
            var source = new InMemoryDataSource();
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.sample_max_health.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]}");

            var catalog = Core.Foundation.EventBus.EventCatalog.FromDefinitions(new[]
            {
                new Core.Foundation.EventBus.EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new Core.Foundation.EventBus.EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            var bus = new Core.Foundation.EventBus.EventBus(catalog, new Core.Foundation.EventBus.EventBusOptions { StrictCatalog = false });
            DataLoadCompletedEvent? received = null;
            bus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, e => received = e);

            var options = new ContentValidationOptions
            {
                FailOnUnknownTable = false,
                Strictness = DataRegistryStrictness.WarningsBlock,
                Bus = bus,
            };

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, options);

            Assert.True(run.Report.IsBlocking);
            Assert.Equal(1, run.RecordCount);
            Assert.Equal(run.RecordCount, run.Registry.RecordCount);
            Assert.NotNull(received);
            Assert.Equal(received!.RecordCount, run.RecordCount);
        }

        [Fact]
        public void CreateRegistry_ReloadAfterInitialLoad_RecordCount_UpdatesAndMatchesRunOnSameData()
        {
            // 消费方反馈第 17 条：CreateRegistry+Reload 场景（调用方自行持有 registry、自行决定
            // 加载时机）与 Run 场景必须落在同一个计数口径上；重载后计数应随之更新。
            var source = new MutableTableSource("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.sample_max_health.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]}");
            var options = new ContentValidationOptions { FailOnUnknownTable = false };

            var registry = ContentValidationAssembly.CreateRegistry(source, options, out _);
            var firstReport = registry.LoadAll();
            Assert.False(firstReport.IsBlocking, string.Join("; ", firstReport.Issues));
            Assert.Equal(1, registry.RecordCount);

            source.Json =
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.sample_max_health.name\", \"group\": \"primary\", \"default_base\": 0}," +
                "{\"id\": \"stat.max_mana\", \"name_key\": \"l10n.stat.sample_max_mana.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]}";
            var reloadReport = registry.Reload("stat.definition");
            Assert.False(reloadReport.IsBlocking, string.Join("; ", reloadReport.Issues));
            Assert.Equal(2, registry.RecordCount);

            // 同一份两行数据改走 Run 入口，口径应与 CreateRegistry+Reload 一致。
            var runSource = new InMemoryDataSource().Add("stat.definition", source.Json);
            var run = ContentValidationAssembly.Run(new IDataSource[] { runSource }, options);
            Assert.False(run.Report.IsBlocking, string.Join("; ", run.Report.Issues));
            Assert.Equal(registry.RecordCount, run.RecordCount);
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

        // ---------- 消费方反馈第 44 条根治：CreatureTemplateQuery 默认接线（RegistryCreatureTemplateQuery） ----------

        /// <summary>最小支撑表集合（<c>stat.definition</c>/<c>creature.tier_definition</c>），同
        /// <see cref="BuildCreatureTemplateSource"/> 惯例；不含 <c>l10n.text</c>（<c>name_key</c>
        /// 各产出一条 Warning，不阻断，同惯例）。</summary>
        private static InMemoryDataSource BuildSpawnSummonOnlyFixtureSource()
        {
            var source = new InMemoryDataSource();
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.sample_max_health.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]}");
            source.Add("creature.tier_definition",
                "{\"table\": \"creature.tier_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"creature.tier.sample_normal\", \"name_key\": \"l10n.creature.tier.sample_normal.name\", " +
                "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}" +
                "]}");
            return source;
        }

        private static ContentValidationOptions SpawnSummonOnlyFixtureOptions() => new ContentValidationOptions
        {
            FailOnUnknownTable = false,
            // 判断记录：本组用例只关心 SpawnSummonOnlyCreatureRule 的默认接线行为，显式关闭
            // DisplayMapCoverageRule（同类其它用例判断记录），避免 creature.template 记录没有
            // display.map 覆盖行触发的 display_map_coverage 诊断掩盖本组用例要断言的内容。
            DisplayMapCoverageSources = System.Array.Empty<(string, string)>(),
        };

        /// <summary>消费方反馈第 44 条根治（未提供 <see cref="ContentValidationOptions.CreatureTemplateQuery"/>
        /// 时默认改用 <see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery"/>，见该属性
        /// 判断记录）：正例——<c>creature.template</c> 里一条真实登记了 <c>npc_flag.summon_only</c>
        /// 的生物模板被 <c>spawn.table</c> 引用，不必像此前那样调用方必须自己提供
        /// <see cref="ICreatureTemplateQuery"/> 实现才能观测到这条规则的 Error 分支。</summary>
        [Fact]
        public void Run_DefaultCreatureTemplateQuery_SpawnTableReferencesSummonOnlyCreature_ReportsError()
        {
            var source = BuildSpawnSummonOnlyFixtureSource();
            source.Add("creature.template",
                "{\"table\": \"creature.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"creature.sample_totem\", \"name_key\": \"l10n.creature.sample_totem.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.sample_normal\", \"base_stats\": {\"stat.max_health\": 1}, " +
                "\"faction_id\": \"fac.sample_wildlife\", \"npc_flags\": [\"npc_flag.summon_only\"], " +
                "\"display_ref\": \"display.sample_totem\"}" +
                "]}");
            source.Add(SpawnSchemas.Table.Name,
                "{\"table\": \"spawn.table\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"spawn.sample_totem\", \"map_id\": \"world.sample_field\", \"content_ref\": \"creature.sample_totem\", " +
                "\"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0, \"respawn_policy\": \"never\"}" +
                "]}");

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, SpawnSummonOnlyFixtureOptions());

            Assert.DoesNotContain("SpawnSummonOnlyCreatureRule", run.DisabledOptionalRules);
            Assert.Contains(run.Report.Issues, i =>
                i.Check == SpawnSummonOnlyCreatureRule.CheckName &&
                i.Severity == ValidationSeverity.Error &&
                i.RecordKey == "spawn.sample_totem");
        }

        /// <summary>消费方反馈第 44 条根治：<c>spawn.table.content_ref</c> 指向一个
        /// <c>creature.template</c> 里根本不存在的 id——<see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery.Get"/>
        /// 对未登记 id 抛 <see cref="System.ArgumentException"/>，
        /// <c>SpawnSummonOnlyCreatureRule.Validate</c> 捕获后按其既有判断记录"模板未登记：属
        /// reference_integrity/SpawnContentRefRule 职责，本规则不重复报错"跳过，不产出
        /// <c>spawn_summon_only_creature</c>（该场景改由 <c>SpawnContentRefRule</c> 的
        /// <c>spawn_content_ref</c> 报出）。</summary>
        [Fact]
        public void Run_DefaultCreatureTemplateQuery_SpawnTableReferencesUnregisteredCreature_DoesNotReportSpawnSummonOnlyRule()
        {
            var source = BuildSpawnSummonOnlyFixtureSource();
            // creature.template 表本身已注册 schema、但不含 creature.sample_missing 这一行。
            source.Add("creature.template",
                "{\"table\": \"creature.template\", \"schema_version\": 1, \"rows\": []}");
            source.Add(SpawnSchemas.Table.Name,
                "{\"table\": \"spawn.table\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"spawn.sample_missing\", \"map_id\": \"world.sample_field\", \"content_ref\": \"creature.sample_missing\", " +
                "\"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0, \"respawn_policy\": \"never\"}" +
                "]}");

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, SpawnSummonOnlyFixtureOptions());

            Assert.DoesNotContain(run.Report.Issues, i => i.Check == SpawnSummonOnlyCreatureRule.CheckName);
            Assert.Contains(run.Report.Issues, i => i.Check == "spawn_content_ref");
        }

        /// <summary>消费方反馈第 44 条根治：<c>creature.template</c> 记录的 <c>npc_flags</c> 含未登记
        /// 职能标志（字段级 <c>field_allowed_value</c> 单独报一条 Error，报告因此阻断，但记录本身
        /// 仍进入表——见 <see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery"/> 类型级
        /// 判断记录）——<c>ContentValidationAssembly.Run</c> 整体必须正常返回报告，不能被
        /// <see cref="Core.Carriers.Creature.CreatureTemplate.FromRecord"/> 抛出的
        /// <see cref="DataFieldException"/> 中断成未处理异常。</summary>
        [Fact]
        public void Run_DefaultCreatureTemplateQuery_SpawnTableReferencesCreatureWithIllegalNpcFlags_DoesNotThrow()
        {
            var source = BuildSpawnSummonOnlyFixtureSource();
            source.Add("creature.template",
                "{\"table\": \"creature.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"creature.sample_bad_flag\", \"name_key\": \"l10n.creature.sample_bad_flag.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.sample_normal\", \"base_stats\": {\"stat.max_health\": 1}, " +
                "\"faction_id\": \"fac.sample_wildlife\", \"npc_flags\": [\"npc_flag.unknown_flag\"], " +
                "\"display_ref\": \"display.sample_bad_flag\"}" +
                "]}");
            source.Add(SpawnSchemas.Table.Name,
                "{\"table\": \"spawn.table\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"spawn.sample_bad_flag\", \"map_id\": \"world.sample_field\", \"content_ref\": \"creature.sample_bad_flag\", " +
                "\"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0, \"respawn_policy\": \"never\"}" +
                "]}");

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, SpawnSummonOnlyFixtureOptions());

            // 字段级问题单独报（field_allowed_value），报告因此阻断；但不应该因本规则额外报
            // spawn_summon_only_creature（没法判定该记录是否 summon_only，同"未登记"判断记录），
            // 更不应该抛出未处理异常（走到这里、能拿到 run 本身就是断言的一部分）。
            Assert.True(run.Report.IsBlocking);
            Assert.Contains(run.Report.Issues, i => i.Check == "field_allowed_value");
            Assert.DoesNotContain(run.Report.Issues, i => i.Check == SpawnSummonOnlyCreatureRule.CheckName);
        }

        /// <summary>分阶段落地计划 T-N0-3：框架级曲线单调有限规则经 <see cref="PresentationSchemaCatalog.RegisterAll"/>
        /// 注册，<see cref="ContentValidationAssembly.Run"/> 的报告规则清单里必须能看到它（T-N0-2 的
        /// <see cref="ValidationReport.Rules"/>），且只登记一份（去重）。</summary>
        [Fact]
        public void Run_RegistersCurveMonotonicFiniteRule_Once()
        {
            var source = BuildCreatureTemplateSource();
            var options = new ContentValidationOptions
            {
                FailOnUnknownTable = false,
                DisplayMapCoverageSources = System.Array.Empty<(string, string)>(),
            };

            var run = ContentValidationAssembly.Run(new IDataSource[] { source }, options);

            var summary = Assert.Single(run.Report.Rules, r => r.RuleId == nameof(CurveMonotonicFiniteRule));
            Assert.Equal(ValidationSeverity.Error, summary.DefaultSeverity);
            Assert.False(summary.NonEscalatable);
        }
    }
}
