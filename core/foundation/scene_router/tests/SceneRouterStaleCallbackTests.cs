using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.SceneRouter;
using Xunit;

namespace Tests.Foundation.SceneRouter
{
    /// <summary>FND-02 收口回归：上一次导航请求的迟到异步回调不得污染下一次导航请求的状态
    /// （见 SceneRouter.cs "_navigationGeneration" 判断记录、审计报告 code-review.md FND-02）。
    /// 复现场景与外部审核 validation-boundaries.md FND02 一致：Town 场景加载失败后开始加载
    /// Forest，Town 的 nav_ref 回调此时才迟到触发——修复前它会被无条件写入共享的
    /// _pendingResources 字典，凭空多出一个 Forest 从未排队过的失败条目，导致 Forest 被
    /// AnyResourceFailed() 误判为失败并提前回退到 MainMenu。</summary>
    public class SceneRouterStaleCallbackTests
    {
        [Fact]
        public void StaleCallbackFromFailedNavigation_IsIgnored_DoesNotPollutePendingNavigation()
        {
            var rows = "[" + SceneRouterTestSupport.TownSquareRow + "," + SceneRouterTestSupport.ForestPathRow + "]";
            var harness = new SceneRouterTestSupport.Harness(rows, deferCallbacks: true);
            harness.App.RequestTransition(AppState.MainMenu);

            // 第一次导航：Town。scene_ref 先失败，nav_ref 仍留在待完成队列（此时 Router 已回落
            // Idle/MainMenu，但 Town 的 nav_ref 回调还没有触发）。
            harness.Router.LoadScene(new Id("world.town_square"));
            harness.Loader.FailPending(new Id("scene.town_square"));
            harness.Router.Update();
            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(AppState.MainMenu, harness.App.GetState());

            // 第二次导航：Forest（新的一代）。
            harness.Router.LoadScene(new Id("world.forest_path"));

            // 关键时序：Town 请求遗留的 nav_ref 迟到回调，此时才触发——发生在 Forest 已经开始
            // 加载之后，且以失败告终。
            harness.Loader.FailPending(new Id("nav.town_square"));

            // 迟到的旧代际回调本身不应立即改变状态（回调只写字典，Update() 才读取判断）。
            Assert.Equal(SceneRouterState.Loading, harness.Router.State);
            Assert.Equal(AppState.Loading, harness.App.GetState());

            harness.Router.Update();

            // 修复后：Forest 的两个资源都还没完成，Update() 应该维持 Loading（既不会因为
            // Town 的迟到失败被误判失败，也不会因为字典里多出一个 key 被误判"全部完成"）。
            Assert.Equal(SceneRouterState.Loading, harness.Router.State);
            Assert.Equal(AppState.Loading, harness.App.GetState());

            // 收尾：Forest 自己的两个资源真正完成后，能正常进入 InWorld。
            harness.Loader.CompletePending(new Id("scene.forest_path"));
            harness.Loader.CompletePending(new Id("nav.forest_path"));
            harness.Router.Update();

            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(AppState.InWorld, harness.App.GetState());
            Assert.Equal((Id?)new Id("world.forest_path"), harness.Router.GetCurrentScene());
        }

        [Fact]
        public void LoadFailure_TerminatesRequest_LaterStaleCallbackForSameGeneration_DoesNotReviveIt()
        {
            // 同一代际内：Town 的 scene_ref 失败触发 HandleLoadFailure 后，nav_ref 才迟到失败——
            // 即便代际号相同（还没有开始下一次 LoadScene），也不应该在 Router 已回落 Idle 后
            // 因为这次迟到回调而产生任何可观察的状态变化或异常。
            var harness = new SceneRouterTestSupport.Harness("[" + SceneRouterTestSupport.TownSquareRow + "]", deferCallbacks: true);
            harness.App.RequestTransition(AppState.MainMenu);

            harness.Router.LoadScene(new Id("world.town_square"));
            harness.Loader.FailPending(new Id("scene.town_square"));
            harness.Router.Update();
            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(AppState.MainMenu, harness.App.GetState());

            // 迟到的同代际 nav_ref 回调：不应抛异常，不应改变状态。
            harness.Loader.FailPending(new Id("nav.town_square"));
            harness.Router.Update();

            Assert.Equal(SceneRouterState.Idle, harness.Router.State);
            Assert.Equal(AppState.MainMenu, harness.App.GetState());
        }
    }
}
