#nullable enable
// ShellFlowTests：U3-2 Shell 流程 PlayMode 测试（阶段 4 验收标准 2）。
using System.Collections;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Shell;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using NUnit.Framework;
using Presentation.Shell;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class ShellFlowTests
    {
        private const string SceneName = "Shell";
        private const string SlotId = "game.sample.slot_shellflow";
        private const string TierId = "diff.sample_story";

        private static IEnumerator LoadShellScene()
        {
            SceneManager.LoadScene(SceneName);
            // 判断记录（与 GreyBoxTests.cs 同款）：仅等两帧 Update 不保证旧场景的 MonoBehaviour 完全卸载/停止泫化（实测复现过旧 GreyBox 场景的 GameFoundationBootstrap.FixedUpdate 在切换到 Shell.unity 后仍多跳一帧）；多等两次 WaitForFixedUpdate 与 GreyBoxTests.LoadGreyBoxScene 同一惯例。
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private static ShellRoot RequireShellRoot()
        {
            var root = Object.FindFirstObjectByType<ShellRoot>();
            Assert.IsNotNull(root, "场景里应当有且仅有一个 ShellRoot 实例");
            return root!;
        }

        private static IEnumerator WaitForPage(ShellRoot shell, ShellPage page, int maxFrames = 200)
        {
            var guard = maxFrames;
            while (shell.Framework.Presentation.Shell.Page != page && guard-- > 0)
            {
                yield return null;
            }
            Assert.AreEqual(page, shell.Framework.Presentation.Shell.Page, $"应当能在 {maxFrames} 帧内到达页面 {page}");
        }

        /// <summary>判断记录：见 UiSuiteTests.EnterInWorld 同款判断记录——场景资源后台读取
        /// （UnityResourceLoader.LoadAsync）极少数情况下会偶发失败，SceneRouter 按既有设计回退到
        /// MainMenu、只记诊断不抛异常；本方法在到达 InWorld 之前失败时换一个新槽位重试一次。</summary>
        private static IEnumerator NewGameReachInWorld(ShellRoot shell, Id slotId, Id tierId)
        {
            shell.Framework.Presentation.Shell.NewGame(slotId, tierId, null);
            var guard = 1000;
            while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0) yield return null;

            if (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld)
            {
                shell.Framework.Presentation.Shell.NewGame(new Id(slotId.Value + "_retry"), tierId, null);
                guard = 200;
                while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0) yield return null;
            }

            Assert.AreEqual(ShellPage.InWorld, shell.Framework.Presentation.Shell.Page, "NewGame 应当能到达 InWorld（含一次重试）");
        }

        [UnityTest]
        public IEnumerator Start_TransitionsBootToMainMenu()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            Assert.AreEqual(ShellPage.MainMenu, shell.Framework.Presentation.Shell.Page, "ShellRoot.Awake 应当已经调用 Shell.Start() 转移到 MainMenu");
        }

        [UnityTest]
        public IEnumerator NewGame_EntersInWorld_PlayerViewExists()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();

            shell.Framework.Presentation.Shell.ShowSlots();
            Assert.AreEqual(ShellPage.SaveSlots, shell.Framework.Presentation.Shell.Page);

            shell.Framework.Presentation.Shell.ShowNewGameSetup();
            Assert.AreEqual(ShellPage.NewGameSetup, shell.Framework.Presentation.Shell.Page);

            yield return NewGameReachInWorld(shell, new Id(SlotId), new Id(TierId));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(shell.Framework.Presentation.ViewBinder.TryGetView(shell.Framework.PlayerId, out var view), "进入游戏内后应当能找到玩家视图");
            Assert.IsTrue(view!.IsAlive);
        }

        [UnityTest]
        public IEnumerator Pause_Resume_RoundTrip()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return NewGameReachInWorld(shell, new Id(SlotId + "_pause"), new Id(TierId));

            var paused = shell.Framework.Presentation.UiIntents.Pause();
            Assert.IsTrue(paused, "InWorld 下 Pause() 应当成功");
            yield return null;
            Assert.AreEqual(AppState.Pause, shell.Framework.Gameplay.AppState.GetState());
            Assert.AreEqual(ShellPage.Paused, shell.Framework.Presentation.Shell.Page);

            var resumed = shell.Framework.Presentation.UiIntents.Resume();
            Assert.IsTrue(resumed, "Pause 下 Resume() 应当成功");
            yield return null;
            Assert.AreEqual(AppState.InWorld, shell.Framework.Gameplay.AppState.GetState());
        }

        [UnityTest]
        public IEnumerator ReturnToMainMenu_FromInWorld_GoesToMainMenuPage()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return NewGameReachInWorld(shell, new Id(SlotId + "_return"), new Id(TierId));

            var ok = shell.Framework.Presentation.Shell.ReturnToMainMenu();
            Assert.IsTrue(ok);
            yield return null;
            Assert.AreEqual(ShellPage.MainMenu, shell.Framework.Presentation.Shell.Page);
        }

        [UnityTest]
        public IEnumerator SaveThenLoad_TwoRoundTrips_RestoresPlayerPosition_KeepsSingletons()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            var hostAfterFirstLoad = UnityEngineHost.Ensure();
            var presentationAfterFirstLoad = shell.Framework.Presentation;

            var slotId = new Id(SlotId + "_roundtrip");

            // ---- 第一次往返：新游戏 -> 移动 -> 暂停 -> 回主菜单。----
            yield return NewGameReachInWorld(shell, slotId, new Id(TierId));
            yield return new WaitForFixedUpdate();

            for (var i = 0; i < 25; i++)
            {
                shell.Framework.Gameplay.Carriers.Movement.Request(
                    Core.Carriers.Unit.MoveRequest.InDirection(shell.Framework.PlayerId, new Vec2(1, 0)));
                yield return new WaitForFixedUpdate();
            }
            var positionBeforeSave = shell.Framework.World.GetEntity(shell.Framework.PlayerId)!.Position;
            Assert.Greater(positionBeforeSave.X, 0.0, "移动后玩家 X 坐标应当大于 0");

            var overwriteResult = shell.Framework.Presentation.Shell.OverwriteSlot(slotId, playTimeSeconds: null, displaySummary: null);
            Assert.IsTrue(overwriteResult.Success, "移动后手动存档应当成功");

            shell.Framework.Presentation.UiIntents.Pause();
            yield return null;
            var returned = shell.Framework.Presentation.Shell.ReturnToMainMenu();
            Assert.IsTrue(returned);
            yield return null;
            Assert.AreEqual(ShellPage.MainMenu, shell.Framework.Presentation.Shell.Page, "第一次往返应当能回到主菜单");

            // ---- 篡改状态：确认"读档后恢复"不是巧合。----
            shell.Framework.World.GetEntity(shell.Framework.PlayerId)!.Position = Vec2.Zero;

            // ---- 第二次往返：读档 -> 游戏内，位置应恢复为存档时的值。----
            var loadResult = shell.Framework.Presentation.Shell.LoadGame(slotId);
            Assert.IsTrue(loadResult.Status == Core.Foundation.SaveSystem.LoadStatus.Loaded ||
                          loadResult.Status == Core.Foundation.SaveSystem.LoadStatus.LoadedFromBackup,
                $"读档应当成功，实际状态：{loadResult.Status}");
            yield return WaitForPage(shell, ShellPage.InWorld);
            yield return new WaitForFixedUpdate();

            var positionAfterLoad = shell.Framework.World.GetEntity(shell.Framework.PlayerId)!.Position;
            Assert.AreEqual(positionBeforeSave.X, positionAfterLoad.X, 0.05, "读档后玩家 X 坐标应当恢复为存档时的值");

            Assert.AreSame(hostAfterFirstLoad, UnityEngineHost.Ensure(), "两次往返后 UnityEngineHost 应当仍是同一个单例");
            Assert.AreSame(presentationAfterFirstLoad, shell.Framework.Presentation, "两次往返后 PresentationAssembly 应当只构造过一次（框架常驻部分不因往返而重建）");

            var allHosts = Object.FindObjectsByType<UnityEngineHost>(FindObjectsSortMode.None);
            Assert.AreEqual(1, allHosts.Length, "不应产生重复的 UnityEngineHost 实例");
            var allFrameworkHosts = Object.FindObjectsByType<FrameworkResidentHost>(FindObjectsSortMode.None);
            Assert.AreEqual(1, allFrameworkHosts.Length, "不应产生重复的 FrameworkResidentHost 实例");
        }

        [UnityTest]
        public IEnumerator DeleteSlot_RemovesSlotFromSaveSlotsViewModel()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            var slotId = new Id(SlotId + "_delete");

            shell.Framework.Presentation.Shell.NewGame(slotId, new Id(TierId), null);
            var guard = 1000;
            while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0) yield return null;
            if (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld)
            {
                // 判断记录：见 UiSuiteTests.EnterInWorld 同款判断记录，换一个新槽位重试一次；本用例
                // 后续断言全部改用实际成功的槽位 id。
                slotId = new Id(SlotId + "_delete_retry");
                shell.Framework.Presentation.Shell.NewGame(slotId, new Id(TierId), null);
                guard = 200;
                while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0) yield return null;
            }
            Assert.AreEqual(ShellPage.InWorld, shell.Framework.Presentation.Shell.Page, "NewGame 应当能到达 InWorld（含一次重试）");
            Assert.IsTrue(shell.Framework.Presentation.SaveSlots.Slots.Any(s => s.SlotId.Equals(slotId)), "新游戏存档后，SaveSlotsViewModel 应当能看到该槽位");

            var deleted = shell.Framework.Presentation.Shell.DeleteSlot(slotId);
            Assert.IsTrue(deleted, "DeleteSlot 应当返回成功（槽位删除前确实存在）");

            // 判断记录：ISaveSystem.DeleteSlot 不发任何事件（见 core 契约），SaveSlotsViewModel
            // 只订阅 SaveEventKeys.SaveCompleted/SaveLoaded 两种事件（见其源码），因此删除后不会
            // 自动刷新——与 UiPanelHost 每帧调用 RefreshUi()（间接调用 IUiDataSource 查询但同样
            // 不强制该 ViewModel 自身重新 Refresh）的实际使用方式一致：真实 UI 需要用户下一次
            // 打开面板时才会看到最新结果。测试里显式调用一次 Refresh() 模拟"面板下一次刷新"。
            shell.Framework.Presentation.SaveSlots.Refresh();
            Assert.IsFalse(shell.Framework.Presentation.SaveSlots.Slots.Any(s => s.SlotId.Equals(slotId)), "删除后，SaveSlotsViewModel 不应再看到该槽位");
        }

        [UnityTest]
        public IEnumerator Quit_FromMainMenu_RequestsExit()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            Assert.AreEqual(ShellPage.MainMenu, shell.Framework.Presentation.Shell.Page);

            LogAssert.ignoreFailingMessages = true; // Application.Quit 在编辑器/测试环境下可能记一条日志，不视为用例失败。
            var ok = shell.Framework.Presentation.Shell.Quit();
            LogAssert.ignoreFailingMessages = false;
            Assert.IsTrue(ok, "MainMenu 下 Quit() 应当被 IAppStateHost 接受");
        }
    }
}
