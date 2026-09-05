using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SceneRouter
{
    public class SceneRouterLoadFlowTests
    {
        [Fact]
        public void LoadScene_FromMainMenu_TransitionsToLoading_PublishesLoadStarted()
        {
            var harness = new SceneRouterTestSupport.Harness("[" + SceneRouterTestSupport.TownSquareRow + "]");
            harness.App.RequestTransition(AppState.MainMenu); // Boot -> MainMenu
            harness.RegisterResourcesLoadable("scene.town_square", "nav.town_square");

            Id? capturedSceneId = null;
            harness.Bus.Subscribe<SceneLoadStartedEvent>(SceneRouterEventKeys.LoadStarted, e => capturedSceneId = e.SceneId);

            harness.Router.LoadScene(new Id("world.town_square"));

            Assert.Equal(SceneRouterState.Loading, harness.Router.State);
            Assert.Equal(AppState.Loading, harness.App.GetState());
            Assert.Equal(new Id("world.town_square"), capturedSceneId);
        }

        [Fact]
        public void Update_DeferredMode_ResourcesNotReadyYet_StaysLoadingAndDoesNotTransition()
        {
            var harness = new SceneRouterTestSupport.Harness("[" + SceneRouterTestSupport.TownSquareRow + "]", deferCallbacks: true);
            harness.App.RequestTransition(AppState.MainMenu);

            harness.Router.LoadScene(new Id("world.town_square"));
            harness.Router.Update();

            Assert.Equal(SceneRouterState.Loading, harness.Router.State);
            Assert.Equal(AppState.Loading, harness.App.GetState());
            Assert.Null(harness.Router.GetCurrentScene());
        }

        [Fact]
        public void Update_AfterResourcesComplete_TransitionsToInWorld_TriggersPostLoadHook_PublishesLoadFinished()
        {
            var harness = new SceneRouterTestSupport.Harness("[" + SceneRouterTestSupport.TownSquareRow + "]", deferCallbacks: true);
            harness.App.RequestTransition(AppState.MainMenu);

            Id? postLoadSceneId = null;
            harness.Router.RegisterPostLoadHook(sceneId => postLoadSceneId = sceneId);

            Id? finishedSceneId = null;
            harness.Bus.Subscribe<SceneLoadFinishedEvent>(SceneRouterEventKeys.LoadFinished, e => finishedSceneId = e.SceneId);

            harness.Router.LoadScene(new Id("world.town_square"));
            harness.Loader.CompletePending(new Id("scene.town_square"));
            harness.Loader.CompletePending(new Id("nav.town_square"));

            harness.Router.Update();

            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(AppState.InWorld, harness.App.GetState());
            Assert.Equal(new Id("world.town_square"), postLoadSceneId);
            Assert.Equal(new Id("world.town_square"), finishedSceneId);
            Assert.Equal(new Id("world.town_square"), harness.Router.GetCurrentScene());
        }

        [Fact]
        public void SecondLoadScene_SwitchingScenes_PreUnloadBeforeClearAllBeforeEntityDestroyedBeforeSceneUnloaded()
        {
            var rows = "[" + SceneRouterTestSupport.TownSquareRow + "," + SceneRouterTestSupport.ForestPathRow + "]";
            var harness = new SceneRouterTestSupport.Harness(rows, deferCallbacks: true);
            harness.App.RequestTransition(AppState.MainMenu);

            // 先加载 town_square 进入 InWorld。
            harness.Router.LoadScene(new Id("world.town_square"));
            harness.Loader.CompletePending(new Id("scene.town_square"));
            harness.Loader.CompletePending(new Id("nav.town_square"));
            harness.Router.Update();
            Assert.Equal(AppState.InWorld, harness.App.GetState());

            // 往 town_square 场景加一个实体，验证 ClearAll 会把它清空。
            var entity = new Tests.Foundation.SimLoop.TestEntity(new Id("unit.hero"), new Id("world.town_square"));
            harness.World.AddEntity(entity);
            harness.Bus.DispatchPending();
            Assert.Equal(1, harness.World.EntityCount);

            var order = new List<string>();
            var entityCountAtPreUnload = -1;
            harness.Router.RegisterPreUnloadHook(sceneId =>
            {
                order.Add("pre_unload");
                entityCountAtPreUnload = harness.World.EntityCount;
            });

            var destroyedIds = new List<Id>();
            harness.Bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e =>
            {
                order.Add("entity_destroyed");
                destroyedIds.Add(e.EntityId);
            });

            harness.Bus.Subscribe<SceneUnloadedEvent>(SceneRouterEventKeys.Unloaded, _ => order.Add("scene_unloaded"));

            // 切到 forest_path。
            harness.Router.LoadScene(new Id("world.forest_path"));
            harness.Loader.CompletePending(new Id("scene.forest_path"));
            harness.Loader.CompletePending(new Id("nav.forest_path"));
            harness.Router.Update();

            Assert.Equal(1, entityCountAtPreUnload); // pre_unload 触发时旧场景实体尚未被清空
            Assert.Equal(0, harness.World.EntityCount); // ClearAll 之后归零
            Assert.Equal(new[] { "pre_unload", "entity_destroyed", "scene_unloaded" }, order);
            Assert.Single(destroyedIds);
            Assert.Equal(entity.EntityId, destroyedIds[0]);
            Assert.Equal(new Id("world.forest_path"), harness.Router.GetCurrentScene());
        }

        [Fact]
        public void SecondLoadScene_ClearsSpatialIndexAndNavigationBlocking_WhenInjected()
        {
            // ADR-0016 决策 7 场景卸载级联清理：SceneRouter 注入 ISpatialQuery/INavigation2D 时，
            // 卸载旧场景应额外整图兜底 Clear（配合 ClearAll 派发的 entity.destroyed 逐实体
            // Unregister，见 EntitySpatialSyncHostTests 覆盖逐实体路径；本测试覆盖"就算没有任何
            // 实体经事件同步注册，遗留的登记也会被整图清空"这一兜底路径）。
            var rows = "[" + SceneRouterTestSupport.TownSquareRow + "," + SceneRouterTestSupport.ForestPathRow + "]";
            var spatial = new StubSpatialQuery();
            var navigation = new StubNavigation2D();
            var harness = new SceneRouterTestSupport.Harness(rows, deferCallbacks: true, spatial: spatial, navigation: navigation);
            harness.App.RequestTransition(AppState.MainMenu);

            harness.Router.LoadScene(new Id("world.town_square"));
            harness.Loader.CompletePending(new Id("scene.town_square"));
            harness.Loader.CompletePending(new Id("nav.town_square"));
            harness.Router.Update();
            Assert.Equal(AppState.InWorld, harness.App.GetState());

            // 模拟遗留登记（不经由 entity.destroyed 同步的路径），验证整图兜底 Clear 生效。
            var townSquareMapId = new Id("world.town_square");
            spatial.Register(new Id("gobj.leftover"), new Vec2(1, 1), 0.1, new[] { "gobj" });
            navigation.SetBlocking(townSquareMapId, new[] { new Core.Foundation.Common.Rect(new Vec2(0, 0), new Vec2(5, 5)) });
            Assert.False(navigation.IsWalkable(townSquareMapId, new Vec2(1, 1)));

            harness.Router.LoadScene(new Id("world.forest_path"));
            harness.Loader.CompletePending(new Id("scene.forest_path"));
            harness.Loader.CompletePending(new Id("nav.forest_path"));
            harness.Router.Update();

            Assert.Null(spatial.Nearest(new Vec2(1, 1), Core.Foundation.EngineAdapter.QueryFilter.None));
            Assert.True(navigation.IsWalkable(townSquareMapId, new Vec2(1, 1)));
        }

        [Fact]
        public void LoadProgress_GoesFromZeroToOneAcrossDeferredLoad()
        {
            var harness = new SceneRouterTestSupport.Harness("[" + SceneRouterTestSupport.TownSquareRow + "]", deferCallbacks: true);
            harness.App.RequestTransition(AppState.MainMenu);

            Assert.Equal(0.0, harness.Router.LoadProgress);

            harness.Router.LoadScene(new Id("world.town_square"));
            Assert.Equal(0.0, harness.Router.LoadProgress);

            harness.Loader.CompletePending(new Id("scene.town_square"));
            Assert.Equal(0.5, harness.Router.LoadProgress); // 两个资源完成一个

            harness.Loader.CompletePending(new Id("nav.town_square"));
            Assert.Equal(1.0, harness.Router.LoadProgress);

            harness.Router.Update();
            Assert.Equal(1.0, harness.Router.LoadProgress); // Idle 后仍为 1.0（曾经加载成功过）
        }
    }
}
