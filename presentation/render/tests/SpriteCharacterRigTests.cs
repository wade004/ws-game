using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.Common;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.PresentationRender
{
    public class SpriteCharacterRigTests
    {
        private static DisplayInfo MakeDisplayInfo(IReadOnlyDictionary<string, AnchorDef>? anchors = null) =>
            new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.creature.hero", 8, mirrorPairs: null, paperdollLayers: null, anchorPoints: anchors), null);

        private static SpriteCharacterRig MakeRig(
            StubRenderer2D renderer, Core.Foundation.EngineAdapter.SpriteHandle handle,
            IReadOnlyDictionary<string, AnchorDef>? anchors = null, IFrameAnimPlayer? frameAnimPlayer = null,
            RenderOptions? options = null) =>
            new SpriteCharacterRig(
                new Id("unit.hero_1"), renderer, handle, new RenderConventionHost(), MakeDisplayInfo(anchors),
                resourceTracker: null, frameAnimPlayer: frameAnimPlayer, options: options);

        [Fact]
        public void ConstructWithModelDisplayInfo_Throws()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var modelInfo = new DisplayInfo(
                new Id("display.hero3d"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Model,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null, null,
                new ModelInfo(new Id("model.hero"), new Id("display.anim_set.hero")));

            Assert.Throws<ArgumentException>(() =>
                new SpriteCharacterRig(new Id("unit.hero_1"), renderer, handle, new RenderConventionHost(), modelInfo));
        }

        [Fact]
        public void SetAnimState_UpdatesCurrentAnimState()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var rig = MakeRig(renderer, handle);

            Assert.Equal(AnimState.Idle, rig.CurrentAnimState);
            rig.SetAnimState(AnimState.Attack);
            Assert.Equal(AnimState.Attack, rig.CurrentAnimState);
        }

        [Fact]
        public void ResolveAnchorLocalOffset_UnknownAnchor_ReturnsNull()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var rig = MakeRig(renderer, handle);

            var result = rig.ResolveAnchorLocalOffset(new Id("anchor.hand_main"), Direction.FromQuantized(0.0, 8));

            Assert.Null(result);
        }

        [Fact]
        public void ResolveAnchorLocalOffset_KnownAnchor_ReturnsOffset_NotMirrored()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var anchors = new Dictionary<string, AnchorDef> { ["hand_main"] = new AnchorDef("layer.hand", new Vec2(1.0, 0.5)) };
            var rig = MakeRig(renderer, handle, anchors);

            // 角度 90 度 → front 档位（不镜像，见 render/README 索引→档位对应表）。
            var result = rig.ResolveAnchorLocalOffset(new Id("anchor.hand_main"), Direction.FromQuantized(Math.PI / 2, 8));

            Assert.Equal(new Vec2(1.0, 0.5), result);
        }

        [Fact]
        public void ResolveAnchorLocalOffset_MirroredDirection_FlipsXComponent()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var anchors = new Dictionary<string, AnchorDef> { ["hand_main"] = new AnchorDef("layer.hand", new Vec2(1.0, 0.5)) };
            var rig = MakeRig(renderer, handle, anchors);

            // 角度 45 度 → front_side_l（量化索引 1，见 render/README 索引→档位对应表）：_l 档位
            // 默认镜像自同族 _r（DirectionSlots.MirrorSourceOf 回退，spriteInfo 未显式登记
            // mirror_pairs 时生效），FlipX=true。
            var result = rig.ResolveAnchorLocalOffset(new Id("anchor.hand_main"), Direction.FromQuantized(Math.PI / 4, 8));

            Assert.Equal(new Vec2(-1.0, 0.5), result);
        }

        /// <summary>GP-PRES-07 收口回归：<c>offset_by_direction</c> 命中时应覆盖默认
        /// <c>offset</c>，未命中的方向仍回退默认值——用同一个锚点配置 front（默认）与 side_r
        /// （覆盖）两套 offset，分别断言两个方向解析出不同的世界/本地坐标。</summary>
        [Fact]
        public void ResolveAnchorLocalOffset_OffsetByDirection_OverridesDefaultOffset_ForMatchingSlotOnly()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var anchors = new Dictionary<string, AnchorDef>
            {
                ["hand_main"] = new AnchorDef(
                    "layer.hand", new Vec2(1.0, 0.5),
                    offsetByDirection: new Dictionary<Id, Vec2> { [new Id("dir.side_r")] = new Vec2(3.0, 0.5) })
            };
            var rig = MakeRig(renderer, handle, anchors);

            // 角度 90 度 → front 档位：没有覆盖值，回退默认 offset。
            var frontResult = rig.ResolveAnchorLocalOffset(new Id("anchor.hand_main"), Direction.FromQuantized(Math.PI / 2, 8));
            Assert.Equal(new Vec2(1.0, 0.5), frontResult);

            // 角度 180 度 → side_r 档位（量化索引 4，见 render/README 索引→档位对应表）：命中
            // offset_by_direction 覆盖值，不镜像（side_r 本身是原创绘制档位，不是 _l 档位）。
            var sideResult = rig.ResolveAnchorLocalOffset(new Id("anchor.hand_main"), Direction.FromQuantized(Math.PI, 8));
            Assert.Equal(new Vec2(3.0, 0.5), sideResult);
        }

        [Fact]
        public void ComposeAndApplyLayers_CallsRendererSetLayers_WithResolvedIds()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var rig = MakeRig(renderer, handle);

            rig.ComposeAndApplyLayers(
                new[] { "body" }, Direction.FromQuantized(Math.PI / 2, 8),
                placement => new Id($"layer.{placement.LayerName}"));

            Assert.Equal(new[] { new Id("layer.body") }, renderer.Layers[handle.Value]);
        }

        [Fact]
        public void ApplyLayers_CallsRendererSetLayers_Directly()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var rig = MakeRig(renderer, handle);

            rig.ApplyLayers(new[] { new Id("layer.custom") });

            Assert.Equal(new[] { new Id("layer.custom") }, renderer.Layers[handle.Value]);
        }

        // -----------------------------------------------------------------
        // 诊断记录 diag-isolation.md 根治配套用例："资源加载完成后回填已渲染层"
        // （SpriteCharacterRig 类型注释同名判断记录）。
        // -----------------------------------------------------------------

        private static SpriteCharacterRig MakeRigWithTracker(
            StubRenderer2D renderer, Core.Foundation.EngineAdapter.SpriteHandle handle, StubResourceLoader loader) =>
            new SpriteCharacterRig(
                new Id("unit.hero_1"), renderer, handle, new RenderConventionHost(), MakeDisplayInfo(),
                resourceTracker: new ResourceReferenceTracker(loader));

        [Fact]
        public void ComposeAndApplyLayers_ResourceLoadCompletesLater_ReappliesFullLayerList()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var rig = MakeRigWithTracker(renderer, handle, loader);
            var resourceId = new Id("layer.body");
            loader.Register(resourceId);

            rig.ComposeAndApplyLayers(new[] { "body" }, Direction.FromQuantized(Math.PI / 2, 8), _ => resourceId);

            // 首次同步调用：此时资源尚未加载完成（引擎侧会落地占位方块，本桩不模拟像素，只记调用）。
            Assert.Single(renderer.SetLayersCalls);

            loader.CompletePending(resourceId);

            // 加载完成后应自动补一次 SetLayers，用同一份已知层列表重新应用——不是"从未再调用"。
            Assert.Equal(2, renderer.SetLayersCalls.Count);
            Assert.Equal(new[] { resourceId }, renderer.SetLayersCalls[1].Layers);
            Assert.Equal(new[] { resourceId }, renderer.Layers[handle.Value]);
        }

        [Fact]
        public void ComposeAndApplyLayers_ResourceLoadFails_DoesNotReapply_RecordsDiagnosticWarning()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var rig = MakeRigWithTracker(renderer, handle, loader);
            var resourceId = new Id("layer.missing");
            // DeferCallbacks 模式下 Register/Unregister 不影响结果（见 StubResourceLoader 类型注释）：
            // 成功/失败完全由测试显式调用 CompletePending/FailPending 决定，这里用 FailPending 模拟
            // 一次真实的加载失败（而不是"尚未加载完成"）。

            rig.ComposeAndApplyLayers(new[] { "body" }, Direction.FromQuantized(Math.PI / 2, 8), _ => resourceId);
            Assert.Single(renderer.SetLayersCalls);

            loader.FailPending(resourceId);

            // 加载失败：不重新应用（占位保持不变），但必须留下一条可诊断的警告，不能静默。
            Assert.Single(renderer.SetLayersCalls);
            var recorder = Assert.IsType<PresentationDiagnosticsRecorder>(rig.Diagnostics);
            Assert.Contains(recorder.Warnings, w => w.Contains(resourceId.Value) && w.Contains("加载失败"));
        }

        /// <summary>多个层引用同一资源 id 时只应有一次回调驱动的重新应用（同一资源 id 只会
        /// LoadAsync 一次），且那一次会覆盖全部引用它的层——不需要按层分别处理，见类型注释
        /// "幂等与防重复刷新风暴"判断记录第②条。</summary>
        [Fact]
        public void ComposeAndApplyLayers_MultipleLayersShareSameResource_OnlyOneReapply()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var rig = MakeRigWithTracker(renderer, handle, loader);
            var sharedId = new Id("layer.shared");
            loader.Register(sharedId);

            rig.ComposeAndApplyLayers(new[] { "body", "hand_main" }, Direction.FromQuantized(Math.PI / 2, 8), _ => sharedId);
            Assert.Single(renderer.SetLayersCalls);

            loader.CompletePending(sharedId);

            Assert.Equal(2, renderer.SetLayersCalls.Count);
            Assert.Equal(new[] { sharedId, sharedId }, renderer.SetLayersCalls[1].Layers);
        }

        [Fact]
        public void HandleResourceLoadCompleted_AfterMarkDestroyed_DoesNotThrow_AndDoesNotReapply()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var rig = MakeRigWithTracker(renderer, handle, loader);
            var resourceId = new Id("layer.body");
            loader.Register(resourceId);

            rig.ComposeAndApplyLayers(new[] { "body" }, Direction.FromQuantized(Math.PI / 2, 8), _ => resourceId);
            Assert.Single(renderer.SetLayersCalls);

            rig.MarkDestroyed();
            renderer.DestroySpriteInstance(handle);

            var ex = Record.Exception(() => loader.CompletePending(resourceId));

            Assert.Null(ex);
            // 仍然只有销毁前那一次调用：已销毁后到达的回调不应再触碰渲染实例。
            Assert.Single(renderer.SetLayersCalls);
        }

        [Fact]
        public void ProceduralAnim_Flash_AppliesShaderParam_AndInvokesCallerSample()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var rig = MakeRig(renderer, handle);
            double? callerSample = null;

            rig.ProceduralAnim.Flash(new FlashParams(1.0, 1.0), onSample: v => callerSample = v);
            rig.Update(0.5);

            Assert.Equal(0.5, renderer.ShaderParams[handle.Value]["flash_intensity"], 6);
            Assert.Equal(0.5, callerSample!.Value, 6);
        }

        [Fact]
        public void ProceduralAnim_Move_DoesNotTouchRenderer_OnlyForwardsSample()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var rig = MakeRig(renderer, handle);
            Vec2? sampled = null;

            rig.ProceduralAnim.Move(new MoveParams(new Vec2(2, 0), 1.0), onSample: v => sampled = v);
            rig.Update(1.0);

            Assert.Equal(new Vec2(2, 0), sampled);
            Assert.False(renderer.ShaderParams.ContainsKey(handle.Value));
        }

        [Fact]
        public void PlayClip_NoFrameAnimPlayerInjected_DoesNotThrow()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var rig = MakeRig(renderer, handle);

            var ex = Record.Exception(() => rig.PlayClip(new Id("anim.sample")));
            Assert.Null(ex);
        }

        [Fact]
        public void PlayClip_ForwardsToInjectedFrameAnimPlayer()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var clipId = new Id("anim.sample_attack");
            var player = new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip> { [clipId] = new FrameAnimClip(clipId, 4, 4.0) });
            var rig = MakeRig(renderer, handle, frameAnimPlayer: player);

            rig.PlayClip(clipId, loop: true, speed: 2.0);

            Assert.Equal(clipId, player.CurrentClipId);
        }

        [Fact]
        public void HitFrameReached_LogicDrivenDefault_NeverFires()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var clipId = new Id("anim.sample_attack");
            var keyframes = new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 1 };
            var player = new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip> { [clipId] = new FrameAnimClip(clipId, 4, 4.0, keyframes) });
            var rig = MakeRig(renderer, handle, frameAnimPlayer: player, options: new RenderOptions { HitFrameSync = HitFrameSyncStrategy.LogicDriven });
            var fired = false;
            rig.HitFrameReached += _ => fired = true;

            rig.PlayClip(clipId);
            player.Update(0.25);

            Assert.False(fired);
        }

        [Fact]
        public void HitFrameReached_AnimKeyframeDriven_FiresWithEntityId_AtHitFrame()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var clipId = new Id("anim.sample_attack");
            var keyframes = new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 1 };
            var player = new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip> { [clipId] = new FrameAnimClip(clipId, 4, 4.0, keyframes) });
            var rig = MakeRig(renderer, handle, frameAnimPlayer: player, options: new RenderOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven });
            Id? firedFor = null;
            rig.HitFrameReached += id => firedFor = id;

            rig.PlayClip(clipId);
            player.Update(0.25); // 第 1 帧。

            Assert.Equal(new Id("unit.hero_1"), firedFor);
        }

        /// <summary>W3b 判断记录 2 收口配套用例：<see cref="SpriteCharacterRig.AttachFrameAnimPlayer"/>
        /// 允许在构造之后再补一个 <see cref="IFrameAnimPlayer"/>，验证 <see cref="ICharacterRig.PlayClip"/>
        /// 不再要求构造期就拿到具体实现（见该方法判断记录"精灵根节点天然晚于 rig 构造"）。</summary>
        [Fact]
        public void AttachFrameAnimPlayer_PostConstruction_PlayClipForwardsToIt()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var clipId = new Id("anim.sample_attack");
            var player = new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip> { [clipId] = new FrameAnimClip(clipId, 4, 4.0) });
            var rig = MakeRig(renderer, handle);

            // 构造期未注入 frameAnimPlayer：此时 PlayClip 仍是静默无操作（同 PlayClip_NoFrameAnimPlayerInjected_DoesNotThrow）。
            rig.AttachFrameAnimPlayer(player);
            rig.PlayClip(clipId, loop: true, speed: 2.0);

            Assert.Equal(clipId, player.CurrentClipId);
        }

        /// <summary>配套用例：补接线之后，<see cref="HitFrameSyncStrategy.AnimKeyframeDriven"/> 策略下的
        /// 命中帧同步同样生效，不要求 <see cref="IFrameAnimPlayer"/> 在构造期就已就绪。</summary>
        [Fact]
        public void AttachFrameAnimPlayer_PostConstruction_AnimKeyframeDriven_FiresHitFrameReached()
        {
            var renderer = new StubRenderer2D();
            var handle = renderer.CreateSpriteInstance(new Id("sprite.creature.hero"));
            var clipId = new Id("anim.sample_attack");
            var keyframes = new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 1 };
            var player = new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip> { [clipId] = new FrameAnimClip(clipId, 4, 4.0, keyframes) });
            var rig = MakeRig(renderer, handle, options: new RenderOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven });
            Id? firedFor = null;
            rig.HitFrameReached += id => firedFor = id;

            rig.AttachFrameAnimPlayer(player);
            rig.PlayClip(clipId);
            player.Update(0.25); // 第 1 帧。

            Assert.Equal(new Id("unit.hero_1"), firedFor);
        }
    }
}
