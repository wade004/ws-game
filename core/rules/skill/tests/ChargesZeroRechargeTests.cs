using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// R06 收口（外部审计 5e779c6，P2；见 <c>Core.Rules.Skill.CooldownTracker.StartCooldown</c>/
    /// <see cref="ChargesRechargeTimeZeroWarningRule"/> 判断记录）：<c>skill.def.charges.
    /// recharge_time &lt;= 0</c> 修复前会让充能耗尽后永久卡死——<c>StartCooldown</c> 把
    /// <c>RechargeRemaining</c> 设成 0（"没有变化"），<c>AdvanceCharges</c> 一看到
    /// <c>RechargeRemaining &lt;= 0</c> 就直接判定"没有需要推进的恢复"提前返回，<c>Current</c> 永远
    /// 不会被加回去。修复后 <c>recharge_time &lt;= 0</c> 统一按"即时恢复"处理：充能在
    /// <c>StartCooldown</c> 内当场原地补满，永不出现"用光后再也回不来"的死状态。
    /// </summary>
    public sealed class ChargesZeroRechargeTests
    {
        private static readonly Id Caster = new Id("unit.czr_caster");
        private static readonly Id Skill = new Id("skill.czr_sample");
        private static readonly Id ChainId = new Id("target.chain.czr_sample");

        private static JsonObject ChargeableSkill(int max, double rechargeTime) => J.O(
            ("id", J.S(Skill.Value)),
            ("school", J.S("skill.school_czr")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("charges", J.O(("max", J.N(max)), ("recharge_time", J.N(rechargeTime)))),
            ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A()));

        [Fact]
        public void RechargeTimeZero_SingleCharge_RemainsUsable_NeverPermanentlyLocked()
        {
            var world = new SkillWorldBuilder().SkillDef(ChargeableSkill(max: 1, rechargeTime: 0)).Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(ChainId, Caster);

            // 修复前：用掉唯一一次充能后，GetCooldown 恒为 0（RechargeRemaining 卡在 0），但
            // GetCharges 永远是 0——CastSkill 因 NoCharges 永久失败，即便反复推进任意长的时间。
            for (var i = 0; i < 5; i++)
            {
                var result = world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>());
                Assert.True(result.Success, $"第 {i + 1} 次施放应当成功——recharge_time=0 即时恢复，不应永久锁死");
            }

            world.Host.Update(100.0); // 即便推进很久也不应改变"即时恢复"下始终可用这件事。
            Assert.True(world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>()).Success);
        }

        [Fact]
        public void RechargeTimeZero_MultiCharge_AllChargesInstantlyRefilled_AfterEachCast()
        {
            var world = new SkillWorldBuilder().SkillDef(ChargeableSkill(max: 3, rechargeTime: 0)).Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(ChainId, Caster);

            // 修复前：连续施放 3 次耗尽 3 层充能后，第 4 次开始永久失败。
            for (var i = 0; i < 6; i++)
            {
                var result = world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>());
                Assert.True(result.Success, $"第 {i + 1} 次施放应当成功");
            }
        }

        [Fact]
        public void RechargeTimeZero_GetCooldown_ReportsReady_AfterCast()
        {
            var world = new SkillWorldBuilder().SkillDef(ChargeableSkill(max: 1, rechargeTime: 0)).Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(ChainId, Caster);

            world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>());

            // 即时恢复：施放完成的那一刻起就应报告"已就绪"（GetCooldown 恒为 0），不需要等待任何
            // Update 推进——GetCooldown 语义"就绪返回 0"，见 CooldownTracker.GetCooldown 判断记录。
            Assert.Equal(0, world.Host.GetCooldown(Caster, Skill));
        }

        [Fact]
        public void PositiveRechargeTime_StillBehavesAsNormalCooldown_Regression()
        {
            // 回归：本次改动不应影响 recharge_time > 0 的既有行为（见 ChargesSpellModTests 同款用例）。
            var world = new SkillWorldBuilder().SkillDef(ChargeableSkill(max: 1, rechargeTime: 4)).Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(ChainId, Caster);

            Assert.True(world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>()).Success);

            var second = world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.NoCharges, second.Reason);

            world.Host.Update(3.9);
            Assert.False(world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>()).Success);

            world.Host.Update(0.2); // 累计 4.1 秒，应已恢复。
            Assert.True(world.Host.CastSkill(Caster, Skill, System.Array.Empty<Id>()).Success);
        }

        // -----------------------------------------------------------------
        // 校验告警（见 ChargesRechargeTimeZeroWarningRule 判断记录）。
        // -----------------------------------------------------------------

        [Fact]
        public void Validation_ChargesRechargeTimeZero_ProducesWarning_NotBlocking()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(ChargeableSkill(max: 1, rechargeTime: 0))
                .ValidationRule(new ChargesRechargeTimeZeroWarningRule())
                .Validate();

            Assert.False(report.IsBlocking, "Warning 不应阻断（本用例未额外声明 Strictness=严格阻断 Warning）");
            var issue = report.Issues.Single(i => i.Check == "charges_recharge_time_zero");
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal("skill.def", issue.Table);
        }

        [Fact]
        public void Validation_ChargesRechargeTimePositive_NoWarning()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(ChargeableSkill(max: 1, rechargeTime: 4))
                .ValidationRule(new ChargesRechargeTimeZeroWarningRule())
                .Validate();

            Assert.DoesNotContain(report.Issues, i => i.Check == "charges_recharge_time_zero");
        }

        [Fact]
        public void Validation_SkillWithoutCharges_NoWarning()
        {
            var skill = J.O(
                ("id", J.S(Skill.Value)),
                ("school", J.S("skill.school_czr")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("cooldown_duration", J.N(5)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A()));

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new ChargesRechargeTimeZeroWarningRule())
                .Validate();

            Assert.DoesNotContain(report.Issues, i => i.Check == "charges_recharge_time_zero");
        }
    }
}
