using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 三维施法射程（<see cref="Core.Rules.Skill.SkillOptions.SpatialRange"/>，ADR-0130 追加决定）：缺省射程只比平面距离（1.95.0 行为），
    /// 打开后射程检查用 <c>sqrt(平面距离² + 高度差²)</c>。复现：同一对位置/高度，开关从关到开，射程检查结果从通过变为 OutOfRange
    /// （期望值由距离公式算出）；不变量：开关关闭与高度无关；射程 0（不限）永不失败；高度差为 0 时开关两态逐位一致。
    /// </summary>
    public sealed class CastRangeSpatialTests
    {
        private static readonly Id CasterId = new Id("unit.caster");
        private static readonly Id TargetId = new Id("unit.target");

        private static Core.Foundation.Common.Json.JsonObject Skill(double range, bool ground = false) =>
            J.O(
                ("id", J.S("skill.sample_bolt")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(range)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("ground_target", J.B(ground)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(10)), ("coefficient", J.N(0))))))));

        private static SkillWorld Build(bool spatial, double range, double casterHeight, double targetHeight, double targetX = 3.0, bool ground = false)
        {
            var builder = new SkillWorldBuilder().SkillDef(Skill(range, ground));
            builder.Options.SpatialRange = spatial;
            var world = builder.Build();
            world.AddUnit(CasterId, new Vec2(0, 0));
            world.AddUnit(TargetId, new Vec2(targetX, 0));
            world.Units.SetHeight(CasterId, casterHeight);
            world.Units.SetHeight(TargetId, targetHeight);
            world.Targets.SetChain(new Id("target.chain.sample"), TargetId);
            return world;
        }

        private static CastResult Cast(SkillWorld world) =>
            world.Host.CastSkill(CasterId, new Id("skill.sample_bolt"), System.Array.Empty<Id>());

        [Fact]
        public void TargetCast_HeightDifferencePushesTheDistanceOverTheRange_OnlyWhenSpatialRangeIsOn()
        {
            // 平面 3，高度差 4.5：三维 sqrt(9 + 20.25) = 5.41 > 5。
            var planar = Cast(Build(spatial: false, range: 5, casterHeight: 0, targetHeight: 4.5));
            Assert.True(planar.Success);

            var spatial = Cast(Build(spatial: true, range: 5, casterHeight: 0, targetHeight: 4.5));
            Assert.False(spatial.Success);
            Assert.Equal(CastFailureReason.OutOfRange, spatial.Reason);
        }

        [Fact]
        public void TargetCast_ThreeDimensionalDistanceExactlyAtTheRange_Passes()
        {
            // 3-4-5：平面 3 + 高度差 4 = 5，不大于射程 5。
            Assert.True(Cast(Build(spatial: true, range: 5, casterHeight: 0, targetHeight: 4)).Success);
            Assert.False(Cast(Build(spatial: true, range: 4.999, casterHeight: 0, targetHeight: 4)).Success);
        }

        [Fact]
        public void TargetCast_HeightDifferenceIsAbsolute_CasterAboveOrTargetAbove()
        {
            Assert.False(Cast(Build(spatial: true, range: 5, casterHeight: 4.5, targetHeight: 0)).Success);
            Assert.False(Cast(Build(spatial: true, range: 5, casterHeight: 0, targetHeight: 4.5)).Success);
            Assert.True(Cast(Build(spatial: true, range: 5, casterHeight: 4.5, targetHeight: 4.5)).Success);
        }

        [Fact]
        public void SpatialRangeOff_IgnoresHeightEntirely()
        {
            foreach (var h in new[] { 0.0, 10.0, 1000.0 })
            {
                Assert.True(Cast(Build(spatial: false, range: 5, casterHeight: 0, targetHeight: h)).Success);
            }
        }

        [Fact]
        public void ZeroHeightDifference_MakesBothSwitchStatesIdentical()
        {
            foreach (var x in new[] { 3.0, 5.0, 5.001, 100.0 })
            {
                var off = Cast(Build(spatial: false, range: 5, casterHeight: 2, targetHeight: 2, targetX: x));
                var on = Cast(Build(spatial: true, range: 5, casterHeight: 2, targetHeight: 2, targetX: x));
                Assert.Equal(off.Success, on.Success);
                Assert.Equal(off.Reason, on.Reason);
            }
        }

        [Fact]
        public void UnlimitedRange_NeverFailsOnDistance_EvenWithSpatialRange()
        {
            Assert.True(Cast(Build(spatial: true, range: 0, casterHeight: 0, targetHeight: 1000)).Success);
        }

        [Fact]
        public void GroundCast_UsesTheCastersHeightAgainstTheGroundPoint()
        {
            // 地面落点高度取 0：施法者在 4.5 高，平面 3 → 三维 5.41 > 5。
            var spatial = Build(spatial: true, range: 5, casterHeight: 4.5, targetHeight: 0, ground: true);
            var failed = spatial.Host.CastSkillAtGround(CasterId, new Id("skill.sample_bolt"), new GroundCastRequest(new Vec2(3, 0)));
            Assert.False(failed.Success);
            Assert.Equal(CastFailureReason.OutOfRange, failed.Reason);

            var planar = Build(spatial: false, range: 5, casterHeight: 4.5, targetHeight: 0, ground: true);
            var ok = planar.Host.CastSkillAtGround(CasterId, new Id("skill.sample_bolt"), new GroundCastRequest(new Vec2(3, 0)));
            Assert.NotEqual(CastFailureReason.OutOfRange, ok.Reason);
        }
    }
}
