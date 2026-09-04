using System.Collections.Generic;
using Presentation.FeedbackBinder.Contracts;
using PlaybackQueue = Presentation.FeedbackBinder.Core.PlaybackQueue;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    public class PlaybackQueueTests
    {
        [Fact]
        public void Immediate_ExecutesSynchronously_AndNeverFiresFinished()
        {
            var queue = new PlaybackQueue(0.1) { Mode = QueueMode.Immediate };
            var executed = new List<int>();
            var finishedCount = 0;
            queue.Finished += () => finishedCount++;

            queue.Enqueue(() => executed.Add(1));
            queue.Enqueue(() => executed.Add(2));

            Assert.Equal(new List<int> { 1, 2 }, executed);
            Assert.Equal(0, queue.PendingCount);
            Assert.Equal(0, finishedCount);
        }

        [Fact]
        public void Sequential_ExecutesInOrder_OnUpdate()
        {
            var queue = new PlaybackQueue(0.1) { Mode = QueueMode.Sequential };
            var executed = new List<int>();

            queue.Enqueue(() => executed.Add(1));
            queue.Enqueue(() => executed.Add(2));
            queue.Enqueue(() => executed.Add(3));

            Assert.Empty(executed);

            queue.Update(0.1);
            Assert.Equal(new List<int> { 1 }, executed);

            queue.Update(0.1);
            Assert.Equal(new List<int> { 1, 2 }, executed);

            queue.Update(0.1);
            Assert.Equal(new List<int> { 1, 2, 3 }, executed);
        }

        [Fact]
        public void Sequential_FiresFinished_ExactlyOnce_WhenQueueDrains()
        {
            var queue = new PlaybackQueue(0.1) { Mode = QueueMode.Sequential };
            var finishedCount = 0;
            queue.Finished += () => finishedCount++;

            queue.Enqueue(() => { });
            queue.Enqueue(() => { });

            queue.Update(0.2);

            Assert.Equal(1, finishedCount);

            // 队列已空，继续 Update 不应重复触发。
            queue.Update(0.5);
            Assert.Equal(1, finishedCount);
        }

        [Fact]
        public void Sequential_SpeedMultiplier_AcceleratesPace()
        {
            var queue = new PlaybackQueue(0.1) { Mode = QueueMode.Sequential, SpeedMultiplier = 2.0 };
            var executed = new List<int>();
            queue.Enqueue(() => executed.Add(1));

            // 加速 2 倍：0.05 秒实际 dt 等效 0.1 秒进度，应当足以触发第一步。
            queue.Update(0.05);

            Assert.Single(executed);
        }

        [Fact]
        public void Sequential_Skip_ExecutesAllRemaining_AndFiresFinishedOnce()
        {
            var queue = new PlaybackQueue(0.1) { Mode = QueueMode.Sequential };
            var executed = new List<int>();
            var finishedCount = 0;
            queue.Finished += () => finishedCount++;

            queue.Enqueue(() => executed.Add(1));
            queue.Enqueue(() => executed.Add(2));
            queue.Enqueue(() => executed.Add(3));

            queue.Skip();

            Assert.Equal(new List<int> { 1, 2, 3 }, executed);
            Assert.Equal(1, finishedCount);
            Assert.Equal(0, queue.PendingCount);
        }

        [Fact]
        public void Skip_OnEmptyQueue_DoesNothing()
        {
            var queue = new PlaybackQueue(0.1) { Mode = QueueMode.Sequential };
            var finishedCount = 0;
            queue.Finished += () => finishedCount++;

            queue.Skip();

            Assert.Equal(0, finishedCount);
        }

        [Fact]
        public void SwitchingModeMidway_QueuedStepsStillDrainViaUpdate()
        {
            var queue = new PlaybackQueue(0.1) { Mode = QueueMode.Sequential };
            var executed = new List<int>();

            queue.Enqueue(() => executed.Add(1));
            queue.Mode = QueueMode.Immediate;
            queue.Enqueue(() => executed.Add(2));

            // 切到 Immediate 之后入队的立即执行；先前排队的第 1 步仍待 Update 推进。
            Assert.Equal(new List<int> { 2 }, executed);

            queue.Update(0.1);
            Assert.Equal(new List<int> { 2, 1 }, executed);
        }
    }
}
