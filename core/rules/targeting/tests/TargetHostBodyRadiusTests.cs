using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    /// <summary>
    /// 目标命中半径（<see cref="TargetingOptions.TargetRadius"/>，手感落地 M4 清扫，手感设计/03 第 2.4 节）：
    /// 复现——形状擦到目标身体（中心在形状外、到形状的距离不超过目标半径）也算命中；不变量——不配置来源（或上界为 0）时只按目标中心判定，行为与改动前逐位一致。
    /// 期望值由规则算出：圆形到点的距离 = max(0, 距圆心 − 圆半径)，命中当且仅当它不超过目标半径。
    /// </summary>
    public class TargetHostBodyRadiusTests
    {
        private static readonly Id HeroFaction = new Id("fac.target_test_hero");
        private static readonly Id MonsterFaction = new Id("fac.target_test_monster");
        private static readonly Id Caster = new Id("unit.caster");

        private const double ShapeRadius = 10.0;

        private const string Rows = @"[
            { ""id"": ""target.chain.r_all"", ""source"": ""all_in_shape"",
              ""shape"": { ""kind"": ""circle"", ""radius"": 10 },
              ""filters"": [""relation:hostile"", ""alive""],
              ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" }, ""max_targets"": 8 },
            { ""id"": ""target.chain.r_nearest"", ""source"": ""nearest_in_shape"",
              ""shape"": { ""kind"": ""circle"", ""radius"": 10 },
              ""filters"": [""relation:hostile"", ""alive""],
              ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" }, ""max_targets"": 1 }
        ]";

        // 靶子（名 → 到施法者的距离、命中半径）：形状内的点、擦边的大个子、擦不到的小个子、够不着的大个子、形状内的大个子。
        private static readonly (string Name, double Distance, double Radius)[] Dummies =
        {
            ("inside_point", 9.0, 0.0),
            ("graze_big", 10.8, 1.0),
            ("miss_small", 10.8, 0.5),
            ("miss_far_big", 12.5, 1.0),
            ("inside_big", 9.5, 2.0),
        };

        private static TargetHostTests.Fixture Build(TargetingOptions? options) =>
            TargetHostTests.Build(Rows, (units, spatial, powers, threat) =>
            {
                units.Add(Caster, HeroFaction, new Vec2(0, 0));
                foreach (var d in Dummies)
                {
                    var id = new Id("unit." + d.Name);
                    var pos = new Vec2(d.Distance, 0);
                    units.Add(id, MonsterFaction, pos);
                    spatial.Register(id, pos, 0);
                }
            }, options: options);

        private static TargetingOptions WithRadius(double? max = null) => new TargetingOptions
        {
            TargetRadius = id => Dummies.First(d => "unit." + d.Name == id.Value).Radius,
            MaxTargetRadius = max ?? Dummies.Max(d => d.Radius),
        };

        private static string[] Names(IReadOnlyList<Id> ids) => ids.Select(i => i.Value.Substring("unit.".Length)).ToArray();

        [Fact]
        public void BodyRadius_HitsTargetsWhoseBodyTouchesTheShape_NotOnlyThoseWhoseCenterIsInside()
        {
            var fx = Build(WithRadius());
            var result = Names(fx.Host.Resolve(new Id("target.chain.r_all"), Caster));

            // 规则：圆形到靶子中心的距离 = max(0, 距离 − 形状半径)，不超过靶子半径即命中；再按到施法者的距离升序。
            var expected = Dummies.Where(d => Math.Max(0.0, d.Distance - ShapeRadius) <= d.Radius + 1e-12)
                .OrderBy(d => d.Distance).Select(d => d.Name).ToArray();
            Assert.Equal(expected, result);
            Assert.Contains("graze_big", result);
            Assert.DoesNotContain("miss_small", result);
            Assert.DoesNotContain("miss_far_big", result);
        }

        [Fact]
        public void WithoutTheOption_OnlyCentersInsideTheShapeAreHit_ExactlyAsBefore()
        {
            var plain = Names(Build(null).Host.Resolve(new Id("target.chain.r_all"), Caster));
            var expected = Dummies.Where(d => d.Distance <= ShapeRadius).OrderBy(d => d.Distance).Select(d => d.Name).ToArray();
            Assert.Equal(expected, plain);

            // 来源给了但上界为 0（未启用）：同样不改变结果。
            var zeroMax = Names(Build(WithRadius(max: 0.0)).Host.Resolve(new Id("target.chain.r_all"), Caster));
            Assert.Equal(plain, zeroMax);
        }

        [Fact]
        public void BodyRadius_DoesNotDuplicateTargetsAlreadyInside_AndKeepsNearestInShapeDeterministic()
        {
            var fx = Build(WithRadius());
            var all = Names(fx.Host.Resolve(new Id("target.chain.r_all"), Caster));
            Assert.Equal(all.Length, all.Distinct().Count()); // 中心在形状内、半径也大的靶子只出现一次。

            var nearest = Names(fx.Host.Resolve(new Id("target.chain.r_nearest"), Caster));
            Assert.Equal(new[] { Dummies.OrderBy(d => d.Distance).First().Name }, nearest); // 最近者仍是形状内最近的点
        }

        [Fact]
        public void BodyRadius_TargetBeyondTheDeclaredMaximum_IsNotConsidered_TheMaximumIsTheBroadPhaseBound()
        {
            // 上界小于靶子实际半径时按上界做广相位：半径 1.0 的擦边靶在上界 0.5 时漏判，说明上界必须覆盖最大半径（文档约束）。
            var fx = Build(WithRadius(max: 0.5));
            var result = Names(fx.Host.Resolve(new Id("target.chain.r_all"), Caster));
            Assert.DoesNotContain("graze_big", result);
        }
    }
}
