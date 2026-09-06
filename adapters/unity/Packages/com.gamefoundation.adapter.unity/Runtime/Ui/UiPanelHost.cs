#nullable enable
// UiPanelHost：十个界面单元的登记/开关/层叠（任务书 U3-1）。
//
// 判断记录（"按 ui_layout_definition 表登记面板"落地方式）：该表（见 presentation/ui/schema/
// UiLayoutSchema.cs）只登记 panel 枚举 + 可选 slots + 引擎侧布局参数 fields（结构留给引擎适配层
// 解释，本模块不强行约定其内容），不登记"该面板默认是否打开""快捷键是哪个键"这类引擎侧交互
// 细节——这些不是数据表的职责范围（08/09 文档均未把"快捷键绑定"列为该表字段）。本类型按
// "读取该表确认十个面板 id 齐全（诊断用途，缺行时记警告但不阻断——面板本身仍用代码里固定的
// 十个 UiPanel 枚举值构建，不因数据表缺行而少造）+ 用面板自身的十个视图模型驱动具体展示"这一
// 折中方式落地"按表登记"，具体开关快捷键固定在本类型（游戏层可通过替换/包装本类型自定义）。
using System;
using System.Collections.Generic;
using Adapter.Unity.Ui.Panels;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Presentation.Ui;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Adapter.Unity.Ui
{
    public sealed class UiPanelHost : MonoBehaviour
    {
        private readonly Dictionary<UiPanel, IUiPanel> _panels = new Dictionary<UiPanel, IUiPanel>();

        public HudPanel Hud { get; private set; } = null!;
        public ActionBarPanel ActionBar { get; private set; } = null!;
        public InventoryPanel Inventory { get; private set; } = null!;
        public QuestLogPanel QuestLog { get; private set; } = null!;
        public DialogPanel Dialog { get; private set; } = null!;
        public SkillBookPanel SkillBook { get; private set; } = null!;
        public CharacterStatsPanel CharacterStats { get; private set; } = null!;
        public SettingsPanel Settings { get; private set; } = null!;
        public SaveSlotsPanel SaveSlots { get; private set; } = null!;
        public PauseMenuPanel PauseMenu { get; private set; } = null!;

        public IReadOnlyDictionary<UiPanel, IUiPanel> Panels => _panels;

        /// <summary>八个"游戏内"面板（Hud/ActionBar/Inventory/QuestLog/Dialog/SkillBook/
        /// CharacterStats/Settings）的公共父节点；<see cref="SaveSlots"/>/<see cref="PauseMenu"/>
        /// 不挂在本节点下——两者同时被 <c>Adapter.Unity.Shell.ShellRoot</c> 的主菜单存档槽页/
        /// 暂停覆盖层复用（见 SharedPanels.cs 类型注释），需要在"游戏内面板整体隐藏"（回到主菜单）
        /// 时仍可单独显示，因此单独控制显隐，不随本节点一起隐藏。</summary>
        public RectTransform GameplayGroup { get; private set; } = null!;

        /// <summary>整体显示/隐藏八个"游戏内"面板（ShellRoot 在 InWorld/Paused 之外的页面调用
        /// 传 false）。</summary>
        public void SetGameplayGroupVisible(bool visible) => GameplayGroup.gameObject.SetActive(visible);

        /// <summary>按 <see cref="UiPanel"/> 十个取值构建对应面板实例并全部挂到 <paramref name="content"/>
        /// 下；<see cref="Inventory"/>/<see cref="QuestLog"/>/<see cref="Dialog"/>/<see cref="SkillBook"/>/
        /// <see cref="CharacterStats"/>/<see cref="Settings"/>/<see cref="SaveSlots"/>/<see cref="PauseMenu"/>
        /// 默认关闭；<see cref="Hud"/>/<see cref="ActionBar"/> 默认打开（09 §7.1：HUD/动作条是常驻
        /// 界面，其余是"按需打开"的菜单类面板）。</summary>
        public void Initialize(
            RectTransform content,
            IDataRegistryView registry,
            PresentationAssembly presentation,
            IReadOnlyList<Core.Foundation.Common.Id> saveSlotCandidates,
            Action<Core.Foundation.Common.Id> onSaveSlotLoad,
            Action<Core.Foundation.Common.Id> onSaveSlotSaveOrOverwrite,
            Action<Core.Foundation.Common.Id> onSaveSlotDelete,
            Action<Core.Foundation.Common.Id> onPauseOptionClicked,
            Func<double>? getSfxBusVolume = null,
            Action<double>? onSfxBusVolumeChanged = null)
        {
            var declaredPanels = new HashSet<UiPanel>();
            foreach (var record in registry.GetAll(UiSchemas.UiLayoutDefinition.Name))
            {
                declaredPanels.Add(UiLayoutDefinition.FromRecord(record).Panel);
            }
            foreach (var panel in (UiPanel[])Enum.GetValues(typeof(UiPanel)))
            {
                if (!declaredPanels.Contains(panel))
                {
                    Debug.LogWarning($"[UiPanelHost] ui_layout_definition 未登记面板 \"{panel}\" 的行，仍按默认布局构建（不阻断）。");
                }
            }

            GameplayGroup = UiWidgets.CreateRoot("GameplayGroup", content);

            // 技术债 17 收口：Hud 面板并入回合状态展示，额外接 UiIntents（"结束回合"意图转发）与
            // InputMap（input.action.end_turn 键盘/手柄绑定，见 HudPanel.Construct 判断记录）；原
            // 独立于本登记表之外的 TurnStatusPanel 已删除。
            Hud = CreatePanel<HudPanel>("Hud", GameplayGroup);
            Hud.Construct(GameplayGroup, presentation.Hud, presentation.UiIntents, presentation.InputMap);
            _panels[UiPanel.Hud] = Hud;

            ActionBar = CreatePanel<ActionBarPanel>("ActionBar", GameplayGroup);
            ActionBar.Construct(GameplayGroup, presentation.ActionBar, presentation.UiIntents);
            _panels[UiPanel.ActionBar] = ActionBar;

            Inventory = CreatePanel<InventoryPanel>("Inventory", GameplayGroup);
            Inventory.Construct(GameplayGroup, presentation.Inventory, presentation.UiIntents);
            Inventory.Hide();
            _panels[UiPanel.Inventory] = Inventory;

            QuestLog = CreatePanel<QuestLogPanel>("QuestLog", GameplayGroup);
            QuestLog.Construct(GameplayGroup, presentation.QuestLog);
            QuestLog.Hide();
            _panels[UiPanel.QuestLog] = QuestLog;

            Dialog = CreatePanel<DialogPanel>("Dialog", GameplayGroup);
            Dialog.Construct(GameplayGroup, presentation.DialogView, presentation.UiIntents, presentation.L10n);
            Dialog.Hide();
            _panels[UiPanel.Dialog] = Dialog;

            SkillBook = CreatePanel<SkillBookPanel>("SkillBook", GameplayGroup);
            SkillBook.Construct(GameplayGroup, presentation.SkillBook, presentation.UiIntents);
            SkillBook.Hide();
            _panels[UiPanel.SkillBook] = SkillBook;

            CharacterStats = CreatePanel<CharacterStatsPanel>("CharacterStats", GameplayGroup);
            CharacterStats.Construct(GameplayGroup, presentation.CharacterStats);
            CharacterStats.Hide();
            _panels[UiPanel.CharacterStats] = CharacterStats;

            // 判断记录：Settings 不挂在 GameplayGroup 下——见 SaveSlots/PauseMenu 同款判断记录，
            // Settings 面板同样需要在"游戏内面板整体隐藏"的主菜单页面下也能被 ShellRoot 独立打开
            // （shell_menu_definition 的 settings 菜单项）。
            Settings = CreatePanel<SettingsPanel>("Settings", content);
            Settings.Construct(content, presentation.Settings, presentation.UiIntents, SaveSettingsToStore(presentation), getSfxBusVolume, onSfxBusVolumeChanged);
            Settings.Hide();
            _panels[UiPanel.Settings] = Settings;

            SaveSlots = CreatePanel<SaveSlotsPanel>("SaveSlots", content);
            SaveSlots.Construct(content, presentation.SaveSlots, saveSlotCandidates, onSaveSlotLoad, onSaveSlotSaveOrOverwrite, onSaveSlotDelete);
            SaveSlots.Hide();
            _panels[UiPanel.SaveSlots] = SaveSlots;

            PauseMenu = CreatePanel<PauseMenuPanel>("PauseMenu", content);
            PauseMenu.Construct(content, presentation.PauseMenu, presentation.L10n, onPauseOptionClicked);
            PauseMenu.Hide();
            _panels[UiPanel.PauseMenu] = PauseMenu;
        }

        private static Action SaveSettingsToStore(PresentationAssembly presentation) => () =>
        {
            presentation.Shell.SaveSettings(new Core.Foundation.Common.Json.JsonObjectBuilder().Build());
        };

        private static T CreatePanel<T>(string name, Transform parent) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        public void Toggle(UiPanel panel)
        {
            if (_panels.TryGetValue(panel, out var p)) p.Toggle();
        }

        public bool IsOpen(UiPanel panel) => _panels.TryGetValue(panel, out var p) && p.IsOpen;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.iKey.wasPressedThisFrame) Toggle(UiPanel.Inventory);
                if (keyboard.jKey.wasPressedThisFrame) Toggle(UiPanel.QuestLog);
                if (keyboard.kKey.wasPressedThisFrame) Toggle(UiPanel.SkillBook);
                if (keyboard.cKey.wasPressedThisFrame) Toggle(UiPanel.CharacterStats);
                if (keyboard.nKey.wasPressedThisFrame) Toggle(UiPanel.Settings);
                if (keyboard.lKey.wasPressedThisFrame) Toggle(UiPanel.SaveSlots);
            }

            foreach (var kv in _panels)
            {
                if (kv.Value.IsOpen)
                {
                    kv.Value.RefreshUi();
                }
            }
        }
    }
}
