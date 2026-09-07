using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// "相邻缺口"根治（第五轮外部审核 audit-5e779c6-20260907，WD 报告"未修复但已如实记录的相邻
    /// 缺口"第 2 条）：<see cref="TimeModelCastTimeRescaleTests"/>（WD B1/B2）只换算了施放/施加
    /// <b>当下</b>的 <c>cooldown_duration</c>/<c>charges.recharge_time</c>/<c>duration</c>/
    /// <c>interval</c>——本文件覆盖 WD 报告明确记录为"未纳入范围"的两处相邻缺口：
    /// <list type="number">
    /// <item><see cref="Core.Rules.Skill.CastPipeline"/> 自身消费的 <c>cast_time</c>/
    /// <c>channel_time</c>（连同引导的 <c>tick_interval</c>）——技能若是在离散战斗<b>进行中</b>才
    /// 第一次读条/引导，原始按连续模式秒数 authoring 的时长此前直接原样写入
    /// <c>CastState.Remaining</c>/<c>TickInterval</c>，被离散模式的 <c>AdvanceOne</c> 当成"轮数"
    /// 消耗，与 WD B1 描述的是同一类缺口。</item>
    /// <item><c>modify_cooldown</c> 效果原语的运行期增量 <c>delta</c>——<see
    /// cref="Core.Rules.Skill.CooldownTracker.ModifyCooldown"/> 判断记录：与
    /// <c>cooldown_duration</c> 同属 authoring 规范时间量，理应同一套系数折算。
    /// </item>
    /// </list>
    /// <c>add_charge</c> 的 <c>amount</c>（离散充能计数，不是时间量）判断记录为不纳入换算，见
    /// <see cref="Core.Rules.Skill.CooldownTracker.AddCharge"/> 注释，本文件不覆盖。
    /// </summary>
    public sealed class CastPipelineTimeModelRescaleTests
    {
        private static readonly Id Caster = new Id("unit.actr2_caster");
        private static readonly Id Target = new Id("unit.actr2_target");
        private static readonly Id ChainId = new Id("target.chain.actr2_sample");

        [Fact]
        public void CastTime_CastWhileAlreadyInDiscreteMode_ScalesRawDurationByCurrentFactor()
        {
            var skill = J.O(
                ("id", J.S("skill.actr2_slow_bolt")),
                ("school", J.S("skill.school_actr2")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(12)), // authoring：连续模式按秒计，12 秒读条。
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            // 先切到离散模式（seconds_per_turn=6，factor=1/6）——技能是在离散战斗进行中才第一次
            // 被释放，不是"连续模式下已有读条、切换时刻被换算"。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));

            var result = world.Host.CastSkill(Caster, new Id("skill.actr2_slow_bolt"), System.Array.Empty<Id>());
            Assert.True(result.Success);
            Assert.True(world.Host.IsCasting(Caster));

            // 修复前：Remaining 直接写成原始 12（当成"12 轮"）；修复后应折算成 2 轮
            // （12 秒 / 6 秒每轮），离散步用 AdvanceCastForActor 逐轮推进（同 SkillTickHandler 惯例）。
            world.Host.AdvanceCastForActor(Caster, 1.0);
            Assert.True(world.Host.IsCasting(Caster)); // 还差 1 轮，未完成。

            world.Host.AdvanceCastForActor(Caster, 1.0);
            Assert.False(world.Host.IsCasting(Caster)); // 恰好第 2 轮完成。

            world.Flush();
            var success = world.Of<SkillCastSuccessEvent>().Single();
            Assert.False(success.IsInstant);
            // CastTimeSeconds 同样应是折算后的 2（与 SkillCastStartEvent.CastTime 同一惯例，见
            // CastState.CastTimeSeconds 判断记录），不是原始未折算的 12。
            Assert.Equal(2.0, success.CastTimeSeconds, 9);
        }

        [Fact]
        public void ChannelTimeAndTickInterval_ChannelWhileAlreadyInDiscreteMode_ScaleByCurrentFactor()
        {
            var skill = J.O(
                ("id", J.S("skill.actr2_channel_dot")),
                ("school", J.S("skill.school_actr2")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("channel_time", J.N(12)), // authoring：连续模式引导 12 秒。
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(
                            ("tick_interval", J.N(6)), // authoring：连续模式每 6 秒一跳。
                            ("base_value", J.N(3)), ("coefficient", J.N(0))))))));

            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));

            var result = world.Host.CastSkill(Caster, new Id("skill.actr2_channel_dot"), System.Array.Empty<Id>());
            Assert.True(result.Success);
            Assert.True(world.Host.IsCasting(Caster));

            // 修复前：channel_time/tick_interval 仍是原始的 12/6（当成"12 轮"/"6 轮一跳"），1 轮后
            // 远未跳一次也远未结束。修复后：channel_time 折算为 2 轮，tick_interval 折算为 1 轮一跳
            // ——每轮应恰好触发一次周期效果，2 轮后引导结束。
            world.Host.AdvanceCastForActor(Caster, 1.0);
            Assert.Single(world.Combat.ResolveCalls);
            Assert.True(world.Host.IsCasting(Caster));

            world.Host.AdvanceCastForActor(Caster, 1.0);
            Assert.Equal(2, world.Combat.ResolveCalls.Count);
            Assert.False(world.Host.IsCasting(Caster));
        }

        [Fact]
        public void ModifyCooldownDelta_AppliedWhileAlreadyInDiscreteMode_ScalesRawDeltaByCurrentFactor()
        {
            var onCooldownSkill = J.O(
                ("id", J.S("skill.actr2_on_cooldown")),
                ("school", J.S("skill.school_actr2")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("cooldown_duration", J.N(12)), // authoring：连续模式 12 秒冷却。
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A()));

            var modifySkill = J.O(
                ("id", J.S("skill.actr2_modify_cd")),
                ("school", J.S("skill.school_actr2")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A(
                    J.O(("kind", J.S("modify_cooldown")),
                        // authoring：缩短 6 秒冷却（与 cooldown_duration 同一份数据、同一口径）。
                        ("params", J.O(("skill_id", J.S("skill.actr2_on_cooldown")), ("delta", J.N(-6))))))));

            var world = new SkillWorldBuilder().SkillDef(onCooldownSkill).SkillDef(modifySkill).Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(ChainId, Caster);

            // 先切到离散模式（seconds_per_turn=6，factor=1/6）。
            world.Bus.PublishImmediate(new TimeModelRescaledEvent(1.0 / 6.0));

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.actr2_on_cooldown"), System.Array.Empty<Id>()).Success);
            // StartCooldown 已按 WD B1 折算：12 秒 authoring / 6 秒每轮 = 2 轮。
            Assert.Equal(2.0, world.Host.GetCooldown(Caster, new Id("skill.actr2_on_cooldown")), 9);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.actr2_modify_cd"), System.Array.Empty<Id>()).Success);

            // 修复前：delta 原样是 -6（当成"-6 轮"），2 + (-6) = -4，夹取到 0——技能被错误地立即
            // 就绪。修复后：delta 折算为 -6 * (1/6) = -1 轮，2 + (-1) = 1 轮，仍有 1 轮冷却剩余。
            Assert.Equal(1.0, world.Host.GetCooldown(Caster, new Id("skill.actr2_on_cooldown")), 9);
        }
    }
}
