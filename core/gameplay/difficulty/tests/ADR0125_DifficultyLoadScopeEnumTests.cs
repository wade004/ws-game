using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Gameplay.Difficulty;
using Xunit;

namespace Tests.Gameplay.Difficulty
{
    /// <summary>
    /// ADR-0125 第三批探针缺陷：<see cref="DifficultyHost.Load"/> 用 <c>Enum.TryParse</c> 解析 <c>scope</c>，
    /// 数字串（含未定义的数字）会被解析成 <see cref="DifficultyScope"/> 值写进 <c>CurrentScope</c>。
    /// 期望：与"无法解析的文本"同一口径——只认枚举名，其余按"未提供 scope"处理（<c>CurrentScope</c> 为 null），
    /// 不把未定义值写进运行期。
    /// </summary>
    public sealed class ADR0125_DifficultyLoadScopeEnumTests
    {
        private static readonly Id Tier = new Id("diff.sample_hard");

        private static DifficultyHost MakeHost()
        {
            var bus = TestSupport.CreateBus();
            var registry = TestSupport.MakeRegistry(bus);
            var factions = new FakeFactionMatrix();
            return new DifficultyHost(
                registry, bus, new FakeEffectSink(), factions, new FakeUnitAccess(),
                new DifficultyOptions(new Id("fac.sample_player")));
        }

        private static JsonValue Snapshot(string scope) =>
            new JsonObjectBuilder()
                .Add("tierId", new JsonString(Tier.Value))
                .Add("scope", new JsonString(scope))
                .Add("mapId", JsonNull.Instance)
                .Build();

        public static System.Collections.Generic.IEnumerable<object[]> NonNameScopes()
        {
            var all = (DifficultyScope[])Enum.GetValues(typeof(DifficultyScope));
            var undefined = 0;
            foreach (var v in all) { undefined = Math.Max(undefined, (int)v); }
            yield return new object[] { (undefined + 1).ToString() };   // 未定义的数字
            yield return new object[] { "-1" };
            foreach (var v in all) { yield return new object[] { ((int)v).ToString() }; } // 已定义值的数字串
        }

        [Theory]
        [MemberData(nameof(NonNameScopes))]
        public void Load_ScopeNotAnEnumName_LeavesCurrentScopeNull(string scope)
        {
            var host = MakeHost();

            host.Load(Snapshot(scope));

            Assert.Null(host.CurrentScope);
            Assert.Equal(Tier, host.CurrentTier); // 其余字段照常恢复
        }

        [Fact]
        public void Load_EveryDefinedScopeName_IsAccepted()
        {
            foreach (var name in Enum.GetNames(typeof(DifficultyScope)))
            {
                var host = MakeHost();

                host.Load(Snapshot(name));

                Assert.Equal(name, host.CurrentScope.ToString());
            }
        }
    }
}
