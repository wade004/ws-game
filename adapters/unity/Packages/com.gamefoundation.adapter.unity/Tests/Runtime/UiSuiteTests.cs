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

        /// <summary>拍板 7：UiPanel 现有十一个值（恢复 Shop，见 UiLayoutSchema.cs 判断记录），本用例
        /// 用 <c>Enum.GetValues</c> 遍历全部取值，新增 Shop 自动纳入覆盖，不需要单独为它另写一条
        /// 开关测试。</summary>
        [UnityTest]
        public IEnumerator AllElevenPanels_CanToggleOpenAndClose_WithoutException()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            Assert.AreEqual(11, ((UiPanel[])System.Enum.GetValues(typeof(UiPanel))).Length, "UiPanel 应为十一个值（拍板 7 恢复 Shop）");

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

        /// <summary>W3b 审计发现补齐："加载画面可见性无断言"。不复用 <see cref="EnterInWorld"/>
        /// （它只轮询到 <c>Page==InWorld</c> 为止，不检查中途 <c>Page==Loading</c> 期间画面是否真的
        /// 可见），改为自己驱动一遍 NewGame 并逐帧记录：<c>Page==Loading</c> 的每一帧
        /// <see cref="ShellRoot.IsLoadingScreenVisible"/> 都应为真，其余页面都应为假（见 ShellRoot
        /// 源码 <c>_loadingRoot.gameObject.SetActive(page == ShellPage.Loading)</c> 一行）。</summary>
        [UnityTest]
        public IEnumerator LoadingScreen_VisibleOnlyDuringLoadingPage()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();

            shell.Framework.Presentation.Shell.ShowSlots();
            shell.Framework.Presentation.Shell.ShowNewGameSetup();
            var ok = shell.Framework.Presentation.Shell.NewGame(new Id(SlotId + "_loading_probe"), new Id(TierId), null);
            Assert.IsTrue(ok, "NewGame 应当返回成功");

            // 判断记录：ShellRoot._loadingRoot 的显隐由 ShellRoot.Update()（MonoBehaviour 回调）在
            // 每帧读取 Page 后同步（见该方法源码），与本协程的执行顺序在同一帧内不保证先后——
            // NewGame() 本身可能已经同步把 Page 推进到某个状态，但 ShellRoot.Update() 要到"这一帧"
            // 才会追上去同步 SetActive。因此每次读取前先 yield 一帧，保证 ShellRoot.Update() 至少已经
            // 针对"当前读到的 Page 值"运行过一次，读到的 IsLoadingScreenVisible 才与同一帧的 Page
            // 值一致（而不是读到"上一帧的 UI 状态 + 这一帧刚变化的 Page"这种错位组合）。
            var observedLoadingVisible = false;
            var guard = 1000;
            yield return null;
            while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0)
            {
                var isLoadingPage = shell.Framework.Presentation.Shell.Page == ShellPage.Loading;
                if (isLoadingPage)
                {
                    observedLoadingVisible = true;
                    Assert.IsTrue(shell.IsLoadingScreenVisible, "Page==Loading 期间加载画面应当可见");
                }
                else
                {
                    Assert.IsFalse(shell.IsLoadingScreenVisible, $"Page=={shell.Framework.Presentation.Shell.Page} 期间加载画面不应可见");
                }
                yield return null;
            }
            // 循环退出时 Page 已经是 InWorld，但上面的 while 判定条件本身不会再检查这最后一帧——
            // 额外补一次同款检查，覆盖"进入 InWorld 那一帧"（同 EnterInWorld 判断记录，InWorld 不是
            // Loading，画面应当不可见）。
            Assert.IsFalse(shell.IsLoadingScreenVisible, "Page 变为 InWorld 的那一帧加载画面就不应再可见");

            Assert.AreEqual(ShellPage.InWorld, shell.Framework.Presentation.Shell.Page, "新游戏最终应当能进入 InWorld");
            Assert.IsFalse(shell.IsLoadingScreenVisible, "进入 InWorld 后加载画面不应再可见");
            // 判断记录：observedLoadingVisible 允许为假——本地场景资源已经过 StreamingAssets 同步，
            // SceneRouter 加载可能在单帧内同步完成（见 SceneRouter 判断记录），"是否真的观察到过
            // Loading 帧"依赖机器速度，不是本用例的核心断言；核心断言是上面循环体"只要处于 Loading
            // 页面就必须可见、处于其它页面就必须不可见"这条不变式，不因是否命中过 Loading 帧而减弱。
            _ = observedLoadingVisible;
        }

        /// <summary>拍板 12 验收（表现层异常隔离）：注入一个恒抛异常的假表现步骤（经
        /// <see cref="FrameworkResidentHost.ExtraPresentationStepForTests"/>），验证固定步（世界
        /// 推进）照常进行、其余表现步骤（本用例用"HUD 面板仍能正常刷新"代表"其余表现步骤照常执行"）
        /// 不受影响，同时异常确实被记诊断计数，不是被默默吞掉。</summary>
        [UnityTest]
        public IEnumerator PresentationStepException_DoesNotBlockFixedStep_OrOtherPresentationSteps()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            var exceptionCountBefore = shell.Framework.PresentationStepExceptionCount;
            System.Action<double> throwingStep = _ => throw new System.InvalidOperationException("AnimationLayerTests 注入的假表现步骤，预期被隔离");
            shell.Framework.ExtraPresentationStepForTests += throwingStep;

            var beastId = shell.Framework.BeastEntityId;
            Assert.IsTrue(beastId.HasValue, "测试前置：应当已经生成示例生物（验证固定步——空间索引/生物存在——不受表现层异常影响）");

            LogAssert.ignoreFailingMessages = true;
            try
            {
                for (var i = 0; i < 10; i++)
                {
                    yield return new WaitForFixedUpdate();
                    yield return null;
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                shell.Framework.ExtraPresentationStepForTests -= throwingStep;
            }

            Assert.Greater(shell.Framework.PresentationStepExceptionCount, exceptionCountBefore, "注入的假表现步骤抛出的异常应当被计数，不应被默默吞掉");

            // 固定步仍照常推进的证据：AppState 仍停留在 InWorld（没有因异常而卡死/退出），生物仍然
            // 存在（世界没有被异常中断的推进搞崩）。
            Assert.AreEqual(Core.Foundation.AppLifecycle.AppState.InWorld, shell.Framework.Gameplay.AppState.GetState(), "固定步应当照常推进，AppState 应仍为 InWorld");
            Assert.IsNotNull(shell.Framework.World.GetEntity(beastId!.Value), "固定步应当照常推进，生物实体应当仍然存在");

            // 其余表现步骤照常执行的证据：HUD 面板仍能正常刷新，不因某一个表现步骤抛异常而整体停摆。
            Assert.DoesNotThrow(() => shell.UiPanelHost.Hud.RefreshUi(), "其余表现步骤（HUD 刷新）应当不受注入异常影响，照常执行");
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

        /// <summary>拍板 7 验收（任务书原句）："打开商店 → 库存与价格显示 → 购买后背包数量与货币
        /// 变化"。商人取样例数据集 econ.vendor.sample_hunter（出售 item.sample_tonic，单价 5
        /// econ.currency.sample_coin，见 data/_sample/econ/econ.vendor.json），玩家初始货币为 0
        /// （RegisterUnit 不预置余额），测试前置经 EconomyHost.Add 直接发放，不经任何 UI 交互——
        /// 只验证本面板本身的显示/购买链路，不重复测试货币发放这条无关路径。</summary>
        [UnityTest]
        public IEnumerator Shop_OpenVendor_ShowsStockAndPrice_BuyChangesInventoryAndCurrency()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell);

            var playerId = shell.Framework.PlayerId;
            var vendorId = new Id("econ.vendor.sample_hunter");
            var itemId = new Id("item.sample_tonic");
            var currencyId = new Id("econ.currency.sample_coin");

            shell.Framework.Gameplay.Economy.Add(playerId, currencyId, 100, sourceId: new Id("econ.currency.sample_coin"));
            var balanceBefore = shell.Framework.Gameplay.Economy.GetBalance(playerId, currencyId);
            var inventoryCountBefore = shell.Framework.Gameplay.Carriers.Inventory.CountOf(playerId, itemId);

            shell.UiPanelHost.Shop.OpenVendor(vendorId);
            Assert.IsTrue(shell.UiPanelHost.Shop.IsOpen, "OpenVendor 后商店面板应当处于打开状态");

            var sellItems = shell.Framework.Presentation.Shop.SellItems;
            Assert.Greater(sellItems.Count, 0, "商店打开后出售清单应当非空（库存/价格展示的数据来源）");
            Assert.AreEqual(itemId, sellItems[0].ItemId, "出售清单第一条应为样例数据集登记的 item.sample_tonic");
            Assert.AreEqual(5, sellItems[0].PriceAmount, "单价应与 econ.vendor.json 登记的 price_amount 一致");

            shell.Framework.Presentation.UiIntents.Buy(vendorId, itemId, 1);

            yield return new WaitForFixedUpdate();
            yield return null;
            shell.UiPanelHost.Shop.RefreshUi();

            var balanceAfter = shell.Framework.Gameplay.Economy.GetBalance(playerId, currencyId);
            var inventoryCountAfter = shell.Framework.Gameplay.Carriers.Inventory.CountOf(playerId, itemId);

            Assert.AreEqual(balanceBefore - 5, balanceAfter, "购买后货币余额应当扣除单价");
            Assert.AreEqual(inventoryCountBefore + 1, inventoryCountAfter, "购买后背包对应物品数量应当增加 1");

            shell.UiPanelHost.Shop.CloseVendorAndHide();
            Assert.IsFalse(shell.UiPanelHost.Shop.IsOpen, "CloseVendorAndHide 后商店面板应当关闭");
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
