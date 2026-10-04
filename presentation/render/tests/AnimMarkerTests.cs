using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// 动画表现标记的发出端（ADR-0148，手感设计/04 第 5 节、07 第 3 节）：同名重复标记（一个剪辑两个 <c>footstep</c>）、
    /// 序列帧 rig 与模型 rig 都把每个标记转发出去（与命中帧同步策略无关）、模型事件名标准化。
    /// </summary>
    public sealed class AnimMarkerTests
    {
        private static readonly Id ClipId = new Id("anim.sample_walk");

        // ------------------------------------------------------------------ 名字规则

        [Theory]
        [InlineData("footstep", 0, "footstep")]
        [InlineData("footstep", 1, "footstep#1")]
        [InlineData("footstep", 7, "footstep#7")]
        public void RepeatKey_AppendsTheOccurrenceIndex_FirstOccurrenceKeepsTheBareName(string name, int occurrence, string expected)
        {
            Assert.Equal(expected, AnimMarkerNames.RepeatKey(name, occurrence));
            Assert.Equal(name, AnimMarkerNames.StripRepeat(expected));
        }

        [Theory]
        [InlineData("footstep#x", "footstep#x")]
        [InlineData("#3", "#3")]
        [InlineData("a#", "a#")]
        [InlineData("fx:vfx.spark#2", "fx:vfx.spark")]
        public void StripRepeat_OnlyRemovesANumericSuffix(string key, string expected) =>
            Assert.Equal(expected, AnimMarkerNames.StripRepeat(key));

        [Theory]
        [InlineData("anim_event.footstep", "footstep")]
        [InlineData("anim_event.trail_start", "trail_start")]
        [InlineData("anim_event.fx.vfx.spark", "fx:vfx.spark")]
        [InlineData("anim_event.", null)]
        [InlineData("other.footstep", null)]
        public void FromModelEventId_NormalizesTheRendererEventName(string eventId, string? expected) =>
            Assert.Equal(expected, AnimMarkerNames.FromModelEventId(eventId));

        // ------------------------------------------------------------------ 序列帧播放器：重复标记

        [Fact]
        public void FrameAnimPlayer_TwoFootstepsInOneClip_FireTwice_InsteadOfOneCollapsedMarker()
        {
            // 复现：此前同名事件在关键帧索引（名字→帧）里互相覆盖，一个走路剪辑的两次落脚只剩一次。
            var keyframes = new Dictionary<string, int>
            {
                [AnimMarkerNames.RepeatKey(AnimMarkerNames.Footstep, 0)] = 0,
                [AnimMarkerNames.RepeatKey(AnimMarkerNames.Footstep, 1)] = 4,
            };
            var clips = new Dictionary<Id, FrameAnimClip> { [ClipId] = new FrameAnimClip(ClipId, 8, 8.0, keyframes) };
            var player = new FrameAnimPlayer(clips);
            var fired = new List<string>();
            player.OnAnimEvent(fired.Add);

            player.Play(ClipId, false, 1.0);
            player.Update(0.0);
            for (var i = 0; i < 8; i++) player.Update(1.0 / 8.0);

            Assert.Equal(new[] { AnimMarkerNames.Footstep, AnimMarkerNames.Footstep }, fired);
        }

        // ------------------------------------------------------------------ rig 转发

        private static DisplayInfo SpriteInfo() =>
            new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.creature.hero", 8, mirrorPairs: null, paperdollLayers: null, anchorPoints: null), null);

        [Theory]
        [InlineData(HitFrameSyncStrategy.LogicDriven)]
        [InlineData(HitFrameSyncStrategy.AnimKeyframeDriven)]
        public void SpriteRig_ForwardsEveryMarker_RegardlessOfTheHitFrameSyncStrategy(HitFrameSyncStrategy strategy)
        {
            var keyframes = new Dictionary<string, int>
            {
                [AnimMarkerNames.Footstep] = 1,
                [AnimMarkerNames.TrailStart] = 2,
                [FrameAnimClip.HitFrameMarker] = 3,
            };
            var player = new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip> { [ClipId] = new FrameAnimClip(ClipId, 6, 6.0, keyframes) });
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var rig = new SpriteCharacterRig(
                new Id("unit.hero_1"), renderer, handle, new RenderConventionHost(), SpriteInfo(),
                resourceTracker: null, frameAnimPlayer: player, options: new RenderOptions { HitFrameSync = strategy });
            var markers = new List<(Id Entity, string Marker)>();
            rig.AnimMarker += (entity, marker) => markers.Add((entity, marker));
            var hitFrames = 0;
            rig.HitFrameReached += _ => hitFrames++;

            rig.PlayClip(ClipId);
            for (var i = 0; i < 6; i++) player.Update(1.0 / 6.0);

            Assert.Equal(
                new[] { AnimMarkerNames.Footstep, AnimMarkerNames.TrailStart, FrameAnimClip.HitFrameMarker },
                markers.ConvertAll(m => m.Marker));
            Assert.All(markers, m => Assert.Equal(new Id("unit.hero_1"), m.Entity));
            // 命中帧同步事件只在逐帧驱动策略下发（既有行为不变）。
            Assert.Equal(strategy == HitFrameSyncStrategy.AnimKeyframeDriven ? 1 : 0, hitFrames);
        }

        [Fact]
        public void ModelRig_ForwardsRendererAnimEventsAsNormalizedMarkers_EvenUnderTheLogicDrivenStrategy()
        {
            var renderer = new StubRenderer3D();
            var handle = renderer.CreateModelInstance(new Id("model.hero"));
            var info = new DisplayInfo(
                new Id("display.hero3d"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Model,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null, null,
                new ModelInfo(new Id("model.hero"), new Id("display.anim_set.hero")));
            var rig = new ModelCharacterRig(new Id("unit.hero_1"), renderer, handle, info, new RenderOptions());
            var markers = new List<string>();
            rig.AnimMarker += (_, marker) => markers.Add(marker);

            renderer.FireAnimEventForTest(handle, new Id("anim_event.footstep"));
            renderer.FireAnimEventForTest(handle, new Id("anim_event.fx.vfx.spark"));
            renderer.FireAnimEventForTest(handle, new Id("anim_event.finished"));

            Assert.Equal(new[] { "footstep", "fx:vfx.spark", "finished" }, markers);
        }
    }
}
