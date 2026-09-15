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
        // T-N3-4（ADR-0031 决策 10）改动：respects_gcd 默认由 false 改为 true——节拍锁泛化后，
        // respects_gcd=false 的瞬发技能在施法者读条/引导中会被 CastPipeline.CastSkill 判定为"反应类
        // 插入"（见 ClassifyReactiveInsert 判断记录），不再进入法术队列/Busy 分支，会直接立即执行；
        // 本类中多个用例用本技能定义充当"占用队列/被节拍锁挡下的普通第二技能"这一角色（并非有意
        // 测试反应类插入语义），respects_gcd 此前恰好为 false 只是历史上 GcdEnabled 默认关闭时该字段
        // 不参与任何裁决的巧合写法——继续留 false 会让这些用例的"排队"前提被新语义架空（技能变成
        // 立即插入执行，不再进入 CastState.Queued）。改为默认 true 让它们保持"respects_gcd=true 的
        // 普通技能"语义，契约不清初判定：需要真正测试反应类插入的用例改为显式传 respectsGcd: false
        // （见本文件新增的 T-N3-4 用例）。
        private static Core.Foundation.Common.Json.JsonObject InstantBolt(
            string id, string? cooldownCategory = null, double cooldownDuration = 0, bool respectsGcd = true)
        {
            var fields = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(respectsGcd)),
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
        public void ActionLocked_Fails_WhenCastingAgain_OutsideQueueWindow_GcdDisabled()
        {
            // T-N3-4（ADR-0031 决策 10，06 第 3.6 节 2026-09-14 修订）更新：本用例原断言
            // CastFailureReason.Busy——节拍锁泛化后，GcdEnabled=false（本 builder 默认）时，
            // respects_gcd=true 的技能在他技能动作时长内被拒绝的精确原因码改为 ActionLocked
            // （见该原因码注释"与既有 Busy 的关系"）；Busy 在 GcdEnabled=false 下改为只保留给
            // "respects_gcd=false 但无法安全插入"的边缘情形（见下方
            // Busy_Fails_ForNonInstantReactiveSkill_UnableToSafelyInsert 用例）。
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
            // InstantBolt 默认 respects_gcd=true（T-N3-4 改动，见该辅助方法判断记录）——不是反应类，
            // 落回节拍锁分支。
            var world = builder.SkillDef(channel).SkillDef(InstantBolt("skill.sample_bolt")).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var start = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_cast_slow"), System.Array.Empty<Id>());
            Assert.True(start.Success);

            // 尚未推进任何时间：剩余读条时间 = 1.0，远大于队列窗口 0.3。
            var locked = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(locked.Success);
            Assert.Equal(CastFailureReason.ActionLocked, locked.Reason);
            Assert.True(world.Host.IsCasting(new Id("unit.caster")));
        }

        // -----------------------------------------------------------------
        // T-N3-4（ADR-0031 决策 10）：节拍锁泛化与反应类插入——先补的"队列 + 反应类"回归用例见
        // 本文件上方 ActionLocked_Fails_WhenCastingAgain_OutsideQueueWindow_GcdDisabled（队列窗口外，
        // respects_gcd=true 落回节拍锁）；本区块新增反应类专属用例。
        // -----------------------------------------------------------------

        /// <summary>respects_gcd=false 且瞬发（cast_time=0）的反应类技能，在他技能动作时长内（读条
        /// 剩余远超队列窗口，本不会入队）应被允许"插入"：不落入 ActionLocked/Busy，立即成功执行，且
        /// 不打断/不覆盖仍在读条中的原技能状态。</summary>
        [Fact]
        public void ReactiveSkill_RespectsGcdFalse_Instant_InsertsWithoutDisturbingActiveCast()
        {
            var channel = J.O(
                ("id", J.S("skill.sample_cast_slow2")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(2.0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));
            var reactive = InstantBolt("skill.sample_reactive_block", respectsGcd: false);

            var builder = new SkillWorldBuilder();
            builder.Options.QueueWindow = 0.3;
            var world = builder.SkillDef(channel).SkillDef(reactive).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            var start = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_cast_slow2"), System.Array.Empty<Id>());
            Assert.True(start.Success);

            // 剩余读条 2.0，远大于队列窗口 0.3——旧语义下会被 Busy/新语义节拍锁下会被 ActionLocked
            // 拒绝；反应类技能应改为直接插入并瞬间成功。
            var inserted = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_reactive_block"), System.Array.Empty<Id>());
            Assert.True(inserted.Success);
            Assert.NotEqual(start.CastInstanceId, inserted.CastInstanceId);

            // 原技能读条未被打断/未被覆盖——仍在施法中，且其 CastState 未被反应类插入触碰。
            Assert.True(world.Host.IsCasting(new Id("unit.caster")));

            world.Flush();
            var reactiveSuccess = world.Of<SkillCastSuccessEvent>().Single(e => e.SkillId == new Id("skill.sample_reactive_block"));
            Assert.True(reactiveSuccess.IsInstant);
            Assert.Equal(inserted.CastInstanceId, reactiveSuccess.CastInstanceId);
            // 原读条尚未完成，不应该有它的成功事件。
            Assert.DoesNotContain(world.Of<SkillCastSuccessEvent>(), e => e.SkillId == new Id("skill.sample_cast_slow2"));

            // 推进到原读条完成：应正常结算成功，证明反应类插入没有打断/污染它的状态。
            world.Host.Update(2.0);
            world.Flush();
            Assert.False(world.Host.IsCasting(new Id("unit.caster")));
            var originalSuccess = world.Of<SkillCastSuccessEvent>().Single(e => e.SkillId == new Id("skill.sample_cast_slow2"));
            Assert.Equal(start.CastInstanceId, originalSuccess.CastInstanceId);
            Assert.False(originalSuccess.IsInstant);
        }

        /// <summary>动作结束后，respects_gcd=true 的技能可以正常施放（节拍锁只在"他技能动作时长内"
        /// 生效，动作结束后立即释放）。</summary>
        [Fact]
        public void ActionLocked_Releases_AfterActiveCastFinishes()
        {
            var channel = J.O(
                ("id", J.S("skill.sample_cast_slow3")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(1.0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var builder = new SkillWorldBuilder();
            builder.Options.QueueWindow = 0.0; // 关闭队列，专注节拍锁本身的释放时机。
            var world = builder.SkillDef(channel).SkillDef(InstantBolt("skill.sample_bolt3")).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            Assert.True(world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_cast_slow3"), System.Array.Empty<Id>()).Success);

            var lockedDuring = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt3"), System.Array.Empty<Id>());
            Assert.False(lockedDuring.Success);
            Assert.Equal(CastFailureReason.ActionLocked, lockedDuring.Reason);

            world.Host.Update(1.0); // 动作完成。
            world.Flush();
            Assert.False(world.Host.IsCasting(new Id("unit.caster")));

            var afterAction = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_bolt3"), System.Array.Empty<Id>());
            Assert.True(afterAction.Success);
        }

        /// <summary>respects_gcd=false 但需要非瞬发读条/引导的反应类技能，在他技能动作时长内无法
        /// 安全插入（会覆盖/丢失原 CastState，见 ClassifyReactiveInsert 判断记录"槽位保护"）——
        /// 保守回退到 Busy，而不是 ActionLocked（它不是被节拍锁挡下的 respects_gcd=true 技能）。</summary>
        [Fact]
        public void Busy_Fails_ForNonInstantReactiveSkill_UnableToSafelyInsert()
        {
            var channel = J.O(
                ("id", J.S("skill.sample_cast_slow4")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(2.0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));
            var nonInstantReactive = J.O(
                ("id", J.S("skill.sample_reactive_channel")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0.5)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var builder = new SkillWorldBuilder();
            builder.Options.QueueWindow = 0.3;
            var world = builder.SkillDef(channel).SkillDef(nonInstantReactive).Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.target"));
            world.Targets.SetChain(new Id("target.chain.sample"), new Id("unit.target"));

            Assert.True(world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_cast_slow4"), System.Array.Empty<Id>()).Success);

            // 剩余读条 2.0，远大于队列窗口 0.3；nonInstantReactive 是 respects_gcd=false 但
            // cast_time=0.5（非瞬发）——不满足安全插入条件，回退到 Busy，原读条状态不受影响。
            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.sample_reactive_channel"), System.Array.Empty<Id>());
            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.Busy, result.Reason);
            Assert.True(world.Host.IsCasting(new Id("unit.caster")));
        }
    }
}
