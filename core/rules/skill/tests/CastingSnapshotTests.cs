using Core.Foundation.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>消费方反馈第 1 条（2026-09-21，ADR-0056）：施法条数据——<see
    /// cref="Core.Rules.Skill.SkillHost.GetCastingSkillId"/>/<see
    /// cref="Core.Rules.Skill.SkillHost.GetCastingRemaining"/>/<see
    /// cref="Core.Rules.Skill.SkillHost.GetCastingTotal"/> 三个新增只读方法随真实读条流程的取值
    /// 变化（见落地方案 ADR-0056 决策 6"验收要求真实触发读条"）。</summary>
    public sealed class CastingSnapshotTests
    {
        private static Core.Foundation.Common.Json.JsonObject SlowCast(string id, double castTime)
        {
            return J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(castTime)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));
        }

        [Fact]
        public void NotCasting_AllThreeReturnNull_SameNullConventionAsIsCasting()
        {
            var world = new SkillWorldBuilder().Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);

            Assert.False(world.Host.IsCasting(caster));
            Assert.Null(world.Host.GetCastingSkillId(caster));
            Assert.Null(world.Host.GetCastingRemaining(caster));
            Assert.Null(world.Host.GetCastingTotal(caster));
        }

        [Fact]
        public void Casting_ExposesSkillIdAndTotal_RemainingCountsDownMonotonically_ThenClearsOnCompletion()
        {
            var world = new SkillWorldBuilder().SkillDef(SlowCast("skill.sample_slow", 2.0)).Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var result = world.Host.CastSkill(caster, new Id("skill.sample_slow"), System.Array.Empty<Id>());
            Assert.True(result.Success);

            // 读条刚开始：技能 id、总时长（等于技能声明的 cast_time，按规则计算得出，不写死裸数——
            // 本用例把它取自同一份传给 SlowCast 的 castTime 参数）立即可见，剩余时长等于总时长。
            Assert.Equal(new Id("skill.sample_slow"), world.Host.GetCastingSkillId(caster));
            Assert.Equal(2.0, world.Host.GetCastingTotal(caster));
            var remainingAtStart = world.Host.GetCastingRemaining(caster);
            Assert.NotNull(remainingAtStart);
            Assert.Equal(2.0, remainingAtStart!.Value);

            world.Host.Update(0.8);
            var remainingAfterTick = world.Host.GetCastingRemaining(caster);
            Assert.NotNull(remainingAfterTick);
            Assert.True(remainingAfterTick!.Value < remainingAtStart.Value, "剩余时长应随时间推进单调递减");
            Assert.Equal(2.0 - 0.8, remainingAfterTick.Value, 5);
            // 读条中途：技能 id/总时长不受推进影响。
            Assert.Equal(new Id("skill.sample_slow"), world.Host.GetCastingSkillId(caster));
            Assert.Equal(2.0, world.Host.GetCastingTotal(caster));

            world.Host.Update(1.2);
            world.Flush();

            // 读条完成：恢复"无人读条"的既有口径（同 IsCasting 恢复 false 同一时刻）。
            Assert.False(world.Host.IsCasting(caster));
            Assert.Null(world.Host.GetCastingSkillId(caster));
            Assert.Null(world.Host.GetCastingRemaining(caster));
            Assert.Null(world.Host.GetCastingTotal(caster));
        }
    }
}
