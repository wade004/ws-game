using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// CORE-118-CAST 根治验收（外部审计 audit-d6fda65-20260911，见 <c>CastPipeline.TerminateCast</c>
    /// 判断记录）：施法者死亡/消失已生效（<c>IUnitAccess.IsAlive</c>/<c>Exists</c> 已经反映新状态），
    /// 但对应的 <see cref="UnitDiedEvent"/>/<see cref="EntityDestroyedEvent"/> 要等本次
    /// <c>SkillHost.Update</c> 之后的 <c>DispatchPending</c> 才真正派发时，当前读条/引导与排队请求
    /// 都必须各收到一次终结事件（<see cref="SkillCastInterruptedEvent"/> 与携带
    /// <see cref="CastFailureReason.QueueCleared"/> 的 <see cref="SkillCastFailedEvent"/>），且各自
    /// 携带正确的 <c>CastInstanceId</c>、不重复发送。真实探针复现见
    /// <c>D:\workespace\ws-game-artifacts\audit-d6fda65-20260911\core\probe\Program.cs</c>：
    /// 先 Flush 再 Update 得到 <c>interrupted=1,queueFailed=1</c>；先 Update 再 Flush（本类型根治前）
    /// 得到 <c>interrupted=0,queueFailed=0</c>。
    /// </summary>
    public sealed class CORE118_CastPipelineDeathTerminationTests
    {
        private static Core.Foundation.Common.Json.JsonObject LongCastSkill(string id, double castTime) =>
            J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_core118")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(castTime)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.core118")),
                ("effects", J.A()));

        private static Core.Foundation.Common.Json.JsonObject InstantSkill(string id) =>
            J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_core118")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.core118")),
                ("effects", J.A()));

        /// <summary>构造一个"当前读条 + 已排队一个请求"的 world，返回两次 CastSkill 的 CastResult
        /// 供各测试断言 CastInstanceId 归属。</summary>
        private static (SkillWorld World, Id Caster, CastResult Active, CastResult Queued) BuildCastingWithQueue()
        {
            var current = LongCastSkill("skill.core118_current", 2.0);
            var queued = InstantSkill("skill.core118_queued");
            var builder = new SkillWorldBuilder().SkillDef(current).SkillDef(queued);
            builder.Options.QueueWindow = 5.0; // 整个读条期间都落在队列窗口内，方便直接入队。
            var world = builder.Build();
            var caster = new Id("unit.core118_caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.core118"), caster);

            var active = world.Host.CastSkill(caster, new Id("skill.core118_current"), System.Array.Empty<Id>());
            Assert.True(active.Success);
            var queuedResult = world.Host.CastSkill(caster, new Id("skill.core118_queued"), System.Array.Empty<Id>());
            Assert.True(queuedResult.Success);
            Assert.True(world.Host.IsCasting(caster));

            return (world, caster, active, queuedResult);
        }

        // -------------------------------------------------------------
        // 死亡事件先/后派发：两种时序都必须得到 Interrupted=1, QueueCleared=1
        // -------------------------------------------------------------

        [Fact]
        public void DeathEventDispatchedBeforeUpdate_FiresInterruptedAndQueueClearedWithCorrectIds()
        {
            var (world, caster, active, queued) = BuildCastingWithQueue();

            world.Units.SetAlive(caster, false);
            world.Bus.Enqueue(new UnitDiedEvent(caster, null));
            world.Flush(); // 死亡事件先派发到 OnCasterDiedOrDestroyed -> Interrupt。
            world.Host.Update(1.0);
            world.Flush();

            Assert.False(world.Host.IsCasting(caster));
            var interrupted = world.Of<SkillCastInterruptedEvent>().Single();
            Assert.Equal(active.CastInstanceId, interrupted.CastInstanceId);
            var queueCleared = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(CastFailureReason.QueueCleared, queueCleared.ReasonCode);
            Assert.Equal(queued.CastInstanceId, queueCleared.CastInstanceId);
        }

        [Fact]
        public void DeathEventEnqueuedButNotDispatchedBeforeUpdate_StillFiresInterruptedAndQueueClearedWithCorrectIds()
        {
            // 真实探针复现的时序：死亡状态已生效、事件已入队，但本次 SkillHost.Update（内部
            // AdvanceOne 的防御性重验）先于 DispatchPending 运行——根治前，AdvanceOne 会静默摘除
            // CastState，随后死亡事件派发到 Interrupt 时 _casting 已空，interrupted/queueFailed
            // 都是 0。
            var (world, caster, active, queued) = BuildCastingWithQueue();

            world.Units.SetAlive(caster, false);
            world.Bus.Enqueue(new UnitDiedEvent(caster, null));
            world.Host.Update(1.0); // AdvanceOne 的防御性重验先于死亡事件派发运行。
            world.Flush();

            Assert.False(world.Host.IsCasting(caster));
            var interrupted = world.Of<SkillCastInterruptedEvent>().Single();
            Assert.Equal(active.CastInstanceId, interrupted.CastInstanceId);
            var queueCleared = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(CastFailureReason.QueueCleared, queueCleared.ReasonCode);
            Assert.Equal(queued.CastInstanceId, queueCleared.CastInstanceId);
        }

        [Fact]
        public void DeathEventEnqueuedButNotDispatched_NoDuplicateTerminationEvents()
        {
            // 幂等性：AdvanceOne 的防御性兜底先摘掉 CastState 并发送终结事件之后，真正的死亡事件
            // 派发到 Interrupt 时应发现 _casting 已空、直接 no-op——不应该出现第二组 Interrupted/
            // QueueCleared。
            var (world, caster, active, queued) = BuildCastingWithQueue();

            world.Units.SetAlive(caster, false);
            world.Bus.Enqueue(new UnitDiedEvent(caster, null));
            world.Host.Update(1.0); // 触发防御性兜底，已经发过一次终结事件。
            world.Flush(); // 死亡事件真正派发；Interrupt 应为 no-op，不重复发送。

            Assert.Single(world.Of<SkillCastInterruptedEvent>());
            Assert.Single(world.Of<SkillCastFailedEvent>().Where(e => e.ReasonCode == CastFailureReason.QueueCleared));
            var interrupted = world.Of<SkillCastInterruptedEvent>().Single();
            Assert.Equal(active.CastInstanceId, interrupted.CastInstanceId);
        }

        // -------------------------------------------------------------
        // 实体销毁：同一类兜底缺口
        // -------------------------------------------------------------

        [Fact]
        public void EntityDestroyedEnqueuedButNotDispatched_DefensiveTerminationFiresInterruptedAndQueueCleared()
        {
            var (world, caster, active, queued) = BuildCastingWithQueue();

            world.Units.Remove(caster); // Exists() 立即归零，模拟真实资源先注销。
            world.Bus.Enqueue(new EntityDestroyedEvent(caster));
            world.Host.Update(1.0); // AdvanceOne 防御性重验先于 entity.destroyed 派发运行。
            world.Flush();

            Assert.False(world.Host.IsCasting(caster));
            var interrupted = world.Of<SkillCastInterruptedEvent>().Single();
            Assert.Equal(active.CastInstanceId, interrupted.CastInstanceId);
            var queueCleared = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(CastFailureReason.QueueCleared, queueCleared.ReasonCode);
            Assert.Equal(queued.CastInstanceId, queueCleared.CastInstanceId);
        }

        // -------------------------------------------------------------
        // FinishCast 兜底：读条本该在本次 Update 完成，但施法者已在事件之外失效
        // -------------------------------------------------------------

        [Fact]
        public void FinishCastDefensiveBranch_CasterInvalidatedWithoutAnyEvent_FiresInterruptedAndQueueCleared()
        {
            // 不派发 unit.died/entity.destroyed，只直接改变 IUnitAccess 状态——证明 FinishCast 的
            // 防御性重验本身（不依赖事件总线）现在也会通过 TerminateCast 补发终结事件，覆盖"施法者
            // 已失效但从未有任何死亡/销毁事件"的兜底场景。
            var current = LongCastSkill("skill.core118_finish_current", 0.1);
            var queued = InstantSkill("skill.core118_finish_queued");
            var builder = new SkillWorldBuilder().SkillDef(current).SkillDef(queued);
            builder.Options.QueueWindow = 5.0;
            var world = builder.Build();
            var caster = new Id("unit.core118_finish_caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.core118"), caster);

            var active = world.Host.CastSkill(caster, new Id("skill.core118_finish_current"), System.Array.Empty<Id>());
            Assert.True(active.Success);
            var queuedResult = world.Host.CastSkill(caster, new Id("skill.core118_finish_queued"), System.Array.Empty<Id>());
            Assert.True(queuedResult.Success);

            world.Units.SetAlive(caster, false); // 不经事件总线，直接让施法者"死亡"。
            world.Host.Update(0.1); // 到达原定完成时间，走到 FinishCast 的防御性重验分支。
            world.Flush();

            Assert.False(world.Host.IsCasting(caster));
            Assert.Empty(world.Of<SkillCastSuccessEvent>());
            var interrupted = world.Of<SkillCastInterruptedEvent>().Single();
            Assert.Equal(active.CastInstanceId, interrupted.CastInstanceId);
            var queueCleared = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(CastFailureReason.QueueCleared, queueCleared.ReasonCode);
            Assert.Equal(queuedResult.CastInstanceId, queueCleared.CastInstanceId);
        }

        // -------------------------------------------------------------
        // 回归：队列替换、正常打断在共用 TerminateCast 之后行为不变
        // -------------------------------------------------------------

        [Fact]
        public void QueueOverwritten_StillEmitsSingleQueueClearedForOverwrittenRequest()
        {
            var current = LongCastSkill("skill.core118_overwrite_current", 2.0);
            var firstQueued = InstantSkill("skill.core118_overwrite_first");
            var secondQueued = InstantSkill("skill.core118_overwrite_second");
            var builder = new SkillWorldBuilder().SkillDef(current).SkillDef(firstQueued).SkillDef(secondQueued);
            builder.Options.QueueWindow = 5.0;
            var world = builder.Build();
            var caster = new Id("unit.core118_overwrite_caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.core118"), caster);

            Assert.True(world.Host.CastSkill(caster, new Id("skill.core118_overwrite_current"), System.Array.Empty<Id>()).Success);
            var first = world.Host.CastSkill(caster, new Id("skill.core118_overwrite_first"), System.Array.Empty<Id>());
            Assert.True(first.Success);
            var second = world.Host.CastSkill(caster, new Id("skill.core118_overwrite_second"), System.Array.Empty<Id>());
            Assert.True(second.Success);
            world.Flush();

            // 队列替换本身不经过 TerminateCast（当前读条仍在继续），只应有覆盖掉的第一个排队请求
            // 收到一次 QueueCleared，第二个（仍在队列里）不受影响。
            var queueCleared = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(first.CastInstanceId, queueCleared.CastInstanceId);
            Assert.Empty(world.Of<SkillCastInterruptedEvent>());
            Assert.True(world.Host.IsCasting(caster));
        }

        [Fact]
        public void NormalControlInterrupt_StillFiresSingleInterruptedAndQueueClearedEvent()
        {
            var (world, caster, active, queued) = BuildCastingWithQueue();

            // 正常打断路径（非死亡/销毁）：Interrupt 本身经共享的 TerminateCast，行为应与根治前一致。
            world.Host.Interrupt(caster, caster, null, 0);
            world.Flush();

            Assert.False(world.Host.IsCasting(caster));
            Assert.Single(world.Of<SkillCastInterruptedEvent>());
            var interrupted = world.Of<SkillCastInterruptedEvent>().Single();
            Assert.Equal(active.CastInstanceId, interrupted.CastInstanceId);
            var queueCleared = world.Of<SkillCastFailedEvent>().Single();
            Assert.Equal(CastFailureReason.QueueCleared, queueCleared.ReasonCode);
            Assert.Equal(queued.CastInstanceId, queueCleared.CastInstanceId);

            // 再次死亡事件到达（施法者本就已经不在读条中）应是安全 no-op，不重复发送。
            world.Bus.Enqueue(new UnitDiedEvent(caster, null));
            world.Flush();
            Assert.Single(world.Of<SkillCastInterruptedEvent>());
            Assert.Single(world.Of<SkillCastFailedEvent>());
        }
    }
}
