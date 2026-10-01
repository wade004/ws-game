// FeedbackBinderDispatchEdgeTests：T-M35 的 FeedbackBinder 部分（测试覆盖剩余项第四批）。
// 既有 FeedbackBinderTests 覆盖主路径；这里补动作派发的边缘/失败分支：
//   1. attach=target 而事件没有 targetId 的五处告警（play_vfx / stop_vfx / play_sfx / stop_sfx / flash），
//      以及同一事件在 attach=source 下正常派发的对照；
//   2. floating_text：Amount 缺 amount 字段、Field 缺字段的告警；挂接实体 targetId ?? selfId 的取值；
//      Field 取值；NumberFormat（默认 AwayFromZero 四舍五入、自定义格式、合并后才格式化）；
//      MergeMode.Fold 经装配传到合并器；
//   3. 条件求值异常路径（FeedbackBinder.OnEvent 的 catch）：抛异常的规则被跳过并记诊断，同事件的
//      其它规则与后续事件不受影响；宿主 Query 抛异常由 ExprEvaluator 收敛为 false。
// 期望值由规则（Math.Round AwayFromZero、求和、首值 + xN）在用例里算出，不依赖文化。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Core.Rules.ExprHost;
using Presentation.FeedbackBinder.Contracts;
using Presentation.VfxSfx.Contracts;
using Xunit;
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Tests.Presentation.FeedbackBinder
{
    public class FeedbackBinderDispatchEdgeTests
    {
        private static readonly Id Hero = new Id("unit.hero");
        private static readonly Id Wolf = new Id("unit.wolf");
        private static readonly Id EdgeEventKey = new Id("test.edge_event");
        private static readonly Id StyleNormal = new Id("feedback.style.normal");

        /// <summary>可选携带 targetId / amount / label 字段的测试事件（IExprReadableEvent）。</summary>
        private sealed class EdgeEvent : IEvent, IExprReadableEvent
        {
            public Id Key => EdgeEventKey;
            public Id SourceId { get; }
            public Id? TargetId { get; }
            public double? Amount { get; }
            public string? Label { get; }

            public EdgeEvent(Id sourceId, Id? targetId = null, double? amount = null, string? label = null)
            {
                SourceId = sourceId;
                TargetId = targetId;
                Amount = amount;
                Label = label;
            }

            public bool TryGetField(string name, out ExprValue value)
            {
                switch (name)
                {
                    case "sourceId":
                        value = ExprValue.OfId(SourceId);
                        return true;
                    case "targetId" when TargetId.HasValue:
                        value = ExprValue.OfId(TargetId.Value);
                        return true;
                    case "amount" when Amount.HasValue:
                        value = ExprValue.OfNumber(Amount.Value);
                        return true;
                    case "label" when Label != null:
                        value = ExprValue.OfString(Label);
                        return true;
                    default:
                        value = default;
                        return false;
                }
            }
        }

        /// <summary>记录全部调用（含 StopSfx 与带 attach 的 PlaySfx 重载）的 sink。</summary>
        private sealed class EdgeSink : IFeedbackSink
        {
            public readonly List<(Id Entity, Id Style, string Text)> Floating = new List<(Id, Id, string)>();
            public readonly List<(Id Id, FeedbackAttachSpec Attach)> PlayVfx_ = new List<(Id, FeedbackAttachSpec)>();
            public readonly List<(Id Id, FeedbackAttachSpec Attach)> StopVfx_ = new List<(Id, FeedbackAttachSpec)>();
            public readonly List<(Id Id, FeedbackAttachSpec Attach)> PlaySfx_ = new List<(Id, FeedbackAttachSpec)>();
            public readonly List<(Id Id, FeedbackAttachSpec Attach)> StopSfx_ = new List<(Id, FeedbackAttachSpec)>();
            public readonly List<(Id Entity, Id Profile)> Flashes = new List<(Id, Id)>();

            public void FloatingText(Id entityId, Id styleId, string text) => Floating.Add((entityId, styleId, text));
            public void PlayVfx(Id vfxId, FeedbackAttachSpec attach) => PlayVfx_.Add((vfxId, attach));
            public void StopVfx(Id vfxId, FeedbackAttachSpec attach) => StopVfx_.Add((vfxId, attach));
            public void PlaySfx(Id sfxId, Vec2? at) { }
            public void PlaySfx(Id sfxId, Vec2? at, FeedbackAttachSpec attach) => PlaySfx_.Add((sfxId, attach));
            public void StopSfx(Id sfxId, FeedbackAttachSpec attach) => StopSfx_.Add((sfxId, attach));
            public void Freeze(double durationMs) { }
            public void ShakeCamera(Id profileId) { }
            public void Flash(Id entityId, Id profileId) => Flashes.Add((entityId, profileId));
            public bool HasPendingPlayback => false;
            public event Action? PendingPlaybackChanged { add { } remove { } }

            public int TotalDispatched =>
                Floating.Count + PlayVfx_.Count + StopVfx_.Count + PlaySfx_.Count + StopSfx_.Count + Flashes.Count;
        }

        private static FeedbackRule RuleOf(string id, params FeedbackAction[] actions) =>
            new FeedbackRule(new Id(id), EdgeEventKey, null, actions);

        private static (IEventBus Bus, EdgeSink Sink, PresentationDiagnosticsRecorder Diag, FeedbackBinderCore Binder) Build(
            IReadOnlyList<FeedbackRule> rules, FeedbackOptions? options = null, IExprHostFactory? hosts = null, ExprDiagnosticsRecorder? exprDiag = null)
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new EdgeSink();
            var diag = new PresentationDiagnosticsRecorder();
            var binder = new FeedbackBinderCore(
                bus, hosts ?? new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink,
                options: options, exprDiagnostics: exprDiag, diagnostics: diag);
            return (bus, sink, diag, binder);
        }

        // -----------------------------------------------------------------
        // 1. attach=target 而事件没有 targetId：五处告警
        // -----------------------------------------------------------------

        public static IEnumerable<object[]> TargetAttachActions()
        {
            yield return new object[] { "play_vfx" };
            yield return new object[] { "stop_vfx" };
            yield return new object[] { "play_sfx" };
            yield return new object[] { "stop_sfx" };
            yield return new object[] { "flash" };
        }

        private static FeedbackAction ActionFor(string kind, FeedbackAttachTarget attach) => kind switch
        {
            "play_vfx" => new PlayVfxAction(new Id("vfx.edge"), null, attach, null),
            "stop_vfx" => new StopVfxAction(new Id("vfx.edge"), null, attach),
            "play_sfx" => new PlaySfxAction(new Id("sfx.edge"), null, attach),
            "stop_sfx" => new StopSfxAction(new Id("sfx.edge"), null, attach),
            "flash" => new FlashAction(new Id("feedback.flash.edge"), attach),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        private static int DispatchedBy(EdgeSink sink, string kind) => kind switch
        {
            "play_vfx" => sink.PlayVfx_.Count,
            "stop_vfx" => sink.StopVfx_.Count,
            "play_sfx" => sink.PlaySfx_.Count,
            "stop_sfx" => sink.StopSfx_.Count,
            "flash" => sink.Flashes.Count,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        [Theory]
        [MemberData(nameof(TargetAttachActions))]
        public void AttachTarget_EventWithoutTargetId_WarnsAndSkipsOnlyThatAction(string kind)
        {
            // 同一规则里 attach=target 的动作前后各放一个不依赖 targetId 的动作，证明只跳过它自己。
            var rule = RuleOf("feedback.edge_target",
                new FreezeAction(1),
                ActionFor(kind, FeedbackAttachTarget.Target),
                new ShakeCameraAction(new Id("feedback.shake.edge")));
            var (bus, sink, diag, binder) = Build(new[] { rule });
            using var _ = binder;

            bus.PublishImmediate(new EdgeEvent(Hero)); // 没有 targetId

            Assert.Equal(0, DispatchedBy(sink, kind));
            var warning = Assert.Single(diag.Warnings);
            Assert.Contains("没有 targetId", warning);
            // flash 的告警文案不含事件 key（其余四种含），其余部分一致。
            if (kind != "flash")
            {
                Assert.Contains(EdgeEventKey.Value, warning);
            }
            // 告警文案点名动作种类（play_vfx / stop_vfx / play_sfx / stop_sfx / flash）。
            Assert.Contains(kind, warning);
        }

        [Theory]
        [MemberData(nameof(TargetAttachActions))]
        public void AttachTarget_EventWithTargetId_Dispatches_WithTargetEntity_NoWarning(string kind)
        {
            var (bus, sink, diag, binder) = Build(new[] { RuleOf("feedback.edge_target", ActionFor(kind, FeedbackAttachTarget.Target)) });
            using var _ = binder;

            bus.PublishImmediate(new EdgeEvent(Hero, targetId: Wolf));

            Assert.Equal(1, DispatchedBy(sink, kind));
            Assert.Empty(diag.Warnings);
            var entity = kind switch
            {
                "play_vfx" => sink.PlayVfx_[0].Attach.EntityId,
                "stop_vfx" => sink.StopVfx_[0].Attach.EntityId,
                "play_sfx" => sink.PlaySfx_[0].Attach.EntityId,
                "stop_sfx" => sink.StopSfx_[0].Attach.EntityId,
                _ => sink.Flashes[0].Entity,
            };
            Assert.Equal(Wolf, entity);
        }

        [Theory]
        [MemberData(nameof(TargetAttachActions))]
        public void AttachSource_EventWithoutTargetId_DispatchesWithSourceEntity(string kind)
        {
            var (bus, sink, diag, binder) = Build(new[] { RuleOf("feedback.edge_source", ActionFor(kind, FeedbackAttachTarget.Source)) });
            using var _ = binder;

            bus.PublishImmediate(new EdgeEvent(Hero));

            Assert.Equal(1, DispatchedBy(sink, kind));
            Assert.Empty(diag.Warnings);
            var entity = kind switch
            {
                "play_vfx" => sink.PlayVfx_[0].Attach.EntityId,
                "stop_vfx" => sink.StopVfx_[0].Attach.EntityId,
                "play_sfx" => sink.PlaySfx_[0].Attach.EntityId,
                "stop_sfx" => sink.StopSfx_[0].Attach.EntityId,
                _ => sink.Flashes[0].Entity,
            };
            Assert.Equal(Hero, entity);
        }

        // -----------------------------------------------------------------
        // 2. floating_text
        // -----------------------------------------------------------------

        [Fact]
        public void FloatingText_AmountSource_EventWithoutAmount_WarnsAndSkips()
        {
            var rule = RuleOf("feedback.edge_amount", new FloatingTextAction(StyleNormal, TextSource.Amount));
            var (bus, sink, diag, binder) = Build(new[] { rule });
            using var _ = binder;

            bus.PublishImmediate(new EdgeEvent(Hero, Wolf)); // 没有 amount

            Assert.Empty(sink.Floating);
            var warning = Assert.Single(diag.Warnings);
            Assert.Contains("amount", warning);
            Assert.Contains(EdgeEventKey.Value, warning);
        }

        [Fact]
        public void FloatingText_FieldSource_MissingField_WarnsAndSkips_PresentFieldDispatchesImmediately()
        {
            var rule = RuleOf("feedback.edge_field", new FloatingTextAction(StyleNormal, TextSource.Field("label")));
            // 合并窗口 > 0 也不影响 Field 来源：非数值文本立即派发，不进合并窗口。
            var (bus, sink, diag, binder) = Build(new[] { rule }, new FeedbackOptions { MergeWindow = 5.0 });
            using var _ = binder;

            bus.PublishImmediate(new EdgeEvent(Hero, Wolf)); // 没有 label
            Assert.Empty(sink.Floating);
            var warning = Assert.Single(diag.Warnings);
            Assert.Contains("label", warning);

            bus.PublishImmediate(new EdgeEvent(Hero, Wolf, label: "Dodge"));
            var shown = Assert.Single(sink.Floating);
            Assert.Equal("Dodge", shown.Text);
            Assert.False(binder.HasPendingPlayback); // 没有进入合并窗口。
        }

        [Fact]
        public void FloatingText_EntityIsTargetWhenPresent_OtherwiseSource()
        {
            var rule = RuleOf("feedback.edge_entity", new FloatingTextAction(StyleNormal, TextSource.Amount));
            var (bus, sink, _, binder) = Build(new[] { rule });
            using var _ = binder;

            bus.PublishImmediate(new EdgeEvent(Hero, Wolf, amount: 3));
            bus.PublishImmediate(new EdgeEvent(Hero, targetId: null, amount: 4));

            Assert.Equal(2, sink.Floating.Count);
            Assert.Equal(Wolf, sink.Floating[0].Entity);
            Assert.Equal(Hero, sink.Floating[1].Entity);
        }

        [Fact]
        public void NumberFormat_Default_RoundsHalfAwayFromZero_InvariantCulture()
        {
            var rule = RuleOf("feedback.edge_round", new FloatingTextAction(StyleNormal, TextSource.Amount));
            var (bus, sink, _, binder) = Build(new[] { rule }); // 默认 NumberFormat，MergeWindow=0 立即派发
            using var _ = binder;

            var amounts = new[] { 0.5, 1.5, 2.5, 2.4999, 41.6, -0.5, -2.5 };
            foreach (var amount in amounts)
            {
                bus.PublishImmediate(new EdgeEvent(Hero, Wolf, amount));
            }

            Assert.Equal(amounts.Length, sink.Floating.Count);
            for (var i = 0; i < amounts.Length; i++)
            {
                var expected = Math.Round(amounts[i], MidpointRounding.AwayFromZero)
                    .ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(expected, sink.Floating[i].Text);
            }
        }

        [Fact]
        public void NumberFormat_Custom_IsUsed_AndAppliedToTheMergedSum_NotToEachPart()
        {
            var rule = RuleOf("feedback.edge_fmt", new FloatingTextAction(StyleNormal, TextSource.Amount));
            var options = new FeedbackOptions
            {
                MergeWindow = 0.5,
                MergeMode = MergeMode.Sum,
                NumberFormat = v => "<" + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ">",
            };
            var (bus, sink, _, binder) = Build(new[] { rule }, options);
            using var _ = binder;

            const double a = 0.4, b = 0.4;
            bus.PublishImmediate(new EdgeEvent(Hero, Wolf, a));
            bus.PublishImmediate(new EdgeEvent(Hero, Wolf, b));
            binder.Update(options.MergeWindow + 0.01);

            var shown = Assert.Single(sink.Floating);
            Assert.Equal(options.NumberFormat(a + b), shown.Text); // 先求和再格式化。
        }

        [Fact]
        public void MergeMode_Fold_ShowsFirstAmountWithCount_ThroughBinder()
        {
            var rule = RuleOf("feedback.edge_fold", new FloatingTextAction(StyleNormal, TextSource.Amount));
            var options = new FeedbackOptions { MergeWindow = 0.2, MergeMode = MergeMode.Fold };
            var (bus, sink, _, binder) = Build(new[] { rule }, options);
            using var _ = binder;

            var amounts = new[] { 7.0, 9.0, 3.0 };
            foreach (var amount in amounts)
            {
                bus.PublishImmediate(new EdgeEvent(Hero, Wolf, amount));
            }
            Assert.Empty(sink.Floating);

            binder.Update(options.MergeWindow + 0.01);

            var shown = Assert.Single(sink.Floating);
            Assert.Equal(options.NumberFormat(amounts[0]) + " x" + amounts.Length, shown.Text);
        }

        // -----------------------------------------------------------------
        // 3. 条件求值异常路径
        // -----------------------------------------------------------------

        /// <summary>求值器真正抛出（而非收敛为 false）的条件：比较运算符取值不在枚举内，
        /// CompareNumeric 的 default 分支抛 ArgumentOutOfRangeException。未知节点类型与宿主 Query 异常
        /// 都被求值器收敛为 false，到不了 FeedbackBinder 的 catch；这是少数能走到它的路径。</summary>
        private static ExprNode ExplodingCondition() => new ExprCompareNode(
            new ExprLiteralNode(ExprValue.OfInt(1)), (ExprCompareOp)999, new ExprLiteralNode(ExprValue.OfInt(2)));

        [Fact]
        public void Condition_EvaluationThrows_RuleSkippedWithDiagnostic_OtherRuleAndLaterEventsUnaffected()
        {
            var rules = new[]
            {
                new FeedbackRule(new Id("feedback.a_boom"), EdgeEventKey, ExplodingCondition(),
                    new FeedbackAction[] { new FreezeAction(1), new FlashAction(new Id("feedback.flash.boom"), FeedbackAttachTarget.Source) }),
                RuleOf("feedback.b_survives", new FlashAction(new Id("feedback.flash.ok"), FeedbackAttachTarget.Source)),
            };
            var (bus, sink, diag, binder) = Build(rules);
            using var _ = binder;

            bus.PublishImmediate(new EdgeEvent(Hero));

            // 抛异常的规则一个动作都没派发（含排在条件后的 flash）；同事件另一条规则照常。
            var flash = Assert.Single(sink.Flashes);
            Assert.Equal(new Id("feedback.flash.ok"), flash.Profile);
            var warning = Assert.Single(diag.Warnings);
            Assert.Contains("feedback.a_boom", warning);
            Assert.Contains(EdgeEventKey.Value, warning);

            // 后续事件不受影响：b_survives 再次派发，a_boom 再记一条（不累积状态）。
            bus.PublishImmediate(new EdgeEvent(Hero));
            Assert.Equal(2, sink.Flashes.Count);
            Assert.Equal(2, diag.Warnings.Count);
        }

        private sealed class ThrowingQueryHostFactory : IExprHostFactory
        {
            public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host();

            private sealed class Host : IExprHost
            {
                public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) =>
                    throw new InvalidOperationException("host query exploded");
            }
        }

        [Fact]
        public void Condition_HostQueryThrows_EvaluatorConvergesToFalse_RuleSkipped_ExprErrorRecorded()
        {
            var condition = new ExprReferenceNode(ExprGroups.Event, "is_crit", Array.Empty<ExprNode>());
            var rules = new[]
            {
                new FeedbackRule(new Id("feedback.a_cond"), EdgeEventKey, condition,
                    new FeedbackAction[] { new FlashAction(new Id("feedback.flash.cond"), FeedbackAttachTarget.Source) }),
                RuleOf("feedback.b_plain", new FlashAction(new Id("feedback.flash.plain"), FeedbackAttachTarget.Source)),
            };
            var exprDiag = new ExprDiagnosticsRecorder();
            var (bus, sink, diag, binder) = Build(rules, hosts: new ThrowingQueryHostFactory(), exprDiag: exprDiag);
            using var _ = binder;

            bus.PublishImmediate(new EdgeEvent(Hero));

            // 宿主异常由 ExprEvaluator 收敛：条件按 false 处理，不走 FeedbackBinder 的 catch（无 Warn），
            // 而是记入 Expr 诊断；同事件另一条规则照常派发。
            var flash = Assert.Single(sink.Flashes);
            Assert.Equal(new Id("feedback.flash.plain"), flash.Profile);
            Assert.True(exprDiag.HasErrors);
            Assert.Empty(diag.Warnings);
        }

        [Fact]
        public void Condition_NonBoolResult_TreatedAsFalse_WithExprError()
        {
            // 条件求值结果不是 Bool：ExprEvaluator.EvaluateBool 记错误并返回 false，规则不执行。
            var condition = new ExprLiteralNode(ExprValue.OfInt(1));
            var rules = new[]
            {
                new FeedbackRule(new Id("feedback.a_nonbool"), EdgeEventKey, condition,
                    new FeedbackAction[] { new FlashAction(new Id("feedback.flash.nonbool"), FeedbackAttachTarget.Source) }),
            };
            var exprDiag = new ExprDiagnosticsRecorder();
            var (bus, sink, _, binder) = Build(rules, exprDiag: exprDiag);
            using var _ = binder;

            bus.PublishImmediate(new EdgeEvent(Hero));

            Assert.Empty(sink.Flashes);
            Assert.True(exprDiag.HasErrors);
        }
    }
}
