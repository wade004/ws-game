using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;

namespace Tests.Foundation.SceneRouter
{
    /// <summary>测试共用夹具（与 sim_loop 的 <c>SimLoopTestSupport</c>、app_lifecycle 的
    /// <c>AppLifecycleTestSupport</c>、display_info 的 <c>DisplayInfoTestSupport</c> 同一惯例）。</summary>
    internal static class SceneRouterTestSupport
    {
        public static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
                new EventDefinition(AppEventKeys.StateChanged, "app", new[] { "oldState", "newState" }),
                new EventDefinition(SimEventKeys.EntityCreated, "entity", new[] { "entityId", "kind", "displayId" }),
                new EventDefinition(SimEventKeys.EntityDestroyed, "entity", new[] { "entityId" }),
                new EventDefinition(SceneRouterEventKeys.LoadStarted, "scene", new[] { "sceneId" }),
                new EventDefinition(SceneRouterEventKeys.LoadFinished, "scene", new[] { "sceneId" }),
                new EventDefinition(SceneRouterEventKeys.Unloaded, "scene", new[] { "sceneId" }),
            });

            // StrictCatalog=false：容忍测试里可能出现的、未逐一登记的其它事件（如 hook.invoked
            // 在 HookRegistryOptions.EmitInvokedEvent=true 时），与 sim_loop 的
            // SimLoopTestSupport 同一惯例。
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }

        /// <summary>构造一个默认配置额外放行 <c>Loading→MainMenu</c> 的 <see cref="AppStateHost"/>
        /// ——见本模块 README 判断记录 3："加载失败路径尝试 RequestTransition(MainMenu)，但
        /// AppStateMachineConfig.Default() 默认不含这条转移，调用方需要自行追加"。</summary>
        public static AppStateHost CreateAppStateHostAllowingLoadingToMainMenu(IEventBus bus)
        {
            var config = AppStateMachineConfig.Default().AllowTransition(AppState.Loading, AppState.MainMenu);
            return new AppStateHost(bus, config);
        }

        /// <summary>登记 <see cref="WorldMapSchema.Table"/> 并加载 <paramref name="rowsJson"/>
        /// （<c>world.map</c> 的 <c>rows</c> 数组文本），断言加载不阻断，返回 registry。</summary>
        public static IDataRegistry BuildWorldMapRegistry(string rowsJson, IEventBus? bus = null)
        {
            var source = new InMemoryDataSource().Add("world.map", Envelope(rowsJson));
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, bus ?? CreateBus());
            registry.RegisterSchema(WorldMapSchema.Table);

            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "测试夹具数据未通过校验：" + string.Join("; ", ToStrings(report.Issues)));
            }

            return registry;
        }

        private static IEnumerable<string> ToStrings(IReadOnlyList<ValidationIssue> issues)
        {
            foreach (var issue in issues) yield return issue.ToString();
        }

        private static string Envelope(string rowsJson) =>
            "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // ------------------------------------------------------------------
        // 样例 world.map 记录（结构参照 05 第 4.1 节示例）
        // ------------------------------------------------------------------

        public const string TownSquareRow = @"
        {
          ""id"": ""world.town_square"",
          ""scene_ref"": ""scene.town_square"",
          ""nav_ref"": ""nav.town_square"",
          ""spawn_points"": [
            {""id"": ""spawn.town_square.default"", ""position"": {""x"": 120, ""y"": 80}, ""facing"": 0}
          ],
          ""regions"": [""region.town_square.market""],
          ""music_ref"": ""music.town_theme""
        }";

        public const string ForestPathRow = @"
        {
          ""id"": ""world.forest_path"",
          ""scene_ref"": ""scene.forest_path"",
          ""nav_ref"": ""nav.forest_path"",
          ""spawn_points"": [
            {""id"": ""spawn.forest_path.default"", ""position"": {""x"": 10, ""y"": 20}, ""facing"": 0}
          ]
        }";

        /// <summary>供 <see cref="ISceneRouter.LoadScene"/> 场景切换测试使用的完整测试夹具：
        /// 一次性搭好 registry、桩资源加载器、应用状态机、世界模拟、钩子注册表、事件总线与
        /// <see cref="Core.Foundation.SceneRouter.SceneRouter"/> 本体。</summary>
        public sealed class Harness
        {
            public IEventBus Bus { get; }
            public IDataRegistry Registry { get; }
            public StubResourceLoader Loader { get; }
            public AppStateHost App { get; }
            public WorldSim World { get; }
            public Core.Foundation.HookRegistry.HookRegistry Hooks { get; }
            public Core.Foundation.SceneRouter.SceneRouter Router { get; }

            public Harness(string worldMapRowsJson, bool deferCallbacks = false)
            {
                Bus = CreateBus();
                Registry = BuildWorldMapRegistry(worldMapRowsJson, Bus);
                Loader = new StubResourceLoader { DeferCallbacks = deferCallbacks };
                App = CreateAppStateHostAllowingLoadingToMainMenu(Bus);
                World = new WorldSim(Bus);
                Hooks = new Core.Foundation.HookRegistry.HookRegistry(Bus);
                Router = new Core.Foundation.SceneRouter.SceneRouter(Registry, Loader, App, World, Hooks, Bus);
            }

            /// <summary>把场景资源引用登记为"可加载成功"（非 defer 模式下 LoadAsync 会立即
            /// 回调成功；defer 模式下需要额外调用 <see cref="StubResourceLoader.CompletePending"/>）。</summary>
            public void RegisterResourcesLoadable(params string[] resourceIds)
            {
                foreach (var id in resourceIds)
                {
                    Loader.Register(new Core.Foundation.Common.Id(id));
                }
            }
        }
    }
}
