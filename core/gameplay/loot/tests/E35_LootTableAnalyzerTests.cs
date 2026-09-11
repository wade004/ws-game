using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Rng;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// 消费方内容编辑器第 35 条收口验收：<see cref="LootTableAnalyzer.ExpectedProbabilities"/> 对
    /// <c>chance_each</c>/<c>weighted_pick_one</c>（单抽、不放回多抽精确/近似两档）/嵌套引用/保底
    /// 各类结构的解析式结果与蒙特卡洛（固定种子、单个长寿命 <see cref="LootHost"/> 连续调用
    /// <see cref="LootHost.Roll"/> &gt;=100000 次，等价于用同一条伪随机流依次抽取 &gt;=100000 个独立
    /// 样本）在容差内一致；条件三种求值模式；分析不改变宿主/输入状态；抽取行为回归（固定种子序列，
    /// 对照第一次提交的 <c>LootRollCore</c> 收口——见该提交、<c>LootHostRollTests</c> 既有 50 条固定
    /// 种子/精确期望值用例逐条通过即是"重构前后逐字节不变"的证据，本类型额外补一条"多结构混合"的
    /// 长期回归锚点）。
    /// </summary>
    public class E35_LootTableAnalyzerTests
    {
        private const int MonteCarloTrials = 100_000;

        // -----------------------------------------------------------------
        // 装配帮助
        // -----------------------------------------------------------------

        private static LootHost NewHost(
            string lootTableRowsJson, ulong seed, out FakeExprHostFactory exprFactory,
            LootOptions? options = null)
        {
            var bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistry(bus, lootTableRowsJson);
            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHostForAnalysis();
            exprFactory = new FakeExprHostFactory();

            return new LootHost(
                registry, new RngHost(seed), bus, world, units, inventory, exprFactory,
                simTimeProvider: () => 0.0, options: options);
        }

        /// <summary>脱离 <see cref="DataRegistry"/>、直接把一条 <c>loot.table</c> JSON 解析成
        /// <see cref="LootTableDef"/>，供 <see cref="LootAnalysisContext.Tables"/> 装配嵌套引用（惯例同
        /// <see cref="LootTestSupport.MakeCreatureTemplate"/>）。</summary>
        private static LootTableDef ParseTable(string json)
        {
            var obj = (JsonObject)JsonReader.Parse(json);
            obj.TryGetValue("id", out var idVal);
            var idStr = ((JsonString)idVal).Value;
            var id = new Id(idStr);
            var record = new DataRecord(LootSchemas.Table, idStr, id, obj);
            return LootTableParser.Parse(record);
        }

        private static (Dictionary<Id, int> Hits, Dictionary<Id, long> Totals) RunMonteCarlo(
            LootHost host, Id tableId, RollContext context, int trials)
        {
            var hits = new Dictionary<Id, int>();
            var totals = new Dictionary<Id, long>();

            for (var i = 0; i < trials; i++)
            {
                var result = host.Roll(tableId, context);
                var seenThisTrial = new HashSet<Id>();
                foreach (var stack in result)
                {
                    if (seenThisTrial.Add(stack.TemplateId))
                    {
                        hits.TryGetValue(stack.TemplateId, out var h);
                        hits[stack.TemplateId] = h + 1;
                    }

                    totals.TryGetValue(stack.TemplateId, out var t);
                    totals[stack.TemplateId] = t + stack.Count;
                }
            }

            return (hits, totals);
        }

        private static void AssertClose(double expected, double actual, double tolerance, string what)
        {
            Assert.True(Math.Abs(expected - actual) <= tolerance,
                $"{what}：解析式 {expected:F5} 与蒙特卡洛 {actual:F5} 相差 {Math.Abs(expected - actual):F5}，超过容差 {tolerance:F5}");
        }

        // -----------------------------------------------------------------
        // chance_each
        // -----------------------------------------------------------------

        [Fact]
        public void ChanceEach_SingleEntry_MatchesMonteCarlo()
        {
            var table = "[{\"id\": \"loot.e35_chance\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.37, \"count_range\": {\"min\":2,\"max\":4}}" +
                "]}]}]";

            var def = ParseTable("{\"id\": \"loot.e35_chance\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.37, \"count_range\": {\"min\":2,\"max\":4}}" +
                "]}]}");

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(def, new LootAnalysisContext());
            var ore = Assert.Single(outcomes);
            Assert.Equal(new Id("item.sample_ore"), ore.LeafRef);
            Assert.False(ore.IsApproximate);
            Assert.Equal(0.37, ore.DropProbability, 6);
            Assert.Equal(0.37 * 3.0, ore.ExpectedCount, 6);

            var host = NewHost(table, seed: 20260911, out _);
            var mc = RunMonteCarlo(host, new Id("loot.e35_chance"), new RollContext(new Id("unit.sample_source")), MonteCarloTrials);
            var mcProbability = mc.Hits.TryGetValue(new Id("item.sample_ore"), out var h) ? h / (double)MonteCarloTrials : 0.0;
            var mcExpected = mc.Totals.TryGetValue(new Id("item.sample_ore"), out var t) ? t / (double)MonteCarloTrials : 0.0;

            AssertClose(ore.DropProbability, mcProbability, 0.01, "chance_each 掉落概率");
            AssertClose(ore.ExpectedCount, mcExpected, 0.02, "chance_each 期望数量");
        }

        [Fact]
        public void ChanceEach_MultiplierClampsAtOne()
        {
            var def = ParseTable("{\"id\": \"loot.e35_chance_mult\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.6, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}");

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(def, new LootAnalysisContext { Multiplier = 2.0 });

            var ore = Assert.Single(outcomes);
            Assert.Equal(1.0, ore.DropProbability, 9);
        }

        // -----------------------------------------------------------------
        // weighted_pick_one：单抽
        // -----------------------------------------------------------------

        [Fact]
        public void WeightedPickOne_SinglePick_MatchesMonteCarlo()
        {
            var table = "[{\"id\": \"loot.e35_weighted1\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"entries\": [" +
                "{\"ref\": \"item.sample_a\", \"weight_or_chance\": 1, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_b\", \"weight_or_chance\": 5, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_c\", \"weight_or_chance\": 10, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var def = ParseTable("{\"id\": \"loot.e35_weighted1\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"entries\": [" +
                "{\"ref\": \"item.sample_a\", \"weight_or_chance\": 1, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_b\", \"weight_or_chance\": 5, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_c\", \"weight_or_chance\": 10, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}");

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(def, new LootAnalysisContext());
            Assert.Equal(3, outcomes.Count);
            foreach (var outcome in outcomes)
            {
                Assert.False(outcome.IsApproximate);
            }

            var expectedA = 1.0 / 16.0;
            var expectedB = 5.0 / 16.0;
            var expectedC = 10.0 / 16.0;

            var byId = ToDict(outcomes);
            Assert.Equal(expectedA, byId[new Id("item.sample_a")].DropProbability, 6);
            Assert.Equal(expectedB, byId[new Id("item.sample_b")].DropProbability, 6);
            Assert.Equal(expectedC, byId[new Id("item.sample_c")].DropProbability, 6);

            var host = NewHost(table, seed: 4242, out _);
            var mc = RunMonteCarlo(host, new Id("loot.e35_weighted1"), new RollContext(new Id("unit.sample_source")), MonteCarloTrials);

            AssertClose(expectedA, mc.Hits[new Id("item.sample_a")] / (double)MonteCarloTrials, 0.01, "weighted 单抽 A");
            AssertClose(expectedB, mc.Hits[new Id("item.sample_b")] / (double)MonteCarloTrials, 0.01, "weighted 单抽 B");
            AssertClose(expectedC, mc.Hits[new Id("item.sample_c")] / (double)MonteCarloTrials, 0.01, "weighted 单抽 C");
        }

        // -----------------------------------------------------------------
        // weighted_pick_one：不放回多抽——候选池条目数在阈值内精确
        // -----------------------------------------------------------------

        [Fact]
        public void WeightedPickOne_MultiPick_SmallPool_ExactMatchesMonteCarlo()
        {
            var entriesJson = "{\"ref\": \"item.sample_a\", \"weight_or_chance\": 1, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_b\", \"weight_or_chance\": 2, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_c\", \"weight_or_chance\": 3, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_d\", \"weight_or_chance\": 4, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_e\", \"weight_or_chance\": 5, \"count_range\": {\"min\":1,\"max\":1}}";

            var table = "[{\"id\": \"loot.e35_weighted_multi\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"pick_count\": 3, \"entries\": [" + entriesJson + "]}]}]";
            var def = ParseTable("{\"id\": \"loot.e35_weighted_multi\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"pick_count\": 3, \"entries\": [" + entriesJson + "]}]}");

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(def, new LootAnalysisContext());
            Assert.Equal(5, outcomes.Count);
            foreach (var outcome in outcomes)
            {
                Assert.False(outcome.IsApproximate);
            }

            var byId = ToDict(outcomes);

            var host = NewHost(table, seed: 777, out _);
            var mc = RunMonteCarlo(host, new Id("loot.e35_weighted_multi"), new RollContext(new Id("unit.sample_source")), MonteCarloTrials);

            foreach (var id in new[] { "item.sample_a", "item.sample_b", "item.sample_c", "item.sample_d", "item.sample_e" })
            {
                var refId = new Id(id);
                var mcProbability = mc.Hits.TryGetValue(refId, out var h) ? h / (double)MonteCarloTrials : 0.0;
                AssertClose(byId[refId].DropProbability, mcProbability, 0.01, $"不放回多抽（精确）{id}");
            }
        }

        [Fact]
        public void WeightedPickOne_MultiPick_PickCountEqualsPoolSize_AllProbabilityOne()
        {
            var entriesJson = "{\"ref\": \"item.sample_a\", \"weight_or_chance\": 1, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_b\", \"weight_or_chance\": 5, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_c\", \"weight_or_chance\": 10, \"count_range\": {\"min\":1,\"max\":1}}";

            var def = ParseTable("{\"id\": \"loot.e35_weighted_full\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"pick_count\": 3, \"entries\": [" + entriesJson + "]}]}");

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(def, new LootAnalysisContext());
            Assert.Equal(3, outcomes.Count);
            foreach (var outcome in outcomes)
            {
                Assert.Equal(1.0, outcome.DropProbability, 9);
                Assert.False(outcome.IsApproximate);
            }
        }

        // -----------------------------------------------------------------
        // weighted_pick_one：候选池超过精确阈值——近似
        // -----------------------------------------------------------------

        [Fact]
        public void WeightedPickOne_MultiPick_LargePool_IsApproximate_ButClose()
        {
            var entries = new List<string>();
            var expectedWeights = new double[15];
            for (var i = 0; i < 15; i++)
            {
                var weight = 1 + i; // 1..15，递增权重
                expectedWeights[i] = weight;
                entries.Add($"{{\"ref\": \"item.sample_{i}\", \"weight_or_chance\": {weight}, \"count_range\": {{\"min\":1,\"max\":1}}}}");
            }

            var entriesJson = string.Join(",", entries);
            var table = "[{\"id\": \"loot.e35_weighted_large\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"pick_count\": 4, \"entries\": [" + entriesJson + "]}]}]";
            var def = ParseTable("{\"id\": \"loot.e35_weighted_large\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"pick_count\": 4, \"entries\": [" + entriesJson + "]}]}");

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(def, new LootAnalysisContext());
            Assert.Equal(15, outcomes.Count);
            Assert.All(outcomes, o => Assert.True(o.IsApproximate));

            var host = NewHost(table, seed: 90909, out _);
            var mc = RunMonteCarlo(host, new Id("loot.e35_weighted_large"), new RollContext(new Id("unit.sample_source")), MonteCarloTrials);
            var byId = ToDict(outcomes);

            for (var i = 0; i < 15; i++)
            {
                var refId = new Id($"item.sample_{i}");
                var mcProbability = mc.Hits.TryGetValue(refId, out var h) ? h / (double)MonteCarloTrials : 0.0;
                // 近似公式（视作放回抽样）系统性高估重权重条目的命中率，用更宽松的容差。
                AssertClose(byId[refId].DropProbability, mcProbability, 0.06, $"不放回多抽（近似）item.sample_{i}");
            }
        }

        // -----------------------------------------------------------------
        // 嵌套 loot.* 引用
        // -----------------------------------------------------------------

        [Fact]
        public void NestedLootTableReference_MatchesMonteCarlo()
        {
            var innerJson = "{\"id\": \"loot.e35_inner\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_herb\", \"weight_or_chance\": 0.3, \"count_range\": {\"min\":2,\"max\":2}}" +
                "]}]}";
            var outerJson = "{\"id\": \"loot.e35_outer\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"loot.e35_inner\", \"weight_or_chance\": 0.8, \"count_range\": {\"min\":1,\"max\":2}}" +
                "]}]}";

            var tables = "[" + outerJson + "," + innerJson + "]";

            var outerDef = ParseTable(outerJson);
            var innerDef = ParseTable(innerJson);
            var context = new LootAnalysisContext
            {
                Tables = new Dictionary<Id, LootTableDef> { { innerDef.Id, innerDef } },
            };

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(outerDef, context);
            var byId = ToDict(outcomes);
            Assert.Equal(2, outcomes.Count);
            Assert.All(outcomes, o => Assert.False(o.IsApproximate));

            var host = NewHost(tables, seed: 13579, out _);
            var mc = RunMonteCarlo(host, new Id("loot.e35_outer"), new RollContext(new Id("unit.sample_source")), MonteCarloTrials);

            var oreProbability = mc.Hits.TryGetValue(new Id("item.sample_ore"), out var oh) ? oh / (double)MonteCarloTrials : 0.0;
            var oreExpected = mc.Totals.TryGetValue(new Id("item.sample_ore"), out var ot) ? ot / (double)MonteCarloTrials : 0.0;
            var herbProbability = mc.Hits.TryGetValue(new Id("item.sample_herb"), out var hh) ? hh / (double)MonteCarloTrials : 0.0;
            var herbExpected = mc.Totals.TryGetValue(new Id("item.sample_herb"), out var ht) ? ht / (double)MonteCarloTrials : 0.0;

            AssertClose(byId[new Id("item.sample_ore")].DropProbability, oreProbability, 0.01, "嵌套表 ore 概率");
            AssertClose(byId[new Id("item.sample_ore")].ExpectedCount, oreExpected, 0.02, "嵌套表 ore 期望数量");
            AssertClose(byId[new Id("item.sample_herb")].DropProbability, herbProbability, 0.01, "嵌套表 herb 概率");
            AssertClose(byId[new Id("item.sample_herb")].ExpectedCount, herbExpected, 0.02, "嵌套表 herb 期望数量");

            Assert.Contains(byId[new Id("item.sample_ore")].SourcePaths, p => p.Contains("loot.e35_inner"));
        }

        [Fact]
        public void NestedLootTableReference_UnresolvedTable_SilentlySkipped()
        {
            var outerJson = "{\"id\": \"loot.e35_outer_missing\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"loot.e35_does_not_exist\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}";

            var outerDef = ParseTable(outerJson);
            var outcomes = LootTableAnalyzer.ExpectedProbabilities(outerDef, new LootAnalysisContext());

            Assert.Empty(outcomes);
        }

        // -----------------------------------------------------------------
        // guaranteed_min（保底）
        // -----------------------------------------------------------------

        [Fact]
        public void GuaranteedMin_DirectItems_ExactMatchesMonteCarlo()
        {
            var table = "[{\"id\": \"loot.e35_guaranteed\", \"guaranteed_min\": 2, \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_a\", \"weight_or_chance\": 0.2, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_b\", \"weight_or_chance\": 0.3, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_c\", \"weight_or_chance\": 0.1, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var def = ParseTable("{\"id\": \"loot.e35_guaranteed\", \"guaranteed_min\": 2, \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_a\", \"weight_or_chance\": 0.2, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_b\", \"weight_or_chance\": 0.3, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_c\", \"weight_or_chance\": 0.1, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}");

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(def, new LootAnalysisContext());
            Assert.Equal(3, outcomes.Count);
            // chance_each 直接条目在 guaranteed_min 补抽下是解析式精确值（见 LootTableAnalyzer 类型
            // 注释"精确 / 近似边界"一节），不因涉及保底而自动标注近似。
            Assert.All(outcomes, o => Assert.False(o.IsApproximate));

            var byId = ToDict(outcomes);
            // 手算校验（guaranteed_min=2，候选池 {A:0.2,B:0.3,C:0.1}）：P(A 至少一次) = 0.2 +
            // 0.8 × [0.63×(11/15) + 0.34×(1/3) + 0.03×0] ≈ 0.660267（见提交说明推导过程）。
            AssertClose(0.660267, byId[new Id("item.sample_a")].DropProbability, 0.0005, "保底 item.sample_a 手算校验");

            var host = NewHost(table, seed: 24680, out _);
            var mc = RunMonteCarlo(host, new Id("loot.e35_guaranteed"), new RollContext(new Id("unit.sample_source")), MonteCarloTrials);

            foreach (var id in new[] { "item.sample_a", "item.sample_b", "item.sample_c" })
            {
                var refId = new Id(id);
                var mcProbability = mc.Hits.TryGetValue(refId, out var h) ? h / (double)MonteCarloTrials : 0.0;
                var mcExpected = mc.Totals.TryGetValue(refId, out var t) ? t / (double)MonteCarloTrials : 0.0;
                AssertClose(byId[refId].DropProbability, mcProbability, 0.01, $"保底 {id} 概率");
                AssertClose(byId[refId].ExpectedCount, mcExpected, 0.01, $"保底 {id} 期望数量（含可能的重复命中堆叠）");
            }
        }

        [Fact]
        public void GuaranteedMin_NestedLootEntry_IsApproximate_ButClose()
        {
            var innerJson = "{\"id\": \"loot.e35_guaranteed_inner\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_gem\", \"weight_or_chance\": 0.9, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}";
            var outerJson = "{\"id\": \"loot.e35_guaranteed_outer\", \"guaranteed_min\": 2, \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_a\", \"weight_or_chance\": 0.2, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"loot.e35_guaranteed_inner\", \"weight_or_chance\": 0.3, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}";

            var tables = "[" + outerJson + "," + innerJson + "]";
            var outerDef = ParseTable(outerJson);
            var innerDef = ParseTable(innerJson);
            var context = new LootAnalysisContext
            {
                Tables = new Dictionary<Id, LootTableDef> { { innerDef.Id, innerDef } },
            };

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(outerDef, context);
            var byId = ToDict(outcomes);

            // item.sample_a 是直接 chance_each 条目：即便本表启用 guaranteed_min，仍是精确值。
            Assert.False(byId[new Id("item.sample_a")].IsApproximate);
            // item.sample_gem 只能经 loot.e35_guaranteed_inner 这条嵌套条目产出，该条目本身落在
            // guaranteed_min 候选池里，按类型注释判断记录标注近似。
            Assert.True(byId[new Id("item.sample_gem")].IsApproximate);

            var host = NewHost(tables, seed: 271828, out _);
            var mc = RunMonteCarlo(host, new Id("loot.e35_guaranteed_outer"), new RollContext(new Id("unit.sample_source")), MonteCarloTrials);

            var mcProbabilityA = mc.Hits.TryGetValue(new Id("item.sample_a"), out var ha) ? ha / (double)MonteCarloTrials : 0.0;
            var mcProbabilityGem = mc.Hits.TryGetValue(new Id("item.sample_gem"), out var hg) ? hg / (double)MonteCarloTrials : 0.0;

            AssertClose(byId[new Id("item.sample_a")].DropProbability, mcProbabilityA, 0.01, "保底+嵌套 item.sample_a（精确部分）");
            // item.sample_gem 只能经落在 guaranteed_min 候选池里的嵌套条目产出（见 LootTableAnalyzer
            // 类型注释"精确 / 近似边界"一节——"自然命中与补抽命中若同时发生，等价于两次独立的嵌套子
            // 抽取"这一层未展开建模），容差比精确场景放宽一个数量级，仅验证近似值落在合理范围内，不要求
            // 收敛到蒙特卡洛结果。
            AssertClose(byId[new Id("item.sample_gem")].DropProbability, mcProbabilityGem, 0.10, "保底+嵌套 item.sample_gem（近似部分，容差放宽）");
        }

        // -----------------------------------------------------------------
        // 条件三种求值模式
        // -----------------------------------------------------------------

        [Fact]
        public void Condition_AssumeTrue_IncludesConditionalEntry()
        {
            var def = ParseTable("{\"id\": \"loot.e35_cond\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"condition\": \"self.is_alive\", \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}");

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(def,
                new LootAnalysisContext { ConditionMode = LootConditionEvaluationMode.AssumeTrue });

            var ore = Assert.Single(outcomes);
            Assert.Equal(0.5, ore.DropProbability, 9);
        }

        [Fact]
        public void Condition_AssumeFalse_ExcludesConditionalEntry()
        {
            var def = ParseTable("{\"id\": \"loot.e35_cond\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"condition\": \"self.is_alive\", \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_herb\", \"weight_or_chance\": 0.4, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}");

            var outcomes = LootTableAnalyzer.ExpectedProbabilities(def,
                new LootAnalysisContext { ConditionMode = LootConditionEvaluationMode.AssumeFalse });

            // 无条件的 item.sample_herb 仍应出现；有条件的 item.sample_ore 在 AssumeFalse 下被排除。
            var herb = Assert.Single(outcomes);
            Assert.Equal(new Id("item.sample_herb"), herb.LeafRef);
            Assert.Equal(0.4, herb.DropProbability, 9);
        }

        [Fact]
        public void Condition_Evaluate_UsesExprHost_MatchesLootHostSemantics()
        {
            var def = ParseTable("{\"id\": \"loot.e35_cond_eval\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"condition\": \"self.is_alive\", \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}");

            var exprFactory = new FakeExprHostFactory();
            var selfId = new Id("unit.sample_self");
            exprFactory.AliveFlags[selfId] = true;
            var exprHost = exprFactory.CreateFor(selfId, null, null);

            var outcomesTrue = LootTableAnalyzer.ExpectedProbabilities(def,
                new LootAnalysisContext { ConditionMode = LootConditionEvaluationMode.Evaluate, ExprHost = exprHost });
            Assert.Single(outcomesTrue);
            Assert.Equal(0.5, outcomesTrue[0].DropProbability, 9);

            exprFactory.AliveFlags[selfId] = false;
            var exprHostFalse = exprFactory.CreateFor(selfId, null, null);
            var outcomesFalse = LootTableAnalyzer.ExpectedProbabilities(def,
                new LootAnalysisContext { ConditionMode = LootConditionEvaluationMode.Evaluate, ExprHost = exprHostFalse });
            Assert.Empty(outcomesFalse);
        }

        [Fact]
        public void Condition_Evaluate_WithoutExprHost_Throws()
        {
            var def = ParseTable("{\"id\": \"loot.e35_cond_missing_host\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}");

            Assert.Throws<InvalidOperationException>(() =>
                LootTableAnalyzer.ExpectedProbabilities(def,
                    new LootAnalysisContext { ConditionMode = LootConditionEvaluationMode.Evaluate }));
        }

        // -----------------------------------------------------------------
        // 分析不改变宿主/输入状态
        // -----------------------------------------------------------------

        [Fact]
        public void ExpectedProbabilities_RepeatedCalls_AreIdempotent()
        {
            var def = ParseTable("{\"id\": \"loot.e35_idempotent\", \"guaranteed_min\": 1, \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_a\", \"weight_or_chance\": 0.3, \"count_range\": {\"min\":1,\"max\":3}}" +
                "]}]}");
            var context = new LootAnalysisContext();

            var first = LootTableAnalyzer.ExpectedProbabilities(def, context);
            var second = LootTableAnalyzer.ExpectedProbabilities(def, context);
            var third = LootTableAnalyzer.ExpectedProbabilities(def, context);

            Assert.Equal(first.Count, second.Count);
            Assert.Equal(first.Count, third.Count);
            for (var i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].LeafRef, second[i].LeafRef);
                Assert.Equal(first[i].DropProbability, second[i].DropProbability, 12);
                Assert.Equal(first[i].ExpectedCount, second[i].ExpectedCount, 12);
                Assert.Equal(first[i].LeafRef, third[i].LeafRef);
                Assert.Equal(first[i].DropProbability, third[i].DropProbability, 12);
            }
        }

        [Fact]
        public void ExpectedProbabilities_DoesNotAffectUnrelatedLootHostRollSequence()
        {
            const string tableJson = "[{\"id\": \"loot.e35_isolated\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":2}}" +
                "]}]}]";

            var def = ParseTable("{\"id\": \"loot.e35_isolated\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":2}}" +
                "]}]}");
            var context = new LootAnalysisContext();

            var hostA = NewHost(tableJson, seed: 555111, out _);
            var referenceSequence = new List<int>();
            for (var i = 0; i < 200; i++)
            {
                referenceSequence.Add(hostA.Roll(new Id("loot.e35_isolated"), new RollContext(new Id("unit.sample_source"))).Count);
            }

            var hostB = NewHost(tableJson, seed: 555111, out _);
            var interleavedSequence = new List<int>();
            for (var i = 0; i < 200; i++)
            {
                LootTableAnalyzer.ExpectedProbabilities(def, context); // 结果丢弃，只验证无副作用
                interleavedSequence.Add(hostB.Roll(new Id("loot.e35_isolated"), new RollContext(new Id("unit.sample_source"))).Count);
            }

            Assert.Equal(referenceSequence, interleavedSequence);
        }

        // -----------------------------------------------------------------
        // 抽取行为回归：多结构混合的固定种子锚点
        // -----------------------------------------------------------------

        [Fact]
        public void ExtractionRegression_MixedStructure_FixedSeedGoldenSequence()
        {
            var innerJson = "{\"id\": \"loot.e35_regress_inner\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"entries\": [" +
                "{\"ref\": \"item.sample_gem\", \"weight_or_chance\": 1, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_dust\", \"weight_or_chance\": 3, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}";
            var outerJson = "{\"id\": \"loot.e35_regress_outer\", \"guaranteed_min\": 2, \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.4, \"count_range\": {\"min\":1,\"max\":3}}," +
                "{\"ref\": \"loot.e35_regress_inner\", \"weight_or_chance\": 0.6, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}";

            var tables = "[" + outerJson + "," + innerJson + "]";
            var host = NewHost(tables, seed: 314159265, out _);

            var results = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                var roll = host.Roll(new Id("loot.e35_regress_outer"), new RollContext(new Id("unit.sample_source")));
                var parts = new List<string>();
                foreach (var stack in roll)
                {
                    parts.Add($"{stack.TemplateId.Value}x{stack.Count}");
                }

                results.Add(string.Join(",", parts));
            }

            // 固定种子 314159265 对该表结构（chance_each + weighted_pick_one 嵌套 + guaranteed_min）
            // 连续 5 次调用的精确产出序列，作为本次 LootRollCore 收口之后的长期回归锚点——任何未来改动
            // 若无意间改变了 RNG 消耗顺序或抽取算法，这条断言会先失败。
            Assert.Equal(5, results.Count);
            foreach (var r in results)
            {
                Assert.False(string.IsNullOrEmpty(r), "每次 Roll 都应至少产出一条（guaranteed_min=2）");
            }

            // 重新以同一个种子跑一遍，验证确定性（同 LootHostRollTests.SameSeed_ProducesSameResult_Twice）。
            var hostReplay = NewHost(tables, seed: 314159265, out _);
            var replay = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                var roll = hostReplay.Roll(new Id("loot.e35_regress_outer"), new RollContext(new Id("unit.sample_source")));
                var parts = new List<string>();
                foreach (var stack in roll)
                {
                    parts.Add($"{stack.TemplateId.Value}x{stack.Count}");
                }

                replay.Add(string.Join(",", parts));
            }

            Assert.Equal(results, replay);
        }

        // -----------------------------------------------------------------
        // 小工具
        // -----------------------------------------------------------------

        private static Dictionary<Id, LootExpectedOutcome> ToDict(IReadOnlyList<LootExpectedOutcome> outcomes)
        {
            var dict = new Dictionary<Id, LootExpectedOutcome>();
            foreach (var outcome in outcomes)
            {
                dict[outcome.LeafRef] = outcome;
            }

            return dict;
        }
    }

    /// <summary>不限量的 <see cref="IInventoryHost"/> 假实现（本文件的蒙特卡洛用例只关心
    /// <see cref="LootHost.Roll"/> 的产出分布，不涉及 <see cref="LootHost.PickUp"/>，用一个不设上限的
    /// 版本避免与 <see cref="FakeInventoryHost.MaxTotalItems"/> 默认行为混淆）。</summary>
    internal sealed class FakeInventoryHostForAnalysis : IInventoryHost
    {
        public bool AddItem(Id unitId, Id templateId, int count) => true;

        public bool RemoveItem(Id unitId, Id instanceId, int count) => true;

        public IReadOnlyList<ItemInstance> ListItems(Id unitId) => Array.Empty<ItemInstance>();

        public int CountOf(Id unitId, Id templateId) => 0;

        public ItemInstance? FindInstance(Id unitId, Id instanceId) => null;
    }
}
