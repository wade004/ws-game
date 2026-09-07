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

        /// <summary>N18 复现与根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// 单帧 loop 播放中，用同一个 clipId 重新登记一份帧数更多、帧率不同的剪辑（模拟
        /// <c>UnityFrameAnimPlayer.RegisterClipFromEffect</c> 冷 clip 加载完成后原地升级——见
        /// <c>UnityViewFactory</c> 判断记录）——旧实现的 <c>_current</c> 只在 <see cref="FrameAnimPlayer.
        /// Play"/> 那一刻缓存了一次对象引用，此后 <see cref="FrameAnimPlayer.Update"/> 全程使用这份
        /// 过期快照，永远停在旧内容，必须调用方手工重新 <see cref="FrameAnimPlayer.Play"/> 才会用上
        /// 新剪辑。修复后：不需要重新 Play，下一次 Update 就应该用上新剪辑的帧数/帧率继续推进。</summary>
        [Fact]
        public void Update_ClipReRegisteredWithSameIdWhilePlaying_UsesNewClipWithoutReplay()
        {
            var clips = new Dictionary<Id, FrameAnimClip>
            {
                [ClipId] = new FrameAnimClip(ClipId, frameCount: 1, frameRate: 1.0, keyframes: null),
            };
            var player = new FrameAnimPlayer(clips);
            var frames = new List<int>();

            player.Play(ClipId, loop: true, speed: 1.0);
            player.FrameChanged += frames.Add;

            // 播放期间（未调用 Stop/Play）用同一个 clipId 热替换成 8 帧、12fps——同真实场景"冷 vfx/
            // 序列帧资源加载完成后原地升级"。
            clips[ClipId] = new FrameAnimClip(ClipId, frameCount: 8, frameRate: 12.0, keyframes: null);

            // 旧的单帧剪辑每帧时长 1 秒，若仍按旧剪辑推进，0.5 秒不会产生任何帧变化（且会一直停在
            // 第 0 帧）；新剪辑 12fps 下 0.5 秒 = 第 6 帧（0.5 * 12 = 6）。
            player.Update(0.5);

            Assert.Equal(6, player.CurrentFrame);
            Assert.Contains(6, frames);
        }

        /// <summary>N18 补充：帧下标恰好与热替换前相同（都是第 0 帧）时，也必须重新触发
        /// <see cref="FrameAnimPlayer.FrameChanged"/>——旧实现"帧下标不变就跳过通知"的优化会让
        /// 引擎适配层以为这一帧没变化、不重新取贴图，但同一个帧下标在新剪辑里对应的可能是完全不同
        /// 的贴图（<c>UnityFrameAnimPlayer.OnFrameChanged</c> 按 clipId+帧下标现查表，见该方法）。
        /// </summary>
        [Fact]
        public void Update_ClipReRegisteredWithSameIdAndSameFrameIndex_StillForcesFrameChanged()
        {
            var clips = new Dictionary<Id, FrameAnimClip>
            {
                [ClipId] = new FrameAnimClip(ClipId, frameCount: 4, frameRate: 4.0, keyframes: null),
            };
            var player = new FrameAnimPlayer(clips);

            player.Play(ClipId, loop: true, speed: 1.0); // 立即处于第 0 帧。

            var frames = new List<int>();
            player.FrameChanged += frames.Add;

            // 热替换成另一份剪辑，帧率/帧数不同，但极小的 dt 仍会落在第 0 帧（换算下取整仍是 0）。
            clips[ClipId] = new FrameAnimClip(ClipId, frameCount: 4, frameRate: 1.0, keyframes: null);
            player.Update(0.01); // 新剪辑下 0.01 * 1.0 = 0.01 帧，取整仍是第 0 帧。

            // 帧下标数值上确实还是 0，但必须仍然收到一次 FrameChanged 通知（强制刷新贴图）。
            Assert.Contains(0, frames);
        }

        // -----------------------------------------------------------------
        // R11 复现与根治（architecture/落地计划/audit-5e779c6-20260907）：一次 Update 的 dt 跨过多帧
        // 时，旧实现只对"推进后落在的那一帧"精确匹配的关键帧触发一次，中途被跳过的帧上的关键帧/
        // FrameChanged 通知全部丢失。以下用例分别覆盖：非循环中途跳过、非循环恰好在这次推进内播完、
        // 循环跨越回绕。
        // -----------------------------------------------------------------

        /// <summary>非循环、一次 Update 的 dt 跨过 3 帧（0→3）但尚未播完：中途第 1、2 帧都应该依次
        /// 触发 FrameChanged，不是只有落地的第 3 帧。修复前 frames 只会是 [3]。</summary>
        [Fact]
        public void Update_LargeDt_SkipsMultipleFrames_FiresFrameChangedForEachSkippedFrameInOrder()
        {
            // 6 帧、10fps → 每帧 0.1 秒，总时长 0.6 秒。
            var player = CreatePlayer(frameCount: 6, frameRate: 10.0);
            var frames = new List<int>();
            player.Play(ClipId, loop: false, speed: 1.0);
            player.FrameChanged += frames.Add;

            player.Update(0.35); // 0.35 * 10 = 3.5 → 落地第 3 帧，中途跨过第 1、2 帧。

            Assert.Equal(new[] { 1, 2, 3 }, frames);
            Assert.Equal(3, player.CurrentFrame);
        }

        /// <summary>非循环、dt 跨过的中途帧上有关键帧标记（不是最后一帧）：修复前该关键帧的
        /// <see cref="FrameAnimPlayer.OnAnimEvent"/> 永远不会触发——旧实现"跨过就丢"的判断记录明确
        /// 点名的正是这个场景。</summary>
        [Fact]
        public void Update_LargeDt_SkippedMiddleFrameHasKeyframe_StillFiresOnAnimEvent()
        {
            var keyframes = new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 1 };
            var player = CreatePlayer(frameCount: 4, frameRate: 4.0, keyframes: keyframes);
            var frames = new List<int>();
            var fired = new List<string>();
            player.Play(ClipId, loop: false, speed: 1.0);
            player.FrameChanged += frames.Add;
            player.OnAnimEvent(fired.Add);

            player.Update(2.0); // 总时长只有 1 秒，一次性跨过全部剩余帧（1、2、3）并自然播完。

            Assert.Equal(new[] { 1, 2, 3 }, frames);
            Assert.Equal(new[] { FrameAnimClip.HitFrameMarker }, fired); // 关键帧在第 1 帧，不是最后一帧。
        }

        /// <summary>循环播放、一次 Update 的 dt 跨越了不止一整圈：先补完这一圈剩余的尾部帧
        /// （1、2、3），回绕后再从第 0 帧补到目标帧（0、1、2）——含端点的第 0 帧关键帧也要重新触发一次
        /// （循环动画每一圈开头本就该重新触发）。补发范围只追一圈，不逐圈重放。</summary>
        [Fact]
        public void Update_Loop_LargeDt_WrapsAcrossBoundary_FiresTailThenWrappedHeadFramesInOrder()
        {
            var keyframes = new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 0 };
            var player = CreatePlayer(frameCount: 4, frameRate: 4.0, keyframes: keyframes); // 每帧 0.25s，一圈 1s。
            var completed = false;
            player.OnComplete(() => completed = true);

            player.Play(ClipId, loop: true, speed: 1.0); // 立即处于第 0 帧（这里已经触发过一次关键帧）。

            var frames = new List<int>();
            var fired = new List<string>();
            player.FrameChanged += frames.Add;
            player.OnAnimEvent(fired.Add);

            player.Update(1.5); // 跨过整整一圈还多半圈：尾部 1,2,3 → 回绕 → 头部 0,1,2。

            Assert.Equal(new[] { 1, 2, 3, 0, 1, 2 }, frames);
            Assert.Equal(new[] { FrameAnimClip.HitFrameMarker }, fired); // 回绕经过第 0 帧，重新触发一次。
            Assert.Equal(2, player.CurrentFrame);
            Assert.False(completed); // 循环不应该触发播放完成。
        }
    }
}
