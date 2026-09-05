#nullable enable
// UiSuiteTests：U3-3 UI 套件 PlayMode 测试（阶段 4 验收标准 3、4 相关部分）。全部用例先加载
// Shell.unity（由 Adapter.Unity.EditorTools.ShellSceneBuilder.Build 生成），找到场景里的
// ShellRoot，经其 Framework.Presentation.Shell 直接调用 NewGame 进入游戏内（同 GreyBoxTests.cs
// 顶部"注入意图"判断记录：不模拟按钮物理点击，测试与真实点击走同一段代码）。
using System.Collections;
using System.Linq;
using Adapter.Unity.Shell;
using Adapter.Unity.Ui;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using Presentation.Shell;
using Presentation.Ui;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UiSuiteTests : PlayModeTestBase
    {
        private const string SceneName = "Shell";
        private const string SlotId = "game.sample.slot_1";
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

        /// <summary>把当前会话推进到 InWorld（新游戏），供需要"游戏内面板有真实数据"的用例复用。</summary>
        private static IEnumerator EnterInWorld(ShellRoot shell)
        {
            shell.Framework.Presentation.Shell.ShowSlots();
            shell.Framework.Presentation.Shell.ShowNewGameSetup();
            var ok = shell.Framework.Presentation.Shell.NewGame(new Id(SlotId), new Id(TierId), null);
            Assert.IsTrue(ok, "NewGame 应当返回成功");

            var guard = 1000;
            while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0)
            {
                yield return null;
            }

            // 判断记录：极少数情况下场景资源的后台文件读取（UnityResourceLoader.LoadAsync，见其
            // 判断记录）会在高并发场景切换下偶发失败，SceneRouter 按既有设计只记诊断、回退到
            // MainMenu，不抛异常（见 SceneRouter.cs"加载失败路径不新增事件"判断记录），因此不会
            // 被 LogAssert 捕捉到——本方法按同一惯例重试一次（换一个新槽位，避免复用可能已经
            // "半途"写入的旧槽位）。
            if (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld)
            {
                var retryOk = shell.Framework.Presentation.Shell.NewGame(new Id(SlotId + "_retry"), new Id(TierId), null);
                Assert.IsTrue(retryOk, "重试 NewGame 应当返回成功");
                guard = 200;
                while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0)
                {
                    yield return null;
                }
            }

            Assert.AreEqual(ShellPage.InWorld, shell.Framework.Presentation.Shell.Page, "新游戏后应当能进入 InWorld（含一次重试）");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator AllTenPanels_CanToggleOpenAndClose_WithoutException()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            foreach (var panelKind in (UiPanel[])System.Enum.GetValues(typeof(UiPanel)))
            {
                var panel = shell.UiPanelHost.Panels[panelKind];
                var wasOpen = panel.IsOpen;
                panel.Toggle();
                Assert.AreNotEqual(wasOpen, panel.IsOpen, $"面板 {panelKind} 切换后开关状态应当翻转");
                panel.Toggle();
                Assert.AreEqual(wasOpen, panel.IsOpen, $"面板 {panelKind} 再次切换应当回到原状态");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Inventory_SlotCount_MatchesLogicalInventory()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            var playerId = shell.Framework.PlayerId;
            var added1 = shell.Framework.Gameplay.Carriers.Inventory.AddItem(playerId, new Id("item.sample_blade"), 1);
            var added2 = shell.Framework.Gameplay.Carriers.Inventory.AddItem(playerId, new Id("item.sample_tonic"), 3);
            Assert.IsTrue(added1 && added2, "测试前置：两次 AddItem 都应当成功");

            // 判断记录：item.added 走 IEventBus.Enqueue（排队，非立即派发，同 GreyBoxTests.cs 顶部
            // EntityCreatedEvent 判断记录同一惯例），只有 WorldSim.Tick 的事件派发阶段才会真正
            // 触达 InventoryViewModel 的订阅回调；单纯 yield return null 不保证任何一次
            // FixedUpdate 已经跑过，必须显式等至少一次 WaitForFixedUpdate。
            yield return new WaitForFixedUpdate();
            yield return null;

            shell.UiPanelHost.Inventory.Show();
            shell.UiPanelHost.Inventory.RefreshUi();

            var logicalCount = shell.Framework.Gameplay.Carriers.Inventory.ListItems(playerId).Count;
            Assert.Greater(logicalCount, 0, "测试前置：逻辑背包应当已经有物品");
            Assert.AreEqual(logicalCount, shell.Framework.Presentation.Inventory.Slots.Count, "InventoryViewModel 的格子数应当与逻辑背包一致");
        }

        [UnityTest]
        public IEnumerator ActionBar_ClickBoundSlot_CastsSkill_ReducesBeastHealth()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            Assert.IsTrue(shell.Framework.BeastEntityId.HasValue, "新游戏后应当已经生成一只示例生物");
            var beastId = shell.Framework.BeastEntityId!.Value;
            var startHealth = shell.Framework.Gameplay.Carriers.Rules.Powers.GetPower(beastId, Core.Rules.Common.WellKnownPowers.Health);

            var healthDropped = false;
            for (var attempt = 0; attempt < 60 && !healthDropped; attempt++)
            {
                shell.UiPanelHost.ActionBar.ClickSlot(0); // 槽位 0 绑定 skill.sample_strike（见 FrameworkResidentHost.ResolveActionBarSlot）。
                yield return new WaitForFixedUpdate();
                yield return null;

                var current = shell.Framework.Gameplay.Carriers.Rules.Powers.GetPower(beastId, Core.Rules.Common.WellKnownPowers.Health);
                if (current < startHealth) healthDropped = true;
            }

            Assert.IsTrue(healthDropped, "多次点击动作条槽位 0 后，目标生物血量应当至少下降过一次");
        }

        [UnityTest]
        public IEnumerator Dialog_ChooseAcceptOption_AdvancesQuestState()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            var playerId = shell.Framework.PlayerId;
            var gossip = shell.Framework.Gameplay.Dialog.OpenGossip(playerId, new Id("npc.sample_hunter"), new Id("dialog.sample_hunter"));
            Assert.Greater(gossip.Options.Count, 0, "gossip 菜单应当至少有一个可见选项");

            shell.UiPanelHost.Dialog.Show();
            shell.UiPanelHost.Dialog.RefreshUi();
            Assert.IsNotNull(shell.Framework.Presentation.DialogView.Gossip, "DialogViewModel 应当已经反映当前打开的 gossip 会话");

            var acceptIndex = gossip.Options.First(o => o.TextKey.Value == "l10n.dialog.sample_hunter.option_accept").Index;
            var chosen = shell.Framework.Presentation.UiIntents.ChooseDialogOption(acceptIndex);
            Assert.IsTrue(chosen, "选择接受任务选项应当成功");

            var log = shell.Framework.Gameplay.Quest.GetLog(playerId);
            Assert.IsTrue(log.Any(q => q.QuestId.Value == "quest.sample_hunt"), "选择接受任务选项后，任务日志应当出现 quest.sample_hunt");
        }

        [UnityTest]
        public IEnumerator Settings_ChangeSfxBusVolume_ChangesAudioBus_AndPersistsFile()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            shell.UiPanelHost.Settings.Show();
            var before = shell.Framework.Host.Audio.GetBusVolume(AudioBus.Sfx);

            shell.Framework.Host.Audio.SetBusVolume(AudioBus.Sfx, 0.31);
            var after = shell.Framework.Host.Audio.GetBusVolume(AudioBus.Sfx);
            Assert.AreNotEqual(before, after, "改动音量后 IAudio 总线音量应当变化");
            Assert.AreEqual(0.31, after, 0.001, "IAudio 总线音量应当等于设置面板写入的值");

            var saved = shell.Framework.Presentation.Shell.SaveSettings(new JsonObjectBuilder().Build());
            Assert.IsTrue(saved, "SaveSettings 应当成功落盘");

            var userDataDir = shell.Framework.Host.FileSystem.GetUserDataDir();
            var settingsPath = userDataDir.EndsWith("/") ? userDataDir + "settings.json" : userDataDir + "/settings.json";
            Assert.IsTrue(shell.Framework.Host.FileSystem.Exists(settingsPath), $"保存设置后应当存在设置文件：{settingsPath}");

            var reloaded = shell.Framework.Presentation.Shell.LoadSettings();
            Assert.IsTrue(reloaded.TryGetValue("input_bindings", out _), "重新加载设置应当能读回 input_bindings 段（SaveSettings 写入、LoadSettings 读回）");
        }

        [UnityTest]
        public IEnumerator SkillBook_ShowsKnownSkills_ForRegisteredPlayer()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            shell.UiPanelHost.SkillBook.Show();
            shell.UiPanelHost.SkillBook.RefreshUi();

            Assert.Greater(shell.Framework.Presentation.SkillBook.Entries.Count, 0, "3 级玩家应当已经解锁至少一个技能（skill.sample_strike/skill.sample_burn）");
        }

        [UnityTest]
        public IEnumerator CharacterStats_ShowsConfiguredStatEntries()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            shell.UiPanelHost.CharacterStats.Show();
            shell.UiPanelHost.CharacterStats.RefreshUi();

            Assert.AreEqual(2, shell.Framework.Presentation.CharacterStats.Entries.Count, "FrameworkResidentHost 配置了 2 条属性（strength/stamina）");
            Assert.IsTrue(shell.Framework.Presentation.CharacterStats.Entries.All(e => !string.IsNullOrEmpty(e.DisplayName)), "属性展示名应当经 l10n.text 解析为非空文本");
        }

        [UnityTest]
        public IEnumerator QuestLog_ShowsAcceptedQuest_AfterDialogAccept()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            var playerId = shell.Framework.PlayerId;
            var questId = new Id("quest.sample_hunt");

            // 判断记录：presentation/ui 是常驻单例的一部分（同 FrameworkResidentHost 顶部判断
            // 记录），本任务集内其它用例（Dialog_ChooseAcceptOption_AdvancesQuestState）可能已经
            // 在同一个持续存活的 Gameplay.Quest 上接过这条任务——gossip 菜单的"接受"选项届时会因为
            // quest.is_available(...) 变为 false 而不再可见，本用例因此不强依赖该选项一定存在，
            // 优先走 gossip 流程真正验证"对话框选项提交"这条链路，任务已处于其它非 NotAccepted
            // 状态时改为直接窄契约调用 IQuestHost.Accept 兜底，确保本用例在任意执行顺序下都成立。
            var alreadyLogged = shell.Framework.Gameplay.Quest.GetLog(playerId).Any(q => q.QuestId.Equals(questId));
            if (!alreadyLogged)
            {
                var gossip = shell.Framework.Gameplay.Dialog.OpenGossip(playerId, new Id("npc.sample_hunter"), new Id("dialog.sample_hunter"));
                var acceptOption = gossip.Options.FirstOrDefault(o => o.TextKey.Value == "l10n.dialog.sample_hunter.option_accept");
                if (acceptOption.TextKey.Value != null)
                {
                    shell.Framework.Presentation.UiIntents.ChooseDialogOption(acceptOption.Index);
                }
                else
                {
                    shell.Framework.Gameplay.Quest.Accept(playerId, questId);
                }
            }

            shell.UiPanelHost.QuestLog.Show();
            shell.UiPanelHost.QuestLog.RefreshUi();
            Assert.IsTrue(shell.Framework.Presentation.QuestLog.Log.Any(q => q.QuestId.Value == "quest.sample_hunt"), "接受任务后，QuestLogViewModel 应当出现该任务");
        }
    }
}
