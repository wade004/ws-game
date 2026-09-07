using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
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

            source.Fire(attacker);

            Assert.NotEmpty(sink.FloatingTexts);
            Assert.False(binder.HasPendingPlayback);
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

            binder.Update(0.3);

            Assert.NotEmpty(sink.FloatingTexts);
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
