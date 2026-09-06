using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    public class FrameAnimPlayerTests
    {
        private static readonly Id ClipId = new Id("anim.sample_attack");

        private static FrameAnimPlayer CreatePlayer(int frameCount = 4, double frameRate = 4.0, IReadOnlyDictionary<string, int>? keyframes = null)
        {
            var clips = new Dictionary<Id, FrameAnimClip>
            {
                [ClipId] = new FrameAnimClip(ClipId, frameCount, frameRate, keyframes),
            };
            return new FrameAnimPlayer(clips);
        }

        [Fact]
        public void Play_UnknownClip_Throws()
        {
            var player = CreatePlayer();
            Assert.Throws<ArgumentException>(() => player.Play(new Id("anim.unknown"), false, 1.0));
        }

        [Fact]
        public void Play_NonPositiveSpeed_Throws()
        {
            var player = CreatePlayer();
            Assert.Throws<ArgumentOutOfRangeException>(() => player.Play(ClipId, false, 0.0));
        }

        [Fact]
        public void Play_FiresFrameChangedAtFrameZero()
        {
            var player = CreatePlayer();
            var frames = new List<int>();
            player.FrameChanged += frames.Add;

            player.Play(ClipId, false, 1.0);

            Assert.Equal(new[] { 0 }, frames);
            Assert.Equal(0, player.CurrentFrame);
        }

        [Fact]
        public void Update_AdvancesFrameIndex_ByElapsedTimeAndFrameRate()
        {
            // 4 帧、4fps → 每帧 0.25 秒。
            var player = CreatePlayer(frameCount: 4, frameRate: 4.0);
            var frames = new List<int>();
            player.Play(ClipId, loop: true, speed: 1.0);
            player.FrameChanged += frames.Add;

            player.Update(0.25);
            Assert.Equal(1, player.CurrentFrame);

            player.Update(0.25);
            Assert.Equal(2, player.CurrentFrame);

            Assert.Equal(new[] { 1, 2 }, frames);
        }

        [Fact]
        public void Update_NonLoop_FiresOnComplete_AtLastFrame_ThenStops()
        {
            var player = CreatePlayer(frameCount: 4, frameRate: 4.0);
            var completed = false;
            player.OnComplete(() => completed = true);

            player.Play(ClipId, loop: false, speed: 1.0);
            player.Update(1.0); // 总时长 1.0 秒，整段播完。

            Assert.True(completed);
            Assert.Null(player.CurrentClipId);
        }

        [Fact]
        public void Update_Loop_WrapsWithoutFiringOnComplete()
        {
            var player = CreatePlayer(frameCount: 4, frameRate: 4.0);
            var completed = false;
            player.OnComplete(() => completed = true);

            player.Play(ClipId, loop: true, speed: 1.0);
            player.Update(1.0); // 恰好一整圈。
            player.Update(0.25); // 再进入下一圈第 1 帧。

            Assert.False(completed);
            Assert.NotNull(player.CurrentClipId);
            Assert.Equal(1, player.CurrentFrame);
        }

        [Fact]
        public void Update_ReachesKeyframe_FiresOnAnimEvent_WithMarkerName()
        {
            var keyframes = new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 2 };
            var player = CreatePlayer(frameCount: 4, frameRate: 4.0, keyframes: keyframes);
            var fired = new List<string>();
            player.OnAnimEvent(fired.Add);

            player.Play(ClipId, loop: false, speed: 1.0);
            player.Update(0.5); // 0.5s * 4fps = 第 2 帧。

            Assert.Equal(new[] { FrameAnimClip.HitFrameMarker }, fired);
        }

        [Fact]
        public void Stop_ClearsCurrentClip_DoesNotFireOnComplete()
        {
            var player = CreatePlayer();
            var completed = false;
            player.OnComplete(() => completed = true);

            player.Play(ClipId, false, 1.0);
            player.Stop();

            Assert.False(completed);
            Assert.Null(player.CurrentClipId);

            player.Update(10.0); // 空闲时 Update 是空操作。
            Assert.False(completed);
        }

        [Fact]
        public void OnComplete_UnsubscribedHandle_DoesNotFire()
        {
            var player = CreatePlayer(frameCount: 2, frameRate: 2.0);
            var completed = false;
            var handle = player.OnComplete(() => completed = true);
            handle.Dispose();

            player.Play(ClipId, false, 1.0);
            player.Update(2.0);

            Assert.False(completed);
        }

        [Fact]
        public void Play_ReplacesInFlightClip_WithoutFiringPreviousOnComplete()
        {
            var player = CreatePlayer(frameCount: 4, frameRate: 4.0);
            var completed = false;
            player.OnComplete(() => completed = true);

            player.Play(ClipId, false, 1.0);
            player.Update(0.1);
            player.Play(ClipId, false, 1.0); // 重新触发，替换正在播放的实例。

            Assert.False(completed);
            Assert.Equal(0, player.CurrentFrame);
        }
    }
}
