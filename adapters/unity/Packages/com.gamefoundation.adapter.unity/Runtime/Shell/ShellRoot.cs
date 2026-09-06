#nullable enable
// ShellRoot：U3-2 Shell 流程（任务书"主菜单（新游戏/读档/设置/退出）→ 存档槽 → 新游戏（选难度）
// → 加载画面 → 游戏内 → 暂停菜单 → 回主菜单"）。挂在 Shell.unity 场景唯一的入口物体上；本类型
// 只做"UI 编排"——具体状态转移/存档/场景加载全部转给 Presentation.Shell.ShellHost（铁律 P3，
// 见 presentation/shell/README.md）。
//
// 判断记录（"注入意图"测试哲学延续到本类型）：与 GreyBoxTests/GameFoundationBootstrap 同一惯例，
// PlayMode 测试直接调用 ShellRoot.Framework.Presentation.Shell 的方法（NewGame/LoadGame/
// ReturnToMainMenu 等）驱动流程，不模拟按钮点击的物理事件；本类型的按钮 OnClick 回调因此可以
// 直接调用同一批方法而不必担心"测试路径与真实点击路径不一致"——两者本就是同一套调用。
//
// 判断记录（占位调试文本标注）：同 Runtime/Ui/Panels 各文件顶部判断记录——"<game> 示例主菜单"
// "选择难度""加载中..."均为 UI 框架级 chrome 文案，不经 l10n.text；主菜单条目本身的文案
// （<see cref="BuildMainMenu"/> 里的 <c>Framework.Presentation.L10n.Text(entry.TextKey)</c>）与
// 难度档位名（<see cref="BuildNewGameSetup"/> 里的 <c>Framework.Presentation.L10n.Text(nameKey)</c>）
// 均经 l10n.text 真正查询。
using System;
using System.Collections.Generic;
using Adapter.Unity.Ui;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Presentation.Shell;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Adapter.Unity.Shell
{
    public sealed class ShellRoot : MonoBehaviour
    {
        public static readonly IReadOnlyList<Id> SaveSlotCandidates = new[]
        {
            new Id("game.sample.slot_1"),
            new Id("game.sample.slot_2"),
            new Id("game.sample.slot_3"),
        };

        public FrameworkResidentHost Framework { get; private set; } = null!;

        public UiPanelHost UiPanelHost { get; private set; } = null!;

        /// <summary>W3b 新增（拍板 10，加载画面可见性断言此前缺失）：供 PlayMode 测试直接读取加载
        /// 画面 GameObject 的显隐状态，不需要新增公开 UI 契约——<c>internal</c> 经
        /// Runtime/AssemblyInfo.cs 的 InternalsVisibleTo 对 Tests.Runtime/Tests.Editor 可见。</summary>
        internal bool IsLoadingScreenVisible => _loadingRoot.gameObject.activeSelf;

        private UiRoot _uiRoot = null!;
        private RectTransform _mainMenuRoot = null!;
        private RectTransform _newGameSetupRoot = null!;
        private RectTransform _loadingRoot = null!;
        private TextMeshProUGUI _loadingLabel = null!;
        private Id? _pendingNewGameSlot;

        private void Awake()
        {
            Framework = FrameworkResidentHost.Ensure();
            if (Framework.BootstrapFailed)
            {
                Debug.LogError("[ShellRoot] 框架常驻装配失败，Shell 无法启动。");
                return;
            }

            _uiRoot = UiRoot.Create("ShellUiRoot");

            var panelHostGo = new GameObject("UiPanelHost", typeof(RectTransform));
            panelHostGo.transform.SetParent(_uiRoot.Content, false);
            UiPanelHost = panelHostGo.AddComponent<UiPanelHost>();
            UiPanelHost.Initialize(
                _uiRoot.Content, Framework.Registry, Framework.Presentation,
                SaveSlotCandidates, OnSaveSlotLoadClicked, OnSaveSlotPrimaryClicked, OnSaveSlotDeleteClicked, OnPauseOptionClicked,
                getSfxBusVolume: () => Framework.Host.Audio.GetBusVolume(Core.Foundation.EngineAdapter.AudioBus.Sfx),
                onSfxBusVolumeChanged: v => Framework.Host.Audio.SetBusVolume(Core.Foundation.EngineAdapter.AudioBus.Sfx, v));

            // 技术债 17 收口：回合状态（含 input.action.end_turn 键盘/手柄绑定）已并入
            // UiPanelHost.Hud（HudViewModel/HudPanel，见二者判断记录），不再需要本类型单独创建/
            // 接线一个不经 UiPanelHost 登记的回合状态面板。

            BuildMainMenu();
            BuildNewGameSetup();
            BuildLoading();

            // 判断记录（场景重进的"回到干净主菜单"）：FrameworkResidentHost/PresentationAssembly
            // 是常驻单例（见该类型顶部判断记录），ShellHost 内部的 _mainMenuSubPage 字段与
            // IAppStateHost 的当前主状态因此跨场景重进持续存活，不会因为 Shell.unity 被重新
            // LoadScene（每条 PlayMode 测试用例的标准起手式）而重置。ShellHost.Start() 只尝试
            // "转移到 MainMenu"这一次转移（首次启动时是 Boot→MainMenu），不会重置
            // _mainMenuSubPage；若上一次场景存活期间已经导航到过 SaveSlots/NewGameSetup/Settings
            // 子页面，单独调用 Start() 不会清掉这份残留，导致重进后 Page 仍停在旧子页面（实测
            // 复现）。ShellHost.ReturnToMainMenu()（见其源码）无论是否需要真正转移主状态都会把
            // _mainMenuSubPage 重置为 MainMenu，因此本方法固定调用它一次做"回到干净主菜单"；
            // 只有真正首次启动（AppState 仍为 Boot）时才需要先调用 Start() 把主状态从 Boot 转移
            // 走（ReturnToMainMenu 只处理 InWorld/Pause→MainMenu 这两条，不处理 Boot）。
            if (Framework.Gameplay.AppState.GetState() == AppState.Boot)
            {
                Framework.Presentation.Shell.Start();
            }
            Framework.Presentation.Shell.ReturnToMainMenu();
        }

        private void BuildMainMenu()
        {
            _mainMenuRoot = UiWidgets.CreatePanelBackground("MainMenu", _uiRoot.Content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(300f, 280f), Vector2.zero);
            var list = UiWidgets.CreateVerticalList("List", _mainMenuRoot, 8f);
            UiWidgets.SetRect(list, Vector2.zero, Vector2.one, new Vector2(14, 14), new Vector2(-14, -14));
            UiWidgets.CreateLabel("Title", list, "<game> 示例主菜单", 20);

            foreach (var entry in Framework.Presentation.ShellViewModel.MenuEntries)
            {
                var captured = entry;
                UiWidgets.CreateButton($"Entry_{entry.Id.Value}", list, Framework.Presentation.L10n.Text(entry.TextKey), () => OnMainMenuEntryClicked(captured));
            }
        }

        private void OnMainMenuEntryClicked(ShellMenuEntry entry)
        {
            switch (entry.Action)
            {
                case ShellMenuAction.NewGame:
                case ShellMenuAction.LoadGame:
                    Framework.Presentation.Shell.ShowSlots();
                    break;
                case ShellMenuAction.Settings:
                    UiPanelHost.Settings.Show();
                    break;
                case ShellMenuAction.Quit:
                    Framework.Presentation.Shell.Quit();
                    break;
                case ShellMenuAction.Resume:
                    Framework.Presentation.UiIntents.Resume();
                    break;
                case ShellMenuAction.Back:
                    Framework.Presentation.Shell.ReturnToMainMenu();
                    break;
            }
        }

        private void BuildNewGameSetup()
        {
            _newGameSetupRoot = UiWidgets.CreatePanelBackground("NewGameSetup", _uiRoot.Content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(320f, 260f), Vector2.zero);
            var list = UiWidgets.CreateVerticalList("List", _newGameSetupRoot, 8f);
            UiWidgets.SetRect(list, Vector2.zero, Vector2.one, new Vector2(14, 14), new Vector2(-14, -14));
            UiWidgets.CreateLabel("Title", list, "选择难度", 20);

            foreach (var record in Framework.Registry.GetAll("diff.tier"))
            {
                var tierId = record.GetId("id");
                var nameKey = record.GetId("name_key");
                var label = Framework.Presentation.L10n.Text(nameKey);
                UiWidgets.CreateButton($"Tier_{tierId.Value}", list, label, () => StartNewGame(tierId));
            }

            _newGameSetupRoot.gameObject.SetActive(false);
        }

        private void StartNewGame(Id tierId)
        {
            if (!_pendingNewGameSlot.HasValue)
            {
                Debug.LogWarning("[ShellRoot] 未先选择存档槽就请求新游戏，已忽略。");
                return;
            }

            var ok = Framework.Presentation.Shell.NewGame(_pendingNewGameSlot.Value, tierId, archetypeId: null);
            if (!ok)
            {
                Debug.LogWarning($"[ShellRoot] NewGame(slot={_pendingNewGameSlot}, tier={tierId}) 返回失败。");
            }
        }

        private void BuildLoading()
        {
            _loadingRoot = UiWidgets.CreatePanelBackground("Loading", _uiRoot.Content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(320f, 100f), Vector2.zero);
            _loadingLabel = UiWidgets.CreateLabel("Label", _loadingRoot, "加载中...", 18, TMPro.TextAlignmentOptions.Center);
            UiWidgets.SetRect(_loadingLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _loadingRoot.gameObject.SetActive(false);
        }

        private void OnSaveSlotLoadClicked(Id slotId) => Framework.Presentation.Shell.LoadGame(slotId);

        private void OnSaveSlotPrimaryClicked(Id slotId)
        {
            var state = Framework.Gameplay.AppState.GetState();
            if (state == AppState.InWorld || state == AppState.Pause)
            {
                Framework.Presentation.Shell.OverwriteSlot(slotId, playTimeSeconds: null, displaySummary: null);
            }
            else
            {
                _pendingNewGameSlot = slotId;
                Framework.Presentation.Shell.ShowNewGameSetup();
            }
        }

        private void OnSaveSlotDeleteClicked(Id slotId) => Framework.Presentation.Shell.DeleteSlot(slotId);

        private void OnPauseOptionClicked(Id optionId)
        {
            switch (optionId.Value)
            {
                case "pause.resume": Framework.Presentation.UiIntents.Resume(); break;
                case "pause.settings": UiPanelHost.Settings.Show(); break;
                case "pause.main_menu": Framework.Presentation.Shell.ReturnToMainMenu(); break;
                case "pause.quit": Framework.Presentation.Shell.Quit(); break;
            }
        }

        private void Update()
        {
            if (Framework == null || Framework.BootstrapFailed)
            {
                return;
            }

            Framework.Presentation.ShellViewModel.Refresh();
            var page = Framework.Presentation.Shell.Page;
            var inGame = page == ShellPage.InWorld || page == ShellPage.Paused;

            _mainMenuRoot.gameObject.SetActive(page == ShellPage.MainMenu);
            _newGameSetupRoot.gameObject.SetActive(page == ShellPage.NewGameSetup);
            _loadingRoot.gameObject.SetActive(page == ShellPage.Loading);
            UiPanelHost.SetGameplayGroupVisible(inGame);

            if (page == ShellPage.SaveSlots) UiPanelHost.SaveSlots.Show();
            else if (!inGame) UiPanelHost.SaveSlots.Hide();

            if (page == ShellPage.Paused) UiPanelHost.PauseMenu.Show();
            else UiPanelHost.PauseMenu.Hide();

            if (page == ShellPage.Loading)
            {
                _loadingLabel.text = $"加载中... {(int)(Framework.Presentation.Shell.LoadingProgress * 100)}%";
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (page == ShellPage.InWorld) Framework.Presentation.UiIntents.Pause();
                else if (page == ShellPage.Paused) Framework.Presentation.UiIntents.Resume();
            }
        }
    }
}
