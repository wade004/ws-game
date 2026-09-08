using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Xunit;
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// <see cref="FeedbackBinderCore"/> 命中帧同步集成用例（ADR-0017 决策 d）：
    /// <c>RenderOptions.HitFrameSync == AnimKeyframeDriven</c> 且注入 <see cref="Presentation.FeedbackBinder.Contracts.IHitFrameSource"/>
    /// 时，<c>sync: "hit_frame"</c> 规则的动作延迟到攻击方命中帧才播放。
    /// </summary>
    public class FeedbackBinderHitFrameSyncTests
    {
        private static List<FeedbackRule> LoadRules(params string[] rows)
        {
            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.binding"] = "[" + string.Join(",", rows) + "]",
            });
            Assert.False(report.IsBlocking);

            var rules = new List<FeedbackRule>();
            foreach (var record in registry.GetAll("feedback.binding"))
            {
                rules.Add(FeedbackRule.FromRecord(record, Core.Rules.ExprHost.RulesExprSchema.Base));
            }
            return rules;
        }

        private static CombatDamageDealtEvent DamageEvent(Id sourceId) =>
            new CombatDamageDealtEvent(sourceId, new Id("unit.target"), new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit);

        private static CombatDamageDealtEvent DamageEvent(Id sourceId, Id targetId) =>
            new CombatDamageDealtEvent(sourceId, targetId, new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit);

        /// <summary>PR150-04 用例专用：携带 <c>attackInstanceId</c> 的伤害事件（见
        /// <see cref="CombatDamageDealtEvent.AttackInstanceId"/> 判断记录）——模拟真实经
        /// <c>CastPipeline.ExecuteEffectsOnly</c> 产生的结算，与上面两个不带该字段的 <c>DamageEvent</c>
        /// 重载（模拟未经 CastPipeline 的结算，如光环周期效果）形成对照。</summary>
        private static CombatDamageDealtEvent DamageEvent(Id sourceId, Id targetId, Id attackInstanceId) =>
            new CombatDamageDealtEvent(
                sourceId, targetId, new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit,
                triggerChainDepth: 0, attackInstanceId: attackInstanceId);

        [Fact]
        public void HitFrameSyncRule_WithoutHitFrameSourceInjected_DispatchesImmediately_IgnoringSyncField()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            var rules = LoadRules(FeedbackBinderTestSupport.CritDamageRuleRow, FeedbackBinderTestSupport.NormalDamageRuleRow);

            // 未注入 hitFrameSource：即便策略是 AnimKeyframeDriven，也应回退为立即派发（见 FeedbackBinder
            // 构造函数判断记录）。
            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options);

            bus.PublishImmediate(DamageEvent(new Id("unit.hero")));

            Assert.NotEmpty(sink.FloatingTexts);
        }

        [Fact]
        public void HitFrameSyncRule_LogicDrivenDefault_DispatchesImmediately_EvenWithHitFrameSourceInjected()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource(); // 未登记任何 rig
            var rules = LoadRules(FeedbackBinderTestSupport.CritDamageRuleRow, FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, hitFrameSource: source);

            bus.PublishImmediate(DamageEvent(new Id("unit.hero")));

            Assert.NotEmpty(sink.FloatingTexts);
        }

        [Fact]
        public void HitFrameSyncRule_AnimKeyframeDriven_DelaysUntilHitFrame()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource();
            var attacker = new Id("unit.hero");
            source.RegisterRig(attacker, null!);
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            var rules = LoadRules(FeedbackBinderTestSupport.CritDamageRuleRow, FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options, hitFrameSource: source);

            bus.PublishImmediate(DamageEvent(attacker));
            Assert.Empty(sink.FloatingTexts);
            Assert.True(binder.HasPendingPlayback);
            // H5b 根治（游戏侧复核发现 2）：入队但尚未真正释放前，诊断不应报告任何具体原因。
            Assert.Null(binder.LastHitFrameSyncReleaseReason);

            source.Fire(attacker);

            Assert.NotEmpty(sink.FloatingTexts);
            Assert.False(binder.HasPendingPlayback);
            // 经命中帧事件路径释放，诊断应报告 HitFrame——与下面 HitFrameSyncRule_Timeout_ReleasesAnyway
            // 互为对照，锁定 FeedbackBinder 对外转发的诊断也随底层 HitFrameSyncPolicy 正确区分两条路径。
            Assert.Equal(HitFrameSyncReleaseReason.HitFrame, binder.LastHitFrameSyncReleaseReason);
        }

        [Fact]
        public void HitFrameSyncRule_AttackerWithoutRig_DispatchesImmediately()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource(); // 未登记攻击方
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options, hitFrameSource: source);

            bus.PublishImmediate(DamageEvent(new Id("unit.hero")));

            Assert.NotEmpty(sink.FloatingTexts);
        }

        [Fact]
        public void HitFrameSyncRule_Timeout_ReleasesAnyway()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource();
            var attacker = new Id("unit.hero");
            source.RegisterRig(attacker, null!);
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven, HitFrameSyncTimeoutSeconds = 0.2 };
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options, hitFrameSource: source);

            bus.PublishImmediate(DamageEvent(attacker));
            Assert.Empty(sink.FloatingTexts);
            Assert.Null(binder.LastHitFrameSyncReleaseReason);

            binder.Update(0.3);

            Assert.NotEmpty(sink.FloatingTexts);
            // H5b 根治（游戏侧复核发现 2）：经超时兜底路径释放，诊断应报告 Timeout，不是 HitFrame。
            Assert.Equal(HitFrameSyncReleaseReason.Timeout, binder.LastHitFrameSyncReleaseReason);
        }

        /// <summary>PR130-04 复现/回归用例：两条各自都 <c>sync: hit_frame</c>（<c>combat.damage_dealt</c>
        /// 未显式声明 <c>sync</c> 时默认即 hit_frame，见 <see cref="Presentation.FeedbackBinder.Contracts.FeedbackRule.Sync"/>
        /// 判断记录）的规则同时命中同一个事件——根治前每条规则各自入队一个独立等待项，
        /// <see cref="HitFrameSyncPolicy"/> 每次命中帧只释放同一实体最早入队的那一条，第二条规则的
        /// 动作要等到超时（0.5s 默认）才播放。根治后二者合并成同一个批次，一次命中帧应当同时释放，不
        /// 依赖任何超时。</summary>
        [Fact]
        public void HitFrameSyncRule_TwoRulesMatchSameEvent_BothReleaseTogetherOnSingleHitFrame_NoTimeoutNeeded()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource();
            var attacker = new Id("unit.hero");
            source.RegisterRig(attacker, null!);
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            // 两条规则都订阅 combat.damage_dealt、都不声明 condition（对任意一次伤害事件都命中）、
            // 都不显式声明 sync（默认落到 hit_frame，见类型注释判断记录）——精确复现"两条同命中规则"。
            var rules = LoadRules(FeedbackBinderTestSupport.PlaySfxOnlyRuleRow, FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options, hitFrameSource: source);

            bus.PublishImmediate(DamageEvent(attacker));
            Assert.Empty(sink.PlaySfxCalls);
            Assert.Empty(sink.FloatingTexts);

            source.Fire(attacker);

            Assert.NotEmpty(sink.PlaySfxCalls);
            Assert.NotEmpty(sink.FloatingTexts);
            Assert.False(binder.HasPendingPlayback, "两条规则的动作应当随同一次命中帧一起释放，不应该还有任何一条留在等待队列里");
        }

        /// <summary>PR140-04 复现/回归用例（改造自 <c>architecture/落地计划/audit-c86bfa9-20260908/
        /// evidence/probes-presentation/Program.cs</c> 的 AoE 探针）：同一个攻击者一次范围攻击对目标
        /// A/B 各自派发独立的 <c>combat.damage_dealt</c> 事件（<see cref="CombatDamageDealtEvent"/>
        /// 单源单目标形状，没有施法/攻击实例 id 可用），根治前 <see cref="HitFrameSyncPolicy"/> 每次
        /// 命中帧只释放同一攻击者最早入队的那一条——A 在第一次命中帧释放，B 要等到第二次命中帧或超时
        /// 才释放，与"同一次攻击应当同时命中"的直觉不符。根治后 A/B 共用
        /// <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/> 按攻击者维护的同一个批次
        /// token，一次命中帧原子释放二者；随后 <see cref="HitFrameSyncPolicy.BatchReleased"/> 清空该
        /// 攻击者的 token，目标 C 因此落进一批全新的批次，需要下一次命中帧才释放（不是超时——
        /// 用于和旧探针"C 靠超时兜底释放"的观测区分，证明 C 是被下一次命中帧正常释放，不是退化到兜底
        /// 路径）。</summary>
        [Fact]
        public void HitFrameSyncRule_AoeMultipleTargets_SameAttackTargetsReleaseTogether_LaterAttackStaysIndependent()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource();
            var attacker = new Id("unit.aoe.attacker");
            source.RegisterRig(attacker, null!);
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven, HitFrameSyncTimeoutSeconds = 0.5 };
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options, hitFrameSource: source);

            var targetA = new Id("unit.target.a");
            var targetB = new Id("unit.target.b");
            var targetC = new Id("unit.target.c");

            bus.PublishImmediate(DamageEvent(attacker, targetA));
            bus.PublishImmediate(DamageEvent(attacker, targetB));
            Assert.Empty(sink.FloatingTexts);
            Assert.True(binder.HasPendingPlayback);

            // 一次命中帧应当把 A、B 一并原子释放——不是只释放最早入队的 A。
            source.Fire(attacker);
            Assert.Equal(2, sink.FloatingTexts.Count);
            Assert.Equal(new[] { targetA, targetB }, new[] { sink.FloatingTexts[0].EntityId, sink.FloatingTexts[1].EntityId });
            Assert.True(binder.HasPendingPlayback == false, "A、B 所在的批次已经随这一次命中帧全部释放，不应该还有残留等待项");

            // C 属于 A/B 批次释放之后的新一批（同一攻击者，但上一批 token 已经因 BatchReleased 被清空），
            // 因此需要下一次命中帧才会释放，而不是立刻搭上一次已经空的批次。
            bus.PublishImmediate(DamageEvent(attacker, targetC));
            Assert.Equal(2, sink.FloatingTexts.Count);
            Assert.True(binder.HasPendingPlayback);

            source.Fire(attacker);
            Assert.Equal(3, sink.FloatingTexts.Count);
            Assert.Equal(targetC, sink.FloatingTexts[2].EntityId);
            Assert.False(binder.HasPendingPlayback);
        }

        /// <summary>PR150-04 复现/回归用例（攻击实例 id 遗留根治，取代上面
        /// <see cref="HitFrameSyncRule_AoeMultipleTargets_SameAttackTargetsReleaseTogether_LaterAttackStaysIndependent"/>
        /// 注释里点名的已知局限）：同一个攻击者在同一个未释放的命中帧同步窗口内发起两次
        /// <em>确实不同</em>的攻击（各自携带不同的 <c>attackInstanceId</c>，模拟极短 GCD 连续两次技能，
        /// 第二次在第一次命中帧到达前就已经结算），根治前 <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/>
        /// 只按"该攻击者是否还有未释放批次"这一时序代理合批，会把两次攻击的目标误合并成一批一起随
        /// 第一次命中帧释放；根治后二者按各自的 <c>attackInstanceId</c> 独立成批：第一次命中帧只释放
        /// 第一次攻击的目标，第二次攻击的目标要等到第二次命中帧才释放。</summary>
        [Fact]
        public void HitFrameSyncRule_TwoDistinctAttacksSameWindow_EachReleasesOnlyItsOwnBatch()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource();
            var attacker = new Id("unit.pr150_04.attacker");
            source.RegisterRig(attacker, null!);
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven, HitFrameSyncTimeoutSeconds = 0.5 };
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options, hitFrameSource: source);

            var target1 = new Id("unit.pr150_04.target1");
            var target2 = new Id("unit.pr150_04.target2");
            var instanceA = new Id("skill.cast_inst_pr150_04_a");
            var instanceB = new Id("skill.cast_inst_pr150_04_b");

            // 两次独立攻击（各自不同 attackInstanceId）都在第一次命中帧到达前完成结算——都落在同一个
            // "该攻击者尚有未释放批次"的时间窗口内，正是旧的时序代理合批会误判的场景。
            bus.PublishImmediate(DamageEvent(attacker, target1, instanceA));
            bus.PublishImmediate(DamageEvent(attacker, target2, instanceB));
            Assert.Empty(sink.FloatingTexts);
            Assert.True(binder.HasPendingPlayback);

            // 第一次命中帧：只应该释放攻击 A（target1），攻击 B（target2）必须仍在等待。
            source.Fire(attacker);
            Assert.Single(sink.FloatingTexts);
            Assert.Equal(target1, sink.FloatingTexts[0].EntityId);
            Assert.True(binder.HasPendingPlayback, "攻击 B 的批次不应该随攻击 A 的命中帧一起被误释放");

            // 第二次命中帧：释放攻击 B（target2）。
            source.Fire(attacker);
            Assert.Equal(2, sink.FloatingTexts.Count);
            Assert.Equal(target2, sink.FloatingTexts[1].EntityId);
            Assert.False(binder.HasPendingPlayback);
        }

        /// <summary>PR150-04 回归：同一次攻击命中多个目标（同一个 <c>attackInstanceId</c>）仍然按
        /// PR140-04 原有要求整批原子释放——攻击实例 id 只是换了一种更精确的方式表达"同一批"，不能
        /// 反过来破坏"同一次攻击的多个目标必须同时释放"这条既有约束。</summary>
        [Fact]
        public void HitFrameSyncRule_SameAttackInstanceIdMultipleTargets_ReleaseTogether()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource();
            var attacker = new Id("unit.pr150_04.attacker2");
            source.RegisterRig(attacker, null!);
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options, hitFrameSource: source);

            var targetA = new Id("unit.pr150_04.aoe_a");
            var targetB = new Id("unit.pr150_04.aoe_b");
            var sameInstance = new Id("skill.cast_inst_pr150_04_aoe");

            bus.PublishImmediate(DamageEvent(attacker, targetA, sameInstance));
            bus.PublishImmediate(DamageEvent(attacker, targetB, sameInstance));
            Assert.Empty(sink.FloatingTexts);

            source.Fire(attacker);

            Assert.Equal(2, sink.FloatingTexts.Count);
            Assert.False(binder.HasPendingPlayback);
        }

        /// <summary>PR150-04 回归：没有 <c>attackInstanceId</c> 的事件（如光环周期效果，未经
        /// <c>CastPipeline.ExecuteEffectsOnly</c> 产生）仍然退回旧的窗口合批兜底，并记一次诊断——见
        /// <c>FeedbackBinder.ResolveHitFrameBatchToken</c> 判断记录。诊断只在新分配兜底 token 那一刻
        /// 触发一次，不随该批次内后续每个命中重复。</summary>
        [Fact]
        public void HitFrameSyncRule_EventWithoutAttackInstanceId_FallsBackToWindowBatching_WarnsOnce()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource();
            var attacker = new Id("unit.pr150_04.fallback_attacker");
            source.RegisterRig(attacker, null!);
            var diagnostics = new PresentationDiagnosticsRecorder();
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink,
                options: options, hitFrameSource: source, diagnostics: diagnostics);

            var targetA = new Id("unit.pr150_04.fallback_a");
            var targetB = new Id("unit.pr150_04.fallback_b");

            // 两个不带 attackInstanceId 的事件（DamageEvent(sourceId, targetId) 两参重载）：退回旧的
            // "该攻击者是否还有未释放批次"合批兜底，二者应当被合成同一批（与根治前逐字相同的行为）。
            bus.PublishImmediate(DamageEvent(attacker, targetA));
            bus.PublishImmediate(DamageEvent(attacker, targetB));
            Assert.Empty(sink.FloatingTexts);

            source.Fire(attacker);

            Assert.Equal(2, sink.FloatingTexts.Count);
            Assert.False(binder.HasPendingPlayback);
            // 两次都落进兜底路径，但只在"新分配 token"那一刻（第一条事件）记一次诊断，第二条复用同一个
            // 已打开的 token，不重复警告。
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void RuleWithoutSyncField_UnaffectedByAnimKeyframeDriven()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var source = new FakeHitFrameSource();
            var attacker = new Id("unit.hero");
            source.RegisterRig(attacker, null!);
            var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            // AuraAppliedRuleRow 订阅 aura.applied（非 combat.damage_dealt），默认 sync=None。
            var rules = LoadRules(FeedbackBinderTestSupport.AuraAppliedRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options, hitFrameSource: source);

            bus.PublishImmediate(new AuraAppliedEvent(new Id("unit.target"), new Id("aura.sample"), attacker, 1));

            Assert.NotEmpty(sink.PlaySfxCalls);
        }
    }
}
