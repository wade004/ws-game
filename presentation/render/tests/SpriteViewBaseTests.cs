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
        private static DisplayInfo MakeSpriteDisplayInfo(
            double sortOffset = 0.0, double scale = 1.0,
            Core.Foundation.DisplayInfo.ShadowMode shadow = Core.Foundation.DisplayInfo.ShadowMode.Blob) =>
            new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Sprite,
                null, null, null, scale, shadow, sortOffset, null,
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

        // -----------------------------------------------------------------
        // GP-PRES-05 收口回归（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
        // 此前 DisplayInfo.Shadow 只存在于数据/转换层，从未被任何实际 View 消费——构造时应该按
        // DisplayInfo.Shadow 调用 IRenderer2D.SetShadow，且经 ShadowSpec.ToEngineShadowMode 正确
        // 转换枚举（数据侧 Core.Foundation.DisplayInfo.ShadowMode → 引擎侧
        // Core.Foundation.EngineAdapter.ShadowMode）。分别覆盖 none/blob/projected 三种取值。
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(Core.Foundation.DisplayInfo.ShadowMode.None, Core.Foundation.EngineAdapter.ShadowMode.None)]
        [InlineData(Core.Foundation.DisplayInfo.ShadowMode.Blob, Core.Foundation.EngineAdapter.ShadowMode.Blob)]
        [InlineData(Core.Foundation.DisplayInfo.ShadowMode.Projected, Core.Foundation.EngineAdapter.ShadowMode.Projected)]
        public void Construct_SetsShadowOnRenderer_MatchingDisplayInfoShadow(
            Core.Foundation.DisplayInfo.ShadowMode dataShadow, Core.Foundation.EngineAdapter.ShadowMode expectedEngineShadow)
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo(shadow: dataShadow));

            var handleValue = new List<int>(renderer.CreatedSpriteSets.Keys)[0];
            Assert.True(renderer.Shadows.TryGetValue(handleValue, out var actualShadow));
            Assert.Equal(expectedEngineShadow, actualShadow);
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

        // ADR-0071 决策 1 收口：EquipVisualDef.MeshRef 的 sprite 型语义从"该层最终资源 Id"改为
        // "装备层资源集引用"，与身体层 SpriteSetId 同一位置、经 ResolveEquipLayerResourceId 与身体层
        // ResolveLayerResourceId 同一套方向档位公式换算。以下用例的 meshRef 统一取
        // "paperdoll.item.sword"（真实数据惯例前缀），StripCategoryPrefix 后为 "item_sword"。

        [Fact]
        public void OnEvent_ItemEquipped_KnownItemInstance_ReplacesLayerResourceAndCallsSetLayers()
        {
            var renderer = new StubRenderer2D();
            var displayInfo = MakeSpriteDisplayInfoWithLayers(new[] { "body", "hand_main" });
            var equipVisuals = new Dictionary<Id, EquipVisualDef>
            {
                [new Id("item.instance_1")] = new EquipVisualDef(
                    new Id("display.equip_visual.sword"), new Id("item.template.sword"), EquipVisualMode.SlotMesh,
                    slotId: new Id("slot.hand_main"), meshRef: new Id("paperdoll.item.sword"), socketId: null, modelRef: null),
            };
            var view = new TestSpriteView(renderer, new RenderConventionHost(), displayInfo, equipVisualByItemInstanceId: equipVisuals);
            view.Bind(new Id("unit.hero_1"));
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI / 2, 8), 0.0);

            view.OnEvent(new ItemEquippedEvent(new Id("unit.hero_1"), new Id("item.instance_1"), new Id("slot.hand_main")));

            var handleValue = new List<int>(renderer.CreatedSpriteSets.Keys)[0];
            var layers = renderer.Layers[handleValue];
            Assert.Equal(2, layers.Count);
            Assert.Equal(new Id("layer.creature_hero__front__body"), layers[0]);
            // 见 ResolveEquipLayerResourceId 判断记录：与身体层同一套 "layer.<资源集名字去掉类别前缀>
            // __<方向裸档位名>__<层名>" 公式，装备层资源集名字来自 meshRef（去掉 "paperdoll" 前缀）。
            Assert.Equal(new Id("layer.item_sword__front__hand_main"), layers[1]);
        }

        /// <summary>ADR-0071 决策 1 核心验收点：同一件装备在三个不同方向档位下解析出三个不同的层资源
        /// Id（修复前 <c>mesh_ref</c> 恒被当作最终资源 Id 直接使用，三个朝向下会得到同一个值——本用例
        /// 先在 front 朝向下取值，再切到 side_l/back 两个朝向重新装备触发 RebuildEquippedLayers，
        /// 断言三次解析结果按规则算出且互不相同）。<see cref="ResetEquipmentVisuals"/> 是"按当前
        /// _lastFacing 重算完整层列表"的既有公开入口（同存档恢复/跨图重放场景使用的路径），本用例借它
        /// 在朝向切换后触发一次重算，不新增测试专属钩子。</summary>
        [Fact]
        public void RebuildEquippedLayers_AcrossThreeDirections_ResolvesThreeDistinctResourceIds()
        {
            var renderer = new StubRenderer2D();
            var displayInfo = MakeSpriteDisplayInfoWithLayers(new[] { "body", "hand_main" });
            var itemInstanceId = new Id("item.instance_1");
            var slotId = new Id("slot.hand_main");
            var equipVisuals = new Dictionary<Id, EquipVisualDef>
            {
                [itemInstanceId] = new EquipVisualDef(
                    new Id("display.equip_visual.sword"), new Id("item.template.sword"), EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("paperdoll.item.sword"), socketId: null, modelRef: null),
            };
            var view = new TestSpriteView(renderer, new RenderConventionHost(), displayInfo, equipVisualByItemInstanceId: equipVisuals);
            view.Bind(new Id("unit.hero_1"));
            var equipped = new[] { new EquippedItemRef(slotId, itemInstanceId, new Id("item.template.sword")) };
            var handleValue = new List<int>(renderer.CreatedSpriteSets.Keys)[0];

            // index 2/8（90 度）-> canonical "front"，无镜像。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI / 2, 8), 0.0);
            view.ResetEquipmentVisuals(equipped);
            var atFront = renderer.Layers[handleValue][1];

            // index 6/8（270 度）-> canonical "back"。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI * 3 / 2, 8), 0.0);
            view.ResetEquipmentVisuals(equipped);
            var atBack = renderer.Layers[handleValue][1];

            // index 0/8（0 度）-> "side_l"，未登记 mirror_pairs 时按 14 默认镜像表回退到 "side_r"
            // （见 DirectionSlots 类型注释"8 方向完整对照表"）。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);
            view.ResetEquipmentVisuals(equipped);
            var atSideR = renderer.Layers[handleValue][1];

            Assert.Equal(new Id("layer.item_sword__front__hand_main"), atFront);
            Assert.Equal(new Id("layer.item_sword__back__hand_main"), atBack);
            Assert.Equal(new Id("layer.item_sword__side_r__hand_main"), atSideR);
            Assert.NotEqual(atFront, atBack);
            Assert.NotEqual(atFront, atSideR);
            Assert.NotEqual(atBack, atSideR);
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
                    slotId: new Id("slot.hand_main"), meshRef: new Id("paperdoll.item.sword"), socketId: null, modelRef: null),
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

        // -----------------------------------------------------------------
        // 诊断记录 diag-isolation.md 根治配套用例："资源加载完成后回填已渲染层"经装备驱动的
        // RebuildEquippedLayers 路径同样生效（不只是 SetPaperdollLayers/ComposeAndApplyLayers 那条
        // 朴素路径），见 SpriteCharacterRig 类型注释同名判断记录。
        // -----------------------------------------------------------------

        [Fact]
        public void OnEvent_ItemEquipped_ResourceLoadCompletesLater_ReappliesLayers()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var displayInfo = MakeSpriteDisplayInfoWithLayers(new[] { "body", "hand_main" });
            var equipLayerSetRef = new Id("paperdoll.item.sword");
            // 见 ResolveEquipLayerResourceId 判断记录：facing=front（index 2/8）下的换算结果。
            var resolvedResourceId = new Id("layer.item_sword__front__hand_main");
            loader.Register(resolvedResourceId);
            // 构造期会先以 sprite_set_id 发起一次加载（与本用例无关），DeferCallbacks 模式下不会
            // 自动完成，不影响下面对 resolvedResourceId 的断言。
            var equipVisuals = new Dictionary<Id, EquipVisualDef>
            {
                [new Id("item.instance_1")] = new EquipVisualDef(
                    new Id("display.equip_visual.sword"), new Id("item.template.sword"), EquipVisualMode.SlotMesh,
                    slotId: new Id("slot.hand_main"), meshRef: equipLayerSetRef, socketId: null, modelRef: null),
            };
            var view = new TestSpriteView(
                renderer, new RenderConventionHost(), displayInfo, resourceLoader: loader,
                equipVisualByItemInstanceId: equipVisuals);
            view.Bind(new Id("unit.hero_1"));
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI / 2, 8), 0.0);

            view.OnEvent(new ItemEquippedEvent(new Id("unit.hero_1"), new Id("item.instance_1"), new Id("slot.hand_main")));

            var handleValue = new List<int>(renderer.CreatedSpriteSets.Keys)[0];
            var callsBeforeCompletion = renderer.SetLayersCalls.Count;
            Assert.True(callsBeforeCompletion >= 1);
            Assert.Equal(resolvedResourceId, renderer.Layers[handleValue][1]);

            loader.CompletePending(resolvedResourceId);

            // 加载完成后应再补一次 SetLayers（同一份最新层列表），不是"从此再无动静"。
            Assert.True(renderer.SetLayersCalls.Count > callsBeforeCompletion);
            var lastCall = renderer.SetLayersCalls[renderer.SetLayersCalls.Count - 1];
            Assert.Equal(new Id("layer.creature_hero__front__body"), lastCall.Layers[0]);
            Assert.Equal(resolvedResourceId, lastCall.Layers[1]);
        }

        [Fact]
        public void Destroy_ThenResourceLoadCompletes_DoesNotThrow_AndDoesNotTouchDestroyedInstance()
        {
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var displayInfo = MakeSpriteDisplayInfoWithLayers(new[] { "body", "hand_main" });
            var equipLayerSetRef = new Id("paperdoll.item.sword");
            var resolvedResourceId = new Id("layer.item_sword__front__hand_main");
            loader.Register(resolvedResourceId);
            var equipVisuals = new Dictionary<Id, EquipVisualDef>
            {
                [new Id("item.instance_1")] = new EquipVisualDef(
                    new Id("display.equip_visual.sword"), new Id("item.template.sword"), EquipVisualMode.SlotMesh,
                    slotId: new Id("slot.hand_main"), meshRef: equipLayerSetRef, socketId: null, modelRef: null),
            };
            var view = new TestSpriteView(
                renderer, new RenderConventionHost(), displayInfo, resourceLoader: loader,
                equipVisualByItemInstanceId: equipVisuals);
            view.Bind(new Id("unit.hero_1"));
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI / 2, 8), 0.0);
            view.OnEvent(new ItemEquippedEvent(new Id("unit.hero_1"), new Id("item.instance_1"), new Id("slot.hand_main")));
            var callsBeforeDestroy = renderer.SetLayersCalls.Count;

            view.Destroy();

            var ex = Record.Exception(() => loader.CompletePending(resolvedResourceId));

            Assert.Null(ex);
            // Destroy 之后到达的迟到回调不应再触发任何一次 SetLayers（rig 已被标记销毁，直接跳过）。
            Assert.Equal(callsBeforeDestroy, renderer.SetLayersCalls.Count);
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
