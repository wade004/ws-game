// FeedbackBinderSummonIdFieldTests：样板游戏 B 缺口（ADR-0164）——召唤事件只带 entityId / ownerId，
// 绑定器此前只认 sourceId/casterId/unitId 与 targetId，导致 summon.created 上的 play_vfx(attach=source/target)
// 都因"没有 sourceId/targetId"被跳过。现在 self 取值链补 ownerId（召唤者），target 取值链补 entityId（被召唤的实体）。
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.VfxSfx.Contracts;
using Xunit;
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Tests.Presentation.FeedbackBinder
{
    public class FeedbackBinderSummonIdFieldTests
    {
        private static readonly Id Owner = new Id("unit.owner");
        private static readonly Id Minion = new Id("unit.minion");
        private static readonly Id SummonKey = new Id("test.summon_created");

        /// <summary>与 SummonCreatedEvent 同形：只暴露 entityId / ownerId。</summary>
        private sealed class SummonLikeEvent : IEvent, IExprReadableEvent
        {
            public Id Key => SummonKey;

            public bool TryGetField(string name, out ExprValue value)
            {
                switch (name)
                {
                    case "entityId": value = ExprValue.OfId(Minion); return true;
                    case "ownerId": value = ExprValue.OfId(Owner); return true;
                    default: value = default; return false;
                }
            }
        }

        [Theory]
        [InlineData(FeedbackAttachTarget.Source, "unit.owner")]
        [InlineData(FeedbackAttachTarget.Target, "unit.minion")]
        public void SummonShapedEvent_AttachResolvesOwnerAsSourceAndEntityAsTarget(FeedbackAttachTarget attach, string expected)
        {
            var rule = new FeedbackRule(new Id("feedback.summon"), SummonKey, null,
                new FeedbackAction[] { new PlayVfxAction(new Id("vfx.summon"), null, attach, null) });
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new RecordingFeedbackSink();
            var diag = new PresentationDiagnosticsRecorder();
            using var binder = new FeedbackBinderCore(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), new List<FeedbackRule> { rule }, sink, diagnostics: diag);

            bus.PublishImmediate(new SummonLikeEvent());

            Assert.Empty(diag.Warnings);
            var call = Assert.Single(sink.PlayVfxCalls);
            Assert.Equal(new Id(expected), call.Attach.EntityId);
        }
    }
}
