using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;
using FloatingTextMerger = Presentation.FeedbackBinder.Core.FloatingTextMerger;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    public class FloatingTextMergerTests
    {
        private static readonly Id Entity = new Id("unit.wolf");
        private static readonly Id Style = new Id("feedback.style.normal");

        private static (FloatingTextMerger Merger, List<(Id, Id, string)> Dispatched) Build(double window, MergeMode mode = MergeMode.Sum)
        {
            var dispatched = new List<(Id, Id, string)>();
            var merger = new FloatingTextMerger(window, v => v.ToString("0"), (e, s, t) => dispatched.Add((e, s, t)));
            return (merger, dispatched);
        }

        [Fact]
        public void Offer_NoWindow_DispatchesImmediately()
        {
            var (merger, dispatched) = Build(window: 0);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);

            Assert.Single(dispatched);
            Assert.Equal("10", dispatched[0].Item3);
        }

        [Fact]
        public void Offer_Sum_AccumulatesWithinWindow_UntilFlushed()
        {
            var (merger, dispatched) = Build(window: 1.0, MergeMode.Sum);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Offer(Entity, Style, 5, MergeMode.Sum);
            Assert.Empty(dispatched);

            merger.Update(1.1);

            Assert.Single(dispatched);
            Assert.Equal("15", dispatched[0].Item3);
        }

        [Fact]
        public void Offer_Fold_UsesFirstAmountWithCount()
        {
            var (merger, dispatched) = Build(window: 1.0, MergeMode.Fold);

            merger.Offer(Entity, Style, 7, MergeMode.Fold);
            merger.Offer(Entity, Style, 9, MergeMode.Fold);
            merger.Offer(Entity, Style, 3, MergeMode.Fold);

            merger.Update(1.1);

            Assert.Single(dispatched);
            Assert.Equal("7 x3", dispatched[0].Item3);
        }

        [Fact]
        public void Update_DoesNotFlushBeforeWindowElapses()
        {
            var (merger, dispatched) = Build(window: 1.0);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Update(0.5);

            Assert.Empty(dispatched);
        }

        [Fact]
        public void Update_FlushesOnlyOnce_ForExpiredWindow()
        {
            var (merger, dispatched) = Build(window: 0.5);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Update(0.6);
            merger.Update(0.6);

            Assert.Single(dispatched);
        }

        [Fact]
        public void OfferImmediate_BypassesMerging()
        {
            var (merger, dispatched) = Build(window: 1.0);

            merger.OfferImmediate(Entity, Style, "闪避");

            Assert.Single(dispatched);
            Assert.Equal("闪避", dispatched[0].Item3);
        }

        [Fact]
        public void DifferentEntities_DoNotMergeTogether()
        {
            var (merger, dispatched) = Build(window: 1.0);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Offer(new Id("unit.bear"), Style, 4, MergeMode.Sum);

            merger.Update(1.1);

            Assert.Equal(2, dispatched.Count);
        }

        // ------------------------------------------------------------------
        // GP-PRES-03 跟进：HasPendingMerges——供 FeedbackBinder.HasPendingPlayback 组合使用。
        // ------------------------------------------------------------------

        [Fact]
        public void HasPendingMerges_FalseInitially_AndAfterNoWindowOffer()
        {
            var (merger, _) = Build(window: 0);

            Assert.False(merger.HasPendingMerges);

            // window<=0 时 Offer 恒立即派发，不进入 _pending，HasPendingMerges 应始终为 false。
            merger.Offer(Entity, Style, 10, MergeMode.Sum);

            Assert.False(merger.HasPendingMerges);
        }

        [Fact]
        public void HasPendingMerges_TrueWhileWindowOpen_FalseAfterExpiryOrFlushAll()
        {
            var (merger, _) = Build(window: 1.0, MergeMode.Sum);

            Assert.False(merger.HasPendingMerges);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            Assert.True(merger.HasPendingMerges);

            merger.Update(0.5);
            Assert.True(merger.HasPendingMerges, "窗口未到期，仍应有待合并飘字");

            merger.Update(0.6);
            Assert.False(merger.HasPendingMerges, "窗口到期并已 Flush，不应再有待合并飘字");

            // 另一路径：FlushAll 立即结算，同样应清空 _pending。
            merger.Offer(Entity, Style, 3, MergeMode.Sum);
            Assert.True(merger.HasPendingMerges);

            merger.FlushAll();
            Assert.False(merger.HasPendingMerges);
        }
    }
}
