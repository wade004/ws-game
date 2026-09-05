#nullable enable
// 存档槽选择、暂停菜单（十个界面单元的第 9、10 个）。两者均"同一套 UI 框架供游戏内面板与 Shell
// 复用"（09 §9），本文件的两个类型因此既被 Runtime/Ui/UiPanelHost（游戏内热键面板）使用，也被
// Runtime/Shell/ShellRoot（主菜单存档槽页/暂停覆盖层）复用，构造期以回调形式注入具体动作，
// 不在类型内部区分"是游戏内面板还是 Shell 页面"。
//
// 判断记录（占位调试文本标注）：同 GameplayPanels.cs 顶部判断记录——"读取""新建""覆盖""删除"
// "（空）""暂停"均为 UI 框架级 chrome 文案，不经 l10n.text；<see cref="PauseMenuPanel"/> 的选项
// 铭牌（"继续""设置""回主菜单""退出"）经 <c>l10n.Text(option.TextKey)</c> 真正查询
// <c>l10n.pause.sample_*</c>（本次已补录进 data/_sample/l10n/l10n.text.json，见任务汇报）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SaveSystem;
using Presentation.Ui;
using TMPro;
using UnityEngine;

namespace Adapter.Unity.Ui.Panels
{
    /// <summary>存档槽选择：新建/覆盖/删除/读取意图（09 §9、<see cref="SaveSlotsViewModel"/>，
    /// 10_存档与持久化.md meta 段摘要展示）。</summary>
    public sealed class SaveSlotsPanel : UiPanelBehaviour
    {
        private SaveSlotsViewModel _vm = null!;
        private IReadOnlyList<Id> _candidateSlotIds = Array.Empty<Id>();
        private Action<Id> _onLoad = null!;
        private Action<Id> _onSaveOrOverwrite = null!;
        private Action<Id> _onDelete = null!;
        private RectTransform _list = null!;
        private readonly List<(GameObject Row, TextMeshProUGUI Summary, UnityEngine.UI.Button LoadButton, UnityEngine.UI.Button SaveButton, UnityEngine.UI.Button DeleteButton)> _rows =
            new List<(GameObject, TextMeshProUGUI, UnityEngine.UI.Button, UnityEngine.UI.Button, UnityEngine.UI.Button)>();

        public void Construct(
            RectTransform parent,
            SaveSlotsViewModel vm,
            IReadOnlyList<Id> candidateSlotIds,
            Action<Id> onLoad,
            Action<Id> onSaveOrOverwrite,
            Action<Id> onDelete)
        {
            _vm = vm;
            _candidateSlotIds = candidateSlotIds;
            _onLoad = onLoad;
            _onSaveOrOverwrite = onSaveOrOverwrite;
            _onDelete = onDelete;

            var root = UiWidgets.CreatePanelBackground("SaveSlotsPanel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(420f, 260f), Vector2.zero);
            _list = UiWidgets.CreateVerticalList("List", root, 6f);
            UiWidgets.SetRect(_list, Vector2.zero, Vector2.one, new Vector2(10, 10), new Vector2(-10, -10));

            foreach (var slotId in _candidateSlotIds)
            {
                var row = new GameObject($"Row_{slotId.Value}", typeof(RectTransform));
                var rect = (RectTransform)row.transform;
                rect.SetParent(_list, false);
                rect.sizeDelta = new Vector2(0, 30);
                var hl = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                hl.spacing = 6f; hl.childControlWidth = false; hl.childControlHeight = true;

                var summary = UiWidgets.CreateLabel("Summary", rect, slotId.Value, 14);
                summary.rectTransform.sizeDelta = new Vector2(180, 26);

                var capturedId = slotId;
                var (_, loadBtn, _) = UiWidgets.CreateButton("Load", rect, "读取", () => _onLoad(capturedId));
                var (_, saveBtn, saveLabel) = UiWidgets.CreateButton("Save", rect, "新建", () => _onSaveOrOverwrite(capturedId));
                var (_, delBtn, _) = UiWidgets.CreateButton("Delete", rect, "删除", () => _onDelete(capturedId));

                _rows.Add((row, summary, loadBtn, saveBtn, delBtn));
            }
        }

        public override void RefreshUi()
        {
            var slots = _vm.Slots;
            for (var i = 0; i < _rows.Count; i++)
            {
                var slotId = _candidateSlotIds[i];
                SaveSlotInfo? info = null;
                foreach (var s in slots)
                {
                    if (s.SlotId.Equals(slotId)) { info = s; break; }
                }

                var (row, summary, loadBtn, saveBtn, _) = _rows[i];
                var exists = info != null;
                summary.text = exists
                    ? $"{slotId.Value}\n{info!.Meta.UpdatedAt}"
                    : $"{slotId.Value}\n（空）";
                loadBtn.interactable = exists;
                saveBtn.transform.Find("Label").GetComponent<TextMeshProUGUI>().text = exists ? "覆盖" : "新建";
                _ = row;
            }
        }
    }

    /// <summary>暂停菜单：继续/设置/回主菜单/退出（03 §2 Pause 主状态、09 §7.1，
    /// <see cref="PauseMenuViewModel"/>）。选项内容固定于构造期（来自
    /// <see cref="Presentation.Assembly.PresentationAssemblyOptions.PauseMenuOptions"/>），本类型
    /// 不在 RefreshUi 里重建按钮列表。</summary>
    public sealed class PauseMenuPanel : UiPanelBehaviour
    {
        private Core.Foundation.Localization.IL10nHost _l10n = null!;

        public void Construct(RectTransform parent, PauseMenuViewModel vm, Core.Foundation.Localization.IL10nHost l10n, Action<Id> onOptionClicked)
        {
            _l10n = l10n;
            var root = UiWidgets.CreatePanelBackground("PauseMenuPanel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(280f, vm.Options.Count * 44f + 60f), Vector2.zero);
            var list = UiWidgets.CreateVerticalList("List", root, 6f);
            UiWidgets.SetRect(list, Vector2.zero, Vector2.one, new Vector2(12, 12), new Vector2(-12, -12));
            UiWidgets.CreateLabel("Title", list, "暂停", 20);
            foreach (var option in vm.Options)
            {
                var capturedId = option.OptionId;
                UiWidgets.CreateButton($"Option_{option.OptionId.Value}", list, l10n.Text(option.TextKey), () => onOptionClicked(capturedId));
            }
        }

        public override void RefreshUi()
        {
            // 选项文案在 Construct 时已按当前语言渲染一次；本套件把"切换语言后暂停菜单文案跟随
            // 刷新"列为可选加强项（不在阶段 4 验收范围内），如实记录，不在此处重建按钮。
        }
    }
}
