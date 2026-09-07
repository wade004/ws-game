using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// "相邻缺口"根治（第五轮外部审核 audit-5e779c6-20260907 AUDIT_REPORT.md，WA 报告"需要说明的
    /// 取舍"第 1/2 条）：<see cref="TimeModelRescaleTests"/>（R05）只验证了"切换那一刻，已经存在的
    /// 冷却/充能/光环状态跟着换算"——本文件验证的是另一条此前完全没有测试覆盖的路径：<b>施放/施加
    /// 当下</b>，如果模型早已经处于离散模式（不是"连续模式下已有状态、切换时刻被换算"），
    /// <c>CooldownTracker.StartCooldown</c>/<c>AuraHost.ApplyAura</c>/<c>AuraHost.Update</c> 从
    /// <c>SkillDef</c>/<c>AuraDef</c> 原始数据（authoring 时按连续模式秒数）读到的
    /// <c>cooldown_duration</c>/<c>charges.recharge_time</c>/<c>duration</c>/<c>interval</c> 是否会
    /// 被正确折算成当前模式的计时单位，而不是被当前模式的 tick 单位（离散模式下是"轮"）直接重新
    /// 解读原始数值。全部用例都先 <c>PublishImmediate(new TimeModelRescaledEvent(...))</c> 模拟
    /// "已经处于离散模式"这个前提（同 <see cref="TimeModelRescaleTests"/> 的直接在 <c>core/rules/skill</c>
    /// 这一层验证接收端惯例，不依赖 <c>core/gameplay/assembly.TimeModelSwitch</c>），再在这个前提下
    /// 才第一次施放/施加，与 R05 覆盖的"先施放/施加、再切换"顺序相反。
    /// </summary>
    public sealed class TimeModelCastTimeRescaleTests
    {
        private static readonly Id Caster = new Id("unit.actr_caster");
        private static readonly Id Target = new Id("unit.actr_target");
        private static readonly Id ChainId = new Id("target.chain.actr_sample");

        [Fact]
        public void SkillCooldown_CastWhileAlreadyInDiscreteMode_ScalesRawDurationByCurrentFactor()
        {
            var skill = J.O(
                ("id", J.S("skill.actr_bolt")),
                ("school", J.S("skill.school_actr")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("cooldown_duration", J.N(12)), // authoring：连续模式按秒计，12 秒。
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            // 先切到离散模式（seconds_per_turn=6，factor=1/6）——技能是在离散战斗<b>进行中</b>才
            // 第一次被释放，不是"连续模式下已有冷却、切换时刻被换算"（那是 R05 覆盖的场景）。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));

            var result = world.Host.CastSkill(Caster, new Id("skill.actr_bolt"), System.Array.Empty<Id>());
            Assert.True(result.Success);

            // 修复前：StartCooldown 直接把原始 12（authoring 连续秒）当成当前模式单位写入，
            // GetCooldown 会返回 12（误当成"12 轮"），而不是应有的 2 轮（12 秒 / 6 秒每轮）。
            Assert.Equal(2.0, world.Host.GetCooldown(Caster, new Id("skill.actr_bolt")), 9);

            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(1.0, world.Host.GetCooldown(Caster, new Id("skill.actr_bolt")), 9);
            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(0.0, world.Host.GetCooldown(Caster, new Id("skill.actr_bolt")), 9);
        }

        [Fact]
        public void ChargeRecharge_ConsumedWhileAlreadyInDiscreteMode_ScalesRawRechargeTimeByCurrentFactor()
        {
            var charges = J.O(("max", J.N(1)), ("recharge_time", J.N(12))); // authoring：连续模式 12 秒恢复一次。
            var skill = J.O(
                ("id", J.S("skill.actr_charge")),
                ("school", J.S("skill.school_actr")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("charges", charges),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));

            world.Host.CastSkill(Caster, new Id("skill.actr_charge"), System.Array.Empty<Id>());

            // 修复前：EffectiveRechargeTime 原始值 12 直接写入 RechargeRemaining，GetCooldown（充能
            // 分支折算成恢复进度）会返回 12，而不是应有的 2 轮。
            Assert.Equal(2.0, world.Host.GetCooldown(Caster, new Id("skill.actr_charge")), 9);

            world.Host.AdvanceRoundTimers(1.0);
            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(0.0, world.Host.GetCooldown(Caster, new Id("skill.actr_charge")), 9);
        }

        [Fact]
        public void AuraDuration_AppliedWhileAlreadyInDiscreteMode_ScalesRawDurationByCurrentFactor()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.actr_buff")),
                ("duration", J.N(12)), // authoring：连续模式 12 秒。
                ("effects", J.A(
                    J.O(("kind", J.S("mod_stat")),
                        ("params", J.O(("stat", J.S("stat.actr_power")), ("op", J.S("flat")), ("value", J.N(100))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Stat("stat.actr_power").Build();
            world.AddUnit(Target);

            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));
            world.Host.EffectSink.ApplyAura(Target, new Id("skill.aura_def.actr_buff"), new Id("unit.actr_source"));

            // 修复前：Remaining 直接写成原始 12（当成"12 轮"），1 轮/2 轮后都远未到期。
            // 修复后：应折算成 2 轮，恰好在第 2 轮到期。
            world.Host.AdvanceRoundTimers(1.0);
            Assert.True(world.Host.AuraQuery.HasAura(Target, new Id("skill.aura_def.actr_buff")));

            world.Host.AdvanceRoundTimers(1.0);
            world.Flush();
            Assert.False(world.Host.AuraQuery.HasAura(Target, new Id("skill.aura_def.actr_buff")));
        }

        [Fact]
        public void PermanentAura_AppliedWhileAlreadyInDiscreteMode_RemainsUnaffected_Regression()
        {
            // 永久光环（duration 未声明）不应因为这次改动被意外赋予一个有限持续时间。
            var aura = J.O(
                ("id", J.S("skill.aura_def.actr_perm")),
                ("effects", J.A(
                    J.O(("kind", J.S("mod_stat")),
                        ("params", J.O(("stat", J.S("stat.actr_power2")), ("op", J.S("flat")), ("value", J.N(100))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Stat("stat.actr_power2").Build();
            world.AddUnit(Target);

            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));
            world.Host.EffectSink.ApplyAura(Target, new Id("skill.aura_def.actr_perm"), new Id("unit.actr_source"));

            world.Host.AdvanceRoundTimers(1000.0);
            Assert.True(world.Host.AuraQuery.HasAura(Target, new Id("skill.aura_def.actr_perm")), "永久光环不应到期");
        }

        [Fact]
        public void PeriodicInterval_AppliedWhileAlreadyInDiscreteMode_ScalesRawIntervalByCurrentFactor()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.actr_dot")),
                ("duration", J.N(600)), // authoring：足够长的连续秒数，折算后仍覆盖多轮。
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(6)), // authoring：连续模式每 6 秒一跳。
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_actr"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(Target);

            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));
            world.Host.EffectSink.ApplyAura(Target, new Id("skill.aura_def.actr_dot"), new Id("unit.actr_source"));

            // 修复前：interval 仍是原始的 6（被当成"6 轮"），1 轮后累加器 1 < 6，不会触发。
            // 修复后：interval 折算成 6 * (1/6) = 1 轮，每轮应恰好触发一次。
            world.Host.AdvanceRoundTimers(1.0);
            Assert.Single(world.Combat.ResolveCalls);

            world.Host.AdvanceRoundTimers(1.0);
            Assert.Equal(2, world.Combat.ResolveCalls.Count);
        }

        [Fact]
        public void PeriodicAccumulator_RescaledOnModelSwitch_PreservesInFlightProgressRatio()
        {
            // 覆盖 AuraHost.RescaleAll 现在同步换算 PeriodicAccumulators 这一半改动（不只是
            // Update 换算 interval）：验证"切换前已经累积的半程进度"换算后仍是"半程"，不会因为
            // interval 换算了、累加器没跟着换算而在切换瞬间产生虚假的一大截/一小截进度。
            var aura = J.O(
                ("id", J.S("skill.aura_def.actr_dot2")),
                ("duration", J.N(600)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(2)), // 连续模式：每 2 秒一跳。
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_actr"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(Target);
            world.Host.EffectSink.ApplyAura(Target, new Id("skill.aura_def.actr_dot2"), new Id("unit.actr_source"));

            // 连续模式推进 1 秒：半程进度（acc=1，interval=2，还差 1 秒才触发）。
            world.Host.Update(1.0);
            Assert.Empty(world.Combat.ResolveCalls);

            // 切到离散，seconds_per_turn=2（factor=1/2）：interval 折算为 1 轮；累加器同样按 1/2
            // 折算，半程进度应保持为"半轮"（0.5），不是被清零重来，也不是被当成"半秒"这种跨单位
            // 无意义的残留数值。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(0.5));

            // 再推进 0.5 轮：0.5（已折算的半程进度）+ 0.5 == 折算后的 interval（1 轮），应恰好
            // 触发一次，不多不少。
            world.Host.AdvanceRoundTimers(0.5);
            Assert.Single(world.Combat.ResolveCalls);
        }
    }
}
