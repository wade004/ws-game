using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using System;
using Xunit;

namespace Tests.Foundation.SaveSystem
{
    /// <summary>
    /// CORE-170-03 根治（architecture/落地计划/audit-8160178-20260908，P2）：<see
    /// cref="RngStreamsPersistable.Load"/> 修复前主种子一校验通过就立即 <see cref="IRngHost.Reset"/>，
    /// 随后逐条流边校验边 <see cref="IRngHost.SetStreamState"/>——排在后面的流状态文本非法时，
    /// <c>Reset</c> 与排在它之前的全部 <c>SetStreamState</c> 都已经真正生效，抛异常后不会回滚，
    /// 与 <c>EquipmentPersistable.Load</c> 曾经的同一类缺陷成因相同。根治后先完整校验主种子与
    /// 全部流状态文本，只有整份数据校验通过才提交 <c>Reset</c> + 逐条 <c>SetStreamState</c>。
    /// </summary>
    public sealed class RngStreamsPersistableTests
    {
        private static readonly Id StreamA = new Id("rng.stream.core_170_03_a");
        private static readonly Id StreamB = new Id("rng.stream.core_170_03_b");

        [Fact]
        public void Load_BadShape_ThrowsFormatException_AndLeavesStreamsUntouched()
        {
            var rng = new RngHost(12345UL);
            rng.NextInt(StreamA, 0, 100);
            var beforeState = rng.GetStreamState(StreamA);
            var persistable = new RngStreamsPersistable(rng);

            var ex = Record.Exception(() => persistable.Load(new JsonString("wrong-shape")));

            Assert.IsType<FormatException>(ex);
            Assert.Equal(12345UL, rng.MasterSeed);
            Assert.Equal(beforeState, rng.GetStreamState(StreamA));
        }

        /// <summary>
        /// 核心复现：捕获一份"过期"的存档快照（流 A 早先的状态），随后让流 A 在真实运行中继续
        /// 前进到一个新状态；再构造一份"主种子 + 流 A 的过期快照 + 流 B 格式非法"的坏存档去
        /// <c>Load</c>。修复前会先 <c>Reset</c>（清空全部流）、再把流 A 覆盖回过期快照，才在流 B
        /// 处抛异常——流 A 最终停在错误的过期状态；修复后流 A 应当保持它读档前的真实状态不变。
        /// </summary>
        [Fact]
        public void Load_LaterStreamEntryBadShape_DoesNotPartiallyCommitEarlierEntries()
        {
            var rng = new RngHost(12345UL);
            rng.NextInt(StreamA, 0, 100);
            rng.NextInt(StreamB, 0, 100);
            var persistable = new RngStreamsPersistable(rng);
            var staleSnapshot = (JsonObject)persistable.Save(); // 捕获"过期"快照（流 A 的旧状态）。

            // 流 A 在真实运行中继续前进，产生一个与 staleSnapshot 不同的新状态。
            rng.NextInt(StreamA, 0, 100);
            rng.NextInt(StreamA, 0, 100);
            var liveStateBeforeLoad = rng.GetStreamState(StreamA);
            var liveStreamBBeforeLoad = rng.GetStreamState(StreamB);
            Assert.NotEqual(((JsonString)staleSnapshot[StreamA.Value]).Value, liveStateBeforeLoad.ToString());

            Assert.True(staleSnapshot.TryGetValue(StreamA.Value, out var staleAValue));
            var badData = new JsonObjectBuilder()
                .Add("master_seed", staleSnapshot["master_seed"])
                .Add(StreamA.Value, staleAValue) // 合法但过期：应当被解析进"计划"，不应该被提交。
                .Add(StreamB.Value, JsonBool.True) // 非法：不是 JsonString。
                .Build();

            var ex = Record.Exception(() => persistable.Load(badData));

            Assert.IsType<FormatException>(ex);
            Assert.Equal(12345UL, rng.MasterSeed);
            Assert.Equal(liveStateBeforeLoad, rng.GetStreamState(StreamA));
            Assert.Equal(liveStreamBBeforeLoad, rng.GetStreamState(StreamB));
        }

        [Fact]
        public void SaveThenLoad_RoundTrips_MasterSeedAndStreamState_IntoFreshHost()
        {
            var rng = new RngHost(999UL);
            rng.NextInt(StreamA, 0, 100);
            rng.NextInt(StreamA, 0, 100);
            var expectedState = rng.GetStreamState(StreamA);
            var saved = new RngStreamsPersistable(rng).Save();

            var restored = new RngHost(1UL);
            new RngStreamsPersistable(restored).Load(saved);

            Assert.Equal(999UL, restored.MasterSeed);
            Assert.Equal(expectedState, restored.GetStreamState(StreamA));
        }
    }
}
