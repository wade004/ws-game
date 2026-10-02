using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Rules.Common;
using Xunit;
using static Tests.Rules.Skill.SpatialRig;
using static Tests.Rules.Skill.TimelineHarness;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 动作式（带 timeline）结算的射程门（<c>SkillOptions.SpatialRangeHitWindow</c>，手感落地 M4-W1b，ADR-0130 追加决定）：
    /// 缺省只由命中窗口几何决定谁被命中（与 1.95.0 一致）；打开后 <c>range &gt; 0</c> 的技能在命中窗口每次结算时额外要求
    /// "施法者当时位置到目标的距离不超过 range"，距离口径同 <c>SpatialRange</c>（开则三维，关则平面）。
    /// 复现：同一场景开关从关到开，被命中的集合从 X 变到 Y（期望由距离公式算出）；不变量：选项关闭、range = 0 都与门不存在时一致；
    /// 空间命中与 hit_mode=instant 两条结算路径都受同一道门。
    /// </summary>
    public sealed class SpatialRangeHitWindowTests
    {
        private static readonly Id Near = new Id("unit.rhw_near");
        private static readonly Id Far = new Id("unit.rhw_far");
        private static readonly Id High = new Id("unit.rhw_high");

        private static SpatialRig Build(bool gate, bool spatial, double range, string? hitMode = null)
        {
            var rig = Create(
                new[] { SpSkill("skill.sample_cone", 50, 200, 100, new[] { HitAt(100) }, hitMode: hitMode, range: range) },
                Shape.Cone(Vec2.Zero, 0, Math.PI, 10.0),
                configure: b =>
                {
                    b.Options.SpatialRangeHitWindow = gate;
                    b.Options.SpatialRange = spatial;
                });
            rig.Units.SetPosition(Foe, new Vec2(1.0, 0.0)); // 缺省靶子当"近"
            rig.H.World.AddUnit(Far, new Vec2(4.0, 0.0));
            rig.H.World.AddUnit(High, new Vec2(1.0, 0.0));
            rig.Units.SetHeight(High, 2.0);
            return rig;
        }

        private static string[] Hit(SpatialRig rig)
        {
            rig.H.CastInTick("skill.sample_cone");
            rig.RunToEnd();
            return rig.Hits.Select(e => e.TargetId.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        }

        [Fact]
        public void Gate_ExcludesTargetsBeyondTheRange_OnlyWhenTheOptionIsOn()
        {
            // 范围 2：Foe 距 1、High 平面距 1（高 2）、Far 距 4。
            var off = Hit(Build(gate: false, spatial: false, range: 2));
            Assert.Equal(new[] { Far.Value, Foe.Value, High.Value }.OrderBy(v => v, StringComparer.Ordinal), off);

            var planar = Hit(Build(gate: true, spatial: false, range: 2));
            Assert.Equal(new[] { Foe.Value, High.Value }.OrderBy(v => v, StringComparer.Ordinal), planar); // 平面口径：Far 被排除，High 保留
        }

        [Fact]
        public void Gate_UsesTheThreeDimensionalDistance_WhenSpatialRangeIsOn()
        {
            // High 的三维距离 sqrt(1 + 4) = 2.236 > 2：开三维口径时被排除；Foe（1）保留。
            var spatial = Hit(Build(gate: true, spatial: true, range: 2));
            Assert.Equal(new[] { Foe.Value }, spatial);
        }

        [Fact]
        public void Gate_RangeZeroMeansUnlimited_AndTheOptionOffIsIdenticalToNoGate()
        {
            var unlimited = Hit(Build(gate: true, spatial: true, range: 0));
            var none = Hit(Build(gate: false, spatial: true, range: 0));
            Assert.Equal(none, unlimited);
            Assert.Equal(3, unlimited.Length);
        }

        [Fact]
        public void Gate_AlsoAppliesToInstantSettlementOfATimelineSkill()
        {
            var off = Hit(Build(gate: false, spatial: false, range: 2, hitMode: "instant"));
            var on = Hit(Build(gate: true, spatial: false, range: 2, hitMode: "instant"));
            Assert.True(off.Length > on.Length);
            Assert.DoesNotContain(Far.Value, on);
            Assert.Contains(Foe.Value, on);
        }
    }
}
