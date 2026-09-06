using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// W1 收边补齐（A3 审计 #6）：<c>SpellModDimension.Charges</c> 此前定义了但从未被
    /// <see cref="CooldownTracker"/> 读取——数据层可合法登记 <c>target_dimension: charges</c> 的
    /// <c>spell_mod_def</c>，运行期却完全不起作用。本文件验证充能上限（<c>max</c>）与单次恢复时间
    /// （<c>recharge_time</c>）均经 <see cref="SpellModResolver"/> 解析该维度（判断记录：06 原文
    /// 只给出 <c>charges</c> 一个维度同时对应两个数值，未规定具体修正哪一个，本模块拍板两者都受
    /// 同一维度的 flat/pct 修正各自独立解析，见 <see cref="CooldownTracker.SpellMods"/> 判断记录）。
    /// </summary>
    public sealed class ChargesSpellModTests
    {
        private static readonly Id Caster = new Id("unit.caster");
        private static readonly Id Skill = new Id("skill.sample_chargeable");

        private static JsonObject ChargeableSkill(double max, double rechargeTime) => J.O(
            ("id", J.S(Skill.Value)),
            ("school", J.S("skill.school_sample")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("charges", J.O(("max", J.N(max)), ("recharge_time", J.N(rechargeTime)))),
            ("target_shape_ref", J.S("target.chain.sample")),
            ("effects", J.A()));

        private static JsonObject ChargesSpellMod(string id, string op, double value) => J.O(
            ("id", J.S(id)),
            ("target_dimension", J.S("charges")),
            ("op", J.S(op)),
            ("value", J.N(value)));

        private static JsonObject AuraWithSpellMod(string id, string spellModRef) => J.O(
            ("id", J.S(id)),
            ("duration", J.N(30)),
            ("effects", J.A(
                J.O(("kind", J.S("spell_mod")), ("params", J.O(("spell_mod_ref", J.S(spellModRef))))))));

        [Fact]
        public void NoModifier_UsesRawMaxAndRechargeTime()
        {
            var world = new SkillWorldBuilder().SkillDef(ChargeableSkill(max: 1, rechargeTime: 4)).Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(new Id("target.chain.sample"), Caster);

            Assert.True(world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>()).Success);

            var second = world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.NoCharges, second.Reason);

            world.Host.Update(4.0);
            Assert.True(world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>()).Success);
        }

        [Fact]
        public void FlatModifier_IncreasesEffectiveMax_AndExtendsRechargeTime()
        {
            var spellMod = ChargesSpellMod("skill.spell_mod_def.charges_flat", "flat", 1);
            var aura = AuraWithSpellMod("skill.aura_def.charges_flat", "skill.spell_mod_def.charges_flat");

            var world = new SkillWorldBuilder()
                .SkillDef(ChargeableSkill(max: 1, rechargeTime: 4))
                .SpellModDef(spellMod)
                .AuraDef(aura)
                .Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(new Id("target.chain.sample"), Caster);

            world.Host.EffectSink.ApplyAura(Caster, new Id("skill.aura_def.charges_flat"), Caster);

            // 有 +1 flat 修饰：原始 max=1 时第二次施放本应立即失败（见上一用例），本用例应连续
            // 成功两次——证明有效充能上限确实被修正到了 2。
            Assert.True(world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>()).Success);
            Assert.True(world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>()).Success);

            var third = world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>());
            Assert.False(third.Success);
            Assert.Equal(CastFailureReason.NoCharges, third.Reason);

            // 原始 recharge_time=4，+1 flat 后有效值应为 5：推进 4 秒还不够，推进满 5 秒才恢复。
            world.Host.Update(4.0);
            var stillNoCharges = world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>());
            Assert.False(stillNoCharges.Success);
            Assert.Equal(CastFailureReason.NoCharges, stillNoCharges.Reason);

            world.Host.Update(1.0); // 累计 5 秒。
            Assert.True(world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>()).Success);
        }

        [Fact]
        public void PctModifier_ScalesEffectiveMax()
        {
            // +100% pct：原始 max=2 应变为有效 4（(2+0)*(1+1.0)=4）。
            var spellMod = ChargesSpellMod("skill.spell_mod_def.charges_pct", "pct", 1.0);
            var aura = AuraWithSpellMod("skill.aura_def.charges_pct", "skill.spell_mod_def.charges_pct");

            var world = new SkillWorldBuilder()
                .SkillDef(ChargeableSkill(max: 2, rechargeTime: 100)) // 恢复时间设很长，本用例不关心恢复。
                .SpellModDef(spellMod)
                .AuraDef(aura)
                .Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(new Id("target.chain.sample"), Caster);

            world.Host.EffectSink.ApplyAura(Caster, new Id("skill.aura_def.charges_pct"), Caster);

            for (var i = 0; i < 4; i++)
            {
                var result = world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>());
                Assert.True(result.Success, $"第 {i + 1} 次施放应成功（有效上限应为 4）");
            }

            var fifth = world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>());
            Assert.False(fifth.Success);
            Assert.Equal(CastFailureReason.NoCharges, fifth.Reason);
        }
    }
}
