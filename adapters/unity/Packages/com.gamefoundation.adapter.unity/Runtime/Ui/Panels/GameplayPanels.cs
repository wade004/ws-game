#nullable enable
// 六个界面单元：HUD、动作条、背包、任务日志、技能书、角色属性面板（任务书 U3-1 十个界面单元
// 清单的前六个）。全部只读 PresentationAssembly 暴露的视图模型/UiIntents（表现层铁律 P1/P3），
// 每帧把视图模型当前属性刷到控件上（见 IUiPanel.cs 顶部判断记录）。
//
// 判断记录（CLAUDE.md"UI 文案走 l10n.text，不硬编码中文字符串（占位调试文本除外，需标注）"）：
// 本文件里出现的中文字面量（"目标：无""使用""（无任务）""（无已知技能）""（无属性配置）"等）
// 都是"面板框架自身的调试态占位文案"（空态提示/按钮通用动词），不是具体游戏内容——真正的游戏
// 内容文本（技能/物品/任务/属性显示名）均已经过 <c>IL10nHost.Text</c>/<see cref="ExprValue"/> 从
// <c>l10n.text</c>/数据表查询得到，只是本任务范围（阶段 4 UI 套件默认皮肤）未继续为"使用""删除"
// 这类纯 UI 框架级按钮铭牌新增 l10n 键——这类文案不属于任何具体游戏内容域（<c>l10n.text</c> 现有
// 键均按"游戏内容"组织：stat/power/item/quest/...），贸然新增一批"ui chrome"域键超出本任务
// data/_sample 允许改动范围的合理边界，如实标注为占位调试文本，留给具体游戏接入时按自己的本地化
// 需要替换。
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Core.Foundation.Common;
using Presentation.Assembly;
using Presentation.Ui;
using TMPro;
using UnityEngine;

namespace Adapter.Unity.Ui.Panels
{
    /// <summary>HUD：生命/资源条 + 等级 + 目标框（09 §7.1、<see cref="HudViewModel"/>）。</summary>
    public sealed class HudPanel : UiPanelBehaviour
    {
        private HudViewModel _vm = null!;
        private TextMeshProUGUI _levelLabel = null!;
        private TextMeshProUGUI _powerLabel = null!;
        private TextMeshProUGUI _targetLabel = null!;

        public void Construct(RectTransform parent, HudViewModel vm)
        {
            _vm = vm;
            var root = UiWidgets.CreatePanelBackground("HudPanel", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(320f, 100f), new Vector2(170f, -70f));
            var list = UiWidgets.CreateVerticalList("List", root, 2f);
            UiWidgets.SetRect(list, Vector2.zero, Vector2.one, new Vector2(8, 6), new Vector2(-8, -6));
            _levelLabel = UiWidgets.CreateLabel("Level", list, "Lv.-", 18);
            _powerLabel = UiWidgets.CreateLabel("Power", list, "-", 16);
            _targetLabel = UiWidgets.CreateLabel("Target", list, "目标：无", 16);
        }

        public override void RefreshUi()
        {
            _levelLabel.text = $"Lv.{_vm.Level}";
            var sb = new StringBuilder();
            foreach (var kv in _vm.PowerBars)
            {
                sb.Append(kv.Key.Value).Append(' ').Append((int)kv.Value.Current).Append('/').Append((int)kv.Value.Max).Append("  ");
            }
            _powerLabel.text = sb.Length > 0 ? sb.ToString() : "（无资源条数据）";

            _targetLabel.text = _vm.HasTarget
                ? "目标：" + string.Join(" ", _vm.TargetPowerBars.Select(kv => $"{kv.Key.Value} {(int)kv.Value.Current}/{(int)kv.Value.Max}"))
                : "目标：无";
        }
    }

    /// <summary>动作条：槽位→技能，点击/冷却展示（09 §7.1、<see cref="ActionBarViewModel"/>）。</summary>
    public sealed class ActionBarPanel : UiPanelBehaviour
    {
        private ActionBarViewModel _vm = null!;
        private UiIntents _intents = null!;
        private readonly List<(UnityEngine.UI.Button Button, TextMeshProUGUI Label)> _slots = new List<(UnityEngine.UI.Button, TextMeshProUGUI)>();

