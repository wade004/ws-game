using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary>
    /// 手感落地 M5-S2a（手感设计/03 第 2.2 节、05 第 3 节）：受击半径倍数字段 <c>hurt_radius_scale</c> 与
    /// "指定动作层 + 分段覆盖"的判定型解析 <see cref="IFeelJudgingSource.ResolveJudgingWithAction"/>。
    /// </summary>
    public class HitGeometryFeelTests
    {
        [Fact]
        public void HurtRadiusScale_IsAnOptionalJudgingReactionRatioField_WithTheDocumentedRange()
        {
            Assert.True(FeelFields.Default.TryGet(FeelFieldNames.HurtRadiusScale, out var def));
            Assert.True(def.Optional);
            Assert.Equal(FeelHalf.Judging, def.Half);
            Assert.Equal(FeelGroup.Reaction, def.Meta.Group);
            Assert.Equal(FeelUnit.Ratio, def.Unit);
            Assert.Equal(0.0, def.Min);
            Assert.Equal(4.0, def.Max);
        }

        [Fact]
        public void HurtRadiusScale_IsAbsentFromTheFrameworkPresets_SoTheDefaultIsAlwaysOne()
        {
            // 框架数据不写这个字段：任何原型 × 武器组合解析出来都没有值（调用方取缺省 1），既有行为不变。
            var profiles = FrameworkProfiles();
            var resolver = new FeelResolver(profiles, profiles.Calibrations[0], 1.0 / 60.0, new FakeState().AsProviders());
            Assert.True(resolver.ResolveJudging(Unit1).GetRaw(FeelFieldNames.HurtRadiusScale).IsNone);
        }

        // ---------- ResolveJudgingWithAction ----------

        private static FeelResolver Make(FakeState state, params FeelRow[] extra) =>
            new FeelResolver(SmallProfiles(extra), SmallCal(), 1.0 / 60.0, state.AsProviders());

        [Fact]
        public void ResolveJudgingWithAction_AppliesTheActionRowThenTheOverlayRow_AndDoesNotPolluteTheUnitCache()
        {
            var action = FeelRow.Overlay(FeelTables.Action, "x.action", new[] { Mul(WeaponScale, 3), Set(AccMs, 200) });
            var overlay = FeelRow.Overlay(FeelTables.Action, "x.overlay", new[] { Set(AccMs, 300) });
            var resolver = Make(new FakeState(), action, overlay);

            var both = resolver.ResolveJudgingWithAction(Unit1, "x.action", "x.overlay");

            // 动作行乘 3、分段覆盖行把 acc_ms 设为 300（同层内后写的覆盖先写的）。
            Assert.Equal(3.0, both.GetRaw(WeaponScale).AsNumber());
            Assert.Equal(300.0, both.GetRaw(AccMs).AsNumber());
            var actionOnly = resolver.ResolveJudgingWithAction(Unit1, "x.action", null);
            Assert.Equal(200.0, actionOnly.GetRaw(AccMs).AsNumber());

            // 单位缓存不受影响（不在动作中）。
            Assert.Equal(1.0, resolver.ResolveJudging(Unit1).GetRaw(WeaponScale).AsNumber());
            Assert.Equal(100.0, resolver.ResolveJudging(Unit1).GetRaw(AccMs).AsNumber());
        }

        [Fact]
        public void ResolveJudgingWithAction_WithNoRefs_EqualsTheInActionViewOfTheUnit()
        {
            var state = new FakeState { Action = new FeelActionState(true, null) };
            var resolver = Make(state);
            var reference = resolver.ResolveJudging(Unit1);
            var view = resolver.ResolveJudgingWithAction(Unit1, null, null);
            foreach (var name in reference.Names)
            {
                Assert.Equal(reference.GetRaw(name), view.GetRaw(name));
            }
        }

        [Fact]
        public void ResolveJudgingWithAction_IsCachedPerRefPair_AndInvalidatedWithTheUnit()
        {
            var action = FeelRow.Overlay(FeelTables.Action, "x.action", new[] { Mul(WeaponScale, 2) });
            var state = new FakeState();
            var resolver = Make(state, action);

            var first = resolver.ResolveJudgingWithAction(Unit1, "x.action", null);
            Assert.Same(first, resolver.ResolveJudgingWithAction(Unit1, "x.action", null));
            Assert.Equal(2.0, first.GetRaw(WeaponScale).AsNumber());

            // 武器变化后失效重算：新视图反映新武器（失效与单位缓存同口径）。
            state.Character = "c.heavy";
            resolver.Invalidate(Unit1, "test");
            var after = resolver.ResolveJudgingWithAction(Unit1, "x.action", null);
            Assert.NotSame(first, after);

            // 另一对引用是另一份缓存。
            Assert.NotSame(after, resolver.ResolveJudgingWithAction(Unit1, null, null));
        }

        [Fact]
        public void ResolveJudgingWithAction_UnknownRow_IsReportedAsDiagnosticAndIgnored()
        {
            var resolver = Make(new FakeState());
            var view = resolver.ResolveJudgingWithAction(Unit1, "x.missing", null);
            Assert.Equal(1.0, view.GetRaw(WeaponScale).AsNumber()); // 行不存在 = 不改写任何字段
        }

        [Fact]
        public void ResolveJudgingWithAction_DefaultMember_FallsBackToTheUnitViewForSourcesThatDoNotOverrideIt()
        {
            IFeelJudgingSource plain = new PlainSource(Make(new FakeState()));
            var view = plain.ResolveJudgingWithAction(Unit1, "x.action", "x.overlay");
            Assert.Equal(1.0, view.GetRaw(WeaponScale).AsNumber());
        }

        private sealed class PlainSource : IFeelJudgingSource
        {
            private readonly FeelResolver _inner;

            public PlainSource(FeelResolver inner) => _inner = inner;

            public JudgingFeelView ResolveJudging(Id unitId) => _inner.ResolveJudging(unitId);
        }
    }
}
