#nullable enable
// FreezeFrameReceiverTests：测试覆盖第四批 T-L15——FreezeFrameReceiver 此前在测试里零直接引用。
// 纯逻辑类（不触碰 UnityEngine API），EditMode 即可直接断言“顿帧剩余时间”的状态机：
// 多次请求取较大者而不是叠加、非正数请求被忽略、Tick 只递减不越过 0、TriggerCount 只计有效请求。
// 数值全部取二进制可精确表示的值（0.5/0.25/1.0），避免浮点累减误差把等值判断变成偶发失败。
using Adapter.Unity.Presentation;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class FreezeFrameReceiverTests
    {
        [Test]
        public void InitialState_NotFrozen_NoTriggers()
        {
            var receiver = new FreezeFrameReceiver();
            Assert.IsFalse(receiver.IsFrozen);
            Assert.AreEqual(0, receiver.TriggerCount);
        }

        [Test]
        public void Freeze_Positive_StartsFreeze_AndCountsOneTrigger()
        {
            var receiver = new FreezeFrameReceiver();
            receiver.Freeze(0.5);
            Assert.IsTrue(receiver.IsFrozen);
            Assert.AreEqual(1, receiver.TriggerCount);
        }

        [TestCase(0.0)]
        [TestCase(-0.25)]
        [TestCase(-100.0)]
        public void Freeze_NonPositive_IsIgnored_NoFreezeNoTriggerCount(double seconds)
        {
            var receiver = new FreezeFrameReceiver();
            receiver.Freeze(seconds);
            Assert.IsFalse(receiver.IsFrozen);
            Assert.AreEqual(0, receiver.TriggerCount);
        }

        [Test]
        public void Freeze_NonPositive_DoesNotShortenAnOngoingFreeze()
        {
            var receiver = new FreezeFrameReceiver();
            receiver.Freeze(0.5);
            receiver.Freeze(0.0);
            receiver.Freeze(-1.0);

            receiver.Tick(0.25);
            Assert.IsTrue(receiver.IsFrozen, "无效请求不应清零或缩短已有顿帧");
            Assert.AreEqual(1, receiver.TriggerCount);
        }

        [Test]
        public void Tick_CountsDownRemainingTime_FrozenExactlyUntilTotalElapsedReachesRequestedSeconds()
        {
            var receiver = new FreezeFrameReceiver();
            receiver.Freeze(1.0);

            receiver.Tick(0.5);
            Assert.IsTrue(receiver.IsFrozen);
            receiver.Tick(0.25);
            Assert.IsTrue(receiver.IsFrozen, "累计 0.75 < 1.0 仍应处于顿帧");
            receiver.Tick(0.25);
            Assert.IsFalse(receiver.IsFrozen, "累计恰好 1.0 即解除（剩余时间不再 > 0）");
        }

        [Test]
        public void Tick_OvershootClampsAtZero_NextFreezeLastsItsOwnFullDuration()
        {
            var receiver = new FreezeFrameReceiver();
            receiver.Freeze(0.25);
            receiver.Tick(10.0);
            Assert.IsFalse(receiver.IsFrozen);

            // 若剩余时间越过 0 变成负数，下一次 Freeze 取 max(负数, 新值) 仍是新值——但 Tick 把剩余时间
            // 钳在 0 是更强的不变量：新一轮顿帧恰好持续请求的时长，不被上一轮的“超调”吃掉。
            receiver.Freeze(0.5);
            receiver.Tick(0.25);
            Assert.IsTrue(receiver.IsFrozen);
            receiver.Tick(0.25);
            Assert.IsFalse(receiver.IsFrozen);
        }

        [Test]
        public void Freeze_WhileFrozen_TakesTheLargerRemaining_NotTheSum()
        {
            var receiver = new FreezeFrameReceiver();
            receiver.Freeze(0.5);
            receiver.Freeze(0.25);   // 较小请求不缩短

            receiver.Tick(0.25);
            Assert.IsTrue(receiver.IsFrozen, "较小的第二次请求不应缩短剩余 0.5");
            receiver.Tick(0.25);
            Assert.IsFalse(receiver.IsFrozen, "总时长 0.5，而不是叠加后的 0.75");

            receiver.Freeze(0.25);
            receiver.Tick(0.125);
            receiver.Freeze(1.0);    // 较大请求延长到 1.0（取较大者，不是 0.125 + 1.0）
            receiver.Tick(0.875);
            Assert.IsTrue(receiver.IsFrozen);
            receiver.Tick(0.125);
            Assert.IsFalse(receiver.IsFrozen);
        }

        [Test]
        public void TriggerCount_CountsEveryAcceptedRequest_EvenWhenRemainingTimeDoesNotChange()
        {
            var receiver = new FreezeFrameReceiver();
            receiver.Freeze(1.0);
            receiver.Freeze(0.25);
            receiver.Freeze(0.5);
            receiver.Freeze(0.0);   // 被忽略，不计

            Assert.AreEqual(3, receiver.TriggerCount);
        }

        [Test]
        public void Tick_WhenNotFrozen_IsHarmless_AndZeroDeltaDoesNotAdvance()
        {
            var receiver = new FreezeFrameReceiver();
            receiver.Tick(5.0);
            Assert.IsFalse(receiver.IsFrozen);

            receiver.Freeze(0.5);
            receiver.Tick(0.0);
            Assert.IsTrue(receiver.IsFrozen);
            receiver.Tick(0.5);
            Assert.IsFalse(receiver.IsFrozen);
        }
    }
}
