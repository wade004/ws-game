using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Gameplay.Economy;
using Core.Gameplay.Loot;
using Tests.Gameplay.Economy;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// 消费方内容编辑器第 48 条收口验收：<see cref="LootTableAnalyzer.ExpectedQualityDistribution"/>/
    /// <see cref="LootTableAnalyzer.ExpectedAffixInclusion"/>/<see cref="LootTableAnalyzer.ExpectedCurrency"/>
    /// 三段新增分析入口与真实抽取（<see cref="LootHost.RollDetailed"/>，固定种子、N=20000 次）在容差内
    /// 一致——核心证据用真实内容数据集（<c>data/_sample</c>/<c>core/sim/tests/data</c> 各取一张表，
    /// 不是本文件现造的最小夹具），另补子集动态规划 vs 暴力枚举、降级/阻断态、参数校验等边界用例。
    /// </summary>
    public sealed class E48_LootDistributionAnalysisTests
    {
        private const int MonteCarloTrials = 20_000;

        // -----------------------------------------------------------------
        // 定位仓库根、装配真实数据集 registry（惯例同
        // core/gameplay/spawn/tests/SpawnSummonOnlyCreatureRealSampleDataTests.cs 同名方法判断记录）。
        // 本文件路径固定是 <repoRoot>/core/gameplay/loot/tests/E48_LootDistributionAnalysisTests.cs，
        // 向上 4 级（tests → loot → gameplay → core）即仓库根。
        // -----------------------------------------------------------------
        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (var i = 0; i < 4; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }

            return dir.FullName;
        }

        /// <summary>直接读一张真实数据集表文件的信封 JSON 全文（<paramref name="datasetRoot"/> 是相对
        /// 仓库根的目录，如 <c>data/_sample</c>/<c>core/sim/tests/data</c>），文件本身已经是
        /// <c>{table, schema_version, rows}</c> 信封格式，不需要再包一层。</summary>
        private static string ReadTableFile(string repoRoot, string datasetRoot, string relativePath) =>
            File.ReadAllText(Path.Combine(repoRoot, datasetRoot, relativePath));

        private sealed class RealDatasetFixture
        {
            public DataRegistry Registry = null!;
            public IEventBus Bus = null!;
            public Core.Foundation.SimLoop.IWorldSim World = null!;
            public Core.Carriers.Unit.WorldUnitAccess Units = null!;
            public FakeInventoryHost Inventory = null!;
            public EconomyHost Economy = null!;
        }

        /// <summary>用真实数据集（<paramref name="datasetRoot"/> 相对仓库根，如
        /// <c>data/_sample</c>/<c>core/sim/tests/data</c>）里 <c>loot.table</c>/<c>item.*</c>/
        /// <c>stat.definition</c>/<c>econ.*</c> 整张文件（不是本文件手写的最小片段）装配一个真实
        /// <see cref="DataRegistry"/>——同一份文件本就是互相引用自洽的既有内容，整张加载即天然满足
        /// 引用完整性，不需要挑着加载。</summary>
        private static RealDatasetFixture BuildRealDatasetFixture(string datasetRoot)
        {
            var repoRoot = FindRepoRoot();
            var bus = LootTestSupport.NewEventBus();

            var source = new InMemoryDataSource()
                .Add(LootSchemas.Table.Name, ReadTableFile(repoRoot, datasetRoot, "loot/loot.table.json"))
                .Add("item.template", ReadTableFile(repoRoot, datasetRoot, "item/item.template.json"))
                .Add("item.slot_definition", ReadTableFile(repoRoot, datasetRoot, "item/item.slot_definition.json"))
                .Add("item.quality_definition", ReadTableFile(repoRoot, datasetRoot, "item/item.quality_definition.json"))
                .Add("item.affix", ReadTableFile(repoRoot, datasetRoot, "item/item.affix.json"))
                .Add("stat.definition", ReadTableFile(repoRoot, datasetRoot, "stat/stat.definition.json"))
                // stat.definition 的 crit/dodge/haste 三条 rating 都登记了 conversion_ref（引用完整性
                // 硬校验，不是可插拔规则）；item.affix.*.grants.auras 同理引用 skill.aura_def——两张表
                // 与本次分析入口本身无关，只是"这份真实样例数据集本就自洽"的必要陪同表，不是本任务
                // 新增的依赖面。
                .Add("stat.rating_conversion", ReadTableFile(repoRoot, datasetRoot, "stat/stat.rating_conversion.json"))
                .Add("skill.aura_def", ReadTableFile(repoRoot, datasetRoot, "skill/skill.aura_def.json"))
                .Add(EconomySchemas.Currency.Name, ReadTableFile(repoRoot, datasetRoot, "econ/econ.currency.json"))
                .Add(EconomySchemas.GoldBaseCurve.Name, ReadTableFile(repoRoot, datasetRoot, "econ/econ.gold_base_curve.json"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.SlotDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.QualityDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Affix);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.RatingConversion);
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.AuraDef);
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.GoldBaseCurve);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            var economy = new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());

            return new RealDatasetFixture { Registry = registry, Bus = bus, World = world, Units = units, Inventory = inventory, Economy = economy };
        }

        private static LootHost NewHost(RealDatasetFixture f, ulong seed, IEconomyHost? economy = null, LootGoldMultiplierProvider? goldMultiplierProvider = null) =>
            new LootHost(
                f.Registry, new RngHost(seed), f.Bus, f.World, f.Units, f.Inventory, new FakeExprHostFactory(),
                simTimeProvider: () => 0.0, options: null, diagnostics: null, conditionSchema: null,
                economyHost: economy, goldMultiplierProvider: goldMultiplierProvider);

        private static LootTableDef LoadTableDef(IDataRegistryView registry, Id tableId)
        {
            var record = registry.Get(LootSchemas.Table.Name, tableId) ?? throw new InvalidOperationException($"未找到 {tableId}");
            return LootTableParser.Parse(record);
        }

        /// <summary>3σ 二项分布容差（<paramref name="n"/> 次独立试验、单次命中概率 <paramref
        /// name="p"/>）——本文件"理论 vs 观测"对账的统一判定标准，见任务书验收标准。</summary>
        private static double BinomialThreeSigma(int n, double p) => 3.0 * Math.Sqrt(n * p * (1 - p));

        // -----------------------------------------------------------------
        // ① 真实数据集对账：data/_sample/loot/loot.table.json 的 loot.sample_beast。
        // -----------------------------------------------------------------

        [Fact]
        public void RealSampleData_LootSampleBeast_QualityAndCurrencyMatchMonteCarlo()
        {
            var f = BuildRealDatasetFixture("data/_sample");
            var tableId = new Id("loot.sample_beast");
            var def = LoadTableDef(f.Registry, tableId);

            var analysisContext = new LootAnalysisContext();
            var qualityOutcomes = LootTableAnalyzer.ExpectedQualityDistribution(def, analysisContext, f.Registry)
                .ToDictionary(o => o.LeafRef, o => o);

            // item.sample_blade：quality_weights {common:3, rare:1} → common 0.75 / rare 0.25（解析式）。
            var blade = qualityOutcomes[new Id("item.sample_blade")];
            Assert.False(blade.IsDegraded);
            Assert.Equal(0.75, blade.QualityProbabilities[new Id("item.quality.sample_common")], 9);
            Assert.Equal(0.25, blade.QualityProbabilities[new Id("item.quality.sample_rare")], 9);

            // item.sample_helm：quality_weights {common:2, rare:2, epic:1} → 0.4/0.4/0.2。
            var helm = qualityOutcomes[new Id("item.sample_helm")];
            Assert.False(helm.IsDegraded);
            Assert.Equal(0.4, helm.QualityProbabilities[new Id("item.quality.sample_common")], 9);
            Assert.Equal(0.4, helm.QualityProbabilities[new Id("item.quality.sample_rare")], 9);
            Assert.Equal(0.2, helm.QualityProbabilities[new Id("item.quality.sample_epic")], 9);

            // item.sample_token/tonic：未配置 quality_weights，回退模板自身品质（p=1，不掷骰）。
            Assert.Equal(1.0, qualityOutcomes[new Id("item.sample_token")].QualityProbabilities[new Id("item.quality.sample_common")], 9);
            Assert.Equal(1.0, qualityOutcomes[new Id("item.sample_tonic")].QualityProbabilities[new Id("item.quality.sample_common")], 9);

            // econ.currency.sample_coin：当量 [1,3] 均匀 × 金币基数(等级1)=2 × 分档倍率(未注入)=1 ×
            // Multiplier(默认1)——手算期望 = 2(均匀分布期望当量) × 2 = 4。
            var currencyOutcomes = LootTableAnalyzer.ExpectedCurrency(def, analysisContext, f.Economy, sourceLevel: 1);
            var coin = Assert.Single(currencyOutcomes);
            Assert.Equal(new Id("econ.currency.sample_coin"), coin.CurrencyRef);
            Assert.False(coin.IsDegraded);
            Assert.Equal(1.0, coin.DropProbability, 9);
            Assert.Equal(4.0, coin.ExpectedAmount, 9);

            // 蒙特卡洛：真实 LootHost.RollDetailed 固定种子跑 20000 次，统计品质频次/货币均值，核对
            // 理论值在 3σ 容差内。
            var host = NewHost(f, seed: 48001, economy: f.Economy);
            var context = new RollContext(new Id("unit.e48_sample"), killerId: null, multiplier: 1.0, contextId: null, sourceLevel: 1);

            var bladeCommon = 0; var bladeRare = 0;
            var helmCommon = 0; var helmRare = 0; var helmEpic = 0;
            var coinAmountSum = 0L;

            for (var i = 0; i < MonteCarloTrials; i++)
            {
                foreach (var outcome in host.RollDetailed(tableId, context))
                {
                    if (outcome.TemplateId.Equals(new Id("item.sample_blade")))
                    {
                        if (outcome.QualityId!.Value.Equals(new Id("item.quality.sample_common"))) bladeCommon++;
                        else bladeRare++;
                    }
                    else if (outcome.TemplateId.Equals(new Id("item.sample_helm")))
                    {
                        if (outcome.QualityId!.Value.Equals(new Id("item.quality.sample_common"))) helmCommon++;
                        else if (outcome.QualityId!.Value.Equals(new Id("item.quality.sample_rare"))) helmRare++;
                        else helmEpic++;
                    }
                    else if (outcome.TemplateId.Equals(new Id("econ.currency.sample_coin")))
                    {
                        coinAmountSum += outcome.Count;
                    }
                }
            }

            AssertWithinThreeSigma(bladeCommon, MonteCarloTrials, 0.1 * 0.75);
            AssertWithinThreeSigma(bladeRare, MonteCarloTrials, 0.1 * 0.25);
            AssertWithinThreeSigma(helmCommon, MonteCarloTrials, 0.05 * 0.4);
            AssertWithinThreeSigma(helmRare, MonteCarloTrials, 0.05 * 0.4);
            AssertWithinThreeSigma(helmEpic, MonteCarloTrials, 0.05 * 0.2);

            var observedCoinAverage = coinAmountSum / (double)MonteCarloTrials;
            // 当量 1..3 均匀（方差 = ((2-1)^2+0+(3-2)^2)/3*... 具体用金币基数=2 折算后的均值/方差手算，
            // 见类文档"金币换算无取整偏差"判断记录：三种当量对应金额 2/4/6，均值 4、样本方差 8/3。
            var coinStd = Math.Sqrt(8.0 / 3.0 / MonteCarloTrials);
            Assert.InRange(observedCoinAverage, 4.0 - 3 * coinStd, 4.0 + 3 * coinStd);
        }

        // -----------------------------------------------------------------
        // ② 嵌入仿真数据集对账：core/sim/tests/data/loot/loot.table.json 的 loot.table.sim_wolf_l1。
        // -----------------------------------------------------------------

        [Fact]
        public void EmbeddedSimData_LootTableSimWolfL1_QualityCurrencyAffixMatchMonteCarlo()
        {
            var f = BuildRealDatasetFixture("core/sim/tests/data");
            var tableId = new Id("loot.table.sim_wolf_l1");
            var def = LoadTableDef(f.Registry, tableId);

            var analysisContext = new LootAnalysisContext();
            var qualityOutcomes = LootTableAnalyzer.ExpectedQualityDistribution(def, analysisContext, f.Registry)
                .ToDictionary(o => o.LeafRef, o => o);

            // item.sim_head_l1_rare：quality_weights {common:1, rare:3} → 0.25/0.75。
            var head = qualityOutcomes[new Id("item.sim_head_l1_rare")];
            Assert.False(head.IsDegraded);
            Assert.Equal(0.25, head.QualityProbabilities[new Id("item.quality.sim_common")], 9);
            Assert.Equal(0.75, head.QualityProbabilities[new Id("item.quality.sim_rare")], 9);

            // 词缀入选：item.sim_head_l1_rare 在 quality=sim_rare 时，白名单 {fortitude(2), berserker(1)}
            // 都落在 quality_pool=sim_rare、都不在白名单外，候选池大小 2 == affix_count(2)——k>=n 时
            // InclusionProbabilitiesExact 恒 1（见 LootTableAnalyzer 判断记录"k>=n 全部必中"），是精确值
            // 不是近似巧合。
            var affixResult = LootTableAnalyzer.ExpectedAffixInclusion(
                new Id("item.sim_head_l1_rare"), new Id("item.quality.sim_rare"), f.Registry);
            Assert.False(affixResult.IsDegraded);
            Assert.Equal(2, affixResult.CandidatePoolSize);
            Assert.Equal(2, affixResult.ActualAffixCount);
            Assert.Equal(1.0, affixResult.InclusionProbabilities![new Id("item.affix.sim_of_fortitude")], 9);
            Assert.Equal(1.0, affixResult.InclusionProbabilities![new Id("item.affix.sim_of_the_berserker")], 9);

            // quality=sim_common 时白名单里两条都是 rare 池，候选池为空——不是降级，是内容本身如此。
            var affixCommon = LootTableAnalyzer.ExpectedAffixInclusion(
                new Id("item.sim_head_l1_rare"), new Id("item.quality.sim_common"), f.Registry);
            Assert.False(affixCommon.IsDegraded);
            Assert.Equal(0, affixCommon.CandidatePoolSize);
            Assert.Equal(0, affixCommon.ActualAffixCount);
            Assert.Empty(affixCommon.InclusionProbabilities!);

            // econ.currency.sim_gold：当量固定 1、chance_each weight=1.0 恒命中 → 期望数量 = 1 × 金币
            // 基数(等级1)=5 × 1 × 1 = 5，且恒定不随机（count_range min=max=1，DropProbability=1）。
            var currencyOutcomes = LootTableAnalyzer.ExpectedCurrency(def, analysisContext, f.Economy, sourceLevel: 1);
            var gold = Assert.Single(currencyOutcomes);
            Assert.Equal(1.0, gold.DropProbability, 9);
            Assert.Equal(5.0, gold.ExpectedAmount, 9);

            var host = NewHost(f, seed: 48002, economy: f.Economy);
            var context = new RollContext(new Id("unit.e48_sim"), killerId: null, multiplier: 1.0, contextId: null, sourceLevel: 1);

            var headCommon = 0; var headRare = 0; var headRareBothAffixes = 0; var headRareCount = 0;
            var goldAmountSum = 0L; var goldFireCount = 0;

            for (var i = 0; i < MonteCarloTrials; i++)
            {
                foreach (var outcome in host.RollDetailed(tableId, context))
                {
                    if (outcome.TemplateId.Equals(new Id("item.sim_head_l1_rare")))
                    {
                        if (outcome.QualityId!.Value.Equals(new Id("item.quality.sim_common")))
                        {
                            headCommon++;
                        }
                        else
                        {
                            headRare++;
                            headRareCount++;
                            if (outcome.Affixes.Count == 2 &&
                                outcome.Affixes.Any(a => a.Equals(new Id("item.affix.sim_of_fortitude"))) &&
                                outcome.Affixes.Any(a => a.Equals(new Id("item.affix.sim_of_the_berserker"))))
                            {
                                headRareBothAffixes++;
                            }
                        }
                    }
                    else if (outcome.TemplateId.Equals(new Id("econ.currency.sim_gold")))
                    {
                        goldFireCount++;
                        goldAmountSum += outcome.Count;
                    }
                }
            }

            AssertWithinThreeSigma(headCommon, MonteCarloTrials, 0.05 * 0.25);
            AssertWithinThreeSigma(headRare, MonteCarloTrials, 0.05 * 0.75);
            Assert.Equal(headRareCount, headRareBothAffixes); // k>=n 恒必中，逐条命中率 100%。

            Assert.Equal(MonteCarloTrials, goldFireCount); // weight_or_chance=1.0 恒命中。
            Assert.Equal(5L * MonteCarloTrials, goldAmountSum); // 当量固定 1，无取整偏差，逐次都是 5。
        }

        private static void AssertWithinThreeSigma(int observedCount, int trials, double p)
        {
            var mean = trials * p;
            var sigma = BinomialThreeSigma(trials, p);
            Assert.InRange(observedCount, mean - sigma, mean + sigma);
        }

        // -----------------------------------------------------------------
        // ③ 合成夹具：嵌套 loot.* 引用的品质分布合并、guaranteed_min 补抽命中不改变品质分布来源、
        //    同一叶子经两条不同条目按期望数量加权混合。
        // -----------------------------------------------------------------

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private const string ItemRowsJson =
            "[{\"id\": \"item.e48_sword\", \"slot\": \"item.slot.e48_main_hand\", \"quality\": \"item.quality.e48_common\", " +
            "\"item_level\": 1, \"display_ref\": \"display.e48\", \"stack_size\": 1, \"name_key\": \"l10n.e48.sword\"}]";

        private const string SlotRowsJson =
            "[{\"id\": \"item.slot.e48_main_hand\", \"name_key\": \"l10n.e48.slot\"}]";

        private const string QualityDefRowsJson =
            "[{\"id\": \"item.quality.e48_common\", \"name_key\": \"l10n.e48.q.common\", \"affix_count\": 0}, " +
            "{\"id\": \"item.quality.e48_rare\", \"name_key\": \"l10n.e48.q.rare\", \"affix_count\": 0}]";

        private const string EmptyAffixRowsJson = "[]";

        private const string StatDefRowsJson = "[]";

        private static DataRegistry BuildSyntheticItemRegistry(IEventBus bus, string lootTableRowsJson) =>
            LootTestSupport.MakeRegistryWithItems(
                bus, lootTableRowsJson, ItemRowsJson, SlotRowsJson, QualityDefRowsJson, EmptyAffixRowsJson, StatDefRowsJson);

        [Fact]
        public void NestedLootReference_QualityDistribution_PropagatesInnerEntryWeights()
        {
            var bus = LootTestSupport.NewEventBus();

            const string innerTableRowsJson =
                "[{\"id\": \"loot.e48_inner\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.e48_sword\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}, " +
                "\"quality_weights\": {\"item.quality.e48_common\": 1, \"item.quality.e48_rare\": 3}}]}]}]";

            var outerTableRowsJson =
                "[{\"id\": \"loot.e48_outer\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"loot.e48_inner\", \"weight_or_chance\": 0.8, \"count_range\": {\"min\": 1, \"max\": 1}}]}]}," +
                Trim(innerTableRowsJson) + "]";

            var registry = BuildSyntheticItemRegistry(bus, outerTableRowsJson);
            var outerId = new Id("loot.e48_outer");
            var innerId = new Id("loot.e48_inner");
            var outerDef = LoadTableDef(registry, outerId);
            var innerDef = LoadTableDef(registry, innerId);

            var context = new LootAnalysisContext { Tables = new Dictionary<Id, LootTableDef> { [innerId] = innerDef } };
            var outcomes = LootTableAnalyzer.ExpectedQualityDistribution(outerDef, context, registry);
            var sword = Assert.Single(outcomes);
            Assert.Equal(new Id("item.e48_sword"), sword.LeafRef);
            // 外层 loot.* 条目本身没有品质概念——不管外层命中概率是多少，条件分布只取决于内层
            // quality_weights（1:3 → 0.25/0.75），与外层 fireProbability=0.8 无关。
            Assert.Equal(0.25, sword.QualityProbabilities[new Id("item.quality.e48_common")], 9);
            Assert.Equal(0.75, sword.QualityProbabilities[new Id("item.quality.e48_rare")], 9);

            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            var host = new LootHost(
                registry, new RngHost(48003), bus, world, units, inventory, new FakeExprHostFactory(), () => 0.0);
            var rollContext = new RollContext(new Id("unit.e48_nested"));

            var common = 0; var rare = 0;
            for (var i = 0; i < MonteCarloTrials; i++)
            {
                foreach (var outcome in host.RollDetailed(outerId, rollContext))
                {
                    if (outcome.QualityId!.Value.Equals(new Id("item.quality.e48_common"))) common++;
                    else rare++;
                }
            }

            AssertWithinThreeSigma(common, MonteCarloTrials, 0.8 * 0.25);
            AssertWithinThreeSigma(rare, MonteCarloTrials, 0.8 * 0.75);
        }

        private static string Trim(string rowsJson) => rowsJson.TrimStart('[').TrimEnd(']');

        [Fact]
        public void GuaranteedMinTopUp_QualityDistribution_UsesFiringEntryOwnWeights()
        {
            var bus = LootTestSupport.NewEventBus();

            // 两条候选各自 chance_each 概率很低（自然产出数几乎总是不足 guaranteed_min=1），几乎全部
            // 命中都经保底补抽——验证补抽命中仍严格按"命中的是哪条 LootEntry"取其自身 quality_weights，
            // 不会被误当成"取任意/固定品质"。
            const string tableRowsJson =
                "[{\"id\": \"loot.e48_guaranteed\", \"guaranteed_min\": 1, \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.e48_sword\", \"weight_or_chance\": 0.01, \"count_range\": {\"min\": 1, \"max\": 1}, " +
                "\"quality_weights\": {\"item.quality.e48_common\": 1, \"item.quality.e48_rare\": 3}}]}]}]";

            var registry = BuildSyntheticItemRegistry(bus, tableRowsJson);
            var tableId = new Id("loot.e48_guaranteed");
            var def = LoadTableDef(registry, tableId);

            var outcomes = LootTableAnalyzer.ExpectedQualityDistribution(def, new LootAnalysisContext(), registry);
            var sword = Assert.Single(outcomes);
            Assert.False(sword.IsDegraded);
            // 保底补抽命中的仍是这同一条 LootEntry，品质分布应与该条目自身 quality_weights（1:3）
            // 完全一致——不会因为大部分产出来自保底补抽就偏向别的比例。
            Assert.Equal(0.25, sword.QualityProbabilities[new Id("item.quality.e48_common")], 9);
            Assert.Equal(0.75, sword.QualityProbabilities[new Id("item.quality.e48_rare")], 9);

            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            var host = new LootHost(
                registry, new RngHost(48004), bus, world, units, inventory, new FakeExprHostFactory(), () => 0.0);
            var rollContext = new RollContext(new Id("unit.e48_guaranteed"));

            var common = 0; var rare = 0; var totalOutcomes = 0;
            for (var i = 0; i < MonteCarloTrials; i++)
            {
                var result = host.RollDetailed(tableId, rollContext);
                totalOutcomes += result.Count;
                foreach (var outcome in result)
                {
                    if (outcome.QualityId!.Value.Equals(new Id("item.quality.e48_common"))) common++;
                    else rare++;
                }
            }

            // guaranteed_min=1、唯一候选池只有这一条——每次 Roll 必定恰好产出一条（自然命中或保底补抽）。
            Assert.Equal(MonteCarloTrials, totalOutcomes);
            AssertWithinThreeSigma(common, MonteCarloTrials, 0.25);
            AssertWithinThreeSigma(rare, MonteCarloTrials, 0.75);
        }

        [Fact]
        public void TwoEntriesSameLeaf_DifferentQualityWeights_MixtureWeightedByExpectedCount()
        {
            var bus = LootTestSupport.NewEventBus();

            // 同一叶子 item.e48_sword 经两条独立 chance_each 条目产出：A 命中率 0.6 恒定 common，
            // B 命中率 0.4 恒定 rare（均 count_range 1..1，期望数量退化为命中概率本身）——理论混合比例
            // = 0.6 : 0.4（按各自期望数量加权，不是"取其一"也不是"条目数平均"）。
            const string tableRowsJson =
                "[{\"id\": \"loot.e48_mixture\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.e48_sword\", \"weight_or_chance\": 0.6, \"count_range\": {\"min\": 1, \"max\": 1}, " +
                "\"quality_weights\": {\"item.quality.e48_common\": 1}}, " +
                "{\"ref\": \"item.e48_sword\", \"weight_or_chance\": 0.4, \"count_range\": {\"min\": 1, \"max\": 1}, " +
                "\"quality_weights\": {\"item.quality.e48_rare\": 1}}]}]}]";

            var registry = BuildSyntheticItemRegistry(bus, tableRowsJson);
            var tableId = new Id("loot.e48_mixture");
            var def = LoadTableDef(registry, tableId);

            var outcomes = LootTableAnalyzer.ExpectedQualityDistribution(def, new LootAnalysisContext(), registry);
            var sword = Assert.Single(outcomes);
            Assert.Equal(0.6, sword.QualityProbabilities[new Id("item.quality.e48_common")], 9);
            Assert.Equal(0.4, sword.QualityProbabilities[new Id("item.quality.e48_rare")], 9);

            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            var host = new LootHost(
                registry, new RngHost(48005), bus, world, units, inventory, new FakeExprHostFactory(), () => 0.0);
            var rollContext = new RollContext(new Id("unit.e48_mixture"));

            var common = 0; var rare = 0;
            for (var i = 0; i < MonteCarloTrials; i++)
            {
                foreach (var outcome in host.RollDetailed(tableId, rollContext))
                {
                    if (outcome.QualityId!.Value.Equals(new Id("item.quality.e48_common"))) common++;
                    else rare++;
                }
            }

            // A、B 各自独立伯努利，直接按各自命中概率核对原始计数（不做比例/比值意义下的方差推导）。
            AssertWithinThreeSigma(common, MonteCarloTrials, 0.6);
            AssertWithinThreeSigma(rare, MonteCarloTrials, 0.4);
        }

        // -----------------------------------------------------------------
        // ④ 词缀入选：子集动态规划 vs 暴力枚举逐位相等（池 ≤ 6）；池 > 16 降级；registry 阻断不抛。
        // -----------------------------------------------------------------

        private static string AffixRowsJson(IReadOnlyList<(string Id, double Weight)> candidates, string qualityPool)
        {
            var rows = candidates.Select(c =>
                "{\"id\": \"" + c.Id + "\", \"name_key\": \"l10n.e48.affix\", \"budget_share\": 0.1, " +
                "\"stat_mix\": [], \"quality_pool\": \"" + qualityPool + "\", \"weight\": " +
                c.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
            return "[" + string.Join(",", rows) + "]";
        }

        private static DataRegistry BuildAffixOnlyRegistry(IEventBus bus, IReadOnlyList<(string Id, double Weight)> candidates)
        {
            const string templateRowsJson =
                "[{\"id\": \"item.e48_affix_target\", \"slot\": \"item.slot.e48_main_hand\", " +
                "\"quality\": \"item.quality.e48_common\", \"item_level\": 1, \"display_ref\": \"display.e48\", " +
                "\"stack_size\": 1, \"name_key\": \"l10n.e48.target\"}]";
            const string qualityDefRowsJson =
                "[{\"id\": \"item.quality.e48_common\", \"name_key\": \"l10n.e48.q.common\", \"affix_count\": 2}]";
            const string emptyLootRowsJson = "[]";

            return LootTestSupport.MakeRegistryWithItems(
                bus, emptyLootRowsJson, templateRowsJson, SlotRowsJson, qualityDefRowsJson,
                AffixRowsJson(candidates, "item.quality.e48_common"), StatDefRowsJson, registerValidationRule: false);
        }

        /// <summary>独立于生产代码 <c>InclusionProbabilitiesExact</c>（位掩码动态规划）的暴力枚举：
        /// 显式递归穷举全部"不放回按权重顺序抽 k 条"的排列，按路径概率求和——两种实现方式不同（递归
        /// 排列枚举 vs 迭代位掩码状态转移），用于交叉验证 DP 实现无 bug，不是同一份代码复制粘贴。</summary>
        private static double[] BruteForceInclusion(double[] weights, int k)
        {
            var n = weights.Length;
            var result = new double[n];
            var included = new bool[n];

            void Recurse(List<int> remaining, int picksLeft, double pathProbability)
            {
                if (picksLeft == 0 || remaining.Count == 0)
                {
                    for (var i = 0; i < n; i++)
                    {
                        if (included[i])
                        {
                            result[i] += pathProbability;
                        }
                    }

                    return;
                }

                var total = remaining.Sum(idx => weights[idx]);
                if (total <= 0)
                {
                    for (var i = 0; i < n; i++)
                    {
                        if (included[i])
                        {
                            result[i] += pathProbability;
                        }
                    }

                    return;
                }

                foreach (var idx in remaining)
                {
                    var p = weights[idx] / total;
                    var nextRemaining = remaining.Where(x => x != idx).ToList();
                    included[idx] = true;
                    Recurse(nextRemaining, picksLeft - 1, pathProbability * p);
                    included[idx] = false;
                }
            }

            Recurse(Enumerable.Range(0, n).ToList(), k, 1.0);
            return result;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        public void AffixInclusion_DPMatchesBruteForce_PoolOfFive(int affixCount)
        {
            var bus = LootTestSupport.NewEventBus();
            var candidates = new List<(string Id, double Weight)>
            {
                ("item.affix.e48_a", 1), ("item.affix.e48_b", 2), ("item.affix.e48_c", 3),
                ("item.affix.e48_d", 4), ("item.affix.e48_e", 5),
            };

            // 用不同 affix_count 复用同一份候选池——affix_count 通过重新构建一条 quality_definition 行传入。
            const string templateRowsJson =
                "[{\"id\": \"item.e48_affix_target\", \"slot\": \"item.slot.e48_main_hand\", " +
                "\"quality\": \"item.quality.e48_common\", \"item_level\": 1, \"display_ref\": \"display.e48\", " +
                "\"stack_size\": 1, \"name_key\": \"l10n.e48.target\"}]";
            var qualityDefRowsJson =
                "[{\"id\": \"item.quality.e48_common\", \"name_key\": \"l10n.e48.q.common\", \"affix_count\": " + affixCount + "}]";
            var registry = LootTestSupport.MakeRegistryWithItems(
                bus, "[]", templateRowsJson, SlotRowsJson, qualityDefRowsJson,
                AffixRowsJson(candidates, "item.quality.e48_common"), StatDefRowsJson, registerValidationRule: false);

            var result = LootTableAnalyzer.ExpectedAffixInclusion(
                new Id("item.e48_affix_target"), new Id("item.quality.e48_common"), registry);

            Assert.False(result.IsDegraded);
            Assert.Equal(5, result.CandidatePoolSize);
            Assert.Equal(Math.Min(affixCount, 5), result.ActualAffixCount);

            var weights = candidates.Select(c => c.Weight).ToArray();
            var expected = BruteForceInclusion(weights, result.ActualAffixCount);
            for (var i = 0; i < candidates.Count; i++)
            {
                Assert.Equal(expected[i], result.InclusionProbabilities![new Id(candidates[i].Id)], 9);
            }
        }

        [Fact]
        public void AffixInclusion_PoolOver16_ReturnsDegradedNullWithReason()
        {
            var bus = LootTestSupport.NewEventBus();
            var candidates = Enumerable.Range(0, 17).Select(i => ($"item.affix.e48_p{i}", 1.0)).ToList();
            var registry = BuildAffixOnlyRegistry(bus, candidates);

            var result = LootTableAnalyzer.ExpectedAffixInclusion(
                new Id("item.e48_affix_target"), new Id("item.quality.e48_common"), registry);

            Assert.True(result.IsDegraded);
            Assert.Null(result.InclusionProbabilities);
            Assert.Equal(17, result.CandidatePoolSize);
            Assert.NotNull(result.Reason);
            Assert.Contains("RollDetailed", result.Reason);
        }

        [Fact]
        public void AffixInclusion_PoolOfSixteen_StillExact()
        {
            var bus = LootTestSupport.NewEventBus();
            var candidates = Enumerable.Range(0, 16).Select(i => ($"item.affix.e48_q{i}", (double)(i + 1))).ToList();
            var registry = BuildAffixOnlyRegistry(bus, candidates);

            var result = LootTableAnalyzer.ExpectedAffixInclusion(
                new Id("item.e48_affix_target"), new Id("item.quality.e48_common"), registry);

            Assert.False(result.IsDegraded);
            Assert.NotNull(result.InclusionProbabilities);
            Assert.Equal(16, result.CandidatePoolSize);
            var sum = result.InclusionProbabilities!.Values.Sum();
            // 恰好 16 条、affix_count=2：期望入选次数总和应为 2（每次抽取恰好命中一条，期望值可加）。
            Assert.Equal(2.0, sum, 6);
        }

        [Fact]
        public void ExpectedAffixInclusion_AffixCountZero_ReturnsEmptyNotDegraded()
        {
            var bus = LootTestSupport.NewEventBus();
            var candidates = new List<(string Id, double Weight)> { ("item.affix.e48_unused", 1.0) };
            const string templateRowsJson =
                "[{\"id\": \"item.e48_affix_target\", \"slot\": \"item.slot.e48_main_hand\", " +
                "\"quality\": \"item.quality.e48_common\", \"item_level\": 1, \"display_ref\": \"display.e48\", " +
                "\"stack_size\": 1, \"name_key\": \"l10n.e48.target\"}]";
            const string qualityDefRowsJson =
                "[{\"id\": \"item.quality.e48_common\", \"name_key\": \"l10n.e48.q.common\", \"affix_count\": 0}]";
            var registry = LootTestSupport.MakeRegistryWithItems(
                bus, "[]", templateRowsJson, SlotRowsJson, qualityDefRowsJson,
                AffixRowsJson(candidates, "item.quality.e48_common"), StatDefRowsJson, registerValidationRule: false);

            var result = LootTableAnalyzer.ExpectedAffixInclusion(
                new Id("item.e48_affix_target"), new Id("item.quality.e48_common"), registry);

            Assert.False(result.IsDegraded);
            Assert.Equal(0, result.ActualAffixCount);
            Assert.Empty(result.InclusionProbabilities!);
        }

        // -----------------------------------------------------------------
        // ⑤ 阻断态不抛：只读分析入口对 registry.Get/GetAll 抛出的替身容错降级。
        // -----------------------------------------------------------------

        /// <summary>始终抛 <see cref="InvalidOperationException"/> 的 <see cref="IDataRegistryView"/>
        /// 替身——不覆盖默认接口成员 <c>TryGet</c>/<c>TryGetAll</c>，因此它们会落回接口默认实现（try
        /// 调 <c>Get</c>/<c>GetAll</c>，捕获 <see cref="InvalidOperationException"/>），真正体现
        /// <see cref="TolerantRegistryView"/> 的降级路径——语义同
        /// <c>ItemBudgetCurveBuildStatBudgetInfoTests</c> 判断记录"真正的降级路径由……不覆盖 Try* 的
        /// 替身覆盖"。</summary>
        private sealed class AlwaysBlockingRegistryView : IDataRegistryView
        {
            public DataRecord? Get(string table, string key) => throw Blocked();

            public DataRecord? Get(string table, Id id) => throw Blocked();

            public IReadOnlyList<DataRecord> GetAll(string table) => throw Blocked();

            public IReadOnlyList<DataRecord> Query(string table, Core.Foundation.Expr.ExprNode predicate) => throw Blocked();

            public IReadOnlyList<DataRecord> Query(string table, string predicateText) => throw Blocked();

            public IReadOnlyList<string> Tables => Array.Empty<string>();

            public TableSchema? GetSchema(string table) => null;

            public int RecordCount => 0;

            private static InvalidOperationException Blocked() =>
                new InvalidOperationException("数据校验未通过，禁止读取（测试替身模拟阻断态）");
        }

        [Fact]
        public void ExpectedQualityDistribution_BlockingRegistry_DoesNotThrow_MarksDegraded()
        {
            var bus = LootTestSupport.NewEventBus();
            const string tableRowsJson =
                "[{\"id\": \"loot.e48_blocked\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.e48_sword\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}}]}]}]";
            var registry = BuildSyntheticItemRegistry(bus, tableRowsJson);
            var def = LoadTableDef(registry, new Id("loot.e48_blocked"));

            var blockedView = new AlwaysBlockingRegistryView();
            var outcomes = LootTableAnalyzer.ExpectedQualityDistribution(def, new LootAnalysisContext(), blockedView);

            var sword = Assert.Single(outcomes);
            Assert.True(sword.IsDegraded);
            Assert.NotNull(sword.Reason);
            Assert.Empty(sword.QualityProbabilities);
        }

        [Fact]
        public void ExpectedAffixInclusion_BlockingRegistry_DoesNotThrow_MarksDegraded()
        {
            var blockedView = new AlwaysBlockingRegistryView();

            var result = LootTableAnalyzer.ExpectedAffixInclusion(
                new Id("item.whatever"), new Id("item.quality.whatever"), blockedView);

            Assert.True(result.IsDegraded);
            Assert.NotNull(result.Reason);
        }

        // -----------------------------------------------------------------
        // ⑥ 货币期望：分档倍率接入、金币基数曲线缺失时显式降级（不抛异常）。
        // -----------------------------------------------------------------

        [Fact]
        public void ExpectedCurrency_TierGoldMultiplier_MatchesResolveCurrencyOutcomeFormula()
        {
            var bus = LootTestSupport.NewEventBus();
            const string tableRowsJson =
                "[{\"id\": \"loot.e48_tier_gold\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"econ.currency.e48_coin\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 2, \"max\": 2}}]}]}]";

            var registry = BuildEconRegistry(bus, tableRowsJson, goldBaseAtLevelOne: 5);
            var def = LoadTableDef(registry, new Id("loot.e48_tier_gold"));
            var economy = new EconomyHost(registry, bus, new FakeInventoryHost(), new FakeNumericExprHostFactory());

            var eliteTierId = new Id("creature.tier.e48_elite");
            LootGoldMultiplierProvider provider = tierId => tierId.HasValue && tierId.Value.Equals(eliteTierId) ? 2.0 : 1.0;

            var context = new LootAnalysisContext { Multiplier = 1.5 };
            var outcomes = LootTableAnalyzer.ExpectedCurrency(
                def, context, economy, sourceLevel: 1, tierId: eliteTierId, goldMultiplierProvider: provider);

            var coin = Assert.Single(outcomes);
            Assert.False(coin.IsDegraded);
            // 手算：当量固定 2 × 金币基数(等级1)=5 × 分档倍率 2 × Multiplier 1.5 = 30。
            Assert.Equal(30.0, coin.ExpectedAmount, 9);

            // 与真实 LootHost.ResolveCurrencyOutcome（经 RollDetailed）交叉核对：weight_or_chance=1.0
            // 恒命中、当量固定，理论上应逐次都换算出同一个整数金额，不需要蒙特卡洛容差。
            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            var host = new LootHost(
                registry, new RngHost(48006), bus, world, units, inventory, new FakeExprHostFactory(), () => 0.0,
                options: null, diagnostics: null, conditionSchema: null, economyHost: economy, goldMultiplierProvider: provider);
            var rollContext = new RollContext(
                new Id("unit.e48_tier"), killerId: null, multiplier: 1.5, contextId: null,
                sourceLevel: 1, itemLevelOffset: 0, tierId: eliteTierId);

            var outcome = Assert.Single(host.RollDetailed(new Id("loot.e48_tier_gold"), rollContext));
            Assert.Equal(30, outcome.Count);
        }

        [Fact]
        public void ExpectedCurrency_GoldBaseCurveMissing_MarksDegraded_NotThrow()
        {
            var bus = LootTestSupport.NewEventBus();
            const string tableRowsJson =
                "[{\"id\": \"loot.e48_no_curve\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"econ.currency.e48_coin\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}}]}]}]";

            // 故意不注册 econ.gold_base_curve 的对应行——EconomyHost.TryGetGoldBaseAmount 因此恒返回
            // null（曲线记录找不到），同 ResolveCurrencyOutcome 判断记录"未注入/曲线不可解析时静默
            // 跳过"，本方法则显式标记 IsDegraded，不悄悄返回 0。
            var registry = BuildEconRegistry(bus, tableRowsJson, goldBaseAtLevelOne: null);
            var def = LoadTableDef(registry, new Id("loot.e48_no_curve"));
            var economy = new EconomyHost(registry, bus, new FakeInventoryHost(), new FakeNumericExprHostFactory());

            var outcomes = LootTableAnalyzer.ExpectedCurrency(def, new LootAnalysisContext(), economy, sourceLevel: 1);

            var coin = Assert.Single(outcomes);
            Assert.True(coin.IsDegraded);
            Assert.Equal(0.0, coin.ExpectedAmount);
            Assert.Equal(1.0, coin.DropProbability, 9);
            Assert.NotNull(coin.Reason);
        }

        private static DataRegistry BuildEconRegistry(IEventBus bus, string lootTableRowsJson, double? goldBaseAtLevelOne)
        {
            var source = new InMemoryDataSource()
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, lootTableRowsJson))
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name,
                    "[{\"id\": \"econ.currency.e48_coin\", \"name_key\": \"l10n.e48.coin\", \"display_ref\": \"display.e48.coin\"}]"));

            if (goldBaseAtLevelOne.HasValue)
            {
                source.Add(EconomySchemas.GoldBaseCurve.Name, Envelope(EconomySchemas.GoldBaseCurve.Name,
                    "[{\"id\": \"econ.gold_base.default\", \"entries\": [{\"x\": 1, \"y\": " +
                    goldBaseAtLevelOne.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}]}]"));
            }

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.GoldBaseCurve);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            return registry;
        }

        // -----------------------------------------------------------------
        // ⑦ 参数校验：与 ExpectedProbabilities 既有惯例一致。
        // -----------------------------------------------------------------

        [Fact]
        public void NewEntries_NullArguments_ThrowArgumentNullException()
        {
            var bus = LootTestSupport.NewEventBus();
            var registry = BuildSyntheticItemRegistry(bus,
                "[{\"id\": \"loot.e48_argcheck\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.e48_sword\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}}]}]}]");
            var def = LoadTableDef(registry, new Id("loot.e48_argcheck"));
            var economy = new EconomyHost(registry, bus, new FakeInventoryHost(), new FakeNumericExprHostFactory());

            Assert.Throws<ArgumentNullException>(() => LootTableAnalyzer.ExpectedQualityDistribution(null!, new LootAnalysisContext(), registry));
            Assert.Throws<ArgumentNullException>(() => LootTableAnalyzer.ExpectedQualityDistribution(def, null!, registry));
            Assert.Throws<ArgumentNullException>(() => LootTableAnalyzer.ExpectedQualityDistribution(def, new LootAnalysisContext(), null!));

            Assert.Throws<ArgumentNullException>(() => LootTableAnalyzer.ExpectedAffixInclusion(new Id("item.x"), new Id("item.quality.x"), null!));

            Assert.Throws<ArgumentNullException>(() => LootTableAnalyzer.ExpectedCurrency(null!, new LootAnalysisContext(), economy));
            Assert.Throws<ArgumentNullException>(() => LootTableAnalyzer.ExpectedCurrency(def, null!, economy));
            Assert.Throws<ArgumentNullException>(() => LootTableAnalyzer.ExpectedCurrency(def, new LootAnalysisContext(), null!));
        }
    }
}
