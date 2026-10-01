using System;
using System.Collections.Generic;
using Presentation.FeedbackBinder.Contracts;
using Presentation.VfxSfx.Contracts;
using PlaybackQueue = Presentation.FeedbackBinder.Core.PlaybackQueue;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// T-M33（测试覆盖剩余项第四批）：PlaybackQueue 的非正参数、负 dt、step 抛异常、step 内重入 Enqueue。
    /// 时间量一律以 Step 的倍数表达，避免浮点边界。
    /// </summary>
    public class PlaybackQueueEdgeTests
    {
        private const double Step = 0.125; // 二进制可精确表示

        private static PlaybackQueue Sequential(double step = Step) => new PlaybackQueue(step) { Mode = QueueMode.Sequential };

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.1)]
        [InlineData(double.NegativeInfinity)]
        public void Constructor_NonPositiveStepSeconds_Throws(double stepSeconds)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackQueue(stepSeconds));
            Assert.Equal("stepSeconds", ex.ParamName);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-2.0)]
        public void SpeedMultiplier_NonPositive_Throws_AndKeepsPreviousValue(double value)
        {
            var queue = Sequential();
            queue.SpeedMultiplier = 4.0;

            Assert.Throws<ArgumentOutOfRangeException>(() => queue.SpeedMultiplier = value);

            Assert.Equal(4.0, queue.SpeedMultiplier);
        }

        [Fact]
        public void SpeedMultiplier_DefaultsToOne()
        {
            Assert.Equal(1.0, new PlaybackQueue(Step).SpeedMultiplier);
        }

        [Fact]
        public void Enqueue_Null_Throws_InBothModes()
        {
            var queue = Sequential();
            Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!));
            queue.Mode = QueueMode.Immediate;
            Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!));
        }

        [Fact]
        public void Update_NegativeDt_RunsNothing_AndDebtMustBeRepaid()
        {
            var queue = Sequential();
            var ran = 0;
            queue.Enqueue(() => ran++);

            queue.Update(-Step);           // 欠一步
            queue.Update(Step);            // 净累计 0
            Assert.Equal(0, ran);
            Assert.Equal(1, queue.PendingCount);

            queue.Update(Step);            // 净累计 = 一步
            Assert.Equal(1, ran);
        }

        [Fact]
        public void Update_ZeroDt_RunsNothing()
        {
            var queue = Sequential();
            var ran = 0;
            queue.Enqueue(() => ran++);
            queue.Update(0.0);
            Assert.Equal(0, ran);
        }

        [Fact]
        public void Update_LargeDt_RunsOneStepPerStepSecondsInOrder_StopsWhenEmpty_FinishedOnce()
        {
            var queue = Sequential();
            var ran = new List<int>();
            var finished = 0;
            queue.Finished += () => finished++;
            for (var i = 0; i < 3; i++)
            {
                var n = i;
                queue.Enqueue(() => ran.Add(n));
            }

            queue.Update(Step * 10); // 远多于 3 步

            Assert.Equal(new[] { 0, 1, 2 }, ran);
            Assert.Equal(0, queue.PendingCount);
            Assert.Equal(1, finished);
        }

        [Fact]
        public void Update_ExactlyNSteps_RunsExactlyN()
        {
            const int n = 3;
            var queue = Sequential();
            var ran = 0;
            for (var i = 0; i < n + 2; i++) queue.Enqueue(() => ran++);

            queue.Update(Step * n);

            Assert.Equal(n, ran);
            Assert.Equal(2, queue.PendingCount);
        }

        [Fact]
        public void Update_SpeedMultiplierScalesDtLinearly()
        {
            const double speed = 4.0;
            var queue = Sequential();
            queue.SpeedMultiplier = speed;
            var ran = 0;
            for (var i = 0; i < 10; i++) queue.Enqueue(() => ran++);

            queue.Update(Step); // 有效累计 = dt * speed = speed 个步长

            Assert.Equal((int)speed, ran);
        }

        [Fact]
        public void Finished_FiresAgainForALaterBatch_AfterQueueDrainedAgain()
        {
            var queue = Sequential();
            var finished = 0;
            queue.Finished += () => finished++;

            queue.Enqueue(() => { });
            queue.Update(Step);
            queue.Enqueue(() => { });
            queue.Update(Step);

            Assert.Equal(2, finished);
        }

        [Fact]
        public void Finished_NoSubscribers_DoesNotThrow()
        {
            var queue = Sequential();
            queue.Enqueue(() => { });
            queue.Update(Step);
            queue.Enqueue(() => { });
            queue.Skip();
        }

        [Fact]
        public void Update_LeftoverTimeIsDiscardedWhenQueueDrains()
        {
            var queue = Sequential();
            var ran = 0;
            queue.Enqueue(() => ran++);
            queue.Update(Step * 1.5);          // 执行 1 步，剩余半步在队列清空时清零
            Assert.Equal(1, ran);

            queue.Enqueue(() => ran++);
            queue.Update(Step * 0.75);         // 若残余保留，0.5 + 0.75 > 1 步会立刻执行
            Assert.Equal(1, ran);
        }

        [Fact]
        public void Update_OnEmptyQueue_IgnoresDt_AndDoesNotAccumulate()
        {
            var queue = Sequential();
            queue.Update(100.0);          // 空队列不累计时间

            var ran = 0;
            queue.Enqueue(() => ran++);
            queue.Update(Step * 0.5);

            Assert.Equal(0, ran);
        }

        // ---------------- step 抛异常 ----------------

        // 收口遗留修复 A2：Sequential 下步骤抛异常——记诊断、继续后续步骤、Finished 照常触发。
        [Fact]
        public void Update_StepThrows_RecordedAsDiagnostic_RestRunInSameUpdate_FinishedFires()
        {
            var diagnostics = new PresentationDiagnosticsRecorder();
            var queue = new PlaybackQueue(Step, diagnostics) { Mode = QueueMode.Sequential };
            var ran = new List<string>();
            var finished = 0;
            queue.Finished += () => finished++;
            queue.Enqueue(() => ran.Add("a"));
            queue.Enqueue(() => throw new InvalidOperationException("boom"));
            queue.Enqueue(() => ran.Add("c"));

            Assert.Null(Record.Exception(() => queue.Update(Step * 3)));

            Assert.Equal(new[] { "a", "c" }, ran);
            Assert.Equal(0, queue.PendingCount);
            Assert.Equal(1, finished);
            var warning = Assert.Single(diagnostics.Warnings);
            Assert.Contains("InvalidOperationException", warning);
            Assert.Contains("boom", warning);
            Assert.Same(diagnostics, queue.Diagnostics);
        }

        [Fact]
        public void Skip_StepThrows_RecordedAsDiagnostic_RestRun_FinishedFires()
        {
            var diagnostics = new PresentationDiagnosticsRecorder();
            var queue = new PlaybackQueue(Step, diagnostics) { Mode = QueueMode.Sequential };
            var ran = new List<string>();
            var finished = 0;
            queue.Finished += () => finished++;
            queue.Enqueue(() => throw new InvalidOperationException("boom"));
            queue.Enqueue(() => ran.Add("b"));

            Assert.Null(Record.Exception(() => queue.Skip()));

            Assert.Equal(0, queue.PendingCount);
            Assert.Equal(new[] { "b" }, ran);
            Assert.Equal(1, finished);
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void Update_AllStepsThrow_EachRecorded_FinishedOnce()
        {
            var diagnostics = new PresentationDiagnosticsRecorder();
            var queue = new PlaybackQueue(Step, diagnostics) { Mode = QueueMode.Sequential };
            var finished = 0;
            queue.Finished += () => finished++;
            for (var i = 0; i < 3; i++)
            {
                queue.Enqueue(() => throw new InvalidOperationException("boom"));
            }

            queue.Update(Step * 3);

            Assert.Equal(3, diagnostics.Warnings.Count);
            Assert.Equal(1, finished);
        }

        [Fact]
        public void Immediate_StepThrows_PropagatesToEnqueueCaller_QueueStaysEmpty()
        {
            var queue = new PlaybackQueue(Step) { Mode = QueueMode.Immediate };

            Assert.Throws<InvalidOperationException>(() => queue.Enqueue(() => throw new InvalidOperationException("boom")));

            Assert.Equal(0, queue.PendingCount);
        }

        /// <summary>
        /// 收口遗留修复 A2：最后一步抛异常时，该批次仍触发 <see cref="PlaybackQueue.Finished"/>
        /// （异常不外抛，之后的新批次不受影响）——否则 PlaybackFinishedEvent 永不发出，节奏门卡死。
        /// </summary>
        [Fact]
        public void Update_LastStepThrows_FinishedStillFires_ForThatBatch_AndLaterBatchesStillFinish()
        {
            var queue = Sequential();
            var finished = 0;
            queue.Finished += () => finished++;
            queue.Enqueue(() => throw new InvalidOperationException("boom"));

            Assert.Null(Record.Exception(() => queue.Update(Step)));
            Assert.Equal(0, queue.PendingCount);
            Assert.Equal(1, finished);

            queue.Enqueue(() => { });
            queue.Update(Step * 5);
            Assert.Equal(2, finished);
        }

        [Fact]
        public void Finished_HandlerThrows_PropagatesFromUpdate_QueueAlreadyEmpty()
        {
            var queue = Sequential();
            queue.Finished += () => throw new InvalidOperationException("handler");
            queue.Enqueue(() => { });

            Assert.Throws<InvalidOperationException>(() => queue.Update(Step));

            Assert.Equal(0, queue.PendingCount);
        }

        // ---------------- 重入 Enqueue ----------------

        [Fact]
        public void Sequential_StepEnqueuesAnotherStep_RunsItAfterExistingOnes_FinishedOnlyAfterItDrains()
        {
            var queue = Sequential();
            var order = new List<string>();
            var finished = 0;
            queue.Finished += () => { finished++; order.Add("finished"); };
            queue.Enqueue(() =>
            {
                order.Add("a");
                queue.Enqueue(() => order.Add("a2"));
            });
            queue.Enqueue(() => order.Add("b"));

            queue.Update(Step * 10);

            Assert.Equal(new[] { "a", "b", "a2", "finished" }, order);
            Assert.Equal(1, finished);
        }

        [Fact]
        public void Sequential_LastStepReenqueues_QueueDoesNotFinishUntilTheChainEnds()
        {
            var queue = Sequential();
            var finished = 0;
            var remaining = 3;
            queue.Finished += () => finished++;
            void Chain()
            {
                if (--remaining > 0) queue.Enqueue(Chain);
            }
            queue.Enqueue(Chain);

            queue.Update(Step);
            Assert.Equal(0, finished);
            queue.Update(Step * 10);

            Assert.Equal(0, remaining);
            Assert.Equal(1, finished);
        }

        [Fact]
        public void Skip_StepEnqueuesAnother_SkipRunsItToo()
        {
            var queue = Sequential();
            var ran = new List<int>();
            queue.Enqueue(() =>
            {
                ran.Add(1);
                queue.Enqueue(() => ran.Add(2));
            });

            queue.Skip();

            Assert.Equal(new[] { 1, 2 }, ran);
            Assert.Equal(0, queue.PendingCount);
        }

        [Fact]
        public void Immediate_StepEnqueuesAnother_RunsNestedSynchronously()
        {
            var queue = new PlaybackQueue(Step) { Mode = QueueMode.Immediate };
            var order = new List<string>();

            queue.Enqueue(() =>
            {
                order.Add("outer-start");
                queue.Enqueue(() => order.Add("inner"));
                order.Add("outer-end");
            });

            Assert.Equal(new[] { "outer-start", "inner", "outer-end" }, order);
            Assert.Equal(0, queue.PendingCount);
        }
    }
}
