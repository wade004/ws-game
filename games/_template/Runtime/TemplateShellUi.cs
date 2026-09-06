#nullable enable
// TemplateShellUi：最小主菜单（"从主菜单进到一张地图"闭环的 UI 一端）。
//
// 判断记录（为什么不是 Adapter.Unity.Shell.ShellRoot 的完整复刻）：ShellRoot 的存档槽/难度选择/
// 暂停菜单/设置面板是围绕 Adapter.Unity 工作台自己的示例数据（diff.tier 多档、
// shell_menu_definition 四个入口）搭的完整示范，功能丰富但也更容易在"最小闭环"阶段引入本模板
// 不需要的耦合；本类型只做任务书要求的最小面：读 shell_menu_definition 表驱动菜单文案与入口
// （数据驱动，不是硬编码按钮），点击"新游戏"入口时用 GameOptions.DefaultDifficultyId 固定难度、
// 固定存档槽直接开局，不做存档槽选择 UI。真实游戏接入阶段（13 §6"配表现"）应参照 ShellRoot 的
// 完整实现扩展存档槽/难度选择等 UI。
using Adapter.Unity.Ui;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Presentation.Shell;
using UnityEngine;

namespace Game.Template
{
    /// <summary>挂在 Shell 场景唯一的入口物体上（见 <c>Editor/GameSceneBuilder.cs</c>）。</summary>
    public sealed class TemplateShellUi : MonoBehaviour
    {
        private static readonly Id DefaultSaveSlotId = new Id("game.template.slot_1");

        /// <summary>GP-PRES-08 收口：shell_menu_definition 的入口缺 text_key（或表整体为空）时
        /// 的本地化兜底 key——复用 data/game/l10n/l10n.text.json 已登记的
        /// l10n.shell.template_new_game 一行，语义上就是同一个"开始游戏"动作，不新造第二份文案。</summary>
        private static readonly Id FallbackNewGameTextKey = new Id("l10n.shell.template_new_game");

        public GameBootstrap Bootstrap { get; private set; } = null!;

        private UiRoot _uiRoot = null!;
        private RectTransform _mainMenuRoot = null!;

        private void Awake()
        {
            Bootstrap = GameBootstrap.Ensure();
            if (Bootstrap.BootstrapFailed)
            {
                Debug.LogError("[TemplateShellUi] 游戏世界装配失败，主菜单无法启动。");
                return;
            }

            _uiRoot = UiRoot.Create("TemplateShellUiRoot");
            BuildMainMenu();

            if (Bootstrap.Gameplay.AppState.GetState() == AppState.Boot)
            {
                Bootstrap.Presentation.Shell.Start();
            }
            Bootstrap.Presentation.Shell.ReturnToMainMenu();
        }

        private void BuildMainMenu()
        {
            _mainMenuRoot = UiWidgets.CreatePanelBackground("MainMenu", _uiRoot.Content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(320f, 220f), Vector2.zero);
            var list = UiWidgets.CreateVerticalList("List", _mainMenuRoot, 8f);
            UiWidgets.SetRect(list, Vector2.zero, Vector2.one, new Vector2(14, 14), new Vector2(-14, -14));
            // GP-PRES-08 收口（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
            // 标题此前硬编码中文字符串，绕开了 09 第 7.3 节"面向玩家文案一律经本地化表"的铁律。
            // 改经 l10n.text（见 data/game/l10n/l10n.text.json 的 l10n.shell.template_main_menu_title
            // 一行）——L10n.Text 对缺失 key 有既定的兜底策略（记诊断 + 占位文本，不抛异常，见
            // IL10nHost.Text 判断记录），复制本模板到新游戏时若忘记补这一行 key，标题会显示为
            // 可诊断的占位符而不是静默显示错误语言/硬编码文案。
            UiWidgets.CreateLabel("Title", list, Bootstrap.Presentation.L10n.Text(new Id("l10n.shell.template_main_menu_title")), 20);

            // 数据驱动：菜单入口读自 shell_menu_definition 表（见模板 data/game/shell/
            // shell_menu_definition.json 的最小示例，只登记 new_game 一条），不是硬编码按钮——
            // 复制模板后按需在该数据文件里增补 load_game/settings/quit 等入口，UI 侧不需要改代码
            // （惯例同 Adapter.Unity.Shell.ShellRoot.BuildMainMenu）。
            var menuRecords = Bootstrap.Registry.GetAll("shell_menu_definition");
            if (menuRecords.Count > 0 && menuRecords[0].TryGetArray("entries", out var entries))
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (!(entries[i] is JsonObject entryObj)) continue;
                    if (!entryObj.TryGetValue("action", out var actionVal) || !(actionVal is JsonString actionStr)) continue;
                    if (actionStr.Value != "new_game") continue; // 最小闭环只处理"新游戏"这一种入口。

                    // GP-PRES-08 收口：text_key 缺失/为空时此前直接硬编码"开始游戏"，绕开本地化——
                    // 改用同一个已登记的 l10n.shell.template_new_game 兜底 key（data/game/l10n/
                    // l10n.text.json 已有该行），仍然经 L10n.Text 出文案，不再有任何硬编码产品
                    // 文案分支；两个分支现在统一走同一条本地化通道，唯一差别是 key 来源。
                    var textKeyStr = entryObj.TryGetValue("text_key", out var tk) && tk is JsonString tks ? tks.Value : "";
                    var textKey = string.IsNullOrEmpty(textKeyStr) ? FallbackNewGameTextKey : new Id(textKeyStr);
                    var label = Bootstrap.Presentation.L10n.Text(textKey);
                    UiWidgets.CreateButton($"Entry_{i}", list, label, OnNewGameClicked);
                }
            }
            else
            {
                // shell_menu_definition 表为空/未登记时的兜底：仍然给一个可点的按钮，保证"最小闭环
                // 不依赖任何具体数据行才能进图"这一验收前提始终成立——GP-PRES-08 收口：文案同样经
                // L10n.Text 出（同 FallbackNewGameTextKey），不再硬编码。
                UiWidgets.CreateButton("Entry_Fallback", list, Bootstrap.Presentation.L10n.Text(FallbackNewGameTextKey), OnNewGameClicked);
            }
        }

        private void OnNewGameClicked()
        {
            var ok = Bootstrap.RequestNewGame(DefaultSaveSlotId);
            if (!ok)
            {
                Debug.LogWarning("[TemplateShellUi] RequestNewGame 返回失败。");
            }
        }

        private void Update()
        {
            if (Bootstrap == null || Bootstrap.BootstrapFailed)
            {
                return;
            }

            Bootstrap.Presentation.ShellViewModel.Refresh();
            var page = Bootstrap.Presentation.Shell.Page;
            _mainMenuRoot.gameObject.SetActive(page == ShellPage.MainMenu);
        }
    }
}
