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
