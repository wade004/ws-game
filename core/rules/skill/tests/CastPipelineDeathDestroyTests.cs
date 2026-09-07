using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// RC-03（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-03）：
    /// 读条/引导/法术队列必须在施法者死亡（<c>unit.died</c>）或被销毁（<c>entity.destroyed</c>）
    /// 时取消，且 <c>AdvanceOne</c>/<c>FinishCast</c> 完成前必须重验施法者仍然有效——此前
    /// <see cref="Core.Rules.Skill.CastPipeline"/> 只处理控制类打断（aura.applied）与受伤打断
    /// （combat.damage_dealt），完全不订阅死亡/销毁，死亡者会继续扣资源并结算，销毁后更会因为
    /// 访问已注销的单位资源而抛异常。
    /// </summary>
    public sealed class CastPipelineDeathDestroyTests
    {
        private static Core.Foundation.Common.Json.JsonObject LongCastSkill(string id, double castTime) =>
            J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(castTime)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("energize")),
                        ("params", J.O(("power_type", J.S("arch.power.sample_counter")), ("amount", J.N(1))))))));

        private static Core.Foundation.Common.Json.JsonObject ChannelSkill(string id, double channelTime) =>
            J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("channel_time", J.N(channelTime)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("energize")),
                        ("params", J.O(("power_type", J.S("arch.power.sample_counter")), ("amount", J.N(1)), ("tick_interval", J.N(1))))))));

        private static Core.Foundation.Common.Json.JsonObject InstantSkill(string id) =>
            J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("energize")),
                        ("params", J.O(("power_type", J.S("arch.power.sample_counter")), ("amount", J.N(100))))))));

        [Fact]
        public void CasterDies_DuringOngoingCast_CancelsCast_NoSuccessEventOrEffects()
        {
            var skill = LongCastSkill("skill.sample_death_cast", 2.0);
            var world = new SkillWorldBuilder().SkillDef(skill)
                .Power("arch.power.sample_counter", 1_000_000, startFull: false)
                .Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var start = world.Host.CastSkill(caster, new Id("skill.sample_death_cast"), System.Array.Empty<Id>());
            Assert.True(start.Success);
            Assert.True(world.Host.IsCasting(caster));

            // 死亡：unit.died 事件到达即应立即取消读条。
            world.Bus.Enqueue(new UnitDiedEvent(caster, killerId: null));
            world.Flush();

            Assert.False(world.Host.IsCasting(caster));
            Assert.Single(world.Of<SkillCastInterruptedEvent>());

            // 修复前：CastPipeline 完全不订阅 unit.died，读条会继续推进到 2.0 秒正常结算——继续
            // Update 到原定完成时间，验证不会补发成功事件或补执行效果。
            world.Host.Update(2.0);
            Assert.Empty(world.Of<SkillCastSuccessEvent>());
            Assert.Equal(0, world.Powers.GetPower(caster, new Id("arch.power.sample_counter")));
        }

        [Fact]
        public void CasterDestroyed_DuringChannel_StopsPeriodicEffects_NoException()
        {
            var skill = ChannelSkill("skill.sample_destroy_channel", 5.0);
            var world = new SkillWorldBuilder().SkillDef(skill)
                .Power("arch.power.sample_counter", 1_000_000, startFull: false)
                .Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var start = world.Host.CastSkill(caster, new Id("skill.sample_destroy_channel"), System.Array.Empty<Id>());
            Assert.True(start.Success);

            world.Host.Update(1.0); // 触发一次周期效果
            Assert.Equal(1, world.Powers.GetPower(caster, new Id("arch.power.sample_counter")));

            // 销毁：先从 IUnitAccess 移除（真实生产路径会先注销资源），再派发 entity.destroyed——
            // 修复前 FinishCast/AdvanceOne 完全不重验施法者存在性，继续 Update 会访问已"销毁"的
            // 施法者（这里以 FakeUnitAccess.Exists 归零模拟真实 PowerHost/StatHost 注销后的效果）。
            world.Units.Remove(caster);
            world.Bus.Enqueue(new EntityDestroyedEvent(caster));

            var ex = Record.Exception(() =>
            {
                world.Flush();
                world.Host.Update(10.0);
            });
            Assert.Null(ex);

            Assert.False(world.Host.IsCasting(caster));
            // 销毁后不应再有任何周期效果结算。
            Assert.Equal(1, world.Powers.GetPower(caster, new Id("arch.power.sample_counter")));
        }

        [Fact]
        public void CasterDies_WithQueuedSpell_QueuedSpellNeverStarts()
        {
            var current = LongCastSkill("skill.sample_death_queue_current", 2.0);
            var queued = InstantSkill("skill.sample_death_queue_next");
            var builder = new SkillWorldBuilder().SkillDef(current).SkillDef(queued)
                .Power("arch.power.sample_counter", 1_000_000, startFull: false);
            builder.Options.QueueWindow = 5.0; // 整个读条期间都落在队列窗口内，方便测试直接入队。
            var world = builder.Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var start = world.Host.CastSkill(caster, new Id("skill.sample_death_queue_current"), System.Array.Empty<Id>());
            Assert.True(start.Success);

            var queueResult = world.Host.CastSkill(caster, new Id("skill.sample_death_queue_next"), System.Array.Empty<Id>());
            Assert.True(queueResult.Success); // 入队本身返回成功（见 CastPipeline.CastSkill 队列分支）。

            world.Bus.Enqueue(new UnitDiedEvent(caster, killerId: null));
            world.Flush();
            Assert.False(world.Host.IsCasting(caster));

            // 排队技能的 amount=100，与当前技能的 amount=1 区分明显——两者都不应结算。
            world.Host.Update(10.0);
            Assert.Equal(0, world.Powers.GetPower(caster, new Id("arch.power.sample_counter")));
            Assert.False(world.Host.IsCasting(caster));
        }

        [Fact]
        public void FinishCast_RevalidatesCasterEvenWithoutDeathEvent_DefenseInDepth()
        {
            // 不派发 unit.died/entity.destroyed，只直接改变 IUnitAccess 状态——单独证明
            // AdvanceOne/FinishCast 自身也重验施法者，不是完全依赖构造函数订阅的死亡/销毁事件
            // （见外部审计 RC-03"AdvanceOne/FinishCast 不重验施法者存在性"这一具体触发点）。
            var skill = LongCastSkill("skill.sample_revalidate_cast", 0.1);
            var world = new SkillWorldBuilder().SkillDef(skill)
                .Power("arch.power.sample_counter", 1_000_000, startFull: false)
                .Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var start = world.Host.CastSkill(caster, new Id("skill.sample_revalidate_cast"), System.Array.Empty<Id>());
            Assert.True(start.Success);

            world.Units.SetAlive(caster, false); // 不经事件总线，直接让施法者"死亡"。

            world.Host.Update(0.1); // 到达原定完成时间。

            Assert.False(world.Host.IsCasting(caster));
            Assert.Empty(world.Of<SkillCastSuccessEvent>());
            Assert.Equal(0, world.Powers.GetPower(caster, new Id("arch.power.sample_counter")));
        }
    }
}
