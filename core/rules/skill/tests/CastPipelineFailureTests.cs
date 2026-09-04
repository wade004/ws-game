using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>施法管线九步固定顺序（06 第 3.6 节）各失败原因码的最小复现用例（见落地方案 T2-4 行
    /// "九步各失败码至少一例"）。</summary>
    public sealed class CastPipelineFailureTests
    {

        private static Core.Foundation.Common.Json.JsonObject InstantDamageSkill(
            string id = "skill.sample_bolt",
            double range = 30,
            bool respectsGcd = true,
            string? cooldownCategory = null,
            double cooldownDuration = 0,
            Core.Foundation.Common.Json.JsonValue? charges = null)
        {
            var fields = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(range)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(respectsGcd)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(10)), ("coefficient", J.N(0))))))),
            };

            if (cooldownCategory != null) fields.Add(("cooldown_category", J.S(cooldownCategory)));
            if (cooldownDuration != 0) fields.Add(("cooldown_duration", J.N(cooldownDuration)));
            if (charges != null) fields.Add(("charges", charges));

            return J.O(fields.ToArray());
        }

        [Fact]
        public void UnknownSkill_Fails()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_missing"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.UnknownSkill, result.Reason);
        }

        [Fact]
        public void PassiveSkill_CannotBeCast()
        {
            var passive = J.O(
                ("id", J.S("skill.sample_passive")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("passive")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var world = new SkillWorldBuilder().SkillDef(passive).Build();
            world.AddUnit(new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_passive"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.PassiveSkill, result.Reason);
        }

        [Fact]
        public void DeadCaster_Fails()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).Build();
            world.AddUnit(new Id("unit.caster"), alive: false);

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.Dead, result.Reason);
        }

        [Fact]
        public void FullyIncapacitated_ReturnsStunned()
        {
            var control = J.O(
                ("id", J.S("skill.aura_def.sample_stun")),
                ("effects", J.A(
                    J.O(("kind", J.S("control")),
                        ("params", J.O(("flags", J.A(
                            J.S("no_move"), J.S("no_cast"), J.S("no_attack")))))))));

            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).AuraDef(control).Build();
            world.AddUnit(new Id("unit.caster"));

            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_stun"), new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.Stunned, result.Reason);
        }

        [Fact]
        public void SilencedOnly_ReturnsSilenced()
        {
            var silence = J.O(
                ("id", J.S("skill.aura_def.sample_silence")),
                ("effects", J.A(
                    J.O(("kind", J.S("control")),
                        ("params", J.O(("flags", J.A(J.S("no_cast")))))))));

            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).AuraDef(silence).Build();
            world.AddUnit(new Id("unit.caster"));

            world.Host.EffectSink.ApplyAura(new Id("unit.caster"), new Id("skill.aura_def.sample_silence"), new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.Silenced, result.Reason);
        }

        [Fact]
        public void OnCooldown_Fails()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill(cooldownDuration: 5)).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var first = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(first.Success);

            var second = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.OnCooldown, second.Reason);
        }

        [Fact]
        public void NoCharges_Fails()
        {
            var charges = J.O(("max", J.N(1)), ("recharge_time", J.N(10)));
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill(charges: charges)).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var first = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(first.Success);

            var second = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.NoCharges, second.Reason);
        }

        [Fact]
        public void GcdActive_FailsWhenEnabled()
        {
            var builder = new SkillWorldBuilder();
            builder.Options.GcdEnabled = true;
            builder.Options.GcdDuration = 1.5;
            var world = builder.SkillDef(InstantDamageSkill(cooldownDuration: 0)).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var first = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(first.Success);

            // 立即再次施放：技能自身无冷却，但公共冷却应挡下。
            var second = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.GcdActive, second.Reason);
        }

        [Fact]
        public void Gcd_AlwaysPassesWhenDisabled()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill(cooldownDuration: 0)).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var first = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            var second = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.True(first.Success);
            Assert.True(second.Success);
        }

        [Fact]
        public void InsufficientPower_Fails()
        {
            var withCost = J.O(
                ("id", J.S("skill.sample_costly")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("cost", J.A(J.O(("power_type", J.S("arch.power.sample_mana")), ("amount", J.N(50))))),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var builder = new SkillWorldBuilder().SkillDef(withCost).Power("arch.power.sample_mana", 10);
            var world = builder.Build();
            world.AddUnit(new Id("unit.caster"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_costly"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.InsufficientPower, result.Reason);
        }

        [Fact]
        public void NoValidTarget_Fails()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill()).Build();
            world.AddUnit(new Id("unit.caster"));
            // 链未登记任何目标 -> Resolve 返回空。

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.NoValidTarget, result.Reason);
        }

        [Fact]
        public void OutOfRange_Fails()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantDamageSkill(range: 5)).Build();
            world.AddUnit(new Id("unit.caster"), new Vec2(0, 0));
            world.AddUnit(new Id("unit.target"), new Vec2(100, 0));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.OutOfRange, result.Reason);
        }

        [Fact]
        public void SchoolLocked_BlocksSameSchoolAfterInterrupt()
        {
            // 用一个带读条的技能制造"打断 + 学派锁定"的场景。
            var channelSkill = J.O(
                ("id", J.S("skill.sample_channel_lockable")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(3)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var world2 = new SkillWorldBuilder().SkillDef(channelSkill).SkillDef(InstantDamageSkill()).Build();
            world2.AddUnit(new Id("unit.caster"));
            world2.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var start = world2.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_channel_lockable"), System.Array.Empty<Id>());
            Assert.True(start.Success);
            Assert.True(world2.Host.IsCasting(new Id("unit.caster")));

            world2.Host.Interrupt(new Id("unit.caster"), new Id("unit.interrupter"), new Id("skill.school_sample"), 5);
            Assert.False(world2.Host.IsCasting(new Id("unit.caster")));

            var recast = world2.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(recast.Success);
            Assert.Equal(CastFailureReason.SchoolLocked, recast.Reason);
        }
    }
}
