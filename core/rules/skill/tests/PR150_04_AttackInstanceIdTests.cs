using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// PR150-04 复现/根治（<c>architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md</c>
    /// "攻击实例 id"，PR140-04 遗留）：<see cref="EffectContext.AttackInstanceId"/> 经
    /// <see cref="Core.Rules.Skill.CastPipeline.ExecuteEffectsOnly"/> 产生（见该方法判断记录"每次调用
    /// 固定分配一个全新的 Id"），本文件直接对真实 <see cref="Core.Rules.Skill.SkillHost"/>/
    /// <see cref="Core.Rules.Skill.CastPipeline"/> 施法，经 <see cref="FakeCombatHost.ResolveCalls"/>
    /// 逐条核对结算入口收到的 <see cref="EffectContext"/>（不经过真实 <c>Resolver</c>——本文件只验证
    /// "谁分配了什么值传给了结算入口"这一段管线本身是否正确，事件落地层的携带见
    /// <c>core/rules/combat/tests</c> 与 <c>presentation/feedback_binder/tests</c> 对应用例）。
    /// <para>
    /// 根治前（<see cref="EffectContext"/> 没有 <see cref="EffectContext.AttackInstanceId"/> 字段、
    /// 事件也不携带）：命中帧同步只能按"同一攻击者未释放窗口"这一时序代理合批，同一窗口内两次确实
    /// 不同的攻击会被误合并成一批（见 <c>presentation/feedback_binder/tests/FeedbackBinderHitFrameSyncTests.cs</c>
    /// <c>HitFrameSyncRule_TwoDistinctAttacksSameWindow_EachReleasesOnlyItsOwnBatch</c>）。
    /// </para>
    /// </summary>
    public sealed class PR150_04_AttackInstanceIdTests
    {
        private static readonly Id Caster = new Id("unit.pr150_04_caster");
        private static readonly Id ChainId = new Id("target.chain.pr150_04_sample");

        private static Core.Foundation.Common.Json.JsonObject InstantBolt(string id) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_pr150_04")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A(
                J.O(("kind", J.S("school_damage")),
                    ("params", J.O(("base_value", J.N(5)), ("coefficient", J.N(0))))))));

        private static Core.Foundation.Common.Json.JsonObject ChannelSkill(string id, double channelTime, double tickInterval) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_pr150_04")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("channel_time", J.N(channelTime)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A(
                J.O(("kind", J.S("school_damage")),
                    ("params", J.O(("tick_interval", J.N(tickInterval)), ("base_value", J.N(1)), ("coefficient", J.N(0))))))));

        /// <summary>核心复现/根治点 1："一次结算"（<see cref="Core.Rules.Skill.CastPipeline.ExecuteEffectsOnly"/>
        /// 一次调用）命中的多个目标必须共享同一个 <see cref="EffectContext.AttackInstanceId"/>——对应
        /// PR140-04 原始的"AoE 命中批次"诉求，本次改用攻击实例 id 这一更精确的机制承载，不能倒退。</summary>
        [Fact]
        public void SingleCast_MultipleTargets_ShareSameAttackInstanceId()
        {
            var skillId = new Id("skill.pr150_04_aoe");
            var world = new SkillWorldBuilder().SkillDef(InstantBolt(skillId.Value)).Build();
            var targetA = new Id("unit.pr150_04_target_a");
            var targetB = new Id("unit.pr150_04_target_b");
            world.AddUnit(Caster);
            world.AddUnit(targetA);
            world.AddUnit(targetB);
            world.Targets.SetChain(ChainId, targetA, targetB);

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Flush();

            Assert.Equal(2, world.Combat.ResolveCalls.Count);
            var idA = world.Combat.ResolveCalls[0].AttackInstanceId;
            var idB = world.Combat.ResolveCalls[1].AttackInstanceId;

            Assert.NotNull(idA);
            Assert.Equal(idA, idB);
        }

        /// <summary>核心复现/根治点 2：两次独立的 <c>CastSkill</c> 调用（哪怕是同一施法者、同一技能、
        /// 同一目标、紧接着连续发起）必须各自分配不同的 <see cref="EffectContext.AttackInstanceId"/>——
        /// 这正是命中帧同步据以区分"同一窗口内两次不同攻击"的根据，见类型注释。</summary>
        [Fact]
        public void TwoSeparateCasts_ProduceDifferentAttackInstanceIds()
        {
            var skillId = new Id("skill.pr150_04_repeat");
            var world = new SkillWorldBuilder().SkillDef(InstantBolt(skillId.Value)).Build();
            var target = new Id("unit.pr150_04_repeat_target");
            world.AddUnit(Caster);
            world.AddUnit(target);
            world.Targets.SetChain(ChainId, target);

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Flush();
            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Flush();

            Assert.Equal(2, world.Combat.ResolveCalls.Count);
            var firstId = world.Combat.ResolveCalls[0].AttackInstanceId;
            var secondId = world.Combat.ResolveCalls[1].AttackInstanceId;

            Assert.NotNull(firstId);
            Assert.NotNull(secondId);
            Assert.NotEqual(firstId, secondId);
        }

        /// <summary>引导（channel）技能的多次周期跳分别是各自独立的"一次结算"（见
        /// <c>CastPipeline.ExecuteEffectsOnly</c> 判断记录"为什么是每次调用而不是每次读条/引导"），各
        /// 自应分配不同的 <see cref="EffectContext.AttackInstanceId"/>——不能因为"同属一次引导"就把
        /// 跨越多个 tick 的效果合并成同一个攻击实例，否则命中帧同步会把本该分别在各自 tick 命中帧释放
        /// 的动作错误地全部堆到第一个 tick 的命中帧一起释放。</summary>
        [Fact]
        public void Channel_MultipleTicks_EachTickGetsDistinctAttackInstanceId()
        {
            var skillId = new Id("skill.pr150_04_channel");
            var world = new SkillWorldBuilder().SkillDef(ChannelSkill(skillId.Value, channelTime: 3, tickInterval: 1)).Build();
            var target = new Id("unit.pr150_04_channel_target");
            world.AddUnit(Caster);
            world.AddUnit(target);
            world.Targets.SetChain(ChainId, target);

            Assert.True(world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>()).Success);
            world.Flush();

            world.Host.Update(5.0); // channel_time=3、tick_interval=1 → 恰好 3 跳（见 CR130-04 同款用例）。

            Assert.Equal(3, world.Combat.ResolveCalls.Count);
            var ids = world.Combat.ResolveCalls.Select(c => c.AttackInstanceId).ToList();

            Assert.All(ids, id => Assert.NotNull(id));
            Assert.Equal(3, ids.Distinct().Count());
        }
    }
}
