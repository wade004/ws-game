using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0108（消费方反馈第五十七批"静息回复"）决策 1/2 端到端（生产装配级：真实
    /// <see cref="Core.Numbers.PowerSet.PowerHost"/> + <see cref="AuraHost"/> + <see cref="SkillHost"/>
    /// 组合，经 <see cref="SkillWorldBuilder"/> 搭建，不是对 <see cref="AuraHost"/> 单元打桩）。
    /// </summary>
    public sealed class Adr0108_RestRegenAuraTests
    {
        [Fact]
        public void ModPowerRegen_OutOfCombatMultiplier_ScalesRegenRate_OnlyOutOfCombat()
        {
            const string powerType = "arch.power.adr0108_rest_energy";
            var aura = J.O(
                ("id", J.S("skill.aura_def.adr0108_rest")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("mod_power_regen")),
                        ("params", J.O(
                            ("power_type", J.S(powerType)),
                            ("multiplier", J.N(2.0)),
                            ("scope", J.S("out_of_combat"))))))));

            var world = new SkillWorldBuilder()
                .Power(powerType, max: 1000, startFull: false, regenOutOfCombat: 6, regenInCombat: 4)
                .AuraDef(aura)
                .Build();
            var target = new Id("unit.adr0108_target");
            var control = new Id("unit.adr0108_control");
            world.AddUnit(target);
            world.AddUnit(control);
            var powerId = new Id(powerType);

            world.Host.EffectSink.ApplyAura(target, new Id("skill.aura_def.adr0108_rest"), new Id("unit.adr0108_source"));

            world.Powers.Advance(target, 2.5);
            world.Powers.Advance(control, 2.5); // 对照：未挂光环单位按基础速率推进。

            Assert.Equal(6 * 2.0 * 2.5, world.Powers.GetPower(target, powerId));
            Assert.Equal(6 * 2.5, world.Powers.GetPower(control, powerId));

            // 在战时 scope=out_of_combat 的倍率不生效，按 regen_in_combat=4 原样推进。
            world.Powers.SetInCombat(target, true);
            var before = world.Powers.GetPower(target, powerId);
            world.Powers.Advance(target, 1.0);
            Assert.Equal(before + 4 * 1.0, world.Powers.GetPower(target, powerId));
        }

        [Fact]
        public void ModPowerRegen_AuraExpiry_RevertsToBaseRate()
        {
            const string powerType = "arch.power.adr0108_rest_energy2";
            var aura = J.O(
                ("id", J.S("skill.aura_def.adr0108_rest2")),
                ("duration", J.N(2)),
                ("effects", J.A(
                    J.O(("kind", J.S("mod_power_regen")),
                        ("params", J.O(
                            ("power_type", J.S(powerType)),
                            ("multiplier", J.N(5.0))))))));

            var world = new SkillWorldBuilder()
                .Power(powerType, max: 1000, startFull: false, regenOutOfCombat: 6)
                .AuraDef(aura)
                .Build();
            var target = new Id("unit.adr0108_target2");
            world.AddUnit(target);
            var powerId = new Id(powerType);

            world.Host.EffectSink.ApplyAura(target, new Id("skill.aura_def.adr0108_rest2"), new Id("unit.adr0108_source"));
            world.Powers.Advance(target, 1.0);
            Assert.Equal(6 * 5.0, world.Powers.GetPower(target, powerId)); // 光环生效期间：倍率×5。

            world.Host.Update(2.0); // 到期，AuraHost.RemoveInstanceInternal 撤销修饰器。
            Assert.False(world.Host.AuraQuery.HasAura(target, new Id("skill.aura_def.adr0108_rest2")));

            var before = world.Powers.GetPower(target, powerId);
            world.Powers.Advance(target, 1.0);
            Assert.Equal(before + 6 * 1.0, world.Powers.GetPower(target, powerId)); // 回到基础速率。
        }

        [Fact]
        public void ModPowerRegen_RemoveAuraBeforeExpiry_RevertsToBaseRate()
        {
            const string powerType = "arch.power.adr0108_rest_energy3";
            var aura = J.O(
                ("id", J.S("skill.aura_def.adr0108_rest3")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("mod_power_regen")),
                        ("params", J.O(
                            ("power_type", J.S(powerType)),
                            ("multiplier", J.N(4.0))))))));

            var world = new SkillWorldBuilder()
                .Power(powerType, max: 1000, startFull: false, regenOutOfCombat: 6)
                .AuraDef(aura)
                .Build();
            var target = new Id("unit.adr0108_target3");
            world.AddUnit(target);
            var powerId = new Id(powerType);

            var handle = world.Host.EffectSink.ApplyAura(target, new Id("skill.aura_def.adr0108_rest3"), new Id("unit.adr0108_source"));
            world.Host.EffectSink.RemoveAura(target, handle);

            world.Powers.Advance(target, 1.0);
            Assert.Equal(6 * 1.0, world.Powers.GetPower(target, powerId));
        }

        [Fact]
        public void PeriodicEnergize_TicksExpectedAmount_WithoutTouchingCombatHost()
        {
            const string powerType = "arch.power.adr0108_periodic_energy";
            var aura = J.O(
                ("id", J.S("skill.aura_def.adr0108_periodic_energize")),
                ("duration", J.N(6)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_energize")),
                        ("params", J.O(
                            ("interval", J.N(2)),
                            ("power_type", J.S(powerType)),
                            ("amount", J.N(5))))))));

            var world = new SkillWorldBuilder()
                .Power(powerType, max: 1000, startFull: false)
                .AuraDef(aura)
                .Build();
            var target = new Id("unit.adr0108_periodic_target");
            world.AddUnit(target);
            var powerId = new Id(powerType);

            world.Host.EffectSink.ApplyAura(target, new Id("skill.aura_def.adr0108_periodic_energize"), new Id("unit.adr0108_source"));

            world.Host.Update(2.0);
            world.Host.Update(2.0);
            world.Host.Update(2.0);

            Assert.Equal(15, world.Powers.GetPower(target, powerId)); // 3 跳 × amount(5)。
            // ApplyEnergize 直接写 IPowerHost，不经 ICombatHost.ResolveEffect——这里的假 CombatHost
            // 若被调用过，ResolveCalls 就不会是空的；IsInCombat 也只在 NotifyCombatEvent 被调用时
            // 才会置真，两者均可证明本条效果原语完全绕开了战斗结算/进战路径。
            Assert.Empty(world.Combat.ResolveCalls);
            Assert.False(world.Combat.IsInCombat(target));
        }

        // -----------------------------------------------------------------
        // 回归保护：瞬时 EffectKind.Energize（非光环、非周期）同样不经 ICombatHost，不触发进战——
        // 任务书点名"消费方漏检"的既有事实，此前完全没有测试覆盖，本次一并补上。
        // -----------------------------------------------------------------

        private static JsonObject InstantEnergizeSkill(string id, string powerType, double amount) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_sample")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S("target.chain.adr0108")),
            ("effects", J.A(J.O(("kind", J.S("energize")),
                ("params", J.O(("power_type", J.S(powerType)), ("amount", J.N(amount))))))));

        [Fact]
        public void InstantEnergize_DoesNotCallCombatHost_OrEnterCombat()
        {
            const string powerType = "arch.power.adr0108_instant_energy";
            var skill = InstantEnergizeSkill("skill.sample_adr0108_energize", powerType, amount: 30);
            var world = new SkillWorldBuilder()
                .SkillDef(skill)
                .Power(powerType, max: 1000, startFull: false)
                .Build();
            var caster = new Id("unit.adr0108_caster");
            var target = new Id("unit.adr0108_instant_target");
            world.AddUnit(caster);
            world.AddUnit(target);
            world.Targets.SetChain(new Id("target.chain.adr0108"), target);

            var result = world.Host.CastSkill(caster, new Id("skill.sample_adr0108_energize"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Equal(30, world.Powers.GetPower(target, new Id(powerType)));
            Assert.Empty(world.Combat.ResolveCalls);
            Assert.False(world.Combat.IsInCombat(caster));
            Assert.False(world.Combat.IsInCombat(target));
        }
    }
}
