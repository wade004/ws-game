using System;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.SceneRouter;
using Xunit;

namespace Tests.Foundation.SceneRouter
{
    public class SceneRouterFailureAndGuardTests
    {
        [Fact]
        public void Update_OneResourceFails_RecordsFailure_StateBackToIdle_TransitionsToMainMenu()
        {
            var harness = new SceneRouterTestSupport.Harness("[" + SceneRouterTestSupport.TownSquareRow + "]", deferCallbacks: true);
            harness.App.RequestTransition(AppState.MainMenu);

            harness.Router.LoadScene(new Id("world.town_square"));
            harness.Loader.CompletePending(new Id("scene.town_square"));
            harness.Loader.FailPending(new Id("nav.town_square"));

            var finishedReceived = false;
            harness.Bus.Subscribe<SceneLoadFinishedEvent>(SceneRouterEventKeys.LoadFinished, _ => finishedReceived = true);

            harness.Router.Update();

            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            // 借助测试夹具在 AppStateMachineConfig 里额外放行的 Loading→MainMenu（见本模块
            // README 判断记录 3：默认配置不含这条转移，调用方需要自行追加）。
            Assert.Equal(AppState.MainMenu, harness.App.GetState());
            Assert.Null(harness.Router.GetCurrentScene());
            Assert.False(finishedReceived);
        }

        [Fact]
        public void LoadScene_WhileAlreadyLoading_ThrowsInvalidOperationException()
        {
            var rows = "[" + SceneRouterTestSupport.TownSquareRow + "," + SceneRouterTestSupport.ForestPathRow + "]";
            var harness = new SceneRouterTestSupport.Harness(rows, deferCallbacks: true);
            harness.App.RequestTransition(AppState.MainMenu);
            harness.Router.LoadScene(new Id("world.town_square"));

            Assert.Throws<InvalidOperationException>(() => harness.Router.LoadScene(new Id("world.forest_path")));
        }

        [Fact]
        public void LoadScene_UnknownSceneId_ThrowsArgumentException_AppStateUnchanged()
        {
            var harness = new SceneRouterTestSupport.Harness("[" + SceneRouterTestSupport.TownSquareRow + "]");
            harness.App.RequestTransition(AppState.MainMenu);

            Assert.Throws<ArgumentException>(() => harness.Router.LoadScene(new Id("world.does_not_exist")));
            Assert.Equal(AppState.MainMenu, harness.App.GetState()); // 未发生任何状态转移
            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
        }

        [Fact]
        public void LoadScene_IllegalAppState_ThrowsInvalidOperationException_AndPublishesNoEvents()
        {
            var harness = new SceneRouterTestSupport.Harness("[" + SceneRouterTestSupport.TownSquareRow + "]");
            // 不做 Boot -> MainMenu 转移：State 仍是 Boot，Boot -> Loading 不在允许集合内。

            var startedReceived = false;
            harness.Bus.Subscribe<SceneLoadStartedEvent>(SceneRouterEventKeys.LoadStarted, _ => startedReceived = true);

            Assert.Throws<InvalidOperationException>(() => harness.Router.LoadScene(new Id("world.town_square")));

            Assert.False(startedReceived);
            Assert.Equal(AppState.Boot, harness.App.GetState());
            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
        }
    }
}
