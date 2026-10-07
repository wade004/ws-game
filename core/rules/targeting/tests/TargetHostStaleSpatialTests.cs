using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    /// <summary>
    /// 空间索引过期条目（样板游戏 C 消费方反馈）：实体在同一固定步里被销毁、"实体已销毁"事件尚未分发时，空间索引里仍有它的 id，
    /// 目标解析不能因此抛异常（此前 <c>nearest_in_shape</c> 对它取位置，<c>WorldUnitAccess.Require</c> 直接抛 InvalidOperationException）。
    /// 复现：空间索引里登记了一个不在单位表里的 id；不变量：结果只含世界里还在的单位，其余候选与排序不受影响；不带过期条目时行为不变。
    /// </summary>
    public class TargetHostStaleSpatialTests
    {
        private static readonly Id HeroFaction = new Id("fac.target_test_hero");
        private static readonly Id MonsterFaction = new Id("fac.target_test_monster");
        private static readonly Id Caster = new Id("unit.caster");
        private static readonly Id Stale = new Id("unit.stale_destroyed");
        private static readonly Id Near = new Id("unit.near");
        private static readonly Id Far = new Id("unit.far");

        private const string Rows = @"[
            { ""id"": ""target.chain.s_all"", ""source"": ""all_in_shape"",
              ""shape"": { ""kind"": ""circle"", ""radius"": 10 },
              ""filters"": [""relation:hostile""],
              ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" }, ""max_targets"": 8 },
            { ""id"": ""target.chain.s_nearest"", ""source"": ""nearest_in_shape"",
              ""shape"": { ""kind"": ""circle"", ""radius"": 10 },
              ""filters"": [""relation:hostile""],
              ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" }, ""max_targets"": 1 }
        ]";

        private static TargetHostTests.Fixture Build(TargetingOptions? options = null) =>
            TargetHostTests.Build(Rows, (units, spatial, powers, threat) =>
            {
                units.Add(Caster, HeroFaction, new Vec2(0, 0));
                units.Add(Near, MonsterFaction, new Vec2(3, 0));
                units.Add(Far, MonsterFaction, new Vec2(7, 0));
                spatial.Register(Near, new Vec2(3, 0), 0);
                spatial.Register(Far, new Vec2(7, 0), 0);
                // 比两个活着的都近，但已经不在单位表里：销毁事件还没分发，空间索引里是过期条目。
                spatial.Register(Stale, new Vec2(1, 0), 0);
            }, options: options);

        [Fact]
        public void NearestInShape_SkipsStaleSpatialEntry_AndPicksNearestLiveUnit()
        {
            var fx = Build();
            var result = fx.Host.Resolve(new Id("target.chain.s_nearest"), Caster);
            Assert.Equal(new[] { Near }, result.ToArray());
        }

        [Fact]
        public void AllInShape_SkipsStaleSpatialEntry_KeepsLiveOnesInDistanceOrder()
        {
            var fx = Build();
            var result = fx.Host.Resolve(new Id("target.chain.s_all"), Caster);
            Assert.Equal(new[] { Near, Far }, result.ToArray());
        }

        [Fact]
        public void BodyRadiusExtraCandidates_SkipStaleSpatialEntry()
        {
            // 过期条目在形状外、但外扩后进入候选：按目标半径重判之前先判存在，不能对它取位置。
            var fx = TargetHostTests.Build(Rows, (units, spatial, powers, threat) =>
            {
                units.Add(Caster, HeroFaction, new Vec2(0, 0));
                units.Add(Near, MonsterFaction, new Vec2(3, 0));
                spatial.Register(Near, new Vec2(3, 0), 0);
                spatial.Register(Stale, new Vec2(10.5, 0), 0);
            }, options: new TargetingOptions { TargetRadius = id => 1.0, MaxTargetRadius = 1.0 });
            var result = fx.Host.Resolve(new Id("target.chain.s_all"), Caster);
            Assert.Equal(new[] { Near }, result.ToArray());
        }

        [Fact]
        public void WithoutStaleEntries_ResultsAreUnchanged()
        {
            var fx = TargetHostTests.Build(Rows, (units, spatial, powers, threat) =>
            {
                units.Add(Caster, HeroFaction, new Vec2(0, 0));
                units.Add(Near, MonsterFaction, new Vec2(3, 0));
                units.Add(Far, MonsterFaction, new Vec2(7, 0));
                spatial.Register(Near, new Vec2(3, 0), 0);
                spatial.Register(Far, new Vec2(7, 0), 0);
            });
            Assert.Equal(new[] { Near, Far }, fx.Host.Resolve(new Id("target.chain.s_all"), Caster).ToArray());
            Assert.Equal(new[] { Near }, fx.Host.Resolve(new Id("target.chain.s_nearest"), Caster).ToArray());
        }
    }
}
