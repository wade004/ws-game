using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Presentation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// 顿帧表现冻结（手感落地 M2-A，手感设计/07 第 5 节）：序列帧播放器暂停、程序动画原语时间轴冻结（闪白不冻、拖尾按 freezeTrail）、
    /// sprite/model 两类 rig 的 <see cref="IPresentationFreezable"/>。期望值都由"冻结外的累计时间"按同一规则算出，不写死裸数。
    /// </summary>
    public class PresentationFreezeTests
    {
        private static readonly Id ClipId = new Id("anim.freeze_clip");
        private const double Fps = 10.0;
        private const int FrameCount = 100;

        private static FrameAnimPlayer NewPlayer() =>
            new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip> { [ClipId] = new FrameAnimClip(ClipId, FrameCount, Fps, null) });

        // ------------------------------------------------------------------ FrameAnimPlayer

        [Fact]
        public void FrameAnimPlayer_Paused_FrameIndexStopsAndResumesFromSamePoint()
        {
            var player = NewPlayer();
            player.Play(ClipId, true, 1.0);
            player.Update(0.55); // 0.55 s × 10 fps → 第 5 帧
            var frozenFrame = player.CurrentFrame;
            Assert.Equal((int)(0.55 * Fps), frozenFrame);

            player.SetPaused(true);
            for (var i = 0; i < 30; i++) player.Update(0.1);
            Assert.True(player.IsPaused);
            Assert.Equal(frozenFrame, player.CurrentFrame);

            // 解冻后只有解冻后的时间计入：与"从未暂停、只累计 0.55 + 0.3 秒"的播放器完全一致（不变量）。
            player.SetPaused(false);
            player.Update(0.3);
            var reference = NewPlayer();
            reference.Play(ClipId, true, 1.0);
            reference.Update(0.55);
            reference.Update(0.3);
            Assert.Equal(reference.CurrentFrame, player.CurrentFrame);
            Assert.True(player.CurrentFrame > frozenFrame);
        }

        [Fact]
        public void FrameAnimPlayer_Paused_DoesNotFireKeyframesOrComplete_UntilResumed()
        {
            var player = new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip>
            {
                [ClipId] = new FrameAnimClip(ClipId, 4, 4.0, new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 2 }),
            });
            var events = new List<string>();
            var completed = 0;
            player.OnAnimEvent(events.Add);
            player.OnComplete(() => completed++);

            player.Play(ClipId, false, 1.0);
            player.SetPaused(true);
            player.Update(10.0);
            Assert.Empty(events);
            Assert.Equal(0, completed);

            player.SetPaused(false);
            player.Update(10.0);
            Assert.Equal(new[] { FrameAnimClip.HitFrameMarker }, events);
            Assert.Equal(1, completed);
        }

        // ------------------------------------------------------------------ ProceduralAnimSequencer

        [Fact]
        public void Sequencer_Frozen_StopsAnimationPrimitives_ButFlashKeepsRunning_TrailFollowsLayer()
        {
            var sequencer = new ProceduralAnimSequencer();
            double move = -1, flash = -1, trail = -1;
            sequencer.Move(new MoveParams(new Vec2(10, 0), 1.0), offset => move = offset.X);
            sequencer.Flash(new FlashParams(1.0, 1.0), v => flash = v);
            sequencer.Trail(new TrailParams(1.0), v => trail = v);
            sequencer.Update(0.25);
            var moveBefore = move;
            Assert.Equal(2.5, moveBefore, 9);

            // 冻结（拖尾不冻）：位移停住，闪白与拖尾继续衰减。
            sequencer.SetFrozen(true, freezeTrail: false);
            sequencer.Update(0.25);
            Assert.Equal(moveBefore, move, 9);
            Assert.Equal(0.5, flash, 9);
            Assert.Equal(0.5, trail, 9);

            // 拖尾也冻：拖尾停住，闪白仍继续。
            sequencer.SetFrozen(true, freezeTrail: true);
            sequencer.Update(0.25);
            Assert.Equal(moveBefore, move, 9);
            Assert.Equal(0.25, flash, 9);
            Assert.Equal(0.5, trail, 9);

            // 解冻后位移从冻结点继续：总共只计入未冻结的 0.25 + 0.25 秒（不变量）。
            sequencer.SetFrozen(false);
            sequencer.Update(0.25);
            Assert.Equal(5.0, move, 9);
        }

        [Fact]
        public void Sequencer_Frozen_CompletionCallbackDeferredUntilUnfrozen()
        {
            var sequencer = new ProceduralAnimSequencer();
            var completed = 0;
            sequencer.Scale(new ScaleParams(1.5, 0.5), null, () => completed++);

            sequencer.SetFrozen(true);
            sequencer.Update(5.0);
            Assert.Equal(0, completed);

            sequencer.SetFrozen(false);
            sequencer.Update(0.5);
            Assert.Equal(1, completed);
        }

        // ------------------------------------------------------------------ SpriteCharacterRig

        private static DisplayInfo SpriteInfo() =>
            new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.creature.hero", 8, mirrorPairs: null, paperdollLayers: null, anchorPoints: null), null);

        private static (StubRenderer2D Renderer, SpriteHandle Handle, SpriteCharacterRig Rig, FrameAnimPlayer Player) NewSpriteRig(bool attach = true)
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var player = NewPlayer();
            var rig = new SpriteCharacterRig(
                new Id("unit.hero_1"), renderer, handle, new RenderConventionHost(), SpriteInfo(),
                resourceTracker: null, frameAnimPlayer: attach ? player : null, options: null);
            return (renderer, handle, rig, player);
        }

        [Fact]
        public void SpriteRig_Freeze_FrameFrozen_FlashStillDecays_UnfreezeResumes()
        {
            var (renderer, handle, rig, player) = NewSpriteRig();
            rig.PlayClip(ClipId, loop: true);
            rig.Update(0.0);
            player.Update(0.3);
            var frame = player.CurrentFrame;

            rig.Flash(new FlashParams(1.0, 1.0));
            rig.FreezePresentation(freezeTrail: false);
            Assert.True(rig.IsPresentationFrozen);
            Assert.True(player.IsPaused);

            for (var i = 0; i < 10; i++)
            {
                player.Update(0.05);
                rig.Update(0.05);
            }

            Assert.Equal(frame, player.CurrentFrame);
            // 闪白参数时间轴与动画时间轴分开：冻结的 0.5 秒里闪白照常衰减到 1 - 0.5。
            Assert.Equal(0.5, renderer.ShaderParams[handle.Value]["flash_intensity"], 9);

            rig.UnfreezePresentation();
            Assert.False(rig.IsPresentationFrozen);
            Assert.False(player.IsPaused);
            player.Update(0.3);
            Assert.True(player.CurrentFrame > frame);
        }

        [Fact]
        public void SpriteRig_FreezeIsIdempotent_AndLateAttachedPlayerStartsPaused()
        {
            var (_, _, rig, _) = NewSpriteRig(attach: false);
            rig.FreezePresentation(false);
            rig.FreezePresentation(false);
            Assert.True(rig.IsPresentationFrozen);

            // 冷路径：播放器晚于冻结才接上（引擎侧组件要等精灵根节点建好），同样处于冻结。
            var late = NewPlayer();
            rig.AttachFrameAnimPlayer(late);
            Assert.True(late.IsPaused);

            rig.UnfreezePresentation();
            rig.UnfreezePresentation();
            Assert.False(rig.IsPresentationFrozen);
            Assert.False(late.IsPaused);
        }

        // ------------------------------------------------------------------ ModelCharacterRig

        private static DisplayInfo ModelInfo() =>
            new DisplayInfo(
                new Id("display.hero_model"), DisplayCategory.Creature, new Id("creature.hero_model"), DisplayKind.Model,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null, null,
                new ModelInfo(new Id("model.hero"), new Id("display.anim_set.hero")));

        [Fact]
        public void ModelRig_Freeze_ZeroesAnimSpeed_PlayClipWhileFrozenStaysZero_UnfreezeRestoresRequestedSpeed()
        {
            var renderer = new StubRenderer3D();
            var handle = renderer.CreateModelInstance(new Id("model.hero"));
            var rig = new ModelCharacterRig(new Id("unit.hero_1"), renderer, handle, ModelInfo());

            rig.PlayClip(ClipId, loop: true, speed: 1.5);
            Assert.Equal(1.5, renderer.CurrentAnims[handle.Value].Speed);

            rig.FreezePresentation(freezeTrail: false);
            Assert.True(rig.IsPresentationFrozen);
            Assert.Equal(0.0, renderer.AnimSpeeds[handle.Value]);

            // 冻结期间换剪辑（状态机照常切状态）：剪辑换上，但播放速率保持 0。
            var other = new Id("anim.other");
            rig.PlayClip(other, loop: false, speed: 2.0);
            Assert.Equal(other, renderer.CurrentAnims[handle.Value].ClipId);
            Assert.Equal(0.0, renderer.CurrentAnims[handle.Value].Speed);

            // 解冻恢复最近一次请求的速率（2.0），不是冻结前的 1.5，也不是缺省 1。
            rig.UnfreezePresentation();
            Assert.False(rig.IsPresentationFrozen);
            Assert.Equal(2.0, renderer.AnimSpeeds[handle.Value]);
        }

        [Fact]
        public void ModelRig_Freeze_FlashStillDecays_AnimationPrimitivesStop()
        {
            var renderer = new StubRenderer3D();
            var handle = renderer.CreateModelInstance(new Id("model.hero"));
            var rig = new ModelCharacterRig(new Id("unit.hero_1"), renderer, handle, ModelInfo());
            rig.SyncPlacement(Vec2.Zero, 0, 0, 1.0, 0);

            rig.Scale(new ScaleParams(2.0, 1.0));
            rig.Flash(new FlashParams(1.0, 1.0));
            rig.Update(0.25);
            var scaleBefore = renderer.Placements[handle.Value].Scale;

            rig.FreezePresentation(false);
            rig.Update(0.25);
            Assert.Equal(scaleBefore, renderer.Placements[handle.Value].Scale, 9);
            Assert.Equal(0.5, renderer.MaterialParams[handle.Value]["flash_intensity"], 9);

            rig.UnfreezePresentation();
            rig.Update(0.25);
            Assert.NotEqual(scaleBefore, renderer.Placements[handle.Value].Scale);
        }

        [Fact]
        public void PlayerWithoutPauseSupport_IsSkippedSilently()
        {
            // 既有实现（未重写默认接口成员）报告"未暂停"、SetPaused 空操作：rig 冻结不抛异常。
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var legacy = new LegacyPlayer();
            var rig = new SpriteCharacterRig(
                new Id("unit.hero_1"), renderer, handle, new RenderConventionHost(), SpriteInfo(),
                resourceTracker: null, frameAnimPlayer: legacy, options: null);

            rig.FreezePresentation(false);
            Assert.True(rig.IsPresentationFrozen);
            Assert.False(((IFrameAnimPlayer)legacy).IsPaused);
        }

        private sealed class LegacyPlayer : IFrameAnimPlayer
        {
            public void Play(Id clipId, bool loop, double speed) { }
            public void Stop() { }
            public SubscriptionHandle OnComplete(Action callback) => new SubscriptionHandle(() => { });
            public SubscriptionHandle OnAnimEvent(Action<string> callback) => new SubscriptionHandle(() => { });
        }
    }
}
