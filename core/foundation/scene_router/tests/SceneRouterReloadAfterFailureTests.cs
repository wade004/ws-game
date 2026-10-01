using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.SceneRouter;
using Xunit;

namespace Tests.Foundation.SceneRouter
{
    /// <summary>
    /// 场景加载失败之后重载（T-L8 scene_router 半，2026-10-01 测试覆盖第四批）：失败不留残余待决资源 /
    /// 进度 / 半卸载状态；失败那一代的迟到回调被代际校验丢弃，不污染重载；失败的切换不触发旧场景卸载，
    /// 之后重试可正常完成。
    /// </summary>
    public sealed class SceneRouterReloadAfterFailureTests
    {
        private static readonly Id Town = new Id("world.town_square");
        private static readonly Id Forest = new Id("world.forest_path");

        private static string TownRows => "[" + SceneRouterTestSupport.TownSquareRow + "]";

        [Fact]
        public void FailedLoad_ThenReloadSameScene_Succeeds_WithNoResidueFromFailedAttempt()
        {
            var harness = new SceneRouterTestSupport.Harness(TownRows);
            harness.App.RequestTransition(AppState.MainMenu);
            harness.RegisterResourcesLoadable("scene.town_square"); // nav.town_square 缺失 → 同步失败

            var started = 0;
            var finished = 0;
            var postLoad = 0;
            harness.Bus.Subscribe<SceneLoadStartedEvent>(SceneRouterEventKeys.LoadStarted, _ => started++);
            harness.Bus.Subscribe<SceneLoadFinishedEvent>(SceneRouterEventKeys.LoadFinished, _ => finished++);
            harness.Router.RegisterPostLoadHook(_ => postLoad++);

            harness.Router.LoadScene(Town);
            harness.Router.Update();

            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(AppState.MainMenu, harness.App.GetState());
            Assert.Null(harness.Router.GetCurrentScene());
            Assert.Equal(0, finished);
            Assert.Equal(0, postLoad);
            Assert.Equal(0.0, harness.Router.LoadProgress);
            var diagnostics = Assert.IsType<InMemorySceneDiagnostics>(harness.Router.Diagnostics);
            Assert.Single(diagnostics.Errors);

            harness.RegisterResourcesLoadable("nav.town_square");
            harness.Router.LoadScene(Town);
            harness.Router.Update();

            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(AppState.InWorld, harness.App.GetState());
            Assert.Equal(Town, harness.Router.GetCurrentScene());
            Assert.Equal(2, started);
            Assert.Equal(1, finished);
            Assert.Equal(1, postLoad);
            Assert.Equal(1.0, harness.Router.LoadProgress);
            Assert.Single(diagnostics.Errors); // 重载成功不追加新的错误
        }

        [Fact]
        public void LateCallbackFromFailedGeneration_DoesNotPolluteTheReload()
        {
            var harness = new SceneRouterTestSupport.Harness(TownRows, deferCallbacks: true);
            harness.App.RequestTransition(AppState.MainMenu);

            harness.Router.LoadScene(Town);
            harness.Loader.FailPending(new Id("nav.town_square")); // 一个失败，另一个仍在途
            harness.Router.Update();
            Assert.Equal(SceneRouterState.Idle, harness.Router.State);

            // 失败那一代的迟到回调：此刻才完成 scene 资源，必须被代际校验丢弃。
            harness.Loader.CompletePending(new Id("scene.town_square"));
            harness.Router.Update();
            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Null(harness.Router.GetCurrentScene());

            // 重载同一场景：两个资源在新一代里重新请求，全部到齐才算完成。
            harness.Router.LoadScene(Town);
            harness.Loader.CompletePending(new Id("scene.town_square"));
            harness.Router.Update();
            Assert.Equal(SceneRouterState.Loading, harness.Router.State); // nav 仍未到

            harness.Loader.CompletePending(new Id("nav.town_square"));
            harness.Router.Update();

            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(AppState.InWorld, harness.App.GetState());
            Assert.Equal(Town, harness.Router.GetCurrentScene());
        }

        [Fact]
        public void FailedSwitchToSecondScene_DoesNotUnloadFirst_AndRetrySucceeds()
        {
            var rows = "[" + SceneRouterTestSupport.TownSquareRow + "," + SceneRouterTestSupport.ForestPathRow + "]";
            var harness = new SceneRouterTestSupport.Harness(rows);
            harness.App.RequestTransition(AppState.MainMenu);
            harness.RegisterResourcesLoadable("scene.town_square", "nav.town_square");
            harness.Router.LoadScene(Town);
            harness.Router.Update();
            Assert.Equal(AppState.InWorld, harness.App.GetState());

            var unloaded = new System.Collections.Generic.List<Id>();
            harness.Bus.Subscribe<SceneUnloadedEvent>(SceneRouterEventKeys.Unloaded, e => unloaded.Add(e.SceneId));

            // forest 资源未登记：切换失败，不应触发旧场景卸载。
            harness.Router.LoadScene(Forest);
            harness.Router.Update();

            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(Town, harness.Router.GetCurrentScene());
            Assert.Empty(unloaded);
            Assert.Equal(AppState.MainMenu, harness.App.GetState()); // 失败路径回退到主菜单

            // 补登资源后重试：此时才卸载 town，完成后当前场景是 forest。
            harness.RegisterResourcesLoadable("scene.forest_path", "nav.forest_path");
            harness.Router.LoadScene(Forest);
            harness.Router.Update();

            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(AppState.InWorld, harness.App.GetState());
            Assert.Equal(Forest, harness.Router.GetCurrentScene());
            Assert.Equal(new[] { Town }, unloaded);
        }
    }
}
