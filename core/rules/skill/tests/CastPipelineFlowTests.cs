using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>施法管线正常流程用例（见落地方案 T2-4 行"冷却结束可再放（含分类冷却、充能恢复）""
    /// 多技能同时就绪可同放""法术队列窗口内入队并顺序执行""skill.cast_start/success/failed 字段"）。</summary>
    public sealed class CastPipelineFlowTests
    {
        private static Core.Foundation.Common.Json.JsonObject InstantBolt(
            string id, string? cooldownCategory = null, double cooldownDuration = 0)
        {
            var fields = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("cooldown_duration", J.N(cooldownDuration)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(7)), ("coefficient", J.N(0))))))),
            };

            if (cooldownCategory != null)
            {
                fields.Add(("cooldown_category", J.S(cooldownCategory)));
            }

            return J.O(fields.ToArray());
        }

        [Fact]
        public void InstantCast_Succeeds_AndEmitsStartSuccessFields()
        {
            var world = new SkillWorldBuilder().SkillDef(InstantBolt("skill.sample_bolt")).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);

            var start = world.Of<SkillCastStartEvent>().Single();
            Assert.Equal(new Id("unit.caster"), start.CasterId);
            Assert.Equal(new Id("skill.sample_bolt"), start.SkillId);
            Assert.Equal(0, start.CastTime);

            var success = world.Of<SkillCastSuccessEvent>().Single();
            Assert.Equal(new Id("unit.caster"), success.CasterId);
            Assert.Equal(new Id("skill.sample_bolt"), success.SkillId);
            Assert.Equal(new[] { new Id("unit.target") }, success.Targets);

            // N19（外部审计 68c9bed，P2）：瞬发——cast_start 与 cast_success 在同一次 Flush 内
            // 背靠背发出（本用例本身就是这条路径的复现场景），success 事件应携带 IsInstant=true，
            // 供只订阅这两个事件的表现层消费方区分"这是瞬发"而不是"读条刚好碰巧同帧完成"。
            Assert.True(success.IsInstant);
            Assert.Equal(0.0, success.CastTimeSeconds);

            Assert.Single(world.Combat.ResolveCalls);
        }

        [Fact]
        public void CastFailed_EmitsFieldsWithReasonCode()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(new Id("unit.caster"));

            world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_missing"), System.Array.Empty<Id>());
            world.Flush();

            var failed = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(new Id("unit.caster"), failed.CasterId);
            Assert.Equal(new Id("skill.sample_missing"), failed.SkillId);
            Assert.Equal(CastFailureReason.UnknownSkill, failed.ReasonCode);
        }

        [Fact]
        public void CategoryCooldown_BlocksSiblingSkill_UntilElapsed()
        {
            var a = InstantBolt("skill.sample_a", cooldownCategory: "skill.category.sample", cooldownDuration: 3);
            var b = InstantBolt("skill.sample_b", cooldownCategory: "skill.category.sample", cooldownDuration: 3);

            var world = new SkillWorldBuilder().SkillDef(a).SkillDef(b).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var first = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_a"), System.Array.Empty<Id>());
            Assert.True(first.Success);

            var second = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_b"), System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.OnCooldown, second.Reason);

            world.Host.Update(3.0);

            var third = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_b"), System.Array.Empty<Id>());
            Assert.True(third.Success);
        }

        [Fact]
        public void Charges_RecoverAfterRechargeTime()
        {
            var charges = J.O(("max", J.N(1)), ("recharge_time", J.N(4)));
            var withCharges = J.O(
                ("id", J.S("skill.sample_chargeable")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("charges", charges),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var world = new SkillWorldBuilder().SkillDef(withCharges).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            Assert.True(world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_chargeable"), System.Array.Empty<Id>()).Success);
            Assert.False(world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_chargeable"), System.Array.Empty<Id>()).Success);

            world.Host.Update(4.0);

            Assert.True(world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_chargeable"), System.Array.Empty<Id>()).Success);
        }

        [Fact]
        public void TwoInstantSkills_BothSucceed_InSameUpdateWindow()
        {
            var a = InstantBolt("skill.sample_a");
            var b = InstantBolt("skill.sample_b");
            var world = new SkillWorldBuilder().SkillDef(a).SkillDef(b).Build();
            world.AddUnit(new Id("unit.caster"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.caster"));

            var resultA = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_a"), System.Array.Empty<Id>());
            var resultB = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_b"), System.Array.Empty<Id>());
            world.Host.Update(0.016);

            Assert.True(resultA.Success);
            Assert.True(resultB.Success);
        }

        [Fact]
        public void QueueWindow_QueuesNextCast_AndExecutesAfterCurrentFinishes()
        {
            var channel = J.O(
                ("id", J.S("skill.sample_cast_slow")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(1.0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var builder = new SkillWorldBuilder();
            builder.Options.QueueWindow = 0.3;
            var world = builder.SkillDef(channel).SkillDef(InstantBolt("skill.sample_bolt")).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var start = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_cast_slow"), System.Array.Empty<Id>());
            Assert.True(start.Success);

            // 推进到剩余 0.2（<= 0.3 队列窗口）。
            world.Host.Update(0.8);
            Assert.True(world.Host.IsCasting(new Id("unit.caster")));

            var queued = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(queued.Success);

            // 完成当前读条：cast_slow 结束后应立即顺序执行排队的瞬发技能。
            world.Host.Update(0.2);
            world.Flush();

            var successes = world.Of<SkillCastSuccessEvent>().ToList();
            Assert.Equal(2, successes.Count);
            Assert.Equal(new Id("skill.sample_cast_slow"), successes[0].SkillId);
            Assert.Equal(new Id("skill.sample_bolt"), successes[1].SkillId);
            Assert.False(world.Host.IsCasting(new Id("unit.caster")));

            // N19（外部审计 68c9bed，P2）：非瞬发（真正读条完成，经 FinishCast 收尾）——
            // IsInstant=false，CastTimeSeconds 等于本次开始时 skill.cast_start 携带的 cast_time
            // （1.0）；紧接着排队触发的瞬发技能仍然是 IsInstant=true、CastTimeSeconds=0，两者在
            // 同一次 Flush 里互不干扰。
            Assert.False(successes[0].IsInstant);
            Assert.Equal(1.0, successes[0].CastTimeSeconds);
            Assert.True(successes[1].IsInstant);
            Assert.Equal(0.0, successes[1].CastTimeSeconds);
        }

        [Fact]
        public void Busy_Fails_WhenCastingAgain_OutsideQueueWindow()
        {
            // 契约缺口已补齐：施法者正在读条中、且剩余读条时间超出法术队列窗口时，再次
            // CastSkill 应失败并携带 CastFailureReason.Busy（不是 OnCooldown——施法者并非真的
            // 处于技能冷却中，只是读条占用中，见 CastFailureReason.Busy 注释、CastPipeline.cs
            // 该分支改动前的判断记录）。
            var channel = J.O(
                ("id", J.S("skill.sample_cast_slow")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(1.0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var builder = new SkillWorldBuilder();
            builder.Options.QueueWindow = 0.3;
            var world = builder.SkillDef(channel).SkillDef(InstantBolt("skill.sample_bolt")).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var start = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_cast_slow"), System.Array.Empty<Id>());
            Assert.True(start.Success);

            // 尚未推进任何时间：剩余读条时间 = 1.0，远大于队列窗口 0.3。
            var busy = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(busy.Success);
            Assert.Equal(CastFailureReason.Busy, busy.Reason);
            Assert.True(world.Host.IsCasting(new Id("unit.caster")));
        }
    }
}
