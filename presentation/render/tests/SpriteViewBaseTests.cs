using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Presentation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>把 <c>SetPaperdollLayers</c>（<c>protected</c>）暴露成 <c>public</c> 供测试直接调用
    /// 的最小 <see cref="SpriteViewBase"/> 子类；<see cref="ResolveLayerResourceId"/> 沿用基类默认
    /// 实现（见 <c>SpriteViewBase</c> 判断记录）。</summary>
    internal sealed class TestSpriteView : SpriteViewBase
    {
        public TestSpriteView(
            IRenderer2D renderer, IRenderConventionHost conventions, DisplayInfo displayInfo,
            RenderOptions? options = null, IResourceLoader? resourceLoader = null,
            IReadOnlyDictionary<Id, EquipVisualDef>? equipVisualByItemInstanceId = null,
            IFrameAnimPlayer? frameAnimPlayer = null)
            : base(renderer, conventions, displayInfo, options, resourceLoader, equipVisualByItemInstanceId, frameAnimPlayer)
        {
        }

        public void SetPaperdollLayersPublic(IReadOnlyList<string> layerNamesInOrder, Direction facing) =>
            SetPaperdollLayers(layerNamesInOrder, facing);
    }

    public class SpriteViewBaseTests
    {
        private static DisplayInfo MakeSpriteDisplayInfo(double sortOffset = 0.0, double scale = 1.0) =>
            new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Sprite,
                null, null, null, scale, Core.Foundation.DisplayInfo.ShadowMode.Blob, sortOffset, null,
                new SpriteInfo("sprite.creature.hero", 8), null);

        [Fact]
        public void Construct_CreatesSpriteInstance_UsingParsedSpriteSetId()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo());

            Assert.Single(renderer.CreatedSpriteSets);
            var handleValue = new List<int>(renderer.CreatedSpriteSets.Keys)[0];
            Assert.Equal(new Id("sprite.creature.hero"), renderer.CreatedSpriteSets[handleValue]);
        }

        [Fact]
        public void Construct_NonSpriteDisplayInfo_Throws()
        {
            var renderer = new StubRenderer2D();
            var modelInfo = new DisplayInfo(
                new Id("display.golem"), DisplayCategory.Creature, new Id("creature.golem"), DisplayKind.Model,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                null, new ModelInfo(new Id("model.golem"), new Id("display.golem_anim")));

            Assert.Throws<ArgumentException>(() => new TestSpriteView(renderer, new RenderConventionHost(), modelInfo));
        }

        [Fact]
        public void Bind_SetsEntityIdAndIsAlive()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo());

            view.Bind(new Id("unit.hero_1"));

            Assert.Equal(new Id("unit.hero_1"), view.EntityId);
            Assert.True(view.IsAlive);
        }

        [Fact]
        public void SyncPose_BeforeBind_Throws()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo());

            Assert.Throws<InvalidOperationException>(() => view.SyncPose(Vec2.Zero, Direction.FromQuantized(0, 8), 0));
        }

        [Fact]
        public void SyncPose_AfterBind_CallsSetTransformWithComputedSortY()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo(sortOffset: 2.0, scale: 1.5));
            view.Bind(new Id("unit.hero_1"));

            // index 2（角度 90 度）-> "dir.front"：canonical 档位本身无默认镜像，flipX 应为 false
            // （见 Presentation.Common.DirectionSlots 类型注释"索引→档位对应表"）。
            view.SyncPose(new Vec2(1, 10), Direction.FromQuantized(System.Math.PI / 2, 8), 0.0);

            var handleValue = new List<int>(renderer.Transforms.Keys)[0];
            var transform = renderer.Transforms[handleValue];
            Assert.Equal(new Vec2(1, 10), transform.Position);
            Assert.Equal(12.0, transform.SortY); // 10 + sortOffset(2.0)
            Assert.Equal(RenderLayers.Units, transform.Layer);
            Assert.Equal(1.5, transform.Scale);
            Assert.False(transform.FlipX);
        }

        [Fact]
        public void SyncPose_PassesHeightThroughSetTransform()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo(), new RenderOptions { PixelsPerUnit = 10.0 });
            view.Bind(new Id("unit.hero_1"));

            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI / 2, 8), 3.0);

            var handleValue = new List<int>(renderer.Transforms.Keys)[0];
            // 高度经 IRenderer2D.SetTransform 的 height 参数传递（ADR-0016 决策 2），
            // 不再借用 SetShaderParam 通道。
            Assert.Equal(30.0, renderer.Transforms[handleValue].Height);
            Assert.Empty(renderer.ShaderParams);
        }

        [Fact]
        public void SyncPose_MirroredDirection_SetsFlipXTrue()
        {
            var renderer = new StubRenderer2D();
            // index 0（角度 0 度）-> "dir.side_l"：未登记 mirror_pairs 时按 14 默认镜像表回退，等价于
            // 显式登记 side_l 镜像自 side_r（此处显式登记只是为了同时覆盖"显式登记"这条路径）。
            var mirrorPairs = new[] { new MirrorPair(DirectionSlots.SideL, DirectionSlots.SideR, true) };
            var displayInfo = new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.creature.hero", 8, mirrorPairs), null);
            var view = new TestSpriteView(renderer, new RenderConventionHost(), displayInfo);
            view.Bind(new Id("unit.hero_1"));

            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);

            var handleValue = new List<int>(renderer.Transforms.Keys)[0];
            Assert.True(renderer.Transforms[handleValue].FlipX);
        }

        [Fact]
        public void SetPaperdollLayers_CallsSetLayersWithResolvedIdsInOrder()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo());
            view.Bind(new Id("unit.hero_1"));

            // index 2（角度 90 度）-> "dir.front"，配合 MakeSpriteDisplayInfo 的
            // sprite_set_id "sprite.creature.hero"：按 14 第 1.2 节命名模板，资源 Id 应为
            // "layer.<sprite_set_id 去掉类别前缀、点号换下划线>__<方向裸档位名>__<层名>"（见
            // SpriteViewBase.ResolveLayerResourceId 判断记录）。
            view.SetPaperdollLayersPublic(new[] { "body", "chest_armor" }, Direction.FromQuantized(System.Math.PI / 2, 8));

            var handleValue = new List<int>(renderer.Layers.Keys)[0];
            var layers = renderer.Layers[handleValue];
            Assert.Equal(2, layers.Count);
            Assert.Equal(new Id("layer.creature_hero__front__body"), layers[0]);
            Assert.Equal(new Id("layer.creature_hero__front__chest_armor"), layers[1]);
        }

        [Fact]
        public void Destroy_CallsDestroySpriteInstance_AndIsIdempotent()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo());
            view.Bind(new Id("unit.hero_1"));

            view.Destroy();
            var ex = Record.Exception(() => view.Destroy());

            Assert.False(view.IsAlive);
            Assert.Null(ex);
        }

        [Fact]
        public void Construct_WithResourceLoaderInjected_LoadsSpriteSetIdAsImage()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader();

            _ = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo(), resourceLoader: loader);

            var requests = loader.LoadRequests.FindAll(r => r.ResourceId.Equals(new Id("sprite.creature.hero")));
            Assert.Single(requests);
            Assert.Equal(ResourceKind.Image, requests[0].Kind);
        }

        [Fact]
        public void SetPaperdollLayers_WithResourceLoaderInjected_LoadsEachResolvedLayerId_OnlyOnce()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo(), resourceLoader: loader);
            view.Bind(new Id("unit.hero_1"));

            view.SetPaperdollLayersPublic(new[] { "body" }, Direction.FromQuantized(System.Math.PI / 2, 8));
            view.SetPaperdollLayersPublic(new[] { "body" }, Direction.FromQuantized(System.Math.PI / 2, 8));

            var requests = loader.LoadRequests.FindAll(r => r.ResourceId.Equals(new Id("layer.creature_hero__front__body")));
            Assert.Single(requests);
            Assert.Equal(ResourceKind.Image, requests[0].Kind);
        }

        [Fact]
        public void OnEvent_DefaultImplementation_DoesNothing_NoThrow()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo());
            view.Bind(new Id("unit.hero_1"));

            // 任意 IEvent 均可——本用例只关心默认实现不抛异常，不关心具体事件语义（原用
            // Presentation.Common.PlaybackFinishedEvent 当占位事件，该类型已随 09 勘误"去重"删除，
            // 见 PresentationEventKeys 类型注释，这里换一个已在本文件 using 范围内的真实事件类型）。
            var ex = Record.Exception(() => view.OnEvent(new UnitMovedEvent(new Id("unit.other"), Vec2.Zero)));

            Assert.Null(ex);
        }

        // -----------------------------------------------------------------
        // 缺口 10：item.equipped/item.unequipped 默认按 display.equip_visual 刷新纸娃娃层。
        // -----------------------------------------------------------------

        private static DisplayInfo MakeSpriteDisplayInfoWithLayers(IReadOnlyList<string> paperdollLayers) =>
            new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.creature.hero", 8, mirrorPairs: null, paperdollLayers: paperdollLayers), null);

        [Fact]
        public void OnEvent_ItemEquipped_KnownItemInstance_ReplacesLayerResourceAndCallsSetLayers()
        {
            var renderer = new StubRenderer2D();
            var displayInfo = MakeSpriteDisplayInfoWithLayers(new[] { "body", "hand_main" });
            var equipVisuals = new Dictionary<Id, EquipVisualDef>
            {
                [new Id("item.instance_1")] = new EquipVisualDef(
                    new Id("display.equip_visual.sword"), new Id("item.template.sword"), EquipVisualMode.SlotMesh,
                    slotId: new Id("slot.hand_main"), meshRef: new Id("layer.sword_hand_main"), socketId: null, modelRef: null),
            };
            var view = new TestSpriteView(renderer, new RenderConventionHost(), displayInfo, equipVisualByItemInstanceId: equipVisuals);
            view.Bind(new Id("unit.hero_1"));
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI / 2, 8), 0.0);

            view.OnEvent(new ItemEquippedEvent(new Id("unit.hero_1"), new Id("item.instance_1"), new Id("slot.hand_main")));

            var handleValue = new List<int>(renderer.CreatedSpriteSets.Keys)[0];
            var layers = renderer.Layers[handleValue];
            Assert.Equal(2, layers.Count);
            Assert.Equal(new Id("layer.creature_hero__front__body"), layers[0]);
            Assert.Equal(new Id("layer.sword_hand_main"), layers[1]);
        }

        [Fact]
        public void OnEvent_ItemEquipped_UnknownItemInstance_LeavesLayersUnchanged()
        {
            var renderer = new StubRenderer2D();
            var displayInfo = MakeSpriteDisplayInfoWithLayers(new[] { "body" });
            var equipVisuals = new Dictionary<Id, EquipVisualDef>();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), displayInfo, equipVisualByItemInstanceId: equipVisuals);
            view.Bind(new Id("unit.hero_1"));

            view.OnEvent(new ItemEquippedEvent(new Id("unit.hero_1"), new Id("item.instance_unknown"), new Id("slot.hand_main")));

            var handleValue = new List<int>(renderer.CreatedSpriteSets.Keys)[0];
            Assert.False(renderer.Layers.ContainsKey(handleValue));
        }

        [Fact]
        public void OnEvent_ItemUnequipped_RemovesOverride_RestoresDefaultLayerResource()
        {
            var renderer = new StubRenderer2D();
            var displayInfo = MakeSpriteDisplayInfoWithLayers(new[] { "body", "hand_main" });
            var equipVisuals = new Dictionary<Id, EquipVisualDef>
            {
                [new Id("item.instance_1")] = new EquipVisualDef(
                    new Id("display.equip_visual.sword"), new Id("item.template.sword"), EquipVisualMode.SlotMesh,
                    slotId: new Id("slot.hand_main"), meshRef: new Id("layer.sword_hand_main"), socketId: null, modelRef: null),
            };
            var view = new TestSpriteView(renderer, new RenderConventionHost(), displayInfo, equipVisualByItemInstanceId: equipVisuals);
            view.Bind(new Id("unit.hero_1"));
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI / 2, 8), 0.0);
            view.OnEvent(new ItemEquippedEvent(new Id("unit.hero_1"), new Id("item.instance_1"), new Id("slot.hand_main")));

            view.OnEvent(new ItemUnequippedEvent(new Id("unit.hero_1"), new Id("slot.hand_main"), new Id("item.instance_1")));

            var handleValue = new List<int>(renderer.CreatedSpriteSets.Keys)[0];
            var layers = renderer.Layers[handleValue];
            Assert.Equal(2, layers.Count);
            Assert.Equal(new Id("layer.creature_hero__front__body"), layers[0]);
            Assert.Equal(new Id("layer.creature_hero__front__hand_main"), layers[1]);
        }

        /// <summary>W3b 判断记录 2 收口配套用例：构造期注入的 <see cref="IFrameAnimPlayer"/> 经
        /// <see cref="SpriteViewBase"/> 转交内部 <c>Rig</c>，<c>Rig.PlayClip</c> 不再是结构性 no-op
        /// （见该类型构造函数 <c>frameAnimPlayer</c> 参数判断记录）。</summary>
        [Fact]
        public void Construct_WithFrameAnimPlayer_RigPlayClipForwardsToIt()
        {
            var renderer = new StubRenderer2D();
            var clipId = new Id("anim.sample_attack");
            var player = new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip> { [clipId] = new FrameAnimClip(clipId, 4, 4.0) });
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo(), frameAnimPlayer: player);

            view.Rig.PlayClip(clipId, loop: true, speed: 2.0);

            Assert.Equal(clipId, player.CurrentClipId);
        }

        /// <summary>配套用例：构造期未注入时，<see cref="SpriteViewBase.AttachFrameAnimPlayer"/> 允许
        /// 之后再补一个 <see cref="IFrameAnimPlayer"/>，同 <see cref="SpriteCharacterRig.AttachFrameAnimPlayer"/>
        /// 判断记录"精灵根节点天然晚于构造"的典型调用方（如 <c>UnitySpriteView</c>）。</summary>
        [Fact]
        public void AttachFrameAnimPlayer_PostConstruction_RigPlayClipForwardsToIt()
        {
            var renderer = new StubRenderer2D();
            var clipId = new Id("anim.sample_attack");
            var player = new FrameAnimPlayer(new Dictionary<Id, FrameAnimClip> { [clipId] = new FrameAnimClip(clipId, 4, 4.0) });
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo());

            view.AttachFrameAnimPlayer(player);
            view.Rig.PlayClip(clipId, loop: true, speed: 2.0);

            Assert.Equal(clipId, player.CurrentClipId);
        }
    }
}