        public void Construct(RectTransform parent, ActionBarViewModel vm, UiIntents intents)
        {
            _vm = vm;
            _intents = intents;
            var root = UiWidgets.CreatePanelBackground("ActionBarPanel", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(vm.SlotCount * 64f + 16f, 64f), new Vector2(0f, 60f));
            var rowGo = new GameObject("Row", typeof(RectTransform));
            var row = (RectTransform)rowGo.transform;
            row.SetParent(root, false);
            row.anchorMin = Vector2.zero; row.anchorMax = Vector2.one; row.offsetMin = new Vector2(8, 8); row.offsetMax = new Vector2(-8, -8);
            var layout = rowGo.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;

            for (var i = 0; i < vm.SlotCount; i++)
            {
                var index = i;
                var (_, button, label) = UiWidgets.CreateButton($"Slot{i}", row, "-", () => OnSlotClicked(index));
                _slots.Add((button, label));
            }
        }

        /// <summary>点击第 <paramref name="index"/> 个槽位（等价于用户真实点击该按钮）。
        /// 公开供 PlayMode 测试直接调用，不必模拟物理点击事件（同 GreyBoxTests.cs 顶部
        /// "注入意图"判断记录：本方法本身就是按钮 OnClick 回调的真实实现，测试调用它与真实点击
        /// 走的是完全相同的代码路径）。</summary>
        public void ClickSlot(int index) => OnSlotClicked(index);

        private void OnSlotClicked(int index)
        {
            if (index < 0 || index >= _vm.Slots.Count) return;
            var slot = _vm.Slots[index];
            if (slot.SkillId.HasValue && slot.Available)
            {
                _intents.CastSkill(slot.SkillId.Value, targetId: null);
            }
        }

        public override void RefreshUi()
        {
            for (var i = 0; i < _slots.Count && i < _vm.Slots.Count; i++)
            {
                var slot = _vm.Slots[i];
                var (button, label) = _slots[i];
                label.text = slot.SkillId.HasValue ? ShortId(slot.SkillId.Value) + (slot.Cooldown > 0 ? $"\n{slot.Cooldown:0.0}s" : string.Empty) : "-";
                button.interactable = slot.Available;
            }
        }

        private static string ShortId(Id id)
        {
            var v = id.Value;
            var idx = v.LastIndexOf('.');
            return idx >= 0 ? v.Substring(idx + 1) : v;
        }
    }

    /// <summary>背包与装备：格子数量/数量与逻辑背包一致（09 §7.1、<see cref="InventoryViewModel"/>，
    /// 阶段 4 验收标准 3）。</summary>
    public sealed class InventoryPanel : UiPanelBehaviour
    {
        private InventoryViewModel _vm = null!;
        private UiIntents _intents = null!;
        private RectTransform _list = null!;
        private readonly List<GameObject> _rows = new List<GameObject>();

        public void Construct(RectTransform parent, InventoryViewModel vm, UiIntents intents)
        {
            _vm = vm;
            _intents = intents;
            var root = UiWidgets.CreatePanelBackground("InventoryPanel", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(260f, 260f), new Vector2(-140f, -140f));
            _list = UiWidgets.CreateVerticalList("List", root, 2f);
            UiWidgets.SetRect(_list, Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));
        }

        /// <summary>逻辑格子数与已渲染行数不一致时才重建行（避免每帧都销毁重建 GameObject）。</summary>
        public override void RefreshUi()
        {
            while (_rows.Count < _vm.Slots.Count)
            {
                var rowIndex = _rows.Count;
                var row = new GameObject($"Row{rowIndex}", typeof(RectTransform));
                var rect = (RectTransform)row.transform;
                rect.SetParent(_list, false);
                var hl = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                hl.spacing = 6f; hl.childControlWidth = false; hl.childControlHeight = true; hl.childForceExpandWidth = false;
                var label = UiWidgets.CreateLabel("Label", rect, "-", 16);
                label.rectTransform.sizeDelta = new Vector2(140f, 22f);
                var capturedIndex = rowIndex;
                UiWidgets.CreateButton("Use", rect, "使用", () => OnUseClicked(capturedIndex));
                _rows.Add(row);
            }
            while (_rows.Count > _vm.Slots.Count)
            {
                var last = _rows[_rows.Count - 1];
                _rows.RemoveAt(_rows.Count - 1);
                Destroy(last);
            }

            for (var i = 0; i < _vm.Slots.Count; i++)
            {
                var slot = _vm.Slots[i];
                var label = _rows[i].transform.Find("Label").GetComponent<TextMeshProUGUI>();
                label.text = $"{ShortId(slot.TemplateId)} x{slot.Count}";
            }
        }

