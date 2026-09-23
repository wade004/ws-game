#nullable enable
// MapLayerHostPlayModeTests：ADR-0080 的 Unity PlayMode 收口。
//
// 分工判断记录（本文件只验证什么、不重复验证什么）：MapLayerHost 本身的调度逻辑（哪一层该建、
// image_transform 缺失/image_size_px 缺失时的降级路径、切图不泄漏、默认接口成员不破坏既有实现）
// 已在 presentation/render/tests/MapLayerHostTests.cs（xUnit，假引擎替身，6/6 通过）逐条验证过，
// 不在本文件重复。本文件独有的验证面是"真实 UnityResourceLoader 读真实样例 PNG（assets/_sample/
// maps/sample_field/）+ 真实 UnityRenderer2D 建 GameObject + 真实 SceneRouter 场景加载流程 + 真实
// PresentationAssembly 生产装配入口"这整条链路本身跑得通、世界矩形数值正确——同
// ModelIntegrationTests.cs/AuditBlockersPlayModeTests.cs 一类"核心逻辑已在别处验证、这里只验证
// 引擎落地"分工，BuildFixture 装配方式直接照抄 AuditBlockersPlayModeTests.cs 已验证过的写法。
//
// 样例地图 world.sample_field（data/_sample/world/world.map.json）只有 ground.png/overlay.png/
// nav_hint.png，没有 decal.png——ADR-0080 决策 6 指定的"可选层缺失→优雅降级"内置测试用例，本文件
// 不补一张 decal.png 去凑齐（凑齐了反而验证不到这条降级路径）。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Render;
using Presentation.Shell;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class MapLayerHostPlayModeTests : PlayModeTestBase
    {
        private const string PlayerFactionId = "fac.player";
        private const string PlayerClassId = "arch.class.sample_a";
        private const string PlayerTemplateId = "creature.sample_hero";
        private const string MapAId = "world.sample_field";

        private sealed class Fixture
        {
            public GameplayAssembly Gameplay = null!;
            public PresentationAssembly Presentation = null!;
            public UnityViewFactory ViewFactory = null!;
            public IWorldSim World = null!;
            public IEventBus Bus = null!;
            public IDataRegistry Registry = null!;
            public Core.Foundation.SceneRouter.SceneRouter SceneRouter = null!;
            public ShellHost Shell = null!;
            public UnityEngineHost Host = null!;
        }

        private Fixture? _fixture;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_fixture != null)
            {
                _fixture.Shell.Dispose();
                _fixture.World.ClearAll();
                _fixture.Bus.DispatchPending();
                _fixture.ViewFactory.DestroyAllCreatedViews();
                _fixture.Presentation.Dispose();
                _fixture = null;
            }

            yield return null;
        }

        /// <summary>照抄 AuditBlockersPlayModeTests.BuildFixture 的装配写法（已验证可用）：真实
        /// DataRegistry 加载 data/_framework + data/_sample，真实 GameplayAssembly/SceneRouter/
        /// PresentationAssembly（内部会自动构造 MapLayers = new MapLayerHost(...)，见
        /// presentation/assembly/PresentationAssembly.cs），真实 ShellHost 驱动场景加载状态机。</summary>
        private Fixture BuildFixture()
        {
            var host = UnityEngineHost.Ensure();

            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: repoRoot);
            var frameworkSource = new FileSystemDataSource(contentFs, "data/_framework");
            var sampleSource = new FileSystemDataSource(contentFs, "data/_sample");

            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(sampleSource, bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, sampleSource });
            Assert.IsFalse(report.IsBlocking, "测试数据集应当能无阻断加载：" + string.Join("; ", report.Issues));

            var rng = new RngHost(20260923UL);
            var world = new WorldSim(bus);
            var playerId = new Id($"unit.map_layer_host_player_{UnityEngine.Random.Range(0, int.MaxValue)}");
            var factionId = new Id(PlayerFactionId);
            var classId = new Id(PlayerClassId);
            var mapAId = new Id(MapAId);

            var slotId = new Id("slot.map_layer_host_autosave");
            var saveSystem = new SaveSystem(host.FileSystem, new SaveSystemOptions(new Id("game.map_layer_host_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, host.SpatialQuery, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: factionId,
                navigation: host.Navigation2D);

            var player = new Core.Carriers.Unit.PlayerUnit(playerId, mapAId, factionId, classId)
            {
                Position = Vec2.Zero,
                TemplateId = new Id(PlayerTemplateId),
            };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(playerId, classId, raceId: null, level: 3);
            gameplay.Economy.RegisterUnit(playerId);

            var viewFactoryDisplayInfo = new DisplayInfoRegistry(registry, bus);
            var viewFactory = new UnityViewFactory(host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, host.ResourceLoader, bus: bus, dataRegistry: registry);
            var presentationRng = new RngHost(20260923UL ^ 0x9E3779B97F4A7C15UL);
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus,
                spatial: host.SpatialQuery, navigation: host.Navigation2D);
            gameplay.AttachSceneRouter(sceneRouter);

            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, presentationRng,
                viewFactory, host.Renderer2D, host.Camera, host.Audio, host.FileSystem, sceneRouter,
                resourceLoader: host.ResourceLoader);

            gameplay.RegisterPersistables(presentation.SaveSystem, player);

            sceneRouter.RegisterPostLoadHook(mapId =>
            {
                if (world.GetEntity(playerId) == null)
                {
                    world.AddEntity(player);
                    bus.DispatchPending();
                }
                gameplay.EnterMap(mapId, playerId);
            });

            var settingsStore = new SettingsStore(host.FileSystem);
            var inputMap = new InputMapHost(bus);
            var shell = new ShellHost(
                gameplay.AppState, sceneRouter, presentation.SaveSystem, settingsStore, gameplay.Difficulty, inputMap, bus,
                newGameStarter: (slot, diff, arch) => mapAId,
                timestampProvider: () => "2026-09-23T00:00:00Z");

            return new Fixture
            {
                Gameplay = gameplay, Presentation = presentation, ViewFactory = viewFactory, World = world, Bus = bus,
                Registry = registry, SceneRouter = sceneRouter, Shell = shell, Host = host,
            };
        }

        private static IEnumerator PumpUntilInWorld(Fixture fx, int maxFrames = 600)
        {
            var guard = maxFrames;
            while (fx.Shell.Page != ShellPage.InWorld && guard-- > 0)
            {
                fx.Shell.Update();
                fx.Bus.DispatchPending();
                yield return null;
            }
            Assert.AreEqual(ShellPage.InWorld, fx.Shell.Page, "场景加载应当在有限帧数内完成并进入 InWorld");
        }

        /// <summary>ResourceKind.MapLayers 的加载经 Task.Run 后台线程读字节 + UnityResourceLoader.Tick
        /// 主线程完成解码回调（同 UnityResourceLoaderTests.cs 里其余异步用例的轮询写法），到达
        /// InWorld 后还需要再抽若干帧才会真正建出 GameObject，不能假定进 InWorld 那一帧就已完成。</summary>
        private static IEnumerator PumpUntilLayerCountStable(Fixture fx, Id mapId, int expectedCount, float timeoutSeconds = 5f)
        {
            var timeout = timeoutSeconds;
            while (fx.Presentation.MapLayers.GetActiveLayerCount(mapId) < expectedCount && timeout > 0f)
            {
                yield return null;
                timeout -= 0.02f;
            }
            // 再多等一帧，确认不会继续增长（用于反证"缺失层没有被误建"）。
            yield return null;
        }

        /// <summary>验收标准 1：通过生产装配入口（PresentationAssembly，本文件 BuildFixture 与
        /// GameFoundationBootstrap 同款装配方式）加载样例地图后，ground/overlay 两层真的建出，世界
        /// 矩形按 image_transform 实际值经 MapImageTransform 计算（不硬编码字面量）；渲染层号（
        /// RenderLayers.Ground/RenderLayers.Foreground）的正确性已在 MapLayerHostTests.cs 用假引擎
        /// 替身验证过，这里额外验证的是"真实引擎适配层收到的世界矩形数值确实正确"。
        /// 验收标准 2：可选层 decal 缺失（sample_field 样例数据没有 decal.png）时只建两层，不建第三层，
        /// 不抛异常。</summary>
        [UnityTest]
        public IEnumerator ProductionAssembly_LoadsSampleField_BuildsGroundAndOverlay_WorldBoundsMatchImageTransform_DecalSkipped()
        {
            var fx = BuildFixture();
            _fixture = fx;
            var mapAId = new Id(MapAId);

            // 判断记录（用差集而不是绝对句柄值/绝对计数断言）：UnityEngineHost 是 DontDestroyOnLoad
            // 单例，本测试进程内其它 PlayMode 用例（AuditBlockersPlayModeTests 等）同样会经生产装配根
            // 加载 world.sample_field，与本用例共用同一个 UnityRenderer2D 实例——句柄计数器单调递增、
            // 全局存活计数会带上同进程内其它夹具的状态，不能假定"这是第一次加载、句柄从 1 开始"。改为
            // 建层前后各拍一次"当前存活句柄集合"快照，取差集，只看本次调用真正新建出的句柄，不受运行
            // 顺序或同进程内其它夹具残留状态影响。
            var handlesBefore = new HashSet<int>(fx.Host.Renderer2D.AliveMapLayerHandleValuesForTests);

            fx.Shell.Start();
            yield return null;
            fx.SceneRouter.LoadScene(mapAId);
            yield return PumpUntilInWorld(fx);

            yield return PumpUntilLayerCountStable(fx, mapAId, expectedCount: 2);

            Assert.AreEqual(2, fx.Presentation.MapLayers.GetActiveLayerCount(mapAId),
                "ground+overlay 应当建出；decal 缺失（sample_field 样例数据没有 decal.png）不应计入");

            var handlesAfter = new HashSet<int>(fx.Host.Renderer2D.AliveMapLayerHandleValuesForTests);
            var newHandles = handlesAfter.Except(handlesBefore).ToList();
            Assert.AreEqual(2, newHandles.Count,
                "本次加载应当只新建两个地图分层图实例（ground+overlay），decal 缺失不应计入第三个");

            // 世界矩形按生产数据的 image_transform 实际值经 MapImageTransform（与 MapLayerHost 生产
            // 代码同一权威算法）计算，不手写字面量——数据本身若之后调整，本测试的期望值跟着联动，不会
            // 因为改了样例数据而误报。
            var record = fx.Registry.Get(WorldMapSchema.Table.Name, mapAId);
            Assert.IsNotNull(record, "world.sample_field 行应当存在");
            var transform = MapImageTransform.FromRecord(record!);
            Assert.IsNotNull(transform, "world.sample_field 应当声明 image_transform（样例数据既有约定）");
            var bounds = transform!.WorldBounds;
            Assert.IsNotNull(bounds, "world.sample_field 的 image_transform 应当声明 image_size_px");
            var expectedBounds = new Core.Foundation.Common.Rect(bounds!.Value.Min, bounds.Value.Max);

            // ground/overlay 共享同一个世界矩形（ADR-0080 决策 2），新建的两个句柄（不论对应哪一层）
            // 世界矩形都应当等于同一个计算结果。
            foreach (var handleValue in newHandles)
            {
                Assert.IsTrue(fx.Host.Renderer2D.TryGetMapLayerWorldBoundsForTests(new MapLayerHandle(handleValue), out var actualBounds),
                    $"新建句柄 {handleValue} 应当能查到世界矩形");
                AssertRectEqual(expectedBounds, actualBounds, $"句柄 {handleValue} 的世界矩形应当与 MapImageTransform.WorldBounds 计算结果一致");
            }
        }

        /// <summary>验收标准 4（切图不泄漏）的 Unity 侧补充验证：核心调度逻辑已在 MapLayerHostTests.cs
        /// 用两张不同的假地图验证过；样例数据目前只有一张真实地图（world.sample_field），本用例改用
        /// "同一张地图重新 LoadScene 一次"复现同一条代码路径——SceneRouter.LoadScene 在已有场景加载
        /// 完成的前提下再次调用，会先经 pre_unload 钩子卸载旧场景（触发 MapLayerHost.OnPreUnload 销毁
        /// 旧层），再 post_load 建新场景（触发 OnPostLoad 重新建层），与"切到另一张地图"触发的是完全
        /// 相同的钩子调用序列，只是地图 id 恰好相同——验证的是"重复经过一次完整的建/销毁循环后，层数
        /// 不会翻倍"，不是"地图 id 必须不同"这件事本身（那件事已经在 MapLayerHostTests.cs 验证过）。</summary>
        [UnityTest]
        public IEnumerator ReloadingSameMap_DoesNotLeakLayers_TotalCountMatchesFirstLoad()
        {
            var fx = BuildFixture();
            _fixture = fx;
            var mapAId = new Id(MapAId);

            // 同上一用例的判断记录：UnityRenderer2D 是跨测试夹具共享的 DontDestroyOnLoad 单例，用
            // "本用例加载前后的差集/相对计数"而不是绝对值断言，不受同进程内其它夹具残留状态影响。
            var globalCountBeforeFirstLoad = fx.Host.Renderer2D.AliveMapLayerCount;
            var handlesBeforeFirstLoad = new HashSet<int>(fx.Host.Renderer2D.AliveMapLayerHandleValuesForTests);

            fx.Shell.Start();
            yield return null;
            fx.SceneRouter.LoadScene(mapAId);
            yield return PumpUntilInWorld(fx);
            yield return PumpUntilLayerCountStable(fx, mapAId, expectedCount: 2);

            var firstLoadCount = fx.Presentation.MapLayers.GetActiveLayerCount(mapAId);
            Assert.AreEqual(2, firstLoadCount, "首次加载应当建出 ground+overlay 两层");
            Assert.AreEqual(globalCountBeforeFirstLoad + 2, fx.Host.Renderer2D.AliveMapLayerCount,
                "首次加载后，UnityRenderer2D 全局存活地图分层图实例数应当恰好比加载前多 2");

            var firstLoadHandles = new HashSet<int>(fx.Host.Renderer2D.AliveMapLayerHandleValuesForTests)
                .Except(handlesBeforeFirstLoad).ToList();
            Assert.AreEqual(2, firstLoadHandles.Count);

            fx.SceneRouter.LoadScene(mapAId);
            yield return PumpUntilInWorld(fx);
            yield return PumpUntilLayerCountStable(fx, mapAId, expectedCount: 2);

            Assert.AreEqual(firstLoadCount, fx.Presentation.MapLayers.GetActiveLayerCount(mapAId),
                "重新加载同一张地图后，该地图当前存活层数应当与首次加载一致，不应翻倍");
            Assert.AreEqual(globalCountBeforeFirstLoad + 2, fx.Host.Renderer2D.AliveMapLayerCount,
                "重新加载后，UnityRenderer2D 全局存活地图分层图实例数应当仍然只比最初多 2，不是 4——真正实测销毁了旧层，不是只未报错");

            // 更强的证据：首次加载建出的那两个具体句柄，在重新加载后应当已经查不到世界矩形（GameObject
            // 已被 UnityEngine.Object.Destroy 真正销毁），不是恰好被新调用复用了同样的计数值。
            foreach (var oldHandleValue in firstLoadHandles)
            {
                Assert.IsFalse(fx.Host.Renderer2D.TryGetMapLayerWorldBoundsForTests(new MapLayerHandle(oldHandleValue), out _),
                    $"首次加载建出的句柄 {oldHandleValue} 在切图后应当已被销毁，查不到世界矩形");
            }
        }

        private static void AssertRectEqual(Core.Foundation.Common.Rect expected, Core.Foundation.Common.Rect actual, string message)
        {
            const float tolerance = 0.001f;
            Assert.AreEqual((float)expected.Min.X, (float)actual.Min.X, tolerance, message + "（Min.X）");
            Assert.AreEqual((float)expected.Min.Y, (float)actual.Min.Y, tolerance, message + "（Min.Y）");
            Assert.AreEqual((float)expected.Max.X, (float)actual.Max.X, tolerance, message + "（Max.X）");
            Assert.AreEqual((float)expected.Max.Y, (float)actual.Max.Y, tolerance, message + "（Max.Y）");
        }
    }
}
