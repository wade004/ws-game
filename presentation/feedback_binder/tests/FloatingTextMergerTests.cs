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

        // -----------------------------------------------------------------
        // T-M35（测试覆盖剩余项第四批）：同键混投 / FlushAll / 窗口边界 / 构造守卫
        // -----------------------------------------------------------------

        [Fact]
        public void Offer_SameKeyMixedModes_FirstOfferDecidesMode_LaterModeIgnored()
        {
            var (merger, dispatched) = Build(window: 1.0);

            merger.Offer(Entity, Style, 7, MergeMode.Fold);
            merger.Offer(Entity, Style, 9, MergeMode.Sum); // 同键后到的模式不生效

            merger.Update(1.1);

            var single = Assert.Single(dispatched);
            Assert.Equal("7 x2", single.Item3);
        }

        [Fact]
        public void Offer_SameEntityDifferentStyles_AreSeparateWindows()
        {
            var (merger, dispatched) = Build(window: 1.0);
            var other = new Id("feedback.style.crit");

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Offer(Entity, other, 4, MergeMode.Sum);
            merger.Update(1.1);

            Assert.Equal(2, dispatched.Count);
            Assert.Contains(dispatched, d => d.Item2.Equals(Style) && d.Item3 == "10");
            Assert.Contains(dispatched, d => d.Item2.Equals(other) && d.Item3 == "4");
        }

        [Fact]
        public void Update_ExactlyWindowLength_Flushes()
        {
            const double window = 0.5;
            var (merger, dispatched) = Build(window);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Update(window); // RemainingWindow == 0 视为到期

            Assert.Single(dispatched);
            Assert.False(merger.HasPendingMerges);
        }

        [Fact]
        public void Window_IsFixedFromFirstOffer_LaterOffersDoNotExtendIt()
        {
            const double window = 1.0;
            var (merger, dispatched) = Build(window);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Update(window * 0.75);
            merger.Offer(Entity, Style, 5, MergeMode.Sum); // 若延长窗口，下一步不会到期
            Assert.Empty(dispatched);
            merger.Update(window * 0.25);

            var single = Assert.Single(dispatched);
            Assert.Equal("15", single.Item3);
        }

        [Fact]
        public void Offer_AfterWindowExpired_StartsANewWindow()
        {
            var (merger, dispatched) = Build(window: 0.5);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Update(0.6);
            merger.Offer(Entity, Style, 3, MergeMode.Sum);
            merger.Update(0.6);

            Assert.Equal(2, dispatched.Count);
            Assert.Equal("10", dispatched[0].Item3);
            Assert.Equal("3", dispatched[1].Item3);
        }

        [Fact]
        public void Update_SeveralWindowsExpireInOneCall_AllFlushed_NoneLeft()
        {
            var (merger, dispatched) = Build(window: 0.5);
            merger.Offer(Entity, Style, 1, MergeMode.Sum);
            merger.Offer(new Id("unit.bear"), Style, 2, MergeMode.Sum);
            merger.Offer(new Id("unit.boar"), Style, 3, MergeMode.Sum);

            merger.Update(0.6);

            Assert.Equal(3, dispatched.Count);
            Assert.False(merger.HasPendingMerges);
        }

        [Fact]
        public void FlushAll_DispatchesEveryOpenWindow_ClearsPending_AndLaterUpdateDoesNotRedispatch()
        {
            var (merger, dispatched) = Build(window: 5.0);
            var bear = new Id("unit.bear");
            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Offer(Entity, Style, 5, MergeMode.Sum);
            merger.Offer(bear, Style, 4, MergeMode.Fold);

            merger.FlushAll();

            Assert.Equal(2, dispatched.Count);
            Assert.Contains(dispatched, d => d.Item1.Equals(Entity) && d.Item3 == "15");
            Assert.Contains(dispatched, d => d.Item1.Equals(bear) && d.Item3 == "4 x1");
            Assert.False(merger.HasPendingMerges);

            merger.Update(100.0);
            Assert.Equal(2, dispatched.Count);
        }

        [Fact]
        public void FlushAll_WithNothingPending_IsNoOp()
        {
            var (merger, dispatched) = Build(window: 1.0);

            merger.FlushAll();

            Assert.Empty(dispatched);
        }

        [Fact]
        public void Offer_FoldWithSingleHit_ShowsFirstAmountTimesOne()
        {
            var (merger, dispatched) = Build(window: 1.0);

            merger.Offer(Entity, Style, 12, MergeMode.Fold);
            merger.Update(1.1);

            Assert.Equal("12 x1", Assert.Single(dispatched).Item3);
        }

        [Fact]
        public void Offer_NegativeWindow_BehavesLikeNoWindow_DispatchesImmediately()
        {
            var (merger, dispatched) = Build(window: -1.0);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.Offer(Entity, Style, 5, MergeMode.Sum);

            Assert.Equal(2, dispatched.Count);
            Assert.False(merger.HasPendingMerges);
        }

        [Fact]
        public void OfferImmediate_DoesNotDisturbAnOpenWindowForTheSameKey()
        {
            var (merger, dispatched) = Build(window: 1.0);

            merger.Offer(Entity, Style, 10, MergeMode.Sum);
            merger.OfferImmediate(Entity, Style, "Dodge");
            Assert.Single(dispatched);
            Assert.True(merger.HasPendingMerges);

            merger.Update(1.1);
            Assert.Equal(2, dispatched.Count);
            Assert.Equal("10", dispatched[1].Item3);
        }

        [Fact]
        public void Constructor_NullDelegates_Throw()
        {
            Assert.Throws<System.ArgumentNullException>(() => new FloatingTextMerger(1.0, null!, (_, __, ___) => { }));
            Assert.Throws<System.ArgumentNullException>(() => new FloatingTextMerger(1.0, v => v.ToString("0"), null!));
        }
    }
}
