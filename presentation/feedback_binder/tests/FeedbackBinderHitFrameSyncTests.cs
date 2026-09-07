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
