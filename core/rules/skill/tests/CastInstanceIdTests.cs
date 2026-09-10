using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 消费方反馈 2026-09-10（施法生命周期事件缺少实例关联标识建议，证据 c08-cast-event-contract，
    /// 见 architecture/落地计划/消费方反馈-2026-09-10-施法时序与实例标识.md）复现与根治：
    /// <see cref="SkillCastStartEvent"/>/<see cref="SkillCastSuccessEvent"/>/
    /// <see cref="SkillCastFailedEvent"/>/<see cref="SkillCastInterruptedEvent"/> 新增只读属性
    /// <c>CastInstanceId</c>，与 <see cref="CastResult.CastInstanceId"/> 同一枚 id、同一身份规则：
    /// <list type="bullet">
    /// <item>校验通过、进入排队或立即开始时分配，随 <see cref="CastResult"/> 返回；校验阶段失败
    /// 不分配。</item>
    /// <item>排队接受即分配；真正开始时携带同一个 id；排队后被覆盖/所在读条被打断而清空队列，都
    /// 发一条携带该排队请求自己 id 的 <see cref="SkillCastFailedEvent"/>（<see cref="CastFailureReason.QueueCleared"/>）。</item>
    /// <item>瞬发与读条/引导路径一致可追踪。</item>
    /// <item>旧构造（不带 <c>castInstanceId</c>）保留，物理签名不变，新构造是纯新增重载。</item>
    /// </list>
    /// </summary>
    public sealed class CastInstanceIdTests
    {
        private static readonly Id Caster = new Id("unit.wu17b_caster");
        private static readonly Id Target = new Id("unit.wu17b_target");
        private static readonly Id ChainId = new Id("target.chain.wu17b_self");

        private static Core.Foundation.Common.Json.JsonObject SkillJson(string id, double castTime) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_wu17b")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(castTime)),
            ("respects_gcd", J.B(false)),
            ("cooldown_duration", J.N(0)),
            ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A(
                J.O(("kind", J.S("school_damage")),
                    ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0))))))));

        private static SkillWorld BuildWorld(params Core.Foundation.Common.Json.JsonObject[] skills)
        {
            var builder = new SkillWorldBuilder();
            builder.Options.QueueWindow = 0.3;
            foreach (var skill in skills)
            {
                builder.SkillDef(skill);
            }

            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            return world;
        }

        // -----------------------------------------------------------------
        // 瞬发 / 读条：start/success 与 CastResult 携带同一个 id
        // -----------------------------------------------------------------

        [Fact]
        public void InstantCast_StartAndSuccessEvents_CarrySameInstanceId_AsCastResult()
        {
            var skillId = new Id("skill.wu17b_instant");
            var world = BuildWorld(SkillJson(skillId.Value, castTime: 0));

            var result = world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            Assert.NotNull(result.CastInstanceId);

            var start = world.Of<SkillCastStartEvent>().Single();
            var success = world.Of<SkillCastSuccessEvent>().Single();
            Assert.Equal(result.CastInstanceId, start.CastInstanceId);
            Assert.Equal(result.CastInstanceId, success.CastInstanceId);
        }

        [Fact]
        public void ReadBarCast_StartAndSuccessEvents_CarrySameInstanceId_AsCastResult()
        {
            var skillId = new Id("skill.wu17b_readbar");
            var world = BuildWorld(SkillJson(skillId.Value, castTime: 0.5));

            var result = world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>());
            world.Host.Update(0.5);
            world.Flush();

            Assert.True(result.Success);
            Assert.NotNull(result.CastInstanceId);

            var start = world.Of<SkillCastStartEvent>().Single();
            var success = world.Of<SkillCastSuccessEvent>().Single();
            Assert.Equal(result.CastInstanceId, start.CastInstanceId);
            Assert.Equal(result.CastInstanceId, success.CastInstanceId);
        }

        [Fact]
        public void TwoConsecutiveCastsOfSameSkill_HaveIndependentlyLinkableLifecycles()
        {
            var skillId = new Id("skill.wu17b_repeat");
            var world = BuildWorld(SkillJson(skillId.Value, castTime: 0));

            var first = world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>());
            world.Flush();
            var second = world.Host.CastSkill(Caster, skillId, System.Array.Empty<Id>());
            world.Flush();

            Assert.NotEqual(first.CastInstanceId, second.CastInstanceId);

            var successes = world.Of<SkillCastSuccessEvent>().ToList();
            Assert.Equal(2, successes.Count);
            Assert.Equal(first.CastInstanceId, successes[0].CastInstanceId);
            Assert.Equal(second.CastInstanceId, successes[1].CastInstanceId);
        }

        // -----------------------------------------------------------------
        // 校验失败：不分配 id
        // -----------------------------------------------------------------

        [Fact]
        public void ValidationFailure_DoesNotAssignInstanceId()
        {
            var world = BuildWorld();

            var result = world.Host.CastSkill(Caster, new Id("skill.wu17b_missing"), System.Array.Empty<Id>());
            world.Flush();

            Assert.False(result.Success);
            Assert.Null(result.CastInstanceId);

            var failed = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(CastFailureReason.UnknownSkill, failed.ReasonCode);
            Assert.Null(failed.CastInstanceId);
        }

        // -----------------------------------------------------------------
        // 排队：接受时分配、真正开始携带同一个 id
        // -----------------------------------------------------------------

        [Fact]
        public void QueuedRequest_ExecutesLater_CarriesSameInstanceId_AsWhenQueued()
        {
            var slowId = new Id("skill.wu17b_slow");
            var queuedId = new Id("skill.wu17b_queued_instant");
            var world = BuildWorld(SkillJson(slowId.Value, castTime: 1.0), SkillJson(queuedId.Value, castTime: 0));

            var slowResult = world.Host.CastSkill(Caster, slowId, System.Array.Empty<Id>());
            Assert.True(slowResult.Success);

            world.Host.Update(0.8); // 剩余 0.2 <= 队列窗口 0.3。
            var queuedResult = world.Host.CastSkill(Caster, queuedId, System.Array.Empty<Id>());
            Assert.True(queuedResult.Success);
            Assert.NotNull(queuedResult.CastInstanceId);
            Assert.NotEqual(slowResult.CastInstanceId, queuedResult.CastInstanceId);

            world.Host.Update(0.2); // 完成 slow，顺序执行排队的 queued。
            world.Flush();

            var starts = world.Of<SkillCastStartEvent>().ToList();
            var successes = world.Of<SkillCastSuccessEvent>().ToList();
            Assert.Equal(2, starts.Count);
            Assert.Equal(2, successes.Count);

            // 排队技能真正开始/成功时的事件应携带排队时（CastSkill 返回 CastResult 那一刻）就
            // 已经分配的同一个 id，不是执行时重新分配的新 id。
            var queuedStart = starts.Single(e => e.SkillId == queuedId);
            var queuedSuccess = successes.Single(e => e.SkillId == queuedId);
            Assert.Equal(queuedResult.CastInstanceId, queuedStart.CastInstanceId);
            Assert.Equal(queuedResult.CastInstanceId, queuedSuccess.CastInstanceId);
        }

        [Fact]
        public void QueuedRequest_OverwrittenByNewerQueueRequest_EmitsQueueClearedWithOldId()
        {
            var slowId = new Id("skill.wu17b_slow2");
            var queuedFirstId = new Id("skill.wu17b_queued_first");
            var queuedSecondId = new Id("skill.wu17b_queued_second");
            var world = BuildWorld(
                SkillJson(slowId.Value, castTime: 1.0),
                SkillJson(queuedFirstId.Value, castTime: 0),
                SkillJson(queuedSecondId.Value, castTime: 0));

            Assert.True(world.Host.CastSkill(Caster, slowId, System.Array.Empty<Id>()).Success);
            world.Host.Update(0.8); // 进入队列窗口。

            var firstQueued = world.Host.CastSkill(Caster, queuedFirstId, System.Array.Empty<Id>());
            Assert.True(firstQueued.Success);

            // 同一队列窗口内再排一次——覆盖第一次排队的请求，第一次永远不会执行。
            var secondQueued = world.Host.CastSkill(Caster, queuedSecondId, System.Array.Empty<Id>());
            Assert.True(secondQueued.Success);
            world.Flush();

            var overwrittenFailure = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(queuedFirstId, overwrittenFailure.SkillId);
            Assert.Equal(CastFailureReason.QueueCleared, overwrittenFailure.ReasonCode);
            Assert.Equal(firstQueued.CastInstanceId, overwrittenFailure.CastInstanceId);
            Assert.NotEqual(secondQueued.CastInstanceId, overwrittenFailure.CastInstanceId);

            world.Host.Update(0.2); // 完成 slow，只有第二次排队的请求会真正执行。
            world.Flush();

            var successes = world.Of<SkillCastSuccessEvent>().ToList();
            Assert.DoesNotContain(successes, e => e.SkillId == queuedFirstId);
            var executedSecond = successes.Single(e => e.SkillId == queuedSecondId);
            Assert.Equal(secondQueued.CastInstanceId, executedSecond.CastInstanceId);
        }

        [Fact]
        public void ActiveCastInterrupted_ClearsQueue_EmitsInterruptedAndQueueClearedWithDistinctIds()
        {
            var slowId = new Id("skill.wu17b_slow3");
            var queuedId = new Id("skill.wu17b_queued_third");
            var world = BuildWorld(SkillJson(slowId.Value, castTime: 1.0), SkillJson(queuedId.Value, castTime: 0));

            var slowResult = world.Host.CastSkill(Caster, slowId, System.Array.Empty<Id>());
            Assert.True(slowResult.Success);
            world.Host.Update(0.8); // 进入队列窗口，但尚未完成。

            var queuedResult = world.Host.CastSkill(Caster, queuedId, System.Array.Empty<Id>());
            Assert.True(queuedResult.Success);

            // 施法中打断——当前读条与排队的下一个技能应该各自携带自己的 id 各发一条事件。
            world.Host.Interrupt(Caster, Caster, null, 0);
            world.Flush();

            var interrupted = world.Of<SkillCastInterruptedEvent>().Single();
            Assert.Equal(slowId, interrupted.SkillId);
            Assert.Equal(slowResult.CastInstanceId, interrupted.CastInstanceId);

            var queueCleared = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(queuedId, queueCleared.SkillId);
            Assert.Equal(CastFailureReason.QueueCleared, queueCleared.ReasonCode);
            Assert.Equal(queuedResult.CastInstanceId, queueCleared.CastInstanceId);

            Assert.NotEqual(interrupted.CastInstanceId, queueCleared.CastInstanceId);
            Assert.False(world.Host.IsCasting(Caster));

            // 打断之后再推进时间，排队的技能不应该被执行（队列已清空）。
            world.Host.Update(1.0);
            world.Flush();
            Assert.Empty(world.Of<SkillCastSuccessEvent>());
        }

        // -----------------------------------------------------------------
        // 旧构造仍可用：不带 castInstanceId 的既有签名保留，值恒为 null
        // -----------------------------------------------------------------

        [Fact]
        public void OldEventConstructors_StillUsable_DefaultInstanceIdToNull()
        {
            var casterId = Caster;
            var skillId = new Id("skill.wu17b_ctor_probe");

            var start = new SkillCastStartEvent(casterId, skillId, 0.5);
            Assert.Null(start.CastInstanceId);

            var success = new SkillCastSuccessEvent(casterId, skillId, new[] { Target });
            Assert.Null(success.CastInstanceId);

            var failed = new SkillCastFailedEvent(casterId, skillId, CastFailureReason.OnCooldown);
            Assert.Null(failed.CastInstanceId);

            var interrupted = new SkillCastInterruptedEvent(casterId, skillId, casterId);
            Assert.Null(interrupted.CastInstanceId);
        }

        // -----------------------------------------------------------------
        // trigger_spell / Proc 直接效果：不发主动施法生命周期事件
        // -----------------------------------------------------------------

        [Fact]
        public void ProcTriggeredCast_DoesNotEmitCastLifecycleEvents()
        {
            var triggerSkill = J.O(
                ("id", J.S("skill.wu17b_proc_effect")),
                ("school", J.S("skill.school_wu17b")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A()));
            var proc = J.O(
                ("id", J.S("skill.proc_def.wu17b_boundary")),
                ("trigger_event", J.S("combat.damage_dealt")),
                ("trigger_skill", J.S("skill.wu17b_proc_effect")),
                ("proc_chance", J.N(1.0)));
            var procAura = J.O(
                ("id", J.S("skill.aura_def.wu17b_proc_holder")),
                ("duration", J.N(600)),
                ("effects", J.A(
                    J.O(("kind", J.S("proc_trigger")), ("params", J.O(("proc_ref", J.S("skill.proc_def.wu17b_boundary"))))))));

            var world = new SkillWorldBuilder().SkillDef(triggerSkill).ProcDef(proc).AuraDef(procAura).Build();
            world.AddUnit(Caster);
            world.Host.EffectSink.ApplyAura(Caster, new Id("skill.aura_def.wu17b_proc_holder"), Caster);

            world.Bus.Enqueue(new CombatDamageDealtEvent(Caster, Target, new Id("skill.school_wu17b"), 1, false, HitResult.Hit));
            world.Flush();

            Assert.Single(world.Of<ProcTriggeredEvent>());
            Assert.Empty(world.Of<SkillCastStartEvent>());
            Assert.Empty(world.Of<SkillCastSuccessEvent>());
            Assert.Empty(world.Of<SkillCastFailedEvent>());
            Assert.Empty(world.Of<SkillCastInterruptedEvent>());
        }
    }
}
