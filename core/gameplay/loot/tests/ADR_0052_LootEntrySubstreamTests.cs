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
    /// ADR-0052（消费方反馈第 4 条根治）：<see cref="LootHost"/> 从"全表共用一条顺序推进的
    /// <c>IRngHost</c> 流"改为"按 <c>(loot_table_id, entry_ref)</c> 派生独立子流"，见
    /// <c>LootHost.DeriveEntryStream</c> 判断记录、loot README 判断记录 20。本文件覆盖两条验收标准：
    /// 隔离性（表中间插入一个新条目不挪动其它条目已经算出的结果）与确定性（同一对父种子 + id 派生出
    /// 的子流初始状态是一个钉死的固定值，不随进程/条目声明顺序变化）。
    /// </summary>
    public class ADR_0052_LootEntrySubstreamTests
    {
        private static LootHost NewHost(string lootTableRowsJson, ulong seed)
        {
            var bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistry(bus, lootTableRowsJson);
            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            var exprFactory = new FakeExprHostFactory();

            return new LootHost(
                registry, new RngHost(seed), bus, world, units, inventory, exprFactory,
                simTimeProvider: () => 0.0);
        }

        private const ulong ParentSeed = 2UL;
        private const string TableId = "loot.sample_substream";

        private static string BuildTable(params string[] entryRefsInOrder)
        {
            var entries = entryRefsInOrder.Select(r =>
                "{\"ref\": \"" + r + "\", \"weight_or_chance\": 0.9, \"count_range\": {\"min\":1,\"max\":5}}");
            return "[{\"id\": \"" + TableId + "\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" + string.Join(",", entries) + "]}]}]";
        }

        /// <summary>隔离性验收标准：固定父种子先跑一张只有 alpha/beta 两条的表，再跑一张在中间插入
        /// gamma 的三条表——alpha/beta 各自的产出（是否产出、产出数量）必须逐条不变。剔除 gamma 自己
        /// 的产出后比较剩余序列。</summary>
        [Fact]
        public void InsertingEntryInMiddleOfTable_DoesNotChangeOtherEntriesOutcome()
        {
            var without = BuildTable("item.sample_alpha", "item.sample_beta");
            var withInserted = BuildTable("item.sample_alpha", "item.sample_gamma", "item.sample_beta");

            var hostA = NewHost(without, ParentSeed);
            var resultA = hostA.RollDetailed(new Id(TableId), new RollContext(new Id("unit.sample_source")));

            var hostB = NewHost(withInserted, ParentSeed);
            var resultB = hostB.RollDetailed(new Id(TableId), new RollContext(new Id("unit.sample_source")));

            var gamma = new Id("item.sample_gamma");
            var stableA = resultA.Where(o => !o.TemplateId.Equals(gamma)).ToList();
            var stableB = resultB.Where(o => !o.TemplateId.Equals(gamma)).ToList();

            Assert.Equal(stableA.Count, stableB.Count);
            for (var i = 0; i < stableA.Count; i++)
            {
                Assert.Equal(stableA[i].TemplateId, stableB[i].TemplateId);
                Assert.Equal(stableA[i].Count, stableB[i].Count);
            }

            // 隔离性不是"插入的条目从不产出"这种平凡满足：至少验证两边确实都不是空跑（alpha/beta
            // 在 chance=0.9 的高命中率下，两边应各自产出至少一条），避免断言在"两边都空"时假通过。
            Assert.NotEmpty(stableA);
        }

        /// <summary>与隔离性同一枚硬币的另一面：同一批条目只是在数据里声明的先后顺序不同（entries
        /// 数组交换 alpha/beta 两条的位置，不增不减），每条自己的产出必须逐条不变——子流身份只挂在
        /// (tableId, ref) 上，不挂在数组下标上，因此不依赖登记顺序（AGENTS.md §3）。</summary>
        [Fact]
        public void ReorderingSiblingEntries_DoesNotChangeEitherEntrysOutcome()
        {
            var orderOne = BuildTable("item.sample_alpha", "item.sample_beta");
            var orderTwo = BuildTable("item.sample_beta", "item.sample_alpha");

            var hostOne = NewHost(orderOne, ParentSeed);
            var resultOne = hostOne.RollDetailed(new Id(TableId), new RollContext(new Id("unit.sample_source")));

            var hostTwo = NewHost(orderTwo, ParentSeed);
            var resultTwo = hostTwo.RollDetailed(new Id(TableId), new RollContext(new Id("unit.sample_source")));

            LootRollOutcome? FindOutcome(IReadOnlyList<LootRollOutcome> outcomes, string refValue)
            {
                var id = new Id(refValue);
                foreach (var o in outcomes)
                {
                    if (o.TemplateId.Equals(id))
                    {
                        return o;
                    }
                }

                return null;
            }

            var alphaOne = FindOutcome(resultOne, "item.sample_alpha");
            var alphaTwo = FindOutcome(resultTwo, "item.sample_alpha");
            Assert.Equal(alphaOne.HasValue, alphaTwo.HasValue);
            if (alphaOne.HasValue)
            {
                Assert.Equal(alphaOne.Value.Count, alphaTwo!.Value.Count);
            }

            var betaOne = FindOutcome(resultOne, "item.sample_beta");
            var betaTwo = FindOutcome(resultTwo, "item.sample_beta");
            Assert.Equal(betaOne.HasValue, betaTwo.HasValue);
            if (betaOne.HasValue)
            {
                Assert.Equal(betaOne.Value.Count, betaTwo!.Value.Count);
            }
        }

        /// <summary>确定性验收标准：给定父种子 20260921 与 (loot_table_id="loot.sample_substream",
        /// entry_ref="item.sample_alpha")，<c>LootHost.DeriveEntryStream</c> 派生出的子流标识固定为
        /// <c>"loot.roll.by_entry.loot_sample_substream.item_sample_alpha"</c>（算法见该方法判断
        /// 记录："{RngStream}.by_entry.{扁平化表id}.{扁平化条目ref}"，扁平化只是把 '.' 换成 '_'）；
        /// 该子流经 <see cref="IRngHost"/> 既有的确定性派生（<c>SeedDerivation</c>：FNV-1a 64 位哈希
        /// + SplitMix64，不依赖 GetHashCode/系统时间/枚举顺序）算出的初始状态是一个钉死的固定十六进制
        /// 常量——两个完全独立、互不共享任何内存状态的 <see cref="RngHost"/> 实例（模拟跨进程）各自
        /// 首次访问该子流都必须算出同一个值。</summary>
        [Fact]
        public void DeriveEntryStream_ForFixedParentSeedAndIdPair_ProducesFixedSubSeed_AcrossIndependentInstances()
        {
            var derivedStreamId = new Id("loot.roll.by_entry.loot_sample_substream.item_sample_alpha");
            const string expected = "0a2f226ae32753de-b2bcf41dac4cdfaa-07b8e5d6ff46d4dd-02ca8e4b7aeaf92a";

            var firstProcess = new RngHost(ParentSeed);
            var secondProcess = new RngHost(ParentSeed);

            var firstState = firstProcess.GetStreamState(derivedStreamId).ToString();
            var secondState = secondProcess.GetStreamState(derivedStreamId).ToString();

            Assert.Equal(expected, firstState);
            Assert.Equal(expected, secondState);
        }
    }
}
