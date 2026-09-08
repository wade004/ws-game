using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Core;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary><see cref="HitFrameSyncPolicy"/> 用例（ADR-0017 决策 d）：按时释放、超时兜底、攻击方
    /// 无 rig 时立即播放、多次攻击不串扰。</summary>
    public class HitFrameSyncPolicyTests
    {
        private static readonly Id Attacker = new Id("unit.attacker_1");
        private static readonly Id OtherAttacker = new Id("unit.attacker_2");

        [Fact]
        public void WaitForHitFrame_NoRigRegistered_ReleasesImmediately()
        {
            var source = new FakeHitFrameSource();
            var policy = new HitFrameSyncPolicy(source);
            var released = false;

            policy.WaitForHitFrame(Attacker, () => released = true);

            Assert.True(released);
            Assert.Equal(0, policy.PendingCount);
        }

        [Fact]
        public void WaitForHitFrame_RigRegistered_DoesNotReleaseUntilHitFrame()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source);
            var released = false;

            policy.WaitForHitFrame(Attacker, () => released = true);

            Assert.False(released);
            Assert.Equal(1, policy.PendingCount);
        }

        [Fact]
        public void HitFrameReached_ReleasesMatchingPendingEntry()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source);
            var released = false;
            policy.WaitForHitFrame(Attacker, () => released = true);

            source.Fire(Attacker);

            Assert.True(released);
            Assert.Equal(0, policy.PendingCount);
        }

        [Fact]
        public void HitFrameReached_ForDifferentEntity_DoesNotReleaseUnrelatedEntry()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source);
            var released = false;
            policy.WaitForHitFrame(Attacker, () => released = true);

            source.Fire(OtherAttacker);

            Assert.False(released);
            Assert.Equal(1, policy.PendingCount);
        }

        [Fact]
        public void Update_TimesOutAfterConfiguredSeconds_ReleasesAndWarns()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var diagnostics = new PresentationDiagnosticsRecorder();
            var policy = new HitFrameSyncPolicy(source, timeoutSeconds: 0.5, diagnostics: diagnostics);
            var released = false;
            policy.WaitForHitFrame(Attacker, () => released = true);

            policy.Update(0.4);
            Assert.False(released);

            policy.Update(0.2); // 累计 0.6s，超过 0.5s 超时
            Assert.True(released);
            Assert.Equal(0, policy.PendingCount);
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void Update_BeforeTimeout_DoesNotRelease()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source, timeoutSeconds: 0.5);
            var released = false;
            policy.WaitForHitFrame(Attacker, () => released = true);

            policy.Update(0.3);

            Assert.False(released);
            Assert.Equal(1, policy.PendingCount);
        }

        [Fact]
        public void MultipleAttacks_SameEntity_DoNotCrossTalk()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source);
            var releasedCount = 0;
            policy.WaitForHitFrame(Attacker, () => releasedCount++);
            policy.WaitForHitFrame(Attacker, () => releasedCount++);

            source.Fire(Attacker);
            Assert.Equal(1, releasedCount);
            Assert.Equal(1, policy.PendingCount);

            source.Fire(Attacker);
            Assert.Equal(2, releasedCount);
            Assert.Equal(0, policy.PendingCount);
        }

        [Fact]
        public void MultipleAttackers_IndependentQueues()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            source.RegisterRig(OtherAttacker, null!);
            var policy = new HitFrameSyncPolicy(source);
            var firstReleased = false;
            var secondReleased = false;
            policy.WaitForHitFrame(Attacker, () => firstReleased = true);
            policy.WaitForHitFrame(OtherAttacker, () => secondReleased = true);

            source.Fire(OtherAttacker);

            Assert.False(firstReleased);
            Assert.True(secondReleased);
            Assert.Equal(1, policy.PendingCount);
        }

        [Fact]
        public void PendingChanged_FiresOnEnqueueReleaseAndTimeout()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source, timeoutSeconds: 0.5);
            var fireCount = 0;
            policy.PendingChanged += () => fireCount++;

            policy.WaitForHitFrame(Attacker, () => { });
            Assert.Equal(1, fireCount);

            source.Fire(Attacker);
            Assert.Equal(2, fireCount);

            policy.WaitForHitFrame(Attacker, () => { });
            policy.Update(1.0);
            Assert.Equal(4, fireCount);
        }

        /// <summary>PR140-04 复现/回归用例：同一个 <c>batchToken</c> 的三次 <see cref="HitFrameSyncPolicy.WaitForHitFrame(Id, object, Action)"/>
        /// 调用代表同一次攻击命中的三个目标——一次命中帧必须把三者一并原子释放（按入队顺序），不是只释放
        /// 最早一条。</summary>
        [Fact]
        public void WaitForHitFrame_SameBatchToken_HitFrameReleasesEntireBatchAtomically_InEnqueueOrder()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source);
            var releaseOrder = new List<int>();
            var token = new object();

            policy.WaitForHitFrame(Attacker, token, () => releaseOrder.Add(1));
            policy.WaitForHitFrame(Attacker, token, () => releaseOrder.Add(2));
            policy.WaitForHitFrame(Attacker, token, () => releaseOrder.Add(3));
            Assert.Equal(3, policy.PendingCount);

            source.Fire(Attacker);

            Assert.Equal(new[] { 1, 2, 3 }, releaseOrder);
            Assert.Equal(0, policy.PendingCount);
        }

        /// <summary>PR140-04：<see cref="HitFrameSyncPolicy.BatchReleased"/> 在批次释放（命中帧路径）
        /// 时携带对应攻击者 id 触发恰好一次，供调用方（<c>FeedbackBinder</c>）据此清空"当前打开批次"
        /// 记账。</summary>
        [Fact]
        public void WaitForHitFrame_SameBatchToken_HitFrameReleaseRaisesBatchReleasedOnce()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source);
            var token = new object();
            var batchReleasedFor = new List<Id>();
            policy.BatchReleased += id => batchReleasedFor.Add(id);

            policy.WaitForHitFrame(Attacker, token, () => { });
            policy.WaitForHitFrame(Attacker, token, () => { });

            source.Fire(Attacker);

            Assert.Equal(new[] { Attacker }, batchReleasedFor);
        }

        /// <summary>PR140-04：不同 <c>batchToken</c> 即便攻击者相同也各自独立释放——两次确实不同的攻击
        /// 不应该被误合并成一批，本条精神与既有 <see cref="MultipleAttacks_SameEntity_DoNotCrossTalk"/>
        /// 一致，只是这里改用显式不同 token 表达"确实不同的两次攻击"。</summary>
        [Fact]
        public void WaitForHitFrame_DifferentBatchTokens_SameAttacker_ReleaseIndependently()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source);
            var releasedA = false;
            var releasedB = false;
            var tokenA = new object();
            var tokenB = new object();

            policy.WaitForHitFrame(Attacker, tokenA, () => releasedA = true);
            policy.WaitForHitFrame(Attacker, tokenB, () => releasedB = true);

            source.Fire(Attacker);

            Assert.True(releasedA);
            Assert.False(releasedB);
            Assert.Equal(1, policy.PendingCount);
        }

        /// <summary>PR140-04：同一批次的多条等待项同时超时时，一次性原子释放且只警告一次（不是每条各自
        /// 警告一次）——见 <see cref="HitFrameSyncPolicy.ReleaseBatch"/>（私有方法，经本用例间接验证）
        /// 判断记录。</summary>
        [Fact]
        public void Update_SameBatchToken_TimesOutTogether_ReleasesAtomicallyWithSingleWarning()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var diagnostics = new PresentationDiagnosticsRecorder();
            var policy = new HitFrameSyncPolicy(source, timeoutSeconds: 0.5, diagnostics: diagnostics);
            var releaseOrder = new List<int>();
            var token = new object();

            policy.WaitForHitFrame(Attacker, token, () => releaseOrder.Add(1));
            policy.WaitForHitFrame(Attacker, token, () => releaseOrder.Add(2));

            policy.Update(0.6); // 超过 0.5s 超时，二者应当一起超时释放。

            Assert.Equal(new[] { 1, 2 }, releaseOrder);
            Assert.Equal(0, policy.PendingCount);
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void Dispose_UnsubscribesFromSource_FurtherFireDoesNotRelease()
        {
            var source = new FakeHitFrameSource();
            source.RegisterRig(Attacker, null!);
            var policy = new HitFrameSyncPolicy(source);
            var released = false;
            policy.WaitForHitFrame(Attacker, () => released = true);

            policy.Dispose();
            source.Fire(Attacker);

            Assert.False(released);
        }
    }
}
