using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>周期效果/吸收/免疫/控制标志/驱散（见落地方案 T2-5 行、06 第 3.3 节）。</summary>
    public sealed class AuraEffectTests
    {
        [Fact]
        public void PeriodicDamage_TicksExpectedNumberOfTimes()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_dot")),
                ("duration", J.N(6)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(2)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_sample"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_dot"), new Id("unit.source"));

            world.Host.Update(2.0);
            world.Host.Update(2.0);
            world.Host.Update(2.0);

            Assert.Equal(3, world.Combat.ResolveCalls.Count);
            Assert.All(world.Combat.ResolveCalls, ctx => Assert.Equal(EffectKind.SchoolDamage, ctx.Kind));
            Assert.All(world.Combat.ResolveCalls, ctx => Assert.True(ctx.IsPeriodic));
        }

        [Fact]
        public void Absorb_RemovesInstance_WhenPoolDepleted()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_shield")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("absorb")),
                        ("params", J.O(("amount", J.N(20)), ("school", J.S("skill.school_sample"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_shield"), new Id("unit.source"));

            var consumed1 = world.Host.AuraQuery.ConsumeAbsorb(new Id("unit.target"), new Id("skill.school_sample"), 12);
            Assert.Equal(12, consumed1);
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_shield")));

            var consumed2 = world.Host.AuraQuery.ConsumeAbsorb(new Id("unit.target"), new Id("skill.school_sample"), 12);
            Assert.Equal(8, consumed2); // 池只剩 8，无法满足全部 12
            world.Flush();

            Assert.False(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_shield")));
            var removed = world.Of<AuraRemovedEvent>();
            Assert.Contains(removed, e => e.Reason == "absorb_depleted");
        }

        [Fact]
        public void Immunity_BlocksEffect_AndSkipsCombatCall()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_immune")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("immunity")),
                        ("params", J.O(
                            ("schools", J.Ids("skill.school_sample")),
                            ("effect_kinds", J.A(J.S("school_damage")))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_immune"), new Id("unit.source"));

            Assert.True(world.Host.AuraQuery.IsImmune(new Id("unit.target"), new Id("skill.school_sample"), EffectKind.SchoolDamage));

            var context = new EffectContext(
                new Id("unit.source"), new Id("unit.target"), new Id("skill.sample_bolt"), EffectKind.SchoolDamage,
                new Id("skill.school_sample"), 10, 0);

            var result = world.Host.EffectSink.ApplyEffect(context);

            Assert.True(result.Immune);
            Assert.Empty(world.Combat.ResolveCalls);
        }

        [Fact]
        public void ControlFlags_AggregateAcrossActiveAuras()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_root")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("control")),
                        ("params", J.O(("flags", J.A(J.S("no_move")))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            Assert.Equal(ControlFlags.None, world.Host.AuraQuery.GetControlFlags(new Id("unit.target")));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_root"), new Id("unit.source"));

            Assert.Equal(ControlFlags.NoMove, world.Host.AuraQuery.GetControlFlags(new Id("unit.target")));
        }

        [Fact]
        public void Dispel_RemovesUpToCount_OfMatchingDispelType()
        {
            var poison1 = J.O(("id", J.S("skill.aura_def.sample_poison1")), ("duration", J.N(30)),
                ("dispel_type", J.S("skill.dispel.poison")), ("effects", J.A()));
            var poison2 = J.O(("id", J.S("skill.aura_def.sample_poison2")), ("duration", J.N(30)),
                ("dispel_type", J.S("skill.dispel.poison")), ("effects", J.A()));
            var curse = J.O(("id", J.S("skill.aura_def.sample_curse")), ("duration", J.N(30)),
                ("dispel_type", J.S("skill.dispel.curse")), ("effects", J.A()));

            var world = new SkillWorldBuilder().AuraDef(poison1).AuraDef(poison2).AuraDef(curse).Build();
            world.AddUnit(new Id("unit.target"));

            var sink = world.Host.EffectSink;
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_poison1"), new Id("unit.source"));
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_poison2"), new Id("unit.source"));
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_curse"), new Id("unit.source"));

            var context = new EffectContext(
                new Id("unit.dispeller"), new Id("unit.target"), new Id("skill.sample_cleanse"), EffectKind.Dispel,
                new Id("skill.school_sample"), 0, 0,
                J.O(("category", J.S("skill.dispel.poison")), ("count", J.N(1))));

            sink.ApplyEffect(context);

            Assert.False(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_poison1")));
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_poison2")));
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_curse")));
        }
    }
}
