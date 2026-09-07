using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// CR130-04（外部审计 audit-5c444f1-20260908，P2）复现与根治：<see cref="Core.Rules.Skill.CastPipeline"/>
    /// 的引导（channel）用完整 <c>dt</c> 结算周期效果——本次推进的 <c>dt</c> 可能已经超出引导剩余时间
    /// （<c>channel_time=0.5、tick_interval=1、Update(1)</c> 期望 0 次实际 1 次，见审计探针
    /// <c>AuditCoreMechanismProbeTests.CastPipeline_ChannelHalfSecond_IntervalOne_UpdateOneHasOneTick</c>，
    /// 该探针原样断言了 <c>resolve_calls=1</c> 复现 bug 本身；本文件断言修复后的正确值）。根治为
    /// <c>Min(dt, Remaining)</c> 只把"引导仍然有效"的那段时间计入周期累加器，与 <c>AuraHost</c> R07
    /// 同款处理。覆盖单区间、跨多个 interval、被打断、模式切换四种场景。
    /// </summary>
    public sealed class CR130_04_ChannelBoundaryTests
    {
        private static readonly Id Caster = new Id("unit.cr130_04_caster");
        private static readonly Id Target = new Id("unit.cr130_04_target");
        private static readonly Id ChainId = new Id("target.chain.cr130_04_sample");

        private static Core.Foundation.Common.Json.JsonObject ChannelSkill(string id, double channelTime, double tickInterval) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_cr130_04")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("channel_time", J.N(channelTime)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A(
                J.O(("kind", J.S("school_damage")),
                    ("params", J.O(("tick_interval", J.N(tickInterval)), ("base_value", J.N(1)), ("coefficient", J.N(0))))))));

        /// <summary>核心复现（对应审计探针，断言改为修复后的正确值）：<c>channel_time=0.5</c>、
        /// <c>tick_interval=1</c>，单次 <c>Update(1)</c>——引导在 0.5 秒时已经结束，超出的 0.5 秒不
        /// 应该被计入周期累加器，一跳都不应该发生。</summary>
        [Fact]
        public void ChannelShorterThanTickInterval_SingleUpdateSpanningEnd_ProducesNoTick()
        {
            var skill = ChannelSkill("skill.cr130_04_short_channel", 0.5, 1);
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            var skillId = new Id("skill.cr130_04_short_channel");

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            Assert.True(world.Host.IsCasting(Caster));

            world.Host.Update(1.0);

            // 修复前该断言会失败：实际 resolve_calls=1（外部审计复现）。
            Assert.Empty(world.Combat.ResolveCalls);
            Assert.False(world.Host.IsCasting(Caster));
        }

        /// <summary>跨多个 interval：<c>channel_time=3</c>、<c>tick_interval=1</c>，单次
        /// <c>Update(5)</c>——引导只在前 3 秒内有效，应恰好产生 3 次结算（<c>Min(dt,Remaining)=3</c>
        /// 累计进周期累加器），超出引导之外的 2 秒不应该再多算一跳。</summary>
        [Fact]
        public void ChannelWithMultipleIntervals_UpdateOvershootsEnd_ProducesExactlyChannelDurationTicks()
        {
            var skill = ChannelSkill("skill.cr130_04_multi_tick_channel", 3, 1);
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            var skillId = new Id("skill.cr130_04_multi_tick_channel");

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);

            world.Host.Update(5.0);

            Assert.Equal(3, world.Combat.ResolveCalls.Count);
            Assert.False(world.Host.IsCasting(Caster));
        }

        /// <summary>被打断：引导进行到一半（未到 <c>tick_interval</c>）时被打断，打断前累计的不足一
        /// 个 interval 的时间不应该产生任何结算，打断后也不应该再继续结算。</summary>
        [Fact]
        public void ChannelInterrupted_BeforeFirstTick_ProducesNoTick()
        {
            var skill = ChannelSkill("skill.cr130_04_interrupted_channel", 3, 1);
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            var skillId = new Id("skill.cr130_04_interrupted_channel");

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);

            world.Host.Update(0.5); // 未到第一个 tick_interval。
            Assert.Empty(world.Combat.ResolveCalls);

            world.Host.Interrupt(Caster, Caster, null, 0);
            Assert.False(world.Host.IsCasting(Caster));

            world.Host.Update(1.0); // 打断后不应该再有任何结算。
            Assert.Empty(world.Combat.ResolveCalls);
        }

        /// <summary>模式切换：引导中途切到离散模式（factor 折算 channel_time/tick_interval/剩余时间，
        /// 见 <see cref="Core.Rules.Skill.CastPipelineTimeModelRescaleTests"/>），切换后按离散步推进
        /// 到刚好越过引导结束点的一步，仍应恰好按折算后的边界只结算有效那部分，不多算尾跳。</summary>
        [Fact]
        public void ChannelAcrossModelSwitch_AdvanceOneOvershootsScaledEnd_DoesNotOvercountTick()
        {
            // 连续模式 authoring：channel_time=1 秒、tick_interval=1 秒——切到离散模式（factor=0.5）
            // 后应折算为 0.5 个离散单位/0.5 个离散单位一跳。
            var skill = ChannelSkill("skill.cr130_04_switch_channel", 1, 1);
            var world = new SkillWorldBuilder().SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            var skillId = new Id("skill.cr130_04_switch_channel");

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);

            world.Bus.PublishImmediate(new TimeModelRescaledEvent(0.5)); // → channel_time/tick_interval 均折算为 0.5。

            // 离散步按行动者推进：dt=1.0 是"一步"的固定口径（见 SkillHost.AdvanceCastForActor 判断
            // 记录），此时折算后的剩余引导时间只有 0.5——超出的 0.5 不应该被多算进周期累加器，只应
            // 该恰好触发一次结算（0.5 累加器达到 0.5 的 tick_interval）。
            world.Host.AdvanceCastForActor(Caster, 1.0);

            Assert.Single(world.Combat.ResolveCalls);
            Assert.False(world.Host.IsCasting(Caster));
        }
    }
}
