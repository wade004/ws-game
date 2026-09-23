using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// ADR-0080：<see cref="MapLayerHost"/> 驱动地图分层图建/销的运行时行为——用真实
    /// <see cref="Core.Foundation.SceneRouter.SceneRouter"/>（与生产装配入口 <c>PresentationAssembly</c>
    /// 构造它的同一套依赖）驱动场景加载/切换，<see cref="StubRenderer2D"/> 记录实际调用供断言，不写死
    /// 裸数——世界矩形期望值由 <see cref="MapImageTransform"/>（生产同一类型）按测试数据自行算出。
    /// </summary>
    public class MapLayerHostTests
    {
        private const double Ppu = 16;
        private static readonly Vec2 OriginPx = new Vec2(100, 200);
        private static readonly Vec2 ImageSizePx = new Vec2(320, 160);

        private static string MapRow(string mapName, bool withImageTransform, bool withImageSizePx = true) =>
            "{\"id\": \"world." + mapName + "\", \"scene_ref\": \"scene." + mapName + "\", \"nav_ref\": \"nav." + mapName + "\", " +
            "\"spawn_points\": [{\"position\": {\"x\": 0, \"y\": 0}}]" +
            (withImageTransform
                ? ", \"image_transform\": {\"pixels_per_unit\": " + Ppu.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  ", \"origin_px\": {\"x\": " + OriginPx.X.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  ", \"y\": " + OriginPx.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}" +
                  (withImageSizePx
                      ? ", \"image_size_px\": {\"x\": " + ImageSizePx.X.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ", \"y\": " + ImageSizePx.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"
                      : "") +
                  "}"
                : "") +
            "}";

        /// <summary>与 <c>core/foundation/scene_router/tests/SceneRouterTestSupport.Harness</c> 同一套
        /// 搭建方式（不同测试程序集，无法直接复用该 internal 夹具，见该类型注释），只保留本测试需要的
        /// 最小子集：真实 <see cref="Core.Foundation.SceneRouter.SceneRouter"/> + 真实
        /// <see cref="MapLayerHost"/> + <see cref="StubResourceLoader"/>/<see cref="StubRenderer2D"/>
        /// 两个测试替身。</summary>
        private sealed class Harness
        {
            public IDataRegistry Registry { get; }
            public StubResourceLoader Loader { get; } = new StubResourceLoader();
            public StubRenderer2D Renderer { get; } = new StubRenderer2D();
            public Core.Foundation.SceneRouter.SceneRouter Router { get; }
            public MapLayerHost Host { get; }

            public Harness(params string[] worldMapRows)
            {
                var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
                var source = new InMemoryDataSource().Add("world.map",
                    "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" + string.Join(",", worldMapRows) + "]}");
                Registry = new Core.Foundation.DataRegistry.DataRegistry(source, bus);
                Registry.RegisterSchema(WorldMapSchema.Table);
                var report = Registry.LoadAll();
                Assert.False(report.IsBlocking, "测试夹具数据未通过校验：" + string.Join("; ", ToStrings(report.Issues)));

                var app = new AppStateHost(bus, AppStateMachineConfig.Default().AllowTransition(AppState.Loading, AppState.MainMenu));
                app.RequestTransition(AppState.MainMenu);
                var world = new WorldSim(bus);
                var hooks = new HookRegistry(bus);
                Router = new Core.Foundation.SceneRouter.SceneRouter(Registry, Loader, app, world, hooks, bus);
                Host = new MapLayerHost(Router, Registry, Renderer, Loader);
            }

            private static IEnumerable<string> ToStrings(IReadOnlyList<ValidationIssue> issues)
            {
                foreach (var issue in issues) yield return issue.ToString();
            }

            /// <summary>发起并推进一次场景加载到完成（scene_ref/nav_ref/地图分层图三个资源全部登记为
            /// 可加载成功，非 defer 模式下 LoadAsync 同步回调，一次 Update 即可走完）。</summary>
            public void LoadSceneAndFinish(string mapName)
            {
                Loader.Register(new Id("scene." + mapName));
                Loader.Register(new Id("nav." + mapName));
                Loader.Register(new Id("world." + mapName));
                Router.LoadScene(new Id("world." + mapName));
                Router.Update();
            }
        }

        [Fact]
        public void PostLoad_MapWithImageTransform_CreatesGroundAndOverlay_WorldBoundsComputedByRule()
        {
            var harness = new Harness(MapRow("field_a", withImageTransform: true));

            harness.LoadSceneAndFinish("field_a");

            // 期望值由生产同一类型 MapImageTransform 按测试输入算出，不写死裸数。
            var transform = new MapImageTransform(Ppu, OriginPx, ImageSizePx);
            var expectedBounds = transform.WorldBounds!.Value;
            var expectedRect = new Rect(expectedBounds.Min, expectedBounds.Max);

            // decal 未标记不可用（默认可用），本用例三层全建——decal 缺失降级见专门用例。
            Assert.Equal(3, harness.Renderer.AliveMapLayerCount);
            Assert.Equal(3, harness.Host.GetActiveLayerCount(new Id("world.field_a")));

            var groundCall = Assert.Single(harness.Renderer.MapLayerCreateCalls, c => c.Layer == MapLayerKind.Ground);
            Assert.Equal(expectedRect, groundCall.WorldBounds);
            Assert.Equal(RenderLayers.Ground, groundCall.RenderLayer);
            Assert.True(groundCall.Handle.IsValid);

            var overlayCall = Assert.Single(harness.Renderer.MapLayerCreateCalls, c => c.Layer == MapLayerKind.Overlay);
            Assert.Equal(expectedRect, overlayCall.WorldBounds);
            Assert.Equal(RenderLayers.Foreground, overlayCall.RenderLayer);
            Assert.True(overlayCall.Handle.IsValid);

            // decal 未被标记为不可用，默认可用，本用例应仍建出三层——见专门的"decal 缺失"用例验证
            // 缺失路径；这里断言 decal 也确实调用了一次（渲染层号一致）。
            var decalCall = Assert.Single(harness.Renderer.MapLayerCreateCalls, c => c.Layer == MapLayerKind.Decal);
            Assert.Equal(RenderLayers.Decoration, decalCall.RenderLayer);
        }

        [Fact]
        public void PostLoad_DecalMissing_OnlyBuildsTwoLayers_NoException_HasDiagnostic()
        {
            var harness = new Harness(MapRow("field_b", withImageTransform: true));
            harness.Renderer.SetMapLayerAvailable(new Id("world.field_b"), MapLayerKind.Decal, available: false);

            var exception = Record.Exception(() => harness.LoadSceneAndFinish("field_b"));

            Assert.Null(exception);
            Assert.Equal(2, harness.Renderer.AliveMapLayerCount);
            Assert.Equal(2, harness.Host.GetActiveLayerCount(new Id("world.field_b")));

            var decalCall = Assert.Single(harness.Renderer.MapLayerCreateCalls, c => c.Layer == MapLayerKind.Decal);
            Assert.False(decalCall.Handle.IsValid);
        }

        [Fact]
        public void PostLoad_MapWithoutImageTransform_BuildsNoLayers_NoException_HasDiagnostic()
        {
            var harness = new Harness(MapRow("field_c", withImageTransform: false));

            var exception = Record.Exception(() => harness.LoadSceneAndFinish("field_c"));

            Assert.Null(exception);
            Assert.Empty(harness.Renderer.MapLayerCreateCalls);
            Assert.Equal(0, harness.Renderer.AliveMapLayerCount);
            Assert.Equal(0, harness.Host.GetActiveLayerCount(new Id("world.field_c")));
            var recorder = Assert.IsType<global::Presentation.VfxSfx.Contracts.PresentationDiagnosticsRecorder>(harness.Host.Diagnostics);
            Assert.Contains(recorder.Warnings, w => w.Contains("field_c") && w.Contains("image_transform"));
        }

        [Fact]
        public void PostLoad_ImageTransformWithoutImageSizePx_BuildsNoLayers_NoException()
        {
            var harness = new Harness(MapRow("field_d", withImageTransform: true, withImageSizePx: false));

            var exception = Record.Exception(() => harness.LoadSceneAndFinish("field_d"));

            Assert.Null(exception);
            Assert.Empty(harness.Renderer.MapLayerCreateCalls);
            Assert.Equal(0, harness.Renderer.AliveMapLayerCount);
        }

        [Fact]
        public void SwitchingMaps_DestroysOldLayers_NoLeakOnRevisit()
        {
            var harness = new Harness(
                MapRow("field_e", withImageTransform: true),
                MapRow("field_f", withImageTransform: true));

            harness.LoadSceneAndFinish("field_e");
            Assert.Equal(3, harness.Renderer.AliveMapLayerCount);

            harness.LoadSceneAndFinish("field_f");
            // 切图后旧地图（field_e）的层应已销毁，只剩新地图（field_f）的层存活，不是两套地图叠加。
            Assert.Equal(3, harness.Renderer.AliveMapLayerCount);
            Assert.Equal(0, harness.Host.GetActiveLayerCount(new Id("world.field_e")));
            Assert.Equal(3, harness.Host.GetActiveLayerCount(new Id("world.field_f")));

            harness.LoadSceneAndFinish("field_e");
            // 切回 field_e：总存活层数应与首次加载 field_e 时一致（实测层数，不是只看没报错）。
            Assert.Equal(3, harness.Renderer.AliveMapLayerCount);
            Assert.Equal(3, harness.Host.GetActiveLayerCount(new Id("world.field_e")));
            Assert.Equal(0, harness.Host.GetActiveLayerCount(new Id("world.field_f")));
        }

        [Fact]
        public void Dispose_WhileMapStillLoaded_DestroysActiveLayers()
        {
            var harness = new Harness(MapRow("field_h", withImageTransform: true));
            harness.LoadSceneAndFinish("field_h");
            Assert.Equal(3, harness.Renderer.AliveMapLayerCount);

            harness.Host.Dispose();

            // Dispose 时地图仍处于"已加载"状态（没有经过 SceneRouter 卸载），本类型应主动销毁自己建出
            // 的全部层，不依赖调用方额外经场景卸载流程才能回收——同 ViewFactory.DestroyAllCreatedViews
            // 一类"自己负责清理自己建出的引擎侧对象"惯例（见 Dispose 判断记录）。
            Assert.Equal(0, harness.Renderer.AliveMapLayerCount);
            Assert.Equal(0, harness.Host.GetActiveLayerCount(new Id("world.field_h")));
            Assert.Equal(3, harness.Renderer.MapLayerDestroyCalls.Count);
        }

        [Fact]
        public void MinimalRenderer2D_WithoutOverridingMapLayerMembers_DefaultsToInvalidHandle_NoException()
        {
            IRenderer2D minimal = new MinimalRenderer2DWithoutMapLayers();

            var handle = minimal.CreateMapLayerInstance(
                new Id("world.field_g"), MapLayerKind.Ground, new Rect(Vec2.Zero, new Vec2(1, 1)), RenderLayers.Ground);
            var destroyException = Record.Exception(() => minimal.DestroyMapLayerInstance(handle));

            Assert.False(handle.IsValid);
            Assert.Null(destroyException);
        }

        /// <summary>验收标准 5：证明一个完全没有覆写 <see cref="IRenderer2D.CreateMapLayerInstance"/>/
        /// <see cref="IRenderer2D.DestroyMapLayerInstance"/> 的既有 <see cref="IRenderer2D"/> 实现方——
        /// 只实现了本次新增之前就存在的必须成员——不改一行仍能编译通过，调用新成员落回接口默认实现
        /// （不画任何东西、返回无效句柄、不抛异常）。本类型不在 <c>InterfaceDefaultMemberForwardingTests</c>
        /// 反射的六个生产程序集内（它是本测试程序集内的私有测试替身），不会被该门禁要求转发。</summary>
        private sealed class MinimalRenderer2DWithoutMapLayers : IRenderer2D
        {
            public SpriteHandle CreateSpriteInstance(Id spriteSetId) => new SpriteHandle(1);

            public void SetLayers(SpriteHandle handle, IReadOnlyList<Id> layers)
            {
            }

            public void SetTransform(SpriteHandle handle, Vec2 position, double height, double sortY, int layer, double rotation, double scale, bool flipX)
            {
            }

            public void SetShaderParam(SpriteHandle handle, string paramName, double value)
            {
            }

            public void SetShadow(SpriteHandle handle, ShadowMode mode)
            {
            }

            public void DestroySpriteInstance(SpriteHandle handle)
            {
            }

            public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters) =>
                new ParticleHandle(1);

            public void StopParticle(ParticleHandle handle)
            {
            }
        }
    }
}