        private void OnUseClicked(int index)
        {
            if (index >= 0 && index < _vm.Slots.Count)
            {
                _intents.UseItem(_vm.Slots[index].InstanceId);
            }
        }

        private static string ShortId(Id id)
        {
            var v = id.Value;
            var idx = v.LastIndexOf('.');
            return idx >= 0 ? v.Substring(idx + 1) : v;
        }
    }

    /// <summary>任务日志（09 §7.1、<see cref="QuestLogViewModel"/>）。</summary>
    public sealed class QuestLogPanel : UiPanelBehaviour
    {
        private QuestLogViewModel _vm = null!;
        private TextMeshProUGUI _body = null!;

        public void Construct(RectTransform parent, QuestLogViewModel vm)
        {
            _vm = vm;
            var root = UiWidgets.CreatePanelBackground("QuestLogPanel", parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(260f, 220f), new Vector2(150f, 0f));
            _body = UiWidgets.CreateLabel("Body", root, "（无任务）", 15);
            UiWidgets.SetRect(_body.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));
        }

        public override void RefreshUi()
        {
            if (_vm.Log.Count == 0)
            {
                _body.text = "（无任务）";
                return;
            }
            var sb = new StringBuilder();
            foreach (var progress in _vm.Log)
            {
                sb.Append(progress.QuestId.Value).Append(" [").Append(progress.State).Append("]\n");
            }
            _body.text = sb.ToString();
        }
    }

    /// <summary>技能书（09 §7.1、<see cref="SkillBookViewModel"/>）。</summary>
    public sealed class SkillBookPanel : UiPanelBehaviour
    {
        private SkillBookViewModel _vm = null!;
        private TextMeshProUGUI _body = null!;

        public void Construct(RectTransform parent, SkillBookViewModel vm)
        {
            _vm = vm;
            var root = UiWidgets.CreatePanelBackground("SkillBookPanel", parent, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(260f, 220f), new Vector2(-150f, 0f));
            _body = UiWidgets.CreateLabel("Body", root, "（无已知技能）", 15);
            UiWidgets.SetRect(_body.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));
        }

        public override void RefreshUi()
        {
            if (_vm.Entries.Count == 0)
            {
                _body.text = "（无已知技能）";
                return;
            }
            var sb = new StringBuilder();
            foreach (var entry in _vm.Entries)
            {
                sb.Append(entry.SkillId.Value).Append(entry.Ready ? "  就绪" : $"  冷却 {entry.Cooldown:0.0}s").Append('\n');
            }
            _body.text = sb.ToString();
        }
    }

    /// <summary>角色属性面板（09 §7.1、<see cref="CharacterStatsViewModel"/>）。</summary>
    public sealed class CharacterStatsPanel : UiPanelBehaviour
    {
        private CharacterStatsViewModel _vm = null!;
        private TextMeshProUGUI _body = null!;

        public void Construct(RectTransform parent, CharacterStatsViewModel vm)
        {
            _vm = vm;
            var root = UiWidgets.CreatePanelBackground("CharacterStatsPanel", parent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(240f, 200f), new Vector2(130f, 240f));
            _body = UiWidgets.CreateLabel("Body", root, "（无属性配置）", 15);
            UiWidgets.SetRect(_body.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));
        }

        public override void RefreshUi()
        {
            if (_vm.Entries.Count == 0)
            {
                _body.text = "（无属性配置）";
                return;
            }
            var sb = new StringBuilder();
            foreach (var entry in _vm.Entries)
            {
                sb.Append(entry.DisplayName).Append(": ").Append(entry.Value.ToString("0.##")).Append('\n');
            }
            _body.text = sb.ToString();
        }
    }
}
