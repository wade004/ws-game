// VfxPoolTests：T-M34（测试覆盖剩余项第四批）——VfxPool 独立测试。此前只经 VfxPlayer 间接覆盖
// "超容量淘汰最快到期者"一条主路径；这里直接断言池的淘汰规则（剩余寿命最短者先出、并列按
// InsertionSeq 最早者先出）、永久条目（lifetime=null）、Untrack 未知句柄、CountInCategory 与
// 按到期推进。期望值一律由规则（剩余寿命 = 初始寿命 - 累计 dt）在用例里算出，不写裸数。
// VfxPool 是 internal sealed，经 Presentation.Common.csproj 的 InternalsVisibleTo 对本测试程序集开放。
using System;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class VfxPoolTests
    {
        private const string Cat = "impact";

        private static VfxPool NewPool(int capacity, List<ParticleHandle> stopped, string category = Cat)
        {
            var options = new VfxOptions
            {
                PoolCapacityPerCategory = new Dictionary<string, int> { [category] = capacity },
            };
            return new VfxPool(options, stopped.Add);
        }

        private static ParticleHandle H(int value) => new ParticleHandle(value);

        [Fact]
        public void Constructor_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new VfxPool(null!, _ => { }));
            Assert.Throws<ArgumentNullException>(() => new VfxPool(new VfxOptions(), null!));
        }

        [Fact]
        public void Track_BelowCapacity_EvictsNothing_AndCountsPerCategory()
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity: 3, stopped);

            pool.Track(Cat, H(1), 5.0);
            pool.Track(Cat, H(2), 5.0);
            pool.Track("other", H(3), 5.0);

            Assert.Empty(stopped);
            Assert.Equal(2, pool.CountInCategory(Cat));
            Assert.Equal(1, pool.CountInCategory("other"));
            Assert.Equal(0, pool.CountInCategory("never_used"));
        }

        [Fact]
        public void Track_OverCapacity_EqualRemainingLifetime_EvictsEarliestInserted()
        {
            const int capacity = 3;
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity, stopped);

            // 并列剩余寿命：InsertionSeq 决胜，最早插入者先出。
            var handles = new List<ParticleHandle>();
            for (var i = 0; i < capacity; i++)
            {
                handles.Add(H(100 + i));
                pool.Track(Cat, handles[i], 4.0);
            }

            pool.Track(Cat, H(999), 4.0);

            Assert.Equal(new[] { handles[0] }, stopped);
            Assert.Equal(capacity, pool.CountInCategory(Cat));

            // 再来一个：现存里最早插入的是 handles[1]。
            pool.Track(Cat, H(1000), 4.0);
            Assert.Equal(new[] { handles[0], handles[1] }, stopped);
            Assert.Equal(capacity, pool.CountInCategory(Cat));
        }

        [Fact]
        public void Track_OverCapacity_EvictsShortestRemaining_EvenIfInsertedLater()
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity: 3, stopped);
            var lifetimes = new[] { 10.0, 3.0, 7.0 };
            var handles = new List<ParticleHandle>();
            for (var i = 0; i < lifetimes.Length; i++)
            {
                handles.Add(H(i + 1));
                pool.Track(Cat, handles[i], lifetimes[i]);
            }

            pool.Track(Cat, H(50), 8.0);

            // 规则：剩余寿命最短者先出——lifetimes 里最小值对应的句柄。
            var minIndex = Array.IndexOf(lifetimes, Min(lifetimes));
            Assert.Equal(new[] { handles[minIndex] }, stopped);
        }

        [Fact]
        public void Track_OverCapacity_UsesRemainingNotInitialLifetime_AfterUpdate()
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity: 2, stopped);
            const double dt = 2.5;
            var longInitial = (handle: H(1), lifetime: 5.0);
            var shortInitial = (handle: H(2), lifetime: 4.0);
            pool.Track(Cat, longInitial.handle, longInitial.lifetime);
            pool.Track(Cat, shortInitial.handle, shortInitial.lifetime);

            pool.Update(dt); // 两者都还没到期，剩余各自 = 初始 - dt，次序不变。
            Assert.Empty(stopped);

            pool.Track(Cat, H(3), 9.0);

            var remainingLong = longInitial.lifetime - dt;
            var remainingShort = shortInitial.lifetime - dt;
            var expectedVictim = remainingLong < remainingShort ? longInitial.handle : shortInitial.handle;
            Assert.Equal(new[] { expectedVictim }, stopped);
        }

        [Fact]
        public void Track_PermanentEntry_IsEvictedLast_FiniteLifetimeGoesFirst()
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity: 2, stopped);
            var permanent = H(1);
            var finite = H(2);
            pool.Track(Cat, permanent, null);
            pool.Track(Cat, finite, 100.0);

            pool.Track(Cat, H(3), null);

            // 永久条目视为剩余寿命无穷大；有限寿命的即使很长也先于它出。
            Assert.Equal(new[] { finite }, stopped);
        }

        [Fact]
        public void Track_AllPermanent_EvictsEarliestInserted_AsDeterministicFallback()
        {
            const int capacity = 2;
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity, stopped);
            var first = H(10);
            var second = H(20);
            pool.Track(Cat, first, null);
            pool.Track(Cat, second, null);

            pool.Track(Cat, H(30), null);

            Assert.Equal(new[] { first }, stopped);
            Assert.Equal(capacity, pool.CountInCategory(Cat));
        }

        [Fact]
        public void Update_PermanentEntry_NeverExpires_RegardlessOfDt()
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity: 4, stopped);
            pool.Track(Cat, H(1), null);

            pool.Update(1e9);
            pool.Update(double.MaxValue);

            Assert.Empty(stopped);
            Assert.Equal(1, pool.CountInCategory(Cat));
        }

        [Fact]
        public void Update_ExpiresWhenRemainingReachesZeroOrBelow_AndRemovesFromCount()
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity: 8, stopped);
            const double lifetime = 1.5;
            var exact = H(1);   // 恰好在累计 dt 等于寿命时到期（<= 0）。
            var longer = H(2);
            pool.Track(Cat, exact, lifetime);
            pool.Track(Cat, longer, lifetime * 2);

            pool.Update(lifetime / 2);
            Assert.Empty(stopped);
            pool.Update(lifetime / 2); // 累计 dt == lifetime（二进制可精确表示的一半相加）。

            Assert.Equal(new[] { exact }, stopped);
            Assert.Equal(1, pool.CountInCategory(Cat));

            pool.Update(lifetime);
            Assert.Equal(new[] { exact, longer }, stopped);
            Assert.Equal(0, pool.CountInCategory(Cat));
        }

        [Fact]
        public void Update_ZeroLifetimeEntry_ExpiresOnNextUpdate_EvenWithZeroDt()
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity: 8, stopped);
            var zero = H(1);
            var positive = H(2);
            pool.Track(Cat, zero, 0.0);
            pool.Track(Cat, positive, 1.0);

            pool.Update(0.0);

            Assert.Equal(new[] { zero }, stopped);
            Assert.Equal(1, pool.CountInCategory(Cat));
        }

        [Fact]
        public void Update_ExpiresEveryDueEntryInOneCall_AcrossCategories()
        {
            var stopped = new List<ParticleHandle>();
            var pool = new VfxPool(new VfxOptions(), stopped.Add);
            var a = H(1);
            var b = H(2);
            var c = H(3);
            pool.Track("cat_a", a, 1.0);
            pool.Track("cat_a", b, 1.0);
            pool.Track("cat_b", c, 1.0);

            pool.Update(1.0);

            Assert.Equal(3, stopped.Count);
            Assert.Contains(a, stopped);
            Assert.Contains(b, stopped);
            Assert.Contains(c, stopped);
            Assert.Equal(0, pool.CountInCategory("cat_a"));
            Assert.Equal(0, pool.CountInCategory("cat_b"));
        }

        [Fact]
        public void Untrack_UnknownHandle_IsSafeNoOp_NoStopCallback_CountsUnchanged()
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity: 4, stopped);
            pool.Track(Cat, H(1), 2.0);

            var ex = Record.Exception(() => pool.Untrack(H(12345)));

            Assert.Null(ex);
            Assert.Empty(stopped);
            Assert.Equal(1, pool.CountInCategory(Cat));
        }

        [Fact]
        public void Untrack_OnEmptyPool_IsSafeNoOp()
        {
            var pool = NewPool(capacity: 4, new List<ParticleHandle>());

            Assert.Null(Record.Exception(() => pool.Untrack(H(1))));
        }

        [Fact]
        public void Untrack_KnownHandle_RemovesOnlyThatEntry_AndLaterUpdateDoesNotStopIt()
        {
            var stopped = new List<ParticleHandle>();
            var pool = new VfxPool(new VfxOptions(), stopped.Add);
            var untracked = H(1);
            var keptSameCategory = H(2);
            var keptOtherCategory = H(3);
            pool.Track("cat_a", untracked, 1.0);
            pool.Track("cat_a", keptSameCategory, 1.0);
            pool.Track("cat_b", keptOtherCategory, 1.0);

            pool.Untrack(untracked);
            Assert.Equal(1, pool.CountInCategory("cat_a"));
            Assert.Equal(1, pool.CountInCategory("cat_b"));
            Assert.Empty(stopped); // 摘除不触发 stop（调用方已自行停止）。

            pool.Update(1.0);

            // 到期回收只发生在仍被跟踪的条目上；被摘除的句柄不会被重复 stop。
            Assert.DoesNotContain(untracked, stopped);
            Assert.Contains(keptSameCategory, stopped);
            Assert.Contains(keptOtherCategory, stopped);
        }

        [Fact]
        public void Untrack_FreesCapacity_SoNextTrackEvictsNothing()
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity: 2, stopped);
            pool.Track(Cat, H(1), 5.0);
            pool.Track(Cat, H(2), 5.0);

            pool.Untrack(H(1));
            pool.Track(Cat, H(3), 5.0);

            Assert.Empty(stopped);
            Assert.Equal(2, pool.CountInCategory(Cat));
        }

        [Fact]
        public void Capacity_IsPerCategory_AndFallsBackToDefaultForUnlistedCategory()
        {
            var stopped = new List<ParticleHandle>();
            var options = new VfxOptions
            {
                PoolCapacityPerCategory = new Dictionary<string, int> { ["listed"] = 1 },
                DefaultPoolCapacity = 2,
            };
            var pool = new VfxPool(options, stopped.Add);

            pool.Track("listed", H(1), 5.0);
            pool.Track("listed", H(2), 5.0);   // 容量 1：淘汰 H(1)
            pool.Track("unlisted", H(3), 5.0);
            pool.Track("unlisted", H(4), 5.0);
            pool.Track("unlisted", H(5), 5.0); // 默认容量 2：淘汰 H(3)

            Assert.Equal(new[] { H(1), H(3) }, stopped);
            Assert.Equal(options.PoolCapacityPerCategory["listed"], pool.CountInCategory("listed"));
            Assert.Equal(options.DefaultPoolCapacity, pool.CountInCategory("unlisted"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Capacity_NonPositive_MeansUnlimited_NoEviction(int capacity)
        {
            var stopped = new List<ParticleHandle>();
            var pool = NewPool(capacity, stopped);
            const int count = 50;

            for (var i = 0; i < count; i++)
            {
                pool.Track(Cat, H(i + 1), 5.0);
            }

            Assert.Empty(stopped);
            Assert.Equal(count, pool.CountInCategory(Cat));
        }

        private static double Min(double[] values)
        {
            var min = values[0];
            for (var i = 1; i < values.Length; i++)
            {
                if (values[i] < min)
                {
                    min = values[i];
                }
            }
            return min;
        }
    }
}
