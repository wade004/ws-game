#nullable enable
// AssemblyFixture：PlayMode 用例共用的“直接构造 GameplayAssembly/PresentationAssembly”夹具（不经完整
// FrameworkResidentHost/ShellRoot），装配方式与 AuditBlockersPlayModeTests.BuildFixture 同款（同判断记录：
// 需要对装配本身做细粒度断言、且不被 Shell 场景的常驻单例干扰时用它）。测试覆盖第四批 T-M47/T-L15 新增，
// 供 DiagnosticsHubCompositionPlayModeTests 与 UiRootAndShopPanelPlayModeTests 共用，不替换既有用例里
// 各自内联的同款夹具（不动已绿用例）。
using System;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    internal sealed class AssemblyFixture
    {
        public GameplayAssembly Gameplay = null!;
        public PresentationAssembly Presentation = null!;
        public EventBus Bus = null!;
        public IDataRegistry Registry = null!;
        public SaveSystem SaveSystem = null!;
        public Core.Foundation.SceneRouter.SceneRouter SceneRouter = null!;
        public UnityViewFactory ViewFactory = null!;
        public IWorldSim World = null!;
        public Id PlayerId;

        /// <summary>总会登记一名玩家单位（实体、规则单位、经济账户）：PresentationAssembly 的 HUD/背包等视图
        /// 模型构造期就要按 playerUnitProvider 查询玩家，未登记会抛“单位未通过 RegisterUnit 注册”。</summary>
        public static AssemblyFixture Build(ulong seed)
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

            var rng = new RngHost(seed);
            var world = new WorldSim(bus);
            var playerId = new Id($"unit.assembly_fixture_player_{UnityEngine.Random.Range(0, int.MaxValue)}");
            var factionId = new Id("fac.player");
            var saveSystem = new SaveSystem(host.FileSystem, new SaveSystemOptions(new Id("game.assembly_fixture_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, host.SpatialQuery, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: factionId,
                navigation: host.Navigation2D);

            var classId = new Id("arch.class.sample_a");
            var player = new PlayerUnit(playerId, new Id("world.sample_field"), factionId, classId)
            {
                Position = Vec2.Zero,
                TemplateId = new Id("creature.sample_hero"),
            };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(playerId, classId, raceId: null, level: 3);
            gameplay.Economy.RegisterUnit(playerId);

            var displayInfo = new DisplayInfoRegistry(registry, bus);
            var viewFactory = new UnityViewFactory(host.Renderer2D, new RenderConventionHost(), displayInfo, host.ResourceLoader, bus: bus, dataRegistry: registry);
            var presentationRng = new RngHost(seed ^ 0x9E3779B97F4A7C15UL);
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus,
                spatial: host.SpatialQuery, navigation: host.Navigation2D);
            gameplay.AttachSceneRouter(sceneRouter);

            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, presentationRng,
                viewFactory, host.Renderer2D, host.Camera, host.Audio, host.FileSystem, sceneRouter,
                resourceLoader: host.ResourceLoader);

            return new AssemblyFixture
            {
                Gameplay = gameplay, Presentation = presentation, Bus = bus, Registry = registry, SaveSystem = saveSystem,
                SceneRouter = sceneRouter, ViewFactory = viewFactory, World = world, PlayerId = playerId,
            };
        }

        public void Dispose()
        {
            World.ClearAll();
            Bus.DispatchPending();
            ViewFactory.DestroyAllCreatedViews();
            Presentation.Dispose();
        }
    }
}
