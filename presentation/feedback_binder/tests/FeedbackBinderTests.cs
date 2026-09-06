using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Tests.Presentation.FeedbackBinder
{
    public class FeedbackBinderTests
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

        // 两条规则按 event.is_crit 分流（见 FeedbackBinderTestSupport 判断记录：P4-3 已在
        // RulesExprHostFactory.Host.QueryEvent 补上 snake_case -> camelCase 的字段名映射，
        // event.is_crit 现在可以直接命中 IExprReadableEvent 登记的 "isCrit" 字段，按 09 第 6.1 节
        // 原文示例"同一事件按条件分流到不同表现"实现，不再需要绕道 amount 阈值）。
        [Fact]
        public void OnEvent_ConditionTrue_DispatchesFloatingTextAndShakeCamera()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.CritDamageRuleRow, FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 42.0, isCrit: true, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Single(sink.FloatingTexts);
            Assert.Equal(new Id("unit.wolf"), sink.FloatingTexts[0].EntityId);
            Assert.Equal(new Id("feedback.style.crit"), sink.FloatingTexts[0].StyleId);
            Assert.Equal("42", sink.FloatingTexts[0].Text);

            Assert.Single(sink.Shakes);
            Assert.Equal(new Id("feedback.shake.crit"), sink.Shakes[0]);
        }

        [Fact]
        public void OnEvent_ConditionFalse_RoutesToTheOtherRule()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.CritDamageRuleRow, FeedbackBinderTestSupport.NormalDamageRuleRow);

            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Single(sink.FloatingTexts);
            Assert.Equal(new Id("feedback.style.normal"), sink.FloatingTexts[0].StyleId);
            Assert.Empty(sink.Shakes);
        }

        [Fact]
        public void OnEvent_RuleWithoutCondition_AlwaysDispatchesAllActions()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.AuraAppliedRuleRow);

            var displayRegistry = new FakeDisplayInfoRegistry(new Dictionary<Id, DisplayInfo>
            {
                [new Id("skill.aura.burning")] = FakeDisplayInfoRegistry.Simple(
                    new Id("display.burning"), new Id("skill.aura.burning"), DisplayCategory.Aura, new Id("vfx.burning_apply"), null),
            });
            var resolver = new DisplayInfoResolver(displayRegistry);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, displayInfoResolver: resolver);

            var evt = new AuraAppliedEvent(new Id("unit.wolf"), new Id("skill.aura.burning"), new Id("unit.hero"), 1);
            bus.PublishImmediate(evt);

            Assert.Single(sink.PlayVfxCalls);
            Assert.Equal(new Id("vfx.burning_apply"), sink.PlayVfxCalls[0].VfxId);
            Assert.Equal(new Id("unit.wolf"), sink.PlayVfxCalls[0].Attach.EntityId);

            Assert.Single(sink.PlaySfxCalls);
            Assert.Equal(new Id("sfx.buff_apply"), sink.PlaySfxCalls[0].SfxId);

            Assert.Single(sink.Flashes);
            Assert.Equal(new Id("unit.wolf"), sink.Flashes[0].EntityId);

            Assert.Single(sink.Freezes);
            Assert.Equal(40.0, sink.Freezes[0]);
        }

        // ------------------------------------------------------------------
        // 缺口 7 恢复：text_source: literal 经注入的 textResolver 解析真正文案（09 第 7.3 节）。
        // ------------------------------------------------------------------

        private const string LiteralFloatingTextRuleRow =
            "{\"id\": \"feedback.sample_literal_text\", \"event\": \"aura.applied\", \"actions\": [" +
            "{\"kind\": \"floating_text\", \"params\": {\"style_id\": \"feedback.style.buff\", \"text_source\": \"literal:l10n.combat.dodge\"}}" +
            "]}";

        [Fact]
        public void TextSourceLiteral_NoResolverInjected_FallsBackToRawTextKey()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(LiteralFloatingTextRuleRow);

            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink);

            var evt = new AuraAppliedEvent(new Id("unit.wolf"), new Id("skill.aura.burning"), new Id("unit.hero"), 1);
            bus.PublishImmediate(evt);

            Assert.Single(sink.FloatingTexts);
            Assert.Equal("l10n.combat.dodge", sink.FloatingTexts[0].Text);
        }

        [Fact]
        public void TextSourceLiteral_ResolverInjected_ResolvesRealText()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(LiteralFloatingTextRuleRow);

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink,
                textResolver: key => key.Equals(new Id("l10n.combat.dodge")) ? "闪避！" : key.Value);

            var evt = new AuraAppliedEvent(new Id("unit.wolf"), new Id("skill.aura.burning"), new Id("unit.hero"), 1);
            bus.PublishImmediate(evt);

            Assert.Single(sink.FloatingTexts);
            Assert.Equal("闪避！", sink.FloatingTexts[0].Text);
        }

        [Fact]
        public void FromDisplaySkill_MissingDisplayMapEntry_SkipsActionWithoutCrashing()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.AuraAppliedRuleRow);

            var displayRegistry = new FakeDisplayInfoRegistry(new Dictionary<Id, DisplayInfo>());
            var resolver = new DisplayInfoResolver(displayRegistry);
            var diagnostics = new PresentationDiagnosticsRecorder();

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink,
                displayInfoResolver: resolver, diagnostics: diagnostics);

            var evt = new AuraAppliedEvent(new Id("unit.wolf"), new Id("skill.aura.burning"), new Id("unit.hero"), 1);
            bus.PublishImmediate(evt);

            Assert.Empty(sink.PlayVfxCalls);
            Assert.NotEmpty(diagnostics.Warnings);
            // 其余动作（play_sfx/flash/freeze）不受 play_vfx 失败影响，照常派发。
            Assert.Single(sink.PlaySfxCalls);
        }

        [Fact]
        public void FromDisplayTarget_ResolvesViaEntityLogicalIdResolver_WhenInjected()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.TargetVfxFromDisplayRuleRow);

            var displayRegistry = new FakeDisplayInfoRegistry(new Dictionary<Id, DisplayInfo>
            {
                [new Id("creature.wolf")] = FakeDisplayInfoRegistry.Simple(
                    new Id("display.wolf"), new Id("creature.wolf"), DisplayCategory.Creature, new Id("vfx.wolf_hit"), null),
            });
            var resolver = new DisplayInfoResolver(displayRegistry);
            EntityLogicalIdResolver entityLogicalIdResolver = entityId =>
                entityId.Equals(new Id("unit.wolf")) ? new Id("creature.wolf") : (Id?)null;

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink,
                displayInfoResolver: resolver, entityLogicalIdResolver: entityLogicalIdResolver);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Single(sink.PlayVfxCalls);
            Assert.Equal(new Id("vfx.wolf_hit"), sink.PlayVfxCalls[0].VfxId);
            Assert.Equal(new Id("unit.wolf"), sink.PlayVfxCalls[0].Attach.EntityId);
        }

        [Fact]
        public void FromDisplayTarget_ResolvesViaUnitAccess_WhenNoResolverInjected()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.TargetVfxFromDisplayRuleRow);

            var displayRegistry = new FakeDisplayInfoRegistry(new Dictionary<Id, DisplayInfo>
            {
                [new Id("creature.wolf")] = FakeDisplayInfoRegistry.Simple(
                    new Id("display.wolf"), new Id("creature.wolf"), DisplayCategory.Creature, new Id("vfx.wolf_hit"), null),
            });
            var resolver = new DisplayInfoResolver(displayRegistry);
            var unitAccess = new FakeUnitAccess(new Dictionary<Id, Id>
            {
                [new Id("unit.wolf")] = new Id("creature.wolf"),
            });

            // 判断记录：本用例不注入 EntityLogicalIdResolver，验证 P4-2 的默认路径（
            // FeedbackBinder.ResolveEntityLogicalId 直接经 IUnitAccess.GetTemplateId 取模板 id）
            // 独立生效，不再强依赖单独的解析委托。
            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink,
                displayInfoResolver: resolver, unitAccess: unitAccess);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Single(sink.PlayVfxCalls);
            Assert.Equal(new Id("vfx.wolf_hit"), sink.PlayVfxCalls[0].VfxId);
            Assert.Equal(new Id("unit.wolf"), sink.PlayVfxCalls[0].Attach.EntityId);
        }

        [Fact]
        public void FromDisplayTarget_NeitherResolverNorUnitAccessInjected_SkipsActionWithDiagnostic()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.TargetVfxFromDisplayRuleRow);

            var displayRegistry = new FakeDisplayInfoRegistry(new Dictionary<Id, DisplayInfo>());
            var resolver = new DisplayInfoResolver(displayRegistry);
            var diagnostics = new PresentationDiagnosticsRecorder();

            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink,
                displayInfoResolver: resolver, diagnostics: diagnostics);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Empty(sink.PlayVfxCalls);
            Assert.NotEmpty(diagnostics.Warnings);
        }

        [Fact]
        public void QueueMode_Sequential_FiresPlaybackFinished_AfterDraining()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            var finishedCount = 0;
            bus.Subscribe(EventKeys.PresentationPlaybackFinished, _ => finishedCount++);

            var options = new FeedbackOptions { QueueMode = QueueMode.Sequential, SequentialStepSeconds = 0.1 };
            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            // 动作已入队但还未执行（Sequential 模式）。
            Assert.Empty(sink.FloatingTexts);
            Assert.Equal(0, finishedCount);

            binder.Update(0.1);

            Assert.Single(sink.FloatingTexts);
            Assert.Equal(1, finishedCount);
        }

        [Fact]
        public void QueueMode_Immediate_NeverFiresPlaybackFinished()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            var finishedCount = 0;
            bus.Subscribe(EventKeys.PresentationPlaybackFinished, _ => finishedCount++);

            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Single(sink.FloatingTexts);
            Assert.Equal(0, finishedCount);
        }

        [Fact]
        public void Merge_SumsMultipleAmountFloatingTexts_WithinWindow()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            var options = new FeedbackOptions { MergeWindow = 0.2, MergeMode = MergeMode.Sum };
            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options);

            var target = new Id("unit.wolf");
            bus.PublishImmediate(new CombatDamageDealtEvent(new Id("unit.hero"), target, new Id("skill.school.physical"), 10.0, false, HitResult.Hit));
            bus.PublishImmediate(new CombatDamageDealtEvent(new Id("unit.hero"), target, new Id("skill.school.physical"), 8.0, false, HitResult.Hit));

            Assert.Empty(sink.FloatingTexts);

            binder.Update(0.25);

            Assert.Single(sink.FloatingTexts);
            Assert.Equal("18", sink.FloatingTexts[0].Text);
        }

        [Fact]
        public void Dispose_StopsReceivingFurtherEvents()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink);
            binder.Dispose();

            bus.PublishImmediate(new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 5.0, false, HitResult.Hit));

            Assert.Empty(sink.FloatingTexts);
        }

        // ------------------------------------------------------------------
        // GP-PRES-03 跟进：HasPendingPlayback 统一查询 + playback_finished 推迟到无待合并飘字时发出。
        // ------------------------------------------------------------------

        [Fact]
        public void HasPendingPlayback_FalseWhenNothingDispatched()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            var options = new FeedbackOptions { QueueMode = QueueMode.Sequential, SequentialStepSeconds = 0.1 };
            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options);

            Assert.False(binder.HasPendingPlayback);
        }

        [Fact]
        public void HasPendingPlayback_TracksQueue_WhenNoMergeWindow()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            var finishedCount = 0;
            bus.Subscribe(EventKeys.PresentationPlaybackFinished, _ => finishedCount++);

            var options = new FeedbackOptions { QueueMode = QueueMode.Sequential, SequentialStepSeconds = 0.1, MergeWindow = 0.0 };
            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.True(binder.HasPendingPlayback);

            binder.Update(0.1);

            Assert.False(binder.HasPendingPlayback);
            Assert.Equal(1, finishedCount);
        }

        // 本用例是本次收口的核心新增场景：MergeWindow > 0 时数值飘字暂存在 FloatingTextMerger 内部，
        // 尚未进入 PlaybackQueue——Queue.PendingCount 恒为 0，但 HasPendingPlayback 必须仍为 true，
        // 直到窗口到期、合并结果真正播出。按 PlaybackQueue 的逐步语义精确推算所需 Update 次数：
        // Offer 后 RemainingWindow=1.0；Update(0.5) 后剩 0.5（未到期）；Update(0.6) 后剩 -0.1（到期，
        // Flush→DispatchFloatingText→Queue.Enqueue），随后同一次 FeedbackBinder.Update 内
        // Queue.Update(0.6) 紧接着执行——彼时队列从空变为 1 项，_elapsedInCurrentStep 从 0 起累加
        // 0.6*1.0=0.6，SequentialStepSeconds=0.1，0.6>=0.1 满足一步的条件，同一次 Update 调用内即可
        // 播出，不需要额外的 Update。
        [Fact]
        public void HasPendingPlayback_StaysTrueWhileMergeWindowOpen_QueueEmpty_UntilPlayed()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var rules = LoadRules(FeedbackBinderTestSupport.NormalDamageRuleRow);

            var finishedCount = 0;
            bus.Subscribe(EventKeys.PresentationPlaybackFinished, _ => finishedCount++);

            var options = new FeedbackOptions
            {
                QueueMode = QueueMode.Sequential,
                SequentialStepSeconds = 0.1,
                MergeWindow = 1.0,
                MergeMode = MergeMode.Sum,
            };
            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Equal(0, binder.Queue.PendingCount);
            Assert.True(binder.HasPendingPlayback, "飘字还在合并窗口内，队列虽空也不应视为播完");
            Assert.Empty(sink.FloatingTexts);
            Assert.Equal(0, finishedCount);

            binder.Update(0.5);

            Assert.True(binder.HasPendingPlayback, "窗口未到期，仍应为 true");
            Assert.Empty(sink.FloatingTexts);
            Assert.Equal(0, finishedCount);

            binder.Update(0.6);

            Assert.Single(sink.FloatingTexts);
            Assert.False(binder.HasPendingPlayback);
            Assert.Equal(1, finishedCount);
        }

        [Fact]
        public void PlaybackFinished_DeferredWhileMergePending_MixedActions()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            // CritDamageRuleRow：同一条规则的 actions 里既有 floating_text(amount)（走合并窗口）又有
            // shake_camera（非飘字动作，直接入队），isCrit: true 触发其 condition。
            var rules = LoadRules(FeedbackBinderTestSupport.CritDamageRuleRow);

            var finishedCount = 0;
            bus.Subscribe(EventKeys.PresentationPlaybackFinished, _ => finishedCount++);

            var options = new FeedbackOptions
            {
                QueueMode = QueueMode.Sequential,
                SequentialStepSeconds = 0.1,
                MergeWindow = 1.0,
                MergeMode = MergeMode.Sum,
            };
            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options);

            var evt = new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.wolf"), new Id("skill.school.physical"), 42.0, isCrit: true, HitResult.Hit);
            bus.PublishImmediate(evt);

            // floating_text 进了合并窗口（不进队列），shake_camera 直接入队。
            Assert.Equal(1, binder.Queue.PendingCount);
            Assert.True(binder.HasPendingPlayback);

            binder.Update(0.1);

            // 队列播空（shake_camera 已执行），但合并窗口仍未到期——playback_finished 必须推迟，
            // 不能在这里发出；这正是本次收口要根治的场景。
            Assert.Equal(0, binder.Queue.PendingCount);
            Assert.Single(sink.Shakes);
            Assert.Equal(0, finishedCount);
            Assert.True(binder.HasPendingPlayback, "飘字仍在合并窗口内，不应视为播完");

            // 推进到窗口到期（剩余 0.9）并让合并结果播出。
            binder.Update(1.0);

            Assert.Single(sink.FloatingTexts);
            Assert.Equal(1, finishedCount);
            Assert.False(binder.HasPendingPlayback);
        }
    }
}
