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
    public class LootHostRollTests
    {
        private static LootHost NewHost(
            string lootTableRowsJson,
            ulong seed,
            out FakeExprHostFactory exprFactory,
            LootOptions? options = null,
            bool registerValidationRule = true)
        {
            var bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistry(bus, lootTableRowsJson, registerValidationRule);
            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            exprFactory = new FakeExprHostFactory();

            return new LootHost(
                registry, new RngHost(seed), bus, world, units, inventory, exprFactory,
                simTimeProvider: () => 0.0, options: options);
        }

        private const string SingleChanceTable =
            "[{\"id\": \"loot.sample_single\", \"groups\": [" +
            "{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":1}}" +
            "]}]}]";

        [Fact]
        public void SameSeed_ProducesSameResult_Twice()
        {
            var complexTable = "[{\"id\": \"loot.sample_complex\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 0.7, \"count_range\": {\"min\":1,\"max\":3}}," +
                "{\"ref\": \"item.sample_herb\", \"weight_or_chance\": 0.4, \"count_range\": {\"min\":1,\"max\":2}}" +
                "]}]}]";

            var host1 = NewHost(complexTable, seed: 12345, out _);
            var result1 = host1.Roll(new Id("loot.sample_complex"), new RollContext(new Id("unit.sample_source")));

            var host2 = NewHost(complexTable, seed: 12345, out _);
            var result2 = host2.Roll(new Id("loot.sample_complex"), new RollContext(new Id("unit.sample_source")));

            Assert.Equal(result1.Count, result2.Count);
            for (var i = 0; i < result1.Count; i++)
            {
                Assert.Equal(result1[i].TemplateId, result2[i].TemplateId);
                Assert.Equal(result1[i].Count, result2[i].Count);
            }
        }

        [Fact]
        public void DifferentSeeds_CanProduceDifferentOutcomes()
        {
            var outcomes = new HashSet<bool>();
            for (ulong seed = 0; seed < 50; seed++)
            {
                var host = NewHost(SingleChanceTable, seed, out _);
                var result = host.Roll(new Id("loot.sample_single"), new RollContext(new Id("unit.sample_source")));
                outcomes.Add(result.Count > 0);
            }

            // 50 次独立种子下，chance=0.5 的单条目不太可能全部同一个结果（概率约 2 * 0.5^50），
            // 断言两种结果都出现过，验证种子确实驱动了不同的抽取结果。
            Assert.Equal(2, outcomes.Count);
        }

        [Fact]
        public void Condition_False_PreventsDrop()
        {
            var table = "[{\"id\": \"loot.sample_conditional\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 1.0, \"condition\": \"self.is_alive\", \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var host = NewHost(table, seed: 1, out var exprFactory);
            var killer = new Id("unit.sample_killer");
            exprFactory.AliveFlags[killer] = false;

            var result = host.Roll(new Id("loot.sample_conditional"), new RollContext(new Id("unit.sample_source"), killer));

            Assert.Empty(result);
        }

        [Fact]
        public void Condition_True_AllowsDrop()
        {
            var table = "[{\"id\": \"loot.sample_conditional\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 1.0, \"condition\": \"self.is_alive\", \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var host = NewHost(table, seed: 1, out var exprFactory);
            var killer = new Id("unit.sample_killer");
            exprFactory.AliveFlags[killer] = true;

            var result = host.Roll(new Id("loot.sample_conditional"), new RollContext(new Id("unit.sample_source"), killer));

            Assert.Single(result);
            Assert.Equal(new Id("item.sample_ore"), result[0].TemplateId);
        }

        [Fact]
        public void Multiplier_Zero_NeverDrops()
        {
            var host = NewHost(SingleChanceTable, seed: 999, out _);
            var result = host.Roll(new Id("loot.sample_single"),
                new RollContext(new Id("unit.sample_source"), multiplier: 0.0));

            Assert.Empty(result);
        }

        [Fact]
        public void Multiplier_AboveThreshold_AlwaysDrops()
        {
            var host = NewHost(SingleChanceTable, seed: 999, out _);
            var result = host.Roll(new Id("loot.sample_single"),
                new RollContext(new Id("unit.sample_source"), multiplier: 10.0));

            Assert.Single(result);
        }

        [Fact]
        public void WeightedPickOne_PickCountEqualsEntryCount_YieldsAllDistinctWithoutReplacement()
        {
            var table = "[{\"id\": \"loot.sample_weighted\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"pick_count\": 3, \"entries\": [" +
                "{\"ref\": \"item.sample_a\", \"weight_or_chance\": 1, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_b\", \"weight_or_chance\": 5, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_c\", \"weight_or_chance\": 10, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var host = NewHost(table, seed: 42, out _);
            var result = host.Roll(new Id("loot.sample_weighted"), new RollContext(new Id("unit.sample_source")));

            Assert.Equal(3, result.Count);
            var ids = result.Select(r => r.TemplateId).ToHashSet();
            Assert.Contains(new Id("item.sample_a"), ids);
            Assert.Contains(new Id("item.sample_b"), ids);
            Assert.Contains(new Id("item.sample_c"), ids);
        }

        [Fact]
        public void WeightedPickOne_HeavierWeight_IsPickedMoreOften()
        {
            var table = "[{\"id\": \"loot.sample_weighted2\", \"groups\": [" +
                "{\"roll_mode\": \"weighted_pick_one\", \"entries\": [" +
                "{\"ref\": \"item.sample_common\", \"weight_or_chance\": 95, \"count_range\": {\"min\":1,\"max\":1}}," +
                "{\"ref\": \"item.sample_rare\", \"weight_or_chance\": 5, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var commonCount = 0;
            const int trials = 300;
            for (ulong seed = 0; seed < trials; seed++)
            {
                var host = NewHost(table, seed, out _);
                var result = host.Roll(new Id("loot.sample_weighted2"), new RollContext(new Id("unit.sample_source")));
                if (result.Count == 1 && result[0].TemplateId.Equals(new Id("item.sample_common")))
                {
                    commonCount++;
                }
            }

            // 期望值 285/300；用宽松阈值（>200）避免统计抖动导致偶发失败。
            Assert.True(commonCount > 200, $"重权重条目只被抽中 {commonCount}/{trials} 次，明显偏离期望分布");
        }

        [Fact]
        public void NestedLootTableReference_ResolvesAndMergesResults()
        {
            var tables = "[" +
                "{\"id\": \"loot.sample_outer\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"loot.sample_inner\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}," +
                "{\"id\": \"loot.sample_inner\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":2,\"max\":2}}" +
                "]}]}" +
                "]";

            var host = NewHost(tables, seed: 7, out _);
            var result = host.Roll(new Id("loot.sample_outer"), new RollContext(new Id("unit.sample_source")));

            Assert.Single(result);
            Assert.Equal(new Id("item.sample_ore"), result[0].TemplateId);
            Assert.Equal(2, result[0].Count);
        }

        [Fact]
        public void GuaranteedMin_TopsUpResultWhenNaturalRollFallsShort()
        {
            var table = "[{\"id\": \"loot.sample_guaranteed\", \"guaranteed_min\": 3, \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}]}]";

            var host = NewHost(table, seed: 1, out _);
            var result = host.Roll(new Id("loot.sample_guaranteed"), new RollContext(new Id("unit.sample_source")));

            // 唯一条目 chance=1.0 自然命中 1 次（resultCount=1），保底 3 条但候选池只有这一条目，
            // 补抽一次后候选耗尽（resultCount=2 即停止，见 loot README 判断记录 3"或候选耗尽"）。
            // 两次命中同一 ref，合并堆叠数量应为 2。
            Assert.Single(result);
            Assert.Equal(2, result[0].Count);
        }

        [Fact]
        public void Stacking_MergesSameTemplateAcrossDifferentEntries()
        {
            var table = "[{\"id\": \"loot.sample_stack\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":2,\"max\":2}}," +
                "{\"ref\": \"item.sample_ore\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":3,\"max\":3}}" +
                "]}]}]";

            var host = NewHost(table, seed: 3, out _);
            var result = host.Roll(new Id("loot.sample_stack"), new RollContext(new Id("unit.sample_source")));

            Assert.Single(result);
            Assert.Equal(new Id("item.sample_ore"), result[0].TemplateId);
            Assert.Equal(5, result[0].Count);
        }

        [Fact]
        public void Roll_UnknownTableId_Throws()
        {
            var host = NewHost(SingleChanceTable, seed: 1, out _);
            Assert.Throws<ArgumentException>(() =>
                host.Roll(new Id("loot.does_not_exist"), new RollContext(new Id("unit.sample_source"))));
        }

        [Fact]
        public void CyclicNestedReferences_AreRejectedByValidationRule()
        {
            var bus = LootTestSupport.NewEventBus();
            var cyclicTables = "[" +
                "{\"id\": \"loot.sample_cycle_a\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"loot.sample_cycle_b\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}}]}]}," +
                "{\"id\": \"loot.sample_cycle_b\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"loot.sample_cycle_a\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}}]}]}" +
                "]";

            var source = new Core.Foundation.DataRegistry.InMemoryDataSource()
                .Add(LootSchemas.Table.Name, LootTestSupport.Envelope(LootSchemas.Table.Name, cyclicTables));
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, bus, new Core.Foundation.DataRegistry.DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Message.Contains("成环"));
        }
    }
}
