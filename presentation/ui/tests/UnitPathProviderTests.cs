using Core.Foundation.Common;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// <see cref="UnitPathProvider"/> 直接用例（ADR-0125 D16）：
    /// 路径 <c>unit.&lt;unitId 各段&gt;.power|stat...</c> 按"第一次出现的字面量 <c>power</c>/<c>stat</c> 段"切分
    /// 单位 id，这是<b>设计决定</b>，不是待修缺陷——含字面 <c>power</c>/<c>stat</c> 段的 unitId 无法通过本路径
    /// 查询（unitId 命名约束见 <c>presentation/ui/README.md</c>）。本套用例钉住正常形态与这一现行为。
    /// </summary>
    public class UnitPathProviderTests
    {
        private static readonly Id Health = new Id("arch.power.health");
        private static readonly Id Strength = new Id("stat.strength");

        // ---- 正常形态 ----

        [Theory]
        [InlineData("unit.inst_3")]
        [InlineData("squad.alpha.member_1")]
        public void Query_PowerAndStat_ForUnitIdWithoutReservedSegments_ResolveByExplicitUnitId(string unitIdText)
        {
            var world = new UiWorldFixture();
            var unitId = new Id(unitIdText);
            world.PowerHost.RegisterUnit(unitId, new[] { Health });
            world.PowerHost.SetForTest(unitId, Health, 30, 90);
            world.StatHost.RegisterUnit(unitId);
            world.StatHost.SetBase(unitId, Strength, 12);

            Assert.Equal(30, world.DataSource.Query($"unit.{unitIdText}.power.{Health}.current")!.Value.AsNumber);
            Assert.Equal(90, world.DataSource.Query($"unit.{unitIdText}.power.{Health}.max")!.Value.AsNumber);
            Assert.Equal(12, world.DataSource.Query($"unit.{unitIdText}.stat.{Strength}")!.Value.AsNumber);
            Assert.Empty(world.Diagnostics.Warnings);
        }

        [Fact]
        public void Query_UnregisteredUnit_ReturnsNull_WithoutWarning()
        {
            var world = new UiWorldFixture();

            Assert.Null(world.DataSource.Query($"unit.unit.nobody.power.{Health}.current"));
            Assert.Null(world.DataSource.Query($"unit.unit.nobody.stat.{Strength}"));
            Assert.Empty(world.Diagnostics.Warnings);
        }

        // ---- D16：含字面 power/stat 段的 unitId 的现行为（设计决定，见 ADR-0125） ----

        /// <summary>unitId 以 "power" 段开头或切出的片段不足两段（不是合法 Id）：返回 null 并记一条诊断。</summary>
        [Theory]
        [InlineData("squad.power.lead")]     // 切出 "squad"（单段，非法 Id）
        [InlineData("squad.stat.lead")]
        public void Query_UnitIdWithReservedSegment_SplitsAtFirstKeyword_SingleSegmentPrefixIsRejectedWithWarning(string unitIdText)
        {
            var world = new UiWorldFixture();
            var unitId = new Id(unitIdText);
            world.PowerHost.RegisterUnit(unitId, new[] { Health });
            world.PowerHost.SetForTest(unitId, Health, 5, 50);
            world.StatHost.RegisterUnit(unitId);
            world.StatHost.SetBase(unitId, Strength, 7);

            Assert.Null(world.DataSource.Query($"unit.{unitIdText}.power.{Health}.current"));
            Assert.Null(world.DataSource.Query($"unit.{unitIdText}.stat.{Strength}"));

            Assert.Equal(2, world.Diagnostics.Warnings.Count);
            Assert.All(world.Diagnostics.Warnings, w => Assert.Contains("单位 id 片段不是合法 Id", w));
        }

        /// <summary>unitId 首段就是关键字：找不到"关键字之前的单位 id"，返回 null 并记诊断。</summary>
        [Fact]
        public void Query_UnitIdStartingWithReservedSegment_ReturnsNullWithWarning()
        {
            var world = new UiWorldFixture();
            var unitId = new Id("power.boss");
            world.PowerHost.RegisterUnit(unitId, new[] { Health });
            world.PowerHost.SetForTest(unitId, Health, 5, 50);

            Assert.Null(world.DataSource.Query($"unit.power.boss.power.{Health}.current"));

            var warning = Assert.Single(world.Diagnostics.Warnings);
            Assert.Contains("找不到 unit.<id>.power|stat 的关键字分界", warning);
        }

        /// <summary>最危险的形态：关键字之前恰好是另一个合法 Id（真实单位 <c>squad.alpha.power.lead</c> 被切成
        /// <c>squad.alpha</c> + 残片）——不抛、不记诊断，静默查的是另一个单位（这里该"另一个单位"存在但
        /// 没有残片对应的资源类型，于是得到 null）。真实单位的数值<b>不会</b>被读到。这是命名约束存在的原因。</summary>
        [Fact]
        public void Query_UnitIdWithReservedSegmentAfterValidPrefix_QueriesThePrefixUnitInstead()
        {
            var world = new UiWorldFixture();
            var realUnit = new Id("squad.alpha.power.lead");
            var prefixUnit = new Id("squad.alpha");
            world.PowerHost.RegisterUnit(realUnit, new[] { Health });
            world.PowerHost.SetForTest(realUnit, Health, 42, 100);
            world.PowerHost.RegisterUnit(prefixUnit, new[] { Health });
            world.PowerHost.SetForTest(prefixUnit, Health, 1, 2);

            var result = world.DataSource.Query($"unit.{realUnit}.power.{Health}.current");

            // 被解释为 unitId=squad.alpha、powerType=lead.power.arch.power.health（未登记）-> null；
            // 无论如何都不会返回真实单位的 42。
            Assert.Null(result);
            Assert.Empty(world.Diagnostics.Warnings);
        }

        // ---- 其它错误路径 ----

        [Fact]
        public void Query_PathWithoutPowerOrStatKeyword_WarnsAndReturnsNull()
        {
            var world = new UiWorldFixture();

            Assert.Null(world.DataSource.Query("unit.unit.inst_3.health"));

            var warning = Assert.Single(world.Diagnostics.Warnings);
            Assert.Contains("关键字分界", warning);
        }

        [Fact]
        public void Query_IndexedUnitIdSegment_IsRejectedAsInvalidId()
        {
            var world = new UiWorldFixture();

            Assert.Null(world.DataSource.Query($"unit.unit[0].inst_3.power.{Health}.current"));

            var warning = Assert.Single(world.Diagnostics.Warnings);
            Assert.Contains("单位 id 片段不是合法 Id", warning);
        }

        [Fact]
        public void Constructor_NullArguments_Throw()
        {
            var world = new UiWorldFixture();

            Assert.Equal("statHost", Assert.Throws<System.ArgumentNullException>(
                () => new UnitPathProvider(null!, world.PowerHost)).ParamName);
            Assert.Equal("powerHost", Assert.Throws<System.ArgumentNullException>(
                () => new UnitPathProvider(world.StatHost, null!)).ParamName);
        }
    }
}
