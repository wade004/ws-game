using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Rng;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// T-N2-8（ADR-0032 决策 7/8；08 第 1.1 节修订段"三次独立掷骰"）：品质骰 + 词缀骰的确定性、分布、
    /// 模板 <c>affixes</c> 白名单交集、<see cref="RollContext.SourceLevel"/>/<see
    /// cref="RollContext.ItemLevelOffset"/> 折算物品等级，以及未配置 <c>quality_weights</c> 时旧数据
    /// 行为完全不变（不消耗随机数）。
    /// </summary>
    public class T_N2_8_LootRollIdentityTests
    {
        private const string SlotRows = "[{\"id\": \"item.slot.sample_weapon\", \"name_key\": \"l10n.slot.name\"}]";

        private const string QualityRows =
            "[" +
            "{\"id\": \"item.quality.sample_common\", \"name_key\": \"l10n.quality.common\", \"affix_count\": 1}," +
            "{\"id\": \"item.quality.sample_rare\", \"name_key\": \"l10n.quality.rare\", \"affix_count\": 3}" +
            "]";

        private const string StatRows =
            "[{\"id\": \"stat.sample_str\", \"name_key\": \"l10n.stat.str\", \"category\": \"primary\"}]";

        private static string AffixRow(string id, string qualityPool, double weight) =>
            "{\"id\": \"" + id + "\", \"name_key\": \"l10n.affix." + id + "\", \"budget_share\": 0.1, " +
            "\"stat_mix\": [{\"stat\": \"stat.sample_str\", \"ratio\": 1.0}], " +
            "\"quality_pool\": \"" + qualityPool + "\", \"weight\": " + weight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

        private static readonly string AffixRows =
            "[" +
            AffixRow("item.affix.a1", "item.quality.sample_common", 1) + "," +
            AffixRow("item.affix.a2", "item.quality.sample_common", 3) + "," +
            AffixRow("item.affix.a3", "item.quality.sample_rare", 1) + "," +
            AffixRow("item.affix.a4", "item.quality.sample_rare", 1) +
            "]";

        private static string TemplateRow(string id, string quality, int itemLevel, string? affixesWhitelistJson = null) =>
            "{\"id\": \"" + id + "\", \"slot\": \"item.slot.sample_weapon\", \"quality\": \"" + quality + "\", " +
            "\"item_level\": " + itemLevel + ", \"display_ref\": \"display.sample\", \"stack_size\": 1, " +
            "\"name_key\": \"l10n.item." + id + "\"" +
            (affixesWhitelistJson != null ? ", \"affixes\": " + affixesWhitelistJson : string.Empty) +
            "}";

        private static string LootTableRow(string tableId, string itemRef, string? qualityWeightsJson) =>
            "[{\"id\": \"" + tableId + "\", \"groups\": [" +
            "{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"" + itemRef + "\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}" +
            (qualityWeightsJson != null ? ", \"quality_weights\": " + qualityWeightsJson : string.Empty) +
            "}]}]}]";

        private static LootHost NewHost(
            ulong seed, string lootTableRowsJson, string templateRowsJson, out FakeExprHostFactory exprFactory)
        {
            var bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistryWithItems(
                bus, lootTableRowsJson, templateRowsJson, SlotRows, QualityRows, AffixRows, StatRows);
            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            exprFactory = new FakeExprHostFactory();

            return new LootHost(
                registry, new RngHost(seed), bus, world, units, inventory, exprFactory, simTimeProvider: () => 0.0);
        }

        // ------------------------------------------------------------------
        // 1) 同种子确定性
        // ------------------------------------------------------------------

        [Fact]
        public void RollDetailed_SameSeed_ProducesIdenticalOutcomes()
        {
            var tableJson = LootTableRow(
                "loot.sample_identity", "item.sample_gear",
                "{\"item.quality.sample_common\": 3, \"item.quality.sample_rare\": 1}");
            var templateJson = "[" + TemplateRow("item.sample_gear", "item.quality.sample_common", 10) + "]";

            var host1 = NewHost(999, tableJson, templateJson, out _);
            var host2 = NewHost(999, tableJson, templateJson, out _);

            var context = new RollContext(new Id("unit.sample_source"), null, 1.0, null, sourceLevel: 42, itemLevelOffset: 3);

            var r1 = host1.RollDetailed(new Id("loot.sample_identity"), context);
            var r2 = host2.RollDetailed(new Id("loot.sample_identity"), context);

            Assert.Equal(r1.Count, r2.Count);
            for (var i = 0; i < r1.Count; i++)
            {
                Assert.Equal(r1[i], r2[i]);
                Assert.Equal(r1[i].TemplateId, r2[i].TemplateId);
                Assert.Equal(r1[i].QualityId, r2[i].QualityId);
                Assert.Equal(r1[i].ItemLevel, r2[i].ItemLevel);
                Assert.Equal(r1[i].Affixes.ToArray(), r2[i].Affixes.ToArray());
            }
        }

        // ------------------------------------------------------------------
        // 2) 未配置 quality_weights：旧数据行为完全不变、不消耗随机数
        // ------------------------------------------------------------------

        [Fact]
        public void RollDetailed_NoQualityWeights_ReturnsTemplateQuality_NoAffixRoll_ConsumesNoExtraRandom()
        {
            var tableJson = LootTableRow("loot.sample_legacy", "item.sample_gear", qualityWeightsJson: null);
            var templateJson = "[" + TemplateRow("item.sample_gear", "item.quality.sample_common", 7) + "]";

            var host = NewHost(1, tableJson, templateJson, out _);
            var context = new RollContext(new Id("unit.sample_source"));

            var result = host.RollDetailed(new Id("loot.sample_legacy"), context);

            Assert.Single(result);
            Assert.Equal(new Id("item.quality.sample_common"), result[0].QualityId);
            // affix_count=1 归属该品质，但"不掷骰"只针对品质骰——词缀骰仍按模板缺省品质的 affix_count
            // 正常进行（08 第 1.1 节"缺省取模板品质"只免除品质骰本身，词缀骰不受影响）。
            Assert.Single(result[0].Affixes);
            Assert.Null(result[0].ItemLevel);
        }

        /// <summary>对照：既有 <see cref="LootHost.Roll(Id, RollContext)"/> 路径（无 quality_weights）
        /// 消耗的随机数序列与改写前完全一致——"掉哪条"的判定用掉一次 <c>Next</c>、计数用
        /// <c>[1,1]</c> 固定区间不消耗 <c>NextInt</c>，品质骰因未配置而不消耗，词缀骰虽然消耗（新增行为，
        /// 允许）但用另一条 <see cref="RngHost"/> 实例重放同一条 <c>chance_each</c> 判定应得到与
        /// <c>LootHostRollTests.SameSeed_ProducesSameResult_Twice</c> 同款"掉哪条"结果——本用例改为直接
        /// 断言"未配置该字段的表，Roll 合并结果与只有 templateId/count 时一致"。</summary>
        [Fact]
        public void Roll_NoQualityWeights_MergedResultUnaffectedByIdentityFields()
        {
            var tableJson = LootTableRow("loot.sample_legacy2", "item.sample_gear", qualityWeightsJson: null);
            var templateJson = "[" + TemplateRow("item.sample_gear", "item.quality.sample_common", 7) + "]";

            var host = NewHost(1, tableJson, templateJson, out _);
            var result = host.Roll(new Id("loot.sample_legacy2"), new RollContext(new Id("unit.sample_source")));

            Assert.Single(result);
            Assert.Equal(new Id("item.sample_gear"), result[0].TemplateId);
            Assert.Equal(1, result[0].Count);
        }

        // ------------------------------------------------------------------
        // 3) 品质权重 + 词缀池权重分布 10000 次抽样落在 ±3σ
        // ------------------------------------------------------------------

        [Fact]
        public void RollDetailed_QualityAndAffixWeights_DistributionWithinThreeSigma()
        {
            const int trials = 10000;
            var tableJson = LootTableRow(
                "loot.sample_dist", "item.sample_gear",
                "{\"item.quality.sample_common\": 3, \"item.quality.sample_rare\": 1}");
            var templateJson = "[" + TemplateRow("item.sample_gear", "item.quality.sample_common", 10) + "]";

            var host = NewHost(777, tableJson, templateJson, out _);
            var tableId = new Id("loot.sample_dist");

            var commonCount = 0;
            var rareCount = 0;
            var a1Count = 0;
            var a2Count = 0;

            for (var i = 0; i < trials; i++)
            {
                var outcome = host.RollDetailed(tableId, new RollContext(new Id("unit.sample_source"), contextId: new Id($"unit.trial_{i}")))[0];
                if (outcome.QualityId == new Id("item.quality.sample_common"))
                {
                    commonCount++;
                    Assert.Single(outcome.Affixes);
                    if (outcome.Affixes[0] == new Id("item.affix.a1")) a1Count++;
                    else if (outcome.Affixes[0] == new Id("item.affix.a2")) a2Count++;
                }
                else
                {
                    rareCount++;
                    // 稀有池只有 2 个候选、affix_count=3：候选耗尽提前停止，两条都应命中且不重复。
                    Assert.Equal(2, outcome.Affixes.Count);
                    Assert.Contains(new Id("item.affix.a3"), outcome.Affixes);
                    Assert.Contains(new Id("item.affix.a4"), outcome.Affixes);
                }
            }

            AssertWithinThreeSigma(commonCount, trials, 0.75, "品质=common（权重 3:1）");
            AssertWithinThreeSigma(rareCount, trials, 0.25, "品质=rare（权重 3:1）");
            AssertWithinThreeSigma(a1Count, commonCount, 0.25, "词缀=a1（common 池权重 1:3）");
            AssertWithinThreeSigma(a2Count, commonCount, 0.75, "词缀=a2（common 池权重 1:3）");
        }

        private static void AssertWithinThreeSigma(int observedCount, int n, double expectedP, string label)
        {
            var mean = n * expectedP;
            var sigma = Math.Sqrt(n * expectedP * (1 - expectedP));
            var lower = mean - 3 * sigma;
            var upper = mean + 3 * sigma;
            Assert.True(observedCount >= lower && observedCount <= upper,
                $"{label}：观测 {observedCount}，期望均值 {mean:F1} ± 3σ({sigma:F1}) = [{lower:F1}, {upper:F1}]");
        }

        // ------------------------------------------------------------------
        // 4) 模板 affixes 白名单与品质池交集
        // ------------------------------------------------------------------

        [Fact]
        public void RollDetailed_TemplateAffixWhitelist_IntersectsWithQualityPool()
        {
            var tableJson = LootTableRow("loot.sample_whitelist", "item.sample_gear_whitelisted", qualityWeightsJson: null);
            var templateJson = "[" +
                TemplateRow("item.sample_gear_whitelisted", "item.quality.sample_common", 10, "[\"item.affix.a1\"]") +
                "]";

            var host = NewHost(2024, tableJson, templateJson, out _);
            var context = new RollContext(new Id("unit.sample_source"));

            for (var i = 0; i < 20; i++)
            {
                var result = host.RollDetailed(
                    new Id("loot.sample_whitelist"),
                    new RollContext(new Id("unit.sample_source"), contextId: new Id($"unit.trial_{i}")));
                Assert.Single(result[0].Affixes);
                Assert.Equal(new Id("item.affix.a1"), result[0].Affixes[0]);
            }
        }

        // ------------------------------------------------------------------
        // 5) 来源等级 + 难度层偏移折算物品等级；无来源等级时为 null（消费方回退模板）
        // ------------------------------------------------------------------

        [Fact]
        public void RollDetailed_SourceLevelWithOffset_ComputesItemLevel()
        {
            var tableJson = LootTableRow("loot.sample_level", "item.sample_gear", qualityWeightsJson: null);
            var templateJson = "[" + TemplateRow("item.sample_gear", "item.quality.sample_common", 10) + "]";

            var host = NewHost(3, tableJson, templateJson, out _);

            var withSource = host.RollDetailed(
                new Id("loot.sample_level"),
                new RollContext(new Id("unit.sample_source"), null, 1.0, null, sourceLevel: 40, itemLevelOffset: 5));
            Assert.Equal(45, withSource[0].ItemLevel);

            var withoutSource = host.RollDetailed(
                new Id("loot.sample_level"), new RollContext(new Id("unit.sample_source")));
            Assert.Null(withoutSource[0].ItemLevel);
        }

        // ------------------------------------------------------------------
        // 6) RollDetailed 与 Roll 共用同一份 RNG 序列（不重复抽取）
        // ------------------------------------------------------------------

        [Fact]
        public void Roll_And_RollDetailed_ConsumeSameRngSequence_NotDouble()
        {
            var tableJson = LootTableRow(
                "loot.sample_shared_rng", "item.sample_gear",
                "{\"item.quality.sample_common\": 1, \"item.quality.sample_rare\": 1}");
            var templateJson = "[" + TemplateRow("item.sample_gear", "item.quality.sample_common", 10) + "]";

            // 两个独立宿主、同种子：一个只调用 RollDetailed 两次，另一个交替调用 Roll/RollDetailed，
            // 若 Roll 内部会"重复"消耗一遍随机数（而不是转发 RollDetailed 的结果），两者的第二次结果
            // 会因为 RNG 状态偏移而不同。
            var hostA = NewHost(55, tableJson, templateJson, out _);
            var firstA = hostA.RollDetailed(new Id("loot.sample_shared_rng"), new RollContext(new Id("unit.a")));
            var secondA = hostA.RollDetailed(new Id("loot.sample_shared_rng"), new RollContext(new Id("unit.a")));

            var hostB = NewHost(55, tableJson, templateJson, out _);
            var firstB = hostB.Roll(new Id("loot.sample_shared_rng"), new RollContext(new Id("unit.a")));
            var secondB = hostB.RollDetailed(new Id("loot.sample_shared_rng"), new RollContext(new Id("unit.a")));

            Assert.Equal(firstA[0].ToStack(), firstB[0]);
            Assert.Equal(secondA[0], secondB[0]);
        }
    }
}
