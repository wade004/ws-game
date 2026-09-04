using System;
using System.Collections.Generic;
using Adapters.Stub;
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
        public TestSpriteView(IRenderer2D renderer, IRenderConventionHost conventions, DisplayInfo displayInfo, RenderOptions? options = null)
            : base(renderer, conventions, displayInfo, options)
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

            view.SyncPose(new Vec2(1, 10), Direction.FromQuantized(0.0, 8), 0.0);

            var handleValue = new List<int>(renderer.Transforms.Keys)[0];
            var transform = renderer.Transforms[handleValue];
            Assert.Equal(new Vec2(1, 10), transform.Position);
            Assert.Equal(12.0, transform.SortY); // 10 + sortOffset(2.0)
            Assert.Equal(RenderLayers.Units, transform.Layer);
            Assert.Equal(1.5, transform.Scale);
            Assert.False(transform.FlipX);
        }

        [Fact]
        public void SyncPose_WritesHeightOffsetShaderParam()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo(), new RenderOptions { PixelsPerUnit = 10.0 });
            view.Bind(new Id("unit.hero_1"));

            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 3.0);

            var handleValue = new List<int>(renderer.ShaderParams.Keys)[0];
            Assert.Equal(30.0, renderer.ShaderParams[handleValue][SpriteViewBase.HeightOffsetShaderParam]);
        }

        [Fact]
        public void SyncPose_MirroredDirection_SetsFlipXTrue()
        {
            var renderer = new StubRenderer2D();
            var mirrorPairs = new[] { new MirrorPair(new Id("dir.nw"), new Id("dir.ne"), true) };
            var displayInfo = new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.creature.hero", 8, mirrorPairs), null);
            var view = new TestSpriteView(renderer, new RenderConventionHost(), displayInfo);
            view.Bind(new Id("unit.hero_1"));

            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI * 3 / 4, 8), 0.0);

            var handleValue = new List<int>(renderer.Transforms.Keys)[0];
            Assert.True(renderer.Transforms[handleValue].FlipX);
        }

        [Fact]
        public void SetPaperdollLayers_CallsSetLayersWithResolvedIdsInOrder()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo());
            view.Bind(new Id("unit.hero_1"));

            view.SetPaperdollLayersPublic(new[] { "body", "chest_armor" }, Direction.FromQuantized(0.0, 8));

            var handleValue = new List<int>(renderer.Layers.Keys)[0];
            var layers = renderer.Layers[handleValue];
            Assert.Equal(2, layers.Count);
            Assert.Equal(new Id("layer.body"), layers[0]);
            Assert.Equal(new Id("layer.chest_armor"), layers[1]);
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
        public void OnEvent_DefaultImplementation_DoesNothing_NoThrow()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeSpriteDisplayInfo());
            view.Bind(new Id("unit.hero_1"));

            var ex = Record.Exception(() => view.OnEvent(new PlaybackFinishedEvent()));

            Assert.Null(ex);
        }
    }
}
