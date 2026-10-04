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
        public EquipmentPanel Equipment { get; private set; } = null!;
        public QuestLogPanel QuestLog { get; private set; } = null!;
        public DialogPanel Dialog { get; private set; } = null!;
        public SkillBookPanel SkillBook { get; private set; } = null!;
        public CharacterStatsPanel CharacterStats { get; private set; } = null!;
        public SettingsPanel Settings { get; private set; } = null!;
        public SaveSlotsPanel SaveSlots { get; private set; } = null!;
        public PauseMenuPanel PauseMenu { get; private set; } = null!;
        public ShopPanel Shop { get; private set; } = null!;

        public IReadOnlyDictionary<UiPanel, IUiPanel> Panels => _panels;

        /// <summary>
        /// 面板取皮肤包与图标/纸娃娃层资源的入口（ADR-0149）。<see cref="Initialize"/> 之前可以注入（测试换皮肤包、换资源根）；
        /// 不注入则按数据装配（<see cref="UiVisuals.Create"/>：皮肤包取 ui_layout_definition 的 skin_ref，缺省占位皮肤；加载器取全局宿主的）。
        /// </summary>
        public UiVisuals? Visuals { get; set; }

        private bool _ownsVisuals;
        private bool _installedSkin;

        /// <summary>
        /// 悬停提示框与拖放穿脱（ADR-0152）：背包与装备面板共用；穿脱动作转发 <see cref="UiIntents"/>。<see cref="Initialize"/> 之后可用。
        /// 判断记录（ADR-0155）：运行期换皮肤走 <see cref="UiVisuals.SwitchSkin"/>，所有面板都会换成新皮肤——持有皮肤包精灵的部件（背包、装备、提示框、拖拽）订阅
        /// <see cref="UiVisuals.SkinChanged"/> 自行重建；其余面板（HUD、动作条、任务日志等）经 <see cref="UiWidgets"/> 建控件，由 <see cref="UiSkinBindings.ReapplyAll"/>
        /// 在换皮肤时统一换底图、按钮图、字体与配色，不必自己订阅。（此前这些面板保持建出来时的旧外观，ADR-0082"不回溯重建"的取舍只对直接调
        /// <see cref="UiSkin.Install"/>/<see cref="UiSkin.Reset"/> 仍成立。）
        /// </summary>
        public UiInteraction? Interaction { get; private set; }

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
            if (Visuals == null)
            {
                Visuals = UiVisuals.Create(registry, presentation.DisplayInfo);
                _ownsVisuals = true;
            }

            Visuals.L10n ??= presentation.L10n;

            // 非缺省皮肤包把主题配色/面板底图/字体装成 UiSkin 覆盖；缺省皮肤不装任何覆盖（逐位不变）。调用方自己装过覆盖时以调用方的为准。
            // 装上之后覆盖归 UiVisuals 持有（ADR-0155），宿主只记"是我让它装的"，销毁时对调用方注入的 Visuals 撤掉，自己建的随 Dispose 撤。
            if (Visuals.InstallSkinOverride(onlyIfFree: true))
            {
                _installedSkin = true;
            }

            var declaredPanels = new HashSet<UiPanel>();
            foreach (var record in registry.GetAll(UiSchemas.UiLayoutDefinition.Name))
            {
                declaredPanels.Add(UiLayoutDefinition.FromRecord(record).Panel);
            }
            foreach (var panel in (UiPanel[])Enum.GetValues(typeof(UiPanel)))
            {
                // 装备面板的布局行是可选的（缺省布局见 EquipmentPanel.DefaultLayout），不为缺行告警。
                if (panel != UiPanel.Equipment && !declaredPanels.Contains(panel))
                {
                    Debug.LogWarning($"[UiPanelHost] ui_layout_definition 未登记面板 \"{panel}\" 的行，仍按默认布局构建（不阻断）。");
                }
            }

            GameplayGroup = UiWidgets.CreateRoot("GameplayGroup", content);

            // 技术债 17 收口：Hud 面板并入回合状态展示，额外接 UiIntents（"结束回合"意图转发）与
            // InputMap（input.action.end_turn 键盘/手柄绑定，见 HudPanel.Construct 判断记录）；原
            // 独立于本登记表之外的 TurnStatusPanel 已删除。
            Hud = CreatePanel<HudPanel>("Hud", GameplayGroup);
            Hud.Construct((RectTransform)Hud.transform, presentation.Hud, presentation.UiIntents, presentation.InputMap);
            _panels[UiPanel.Hud] = Hud;

            ActionBar = CreatePanel<ActionBarPanel>("ActionBar", GameplayGroup);
            // 消费方反馈第 3 条（2026-09-20，ADR-0048）：改走 ActionBarPanel.Construct 新增的
            // 携带 IL10nHost 的加性重载（渲染 skill.def.name_key），既有三参数签名保留未动，
            // 见该类型判断记录。
            ActionBar.Construct((RectTransform)ActionBar.transform, presentation.ActionBar, presentation.UiIntents, presentation.L10n);
            _panels[UiPanel.ActionBar] = ActionBar;

            Inventory = CreatePanel<InventoryPanel>("Inventory", GameplayGroup);
            Inventory.Construct((RectTransform)Inventory.transform, presentation.Inventory, presentation.UiIntents, Visuals);
            Inventory.Hide();
            _panels[UiPanel.Inventory] = Inventory;

            // 装备面板（ADR-0149）：槽位网格 + 纸娃娃预览，槽位/外观取自数据，美术取自皮肤包与资源加载器，布局取 ui_layout_definition 的 equipment 行。
            Equipment = CreatePanel<EquipmentPanel>("Equipment", GameplayGroup);
            Equipment.Construct((RectTransform)Equipment.transform, presentation.Equipment, Visuals, presentation.UiIntents);
            Equipment.Hide();
            _panels[UiPanel.Equipment] = Equipment;

            // 悬停提示框与拖放穿脱（ADR-0152）：画在内容节点最上层，穿脱转发 UiIntents。
            var intents = presentation.UiIntents;
            Interaction = new UiInteraction(
                content,
                Visuals,
                new UiEquipActions((instance, slot) => intents.Equip(instance, slot), slot => intents.Unequip(slot) != null));
            Inventory.AttachInteraction(Interaction);
            Equipment.AttachInteraction(Interaction);

            QuestLog = CreatePanel<QuestLogPanel>("QuestLog", GameplayGroup);
            // 消费方反馈第六批（阻塞）：改走 QuestLogPanel.Construct 新增的携带 IL10nHost 的加性重载
            // （渲染 quest.def.title_key），既有两参数签名保留未动，见该类型判断记录。
            QuestLog.Construct((RectTransform)QuestLog.transform, presentation.QuestLog, presentation.L10n);
            QuestLog.Hide();
            _panels[UiPanel.QuestLog] = QuestLog;

            Dialog = CreatePanel<DialogPanel>("Dialog", GameplayGroup);
            Dialog.Construct((RectTransform)Dialog.transform, presentation.DialogView, presentation.UiIntents, presentation.L10n);
            Dialog.Hide();
            _panels[UiPanel.Dialog] = Dialog;

            SkillBook = CreatePanel<SkillBookPanel>("SkillBook", GameplayGroup);
            SkillBook.Construct((RectTransform)SkillBook.transform, presentation.SkillBook, presentation.UiIntents);
            SkillBook.Hide();
            _panels[UiPanel.SkillBook] = SkillBook;

            CharacterStats = CreatePanel<CharacterStatsPanel>("CharacterStats", GameplayGroup);
            CharacterStats.Construct((RectTransform)CharacterStats.transform, presentation.CharacterStats);
            CharacterStats.Hide();
            _panels[UiPanel.CharacterStats] = CharacterStats;

            // 判断记录：Settings 不挂在 GameplayGroup 下——见 SaveSlots/PauseMenu 同款判断记录，
            // Settings 面板同样需要在"游戏内面板整体隐藏"的主菜单页面下也能被 ShellRoot 独立打开
            // （shell_menu_definition 的 settings 菜单项）。
            Settings = CreatePanel<SettingsPanel>("Settings", content);
            Settings.Construct((RectTransform)Settings.transform, presentation.Settings, presentation.UiIntents, SaveSettingsToStore(presentation), getSfxBusVolume, onSfxBusVolumeChanged);
            Settings.Hide();
            _panels[UiPanel.Settings] = Settings;

            SaveSlots = CreatePanel<SaveSlotsPanel>("SaveSlots", content);
            SaveSlots.Construct((RectTransform)SaveSlots.transform, presentation.SaveSlots, saveSlotCandidates, onSaveSlotLoad, onSaveSlotSaveOrOverwrite, onSaveSlotDelete);
            SaveSlots.Hide();
            _panels[UiPanel.SaveSlots] = SaveSlots;

            PauseMenu = CreatePanel<PauseMenuPanel>("PauseMenu", content);
            PauseMenu.Construct((RectTransform)PauseMenu.transform, presentation.PauseMenu, presentation.L10n, onPauseOptionClicked);
            PauseMenu.Hide();
            _panels[UiPanel.PauseMenu] = PauseMenu;

            // 拍板 7：Shop 挂在 GameplayGroup 下（同 Inventory/QuestLog 一贯的"游戏内菜单类面板"归属，
            // 不像 SaveSlots/PauseMenu/Settings 需要在主菜单页面下也能单独打开）。
            Shop = CreatePanel<ShopPanel>("Shop", GameplayGroup);
            Shop.Construct((RectTransform)Shop.transform, presentation.Shop, presentation.Inventory, presentation.UiIntents);
            Shop.Hide();
            _panels[UiPanel.Shop] = Shop;
        }

        private static Action SaveSettingsToStore(PresentationAssembly presentation) => () =>
        {
            presentation.Shell.SaveSettings(new Core.Foundation.Common.Json.JsonObjectBuilder().Build());
        };

        /// <summary>判断记录（面板显隐必须作用在面板自己的视觉子树上，测试覆盖第四批 T-M47/T-L15 发现并修复）：
        /// 此前这里造出的面板物体是一个空壳（不带任何视觉），而各面板 <c>Construct</c> 又把背景与全部控件挂在
        /// 传入的 <c>parent</c>（GameplayGroup/content）下——<see cref="UiPanelBehaviour.Hide"/> 只停用空壳，
        /// 背景与控件仍然留在画面上，“默认关闭”的面板其实一直可见。修法：面板物体本身做成铺满父节点的拉伸
        /// 节点（与 <see cref="UiWidgets.CreateRoot"/> 同口径），<c>Construct</c> 的 parent 改为面板物体自己，
        /// 各面板背景的锚点/偏移相对于“与原父节点同大”的面板节点，布局不变，显隐随面板物体生效。</summary>
        private static T CreatePanel<T>(string name, Transform parent) where T : Component
        {
            var rect = UiWidgets.CreateRoot(name, parent);
            return rect.gameObject.AddComponent<T>();
        }

        public void Toggle(UiPanel panel)
        {
            if (_panels.TryGetValue(panel, out var p)) p.Toggle();
        }

        public bool IsOpen(UiPanel panel) => _panels.TryGetValue(panel, out var p) && p.IsOpen;

        private void OnDestroy()
        {
            // 只撤自己让装的皮肤覆盖与自己建的资源入口；调用方注入的 Visuals 由调用方释放。
            if (_installedSkin)
            {
                Visuals?.ReleaseSkinOverride();
                _installedSkin = false;
            }

            Interaction?.Dispose();
            Interaction = null;

            if (_ownsVisuals && Visuals != null)
            {
                Visuals.Dispose();
                Visuals = null;
            }
        }

        private void Update()
        {
            if (Visuals != null && Visuals.RetiredPackCount > 0)
            {
                Visuals.ReleaseRetiredPacks();      // ADR-0155：换肤后没有存活部件再引用的旧皮肤包尽快释放
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.iKey.wasPressedThisFrame) Toggle(UiPanel.Inventory);
                if (keyboard.uKey.wasPressedThisFrame) Toggle(UiPanel.Equipment);
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
