using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;
using static Tests.Rules.Skill.SpatialRig;
using static Tests.Rules.Skill.TimelineHarness;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 蓄力效果值倍率（手感设计/01 第 3.3 节，M4 清扫）：<c>charge.value_scale</c> 按蓄力比例线性插值，乘在伤害/治疗类效果值上
    /// （直接命中与投射物命中后效果同口径）。期望值由规则算出：蓄力比例 = (按住 ms − min_ms)/(max_ms − min_ms) 夹到 0～1，
    /// 倍率 = min + (max − min) × 比例；基准值取同一技能不蓄力（比例 0、倍率 min）时结算出的值。
    /// </summary>
    public sealed class ChargeValueScaleTests
    {
        private const double MinMs = 100;
        private const double MaxMs = 500;
        private const double ScaleMin = 1.0;
        private const double ScaleMax = 3.0;

        private static JsonObject Charge(bool withScale = true) =>
            withScale
                ? J.O(("min_ms", J.N(MinMs)), ("max_ms", J.N(MaxMs)), ("value_scale", J.O(("min", J.N(ScaleMin)), ("max", J.N(ScaleMax)))))
                : J.O(("min_ms", J.N(MinMs)), ("max_ms", J.N(MaxMs)));

        private static SpatialRig Rig(bool withScale, bool projectile = false, RecordingProjectileSpawner? spawner = null)
        {
            var markers = projectile ? new[] { Marker("release", 100) } : new[] { HitAt(100) };
            var effects = projectile ? new[] { Projectile() } : new[] { Damage() };
            var rig = Create(
                new[] { SpSkill("skill.sample_charged", 50, 200, 100, markers, effects: effects, charge: Charge(withScale)) },
                Shape.Cone(Vec2.Zero, 0, Math.PI / 2, 2.0),
                configure: spawner == null ? (Action<SkillWorldBuilder>?)null : b => b.ProjectileSpawner = spawner);
            rig.Units.SetPosition(Foe, new Vec2(1, 0));
            return rig;
        }

        private static (double Ratio, double Scale) Expected(int heldTicks)
        {
            var heldMs = heldTicks * Step * 1000.0;
            var ratio = Math.Min(1.0, Math.Max(0.0, (heldMs - MinMs) / (MaxMs - MinMs)));
            return (ratio, ScaleMin + (ScaleMax - ScaleMin) * ratio);
        }

        private static double CastAndMeasureDamage(SpatialRig rig, int heldTicks)
        {
            var r = rig.H.World.Host.CastSkillWithContext(Actor, new Id("skill.sample_charged"), Array.Empty<Id>(), new ActionCastContext(null, heldTicks));
            Assert.True(r.Success, r.Reason.ToString());
            rig.RunToEnd();
            return rig.H.World.Combat.ResolveCalls.Single(c => c.Kind == EffectKind.SchoolDamage).BaseValue;
        }

        [Theory]
        [InlineData(0)] // 没有蓄力：比例 0。
        [InlineData(3)] // 按住 50 ms，低于 min：比例夹到 0。
        [InlineData(18)] // 按住 300 ms：比例落在中段。
        [InlineData(24)] // 按住 400 ms。
        [InlineData(60)] // 按住 1000 ms，超过 max：比例夹到 1。
        public void DirectHit_DamageValueFollowsTheChargeRatio(int heldTicks)
        {
            var baseline = CastAndMeasureDamage(Rig(withScale: false), heldTicks);
            var scaled = CastAndMeasureDamage(Rig(withScale: true), heldTicks);

            var (_, scale) = Expected(heldTicks);
            Assert.Equal(baseline * scale, scaled, 9);
        }

        [Fact]
        public void DirectHit_FullCharge_ReachesTheMaxScale_AndNoCharge_StaysAtTheMinScale()
        {
            var rig0 = Rig(withScale: true);
            var rig1 = Rig(withScale: true);
            var baseline = CastAndMeasureDamage(Rig(withScale: false), 0);

            Assert.Equal(baseline * ScaleMin, CastAndMeasureDamage(rig0, 0), 9);
            Assert.Equal(baseline * ScaleMax, CastAndMeasureDamage(rig1, (int)Math.Ceiling(MaxMs / (Step * 1000.0)) + 5), 9);
        }

        [Fact]
        public void WithoutValueScale_ChargeOnlyChangesTheRatio_ValuesAreIdenticalToUnchargedCast()
        {
            // 不变量：声明了 charge 但没有 value_scale 时，任何按住时长下的效果值都等于不蓄力的值（缺省不缩放）。
            var uncharged = CastAndMeasureDamage(Rig(withScale: false), 0);
            foreach (var held in new[] { 3, 18, 60 })
            {
                Assert.Equal(uncharged, CastAndMeasureDamage(Rig(withScale: false), held), 12);
            }
        }

        [Fact]
        public void ReleasedProjectile_HookCarriesTheChargeScale_ProportionalToTheRatio()
        {
            const int held = 18;
            var spawner = new RecordingProjectileSpawner();
            var rig = Rig(withScale: true, projectile: true, spawner: spawner);
            var r = rig.H.World.Host.CastSkillWithContext(Actor, new Id("skill.sample_charged"), Array.Empty<Id>(), new ActionCastContext(null, held));
            Assert.True(r.Success);
            rig.RunToEnd();

            var hook = Assert.Single(spawner.Spawns).Hook!;
            Assert.Equal(Expected(held).Scale, hook.ValueScale, 9);

            // 不蓄力：倍率 = min 端（这里 1），且不声明 value_scale 的技能恒为 1。
            var spawner0 = new RecordingProjectileSpawner();
            var rig0 = Rig(withScale: false, projectile: true, spawner: spawner0);
            rig0.H.World.Host.CastSkillWithContext(Actor, new Id("skill.sample_charged"), Array.Empty<Id>(), new ActionCastContext(null, held));
            rig0.RunToEnd();
            Assert.Equal(1.0, Assert.Single(spawner0.Spawns).Hook!.ValueScale, 9);
        }
    }
}
