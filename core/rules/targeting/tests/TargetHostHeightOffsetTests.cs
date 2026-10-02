using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    /// <summary>
    /// 命中形状自身的竖直偏移（<c>shape.height_offset</c>，ADR-0130 追加决定）：高度窗口的中心 = 施法者脚下高度 + 偏移。
    /// 复现：偏移让同一批靶子的入选集合从 X 变到 Y（期望由 |目标高度 − (施法者高度 + 偏移)| ≤ shape.height 算出）；
    /// 不变量：不声明偏移（或偏移为 0）与 1.95.0 逐位一致；<c>VerticalHit</c> 关闭时偏移被忽略。
    /// </summary>
    public class TargetHostHeightOffsetTests
    {
        private static readonly Id HeroFaction = new Id("fac.target_test_hero");
        private static readonly Id MonsterFaction = new Id("fac.target_test_monster");
        private static readonly Id Caster = new Id("unit.caster");

        private const double Window = 1.0;

        private static string Row(string id, string heightOffset) => $@"{{ ""id"": ""target.chain.{id}"", ""source"": ""all_in_shape"",
              ""shape"": {{ ""kind"": ""circle"", ""radius"": 10, ""height"": 1{heightOffset} }},
              ""filters"": [""relation:hostile"", ""alive""],
              ""sort_by"": {{ ""key"": ""distance"", ""direction"": ""asc"" }}, ""max_targets"": 8 }}";

        private static readonly string Rows = "[" + string.Join(",",
            Row("off_none", ""),
            Row("off_zero", @", ""height_offset"": 0"),
            Row("off_up4", @", ""height_offset"": 4"),
            Row("off_down1", @", ""height_offset"": -1"),
            Row("off_far", @", ""height_offset"": 50")) + "]";

        private static readonly (string Name, Vec2 Pos, double Height)[] Dummies =
        {
            ("ground", new Vec2(3, 0), 0.0),
            ("half", new Vec2(4, 0), 0.5),
            ("edge", new Vec2(5, 0), Window),
            ("above", new Vec2(6, 0), Window + 0.001),
            ("high", new Vec2(7, 0), 4.0),
        };

        private static TargetHostTests.Fixture Build(TargetingOptions? options, double casterHeight = 0.0)
        {
            return TargetHostTests.Build(Rows, (units, spatial, powers, threat) =>
            {
                units.Add(Caster, HeroFaction, new Vec2(0, 0));
                units.SetHeight(Caster, casterHeight);
                foreach (var d in Dummies)
                {
                    var id = new Id("unit." + d.Name);
                    units.Add(id, MonsterFaction, d.Pos);
                    units.SetHeight(id, d.Height);
                    spatial.Register(id, d.Pos, 0);
                }
            }, options: options);
        }

        private static string[] Resolve(TargetHostTests.Fixture fx, string chain) =>
            fx.Host.Resolve(new Id("target.chain." + chain), Caster).Select(i => i.Value.Substring("unit.".Length)).ToArray();

        private static string[] Expected(double center) =>
            Dummies.Where(d => Math.Abs(d.Height - center) <= Window).OrderBy(d => d.Pos.X).Select(d => d.Name).ToArray();

        [Fact]
        public void PositiveOffset_MovesTheWindowUp_ToTheHighTarget()
        {
            var fx = Build(new TargetingOptions { VerticalHit = true });
            Assert.Equal(new[] { "ground", "half", "edge" }, Resolve(fx, "off_none"));
            Assert.Equal(Expected(4.0), Resolve(fx, "off_up4"));
            Assert.Equal(new[] { "high" }, Resolve(fx, "off_up4"));
        }

        [Fact]
        public void NegativeOffset_MovesTheWindowDown()
        {
            var fx = Build(new TargetingOptions { VerticalHit = true });
            Assert.Equal(Expected(-1.0), Resolve(fx, "off_down1"));
            Assert.Equal(new[] { "ground" }, Resolve(fx, "off_down1"));
        }

        [Fact]
        public void OffsetIsRelativeToTheCastersFootHeight_NotToZero()
        {
            // 施法者在 4 高处：偏移 −4 把窗口拉回 0 附近；偏移 +4 则是 8 附近（没有靶子）。
            var fx = Build(new TargetingOptions { VerticalHit = true }, casterHeight: 4.0);
            Assert.Equal(new[] { "high" }, Resolve(fx, "off_none"));
            Assert.Equal(Expected(4.0 + 4.0), Resolve(fx, "off_up4"));
            Assert.Empty(Resolve(fx, "off_up4"));
        }

        [Fact]
        public void WindowFarAboveEverything_SelectsNobody()
        {
            var fx = Build(new TargetingOptions { VerticalHit = true });
            Assert.Empty(Resolve(fx, "off_far"));
        }

        [Fact]
        public void ZeroOrAbsentOffset_IsBitIdenticalToTheOldWindow()
        {
            foreach (var h in new[] { 0.0, 0.3, 4.0 })
            {
                var fx = Build(new TargetingOptions { VerticalHit = true }, casterHeight: h);
                Assert.Equal(Resolve(fx, "off_none"), Resolve(fx, "off_zero"));
                Assert.Equal(Expected(h), Resolve(fx, "off_none"));
            }
        }

        [Fact]
        public void OffsetIsIgnored_WhenVerticalHitIsOff()
        {
            var fx = Build(options: null);
            var all = Dummies.OrderBy(d => d.Pos.X).Select(d => d.Name).ToArray();
            Assert.Equal(all, Resolve(fx, "off_up4"));
            Assert.Equal(all, Resolve(fx, "off_far"));
        }
    }
}
