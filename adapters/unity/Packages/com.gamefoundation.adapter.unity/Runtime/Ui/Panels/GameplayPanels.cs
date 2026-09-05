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
using Core.Gameplay.Assembly;
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

    /// <summary>
    /// 技能书（09 §7.1、<see cref="SkillBookViewModel"/>）。
    /// <para>
    /// 缺口 4：技能书条目提供"绑定到动作条槽位 N"操作——按任务书"最简：点击技能书条目后按数字键
    /// 即绑定"实现：每条已知技能是一个可点击按钮，点击后记为"已选中"（<see cref="_selectedSkillId"/>，
    /// 标签前缀 <c>"> "</c> 高亮）；面板显示期间按数字键 0-9（<c>KeyCode.Alpha0</c>..
    /// <c>Alpha9</c>）经 <see cref="UiIntents.BindActionBarSlot"/> 把已选中技能绑定到对应槽位号
    /// （<see cref="Presentation.Ui.ActionBarViewModel.SlotKey"/> 换算槽位键，与动作条面板同一套
    /// 槽位编号）。绑定被拒绝（技能不是玩家已知技能——理论上不会发生，本面板只列已知技能）时
    /// <see cref="UiIntents.BindActionBarSlot"/> 静默返回 false，不弹任何提示（同其它面板"意图被
    /// 拒绝时不崩溃"的一贯处理，见 <see cref="InventoryPanel"/> 同款判断记录）。
    /// </para>
    /// </summary>
    public sealed class SkillBookPanel : UiPanelBehaviour
    {
        private SkillBookViewModel _vm = null!;
        private UiIntents _intents = null!;
        private RectTransform _list = null!;
        private TextMeshProUGUI _empty = null!;
        private readonly List<(UnityEngine.UI.Button Button, TextMeshProUGUI Label, Id SkillId, string BaseText)> _rows =
            new List<(UnityEngine.UI.Button, TextMeshProUGUI, Id, string)>();
        private Id? _selectedSkillId;

        public void Construct(RectTransform parent, SkillBookViewModel vm, UiIntents intents)
        {
            _vm = vm;
            _intents = intents;
            var root = UiWidgets.CreatePanelBackground("SkillBookPanel", parent, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(260f, 220f), new Vector2(-150f, 0f));

            _empty = UiWidgets.CreateLabel("Empty", root, "（无已知技能）", 15);
            UiWidgets.SetRect(_empty.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));

            var listGo = new GameObject("List", typeof(RectTransform));
            _list = (RectTransform)listGo.transform;
            _list.SetParent(root, false);
            UiWidgets.SetRect(_list, Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));
            var layout = listGo.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        }

        public override void RefreshUi()
        {
            // 判断记录：条目数量/内容可能随 known_skills 变化（学习新技能），每次刷新整体重建按钮
            // 列表——本面板不是每帧刷新的高频路径（RefreshUi 只在相关事件触发时调用，见
            // IUiPanel.cs 判断记录），重建成本可接受，避免维护一份"增量 diff 按钮列表"的额外复杂度。
            foreach (var row in _rows)
            {
                Destroy(row.Button.gameObject);
            }
            _rows.Clear();

            _empty.gameObject.SetActive(_vm.Entries.Count == 0);

            foreach (var entry in _vm.Entries)
            {
                var skillId = entry.SkillId;
                var baseText = entry.SkillId.Value + (entry.Ready ? "  就绪" : $"  冷却 {entry.Cooldown:0.0}s");
                var (_, button, label) = UiWidgets.CreateButton($"Entry_{skillId.Value}", _list, baseText, () => OnEntryClicked(skillId));
                _rows.Add((button, label, skillId, baseText));
            }

            if (_selectedSkillId.HasValue && !_vm.Entries.Any(e => e.SkillId.Equals(_selectedSkillId.Value)))
            {
                _selectedSkillId = null;
            }

            UpdateSelectionHighlight();
        }

        private void OnEntryClicked(Id skillId)
        {
            _selectedSkillId = skillId;
            UpdateSelectionHighlight();
        }

        private void UpdateSelectionHighlight()
        {
            foreach (var row in _rows)
            {
                row.Label.text = (row.SkillId.Equals(_selectedSkillId) ? "> " : "") + row.BaseText;
            }
        }

        private void Update()
        {
            if (!_selectedSkillId.HasValue)
            {
                return;
            }

            for (var slot = 0; slot <= 9; slot++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + slot))
                {
                    _intents.BindActionBarSlot(slot, _selectedSkillId.Value);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 回合状态 HUD（ADR-0013 离散时间模型引擎侧接线，03 第 2/3 节）：显示当前行动者与轮次，
    /// <c>awaiting_input</c> 子态下显示"结束回合"按钮。
    /// <para>
    /// 判断记录（不走 <see cref="UiPanel"/>/<see cref="UiPanelHost"/> 登记表这条既有路径）：
    /// <see cref="UiPanel"/> 是文档明确拍板的"十个值，与十个视图模型一一对应"的封闭枚举（见该
    /// 类型注释"保证……严格对齐，不产生……孤儿"），"状态栏"这类零散元素按既有判断记录应"并入
    /// Hud"而不是新增第十一个枚举值——但 <see cref="HudViewModel"/> 属于 <c>presentation/ui</c>，
    /// 不认识 <c>Core.Foundation.SimLoop.TurnScheduler</c>（回合制是 ADR-0013 新增的 L0 概念，
    /// 未接入既有十个视图模型的任何一个）。改 <c>presentation/ui</c> 给 <c>HudViewModel</c>
    /// 追加回合字段不属于"引擎侧接线被阻断时的最小改动"（20 行以内可以纯读 <see cref="GameplayAssembly"/>
    /// 公开成员在引擎侧解决，不构成阻断）。本面板因此是engine侧独立元素：直接持有
    /// <see cref="GameplayAssembly"/>/<see cref="UiIntents"/> 引用只读展示/转发意图，不经
    /// <see cref="UiPanel"/> 登记、不进 <see cref="UiPanelHost"/> 的面板字典，由自己的
    /// <see cref="Update"/> 每帧刷新（同 <see cref="SkillBookPanel"/> 自带 <c>Update</c> 处理数字键
    /// 绑定的既有先例：本包面板不是所有交互都必须经 <c>UiPanelHost.Update</c> 统一驱动）。
    /// </para>
    /// </summary>
    public sealed class TurnStatusPanel : UiPanelBehaviour
    {
        private GameplayAssembly _gameplay = null!;
        private UiIntents _intents = null!;
        private TextMeshProUGUI _label = null!;
        private UnityEngine.UI.Button _endTurnButton = null!;

        public void Construct(RectTransform parent, GameplayAssembly gameplay, UiIntents intents)
        {
            _gameplay = gameplay;
            _intents = intents;

            var root = UiWidgets.CreatePanelBackground("TurnStatusPanel", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(300f, 76f), new Vector2(0f, -40f));
            var list = UiWidgets.CreateVerticalList("List", root, 4f);
            UiWidgets.SetRect(list, Vector2.zero, Vector2.one, new Vector2(8, 6), new Vector2(-8, -6));
            _label = UiWidgets.CreateLabel("TurnInfo", list, "（未启用回合制）", 16, TextAlignmentOptions.Center);
            var (_, button, _) = UiWidgets.CreateButton("EndTurnButton", list, "结束回合", OnEndTurnClicked);
            _endTurnButton = button;

            RefreshUi();
        }

        /// <summary>供 PlayMode 测试直接调用（等价于用户真实点击"结束回合"按钮），同
        /// <see cref="ActionBarPanel.ClickSlot"/> 惯例。</summary>
        public void ClickEndTurn() => OnEndTurnClicked();

        /// <summary>供 PlayMode 测试直接读取当前"结束回合"按钮的可见性，不依赖
        /// <c>Transform.Find</c> 按路径遍历——<see cref="Construct"/> 建出的可视化层级挂在传入的
        /// <c>parent</c> 之下，不是本 MonoBehaviour 自己的 <c>gameObject</c> 子节点，同
        /// <see cref="HudPanel"/> 等既有面板同一惯例。</summary>
        public bool IsEndTurnButtonVisible => _endTurnButton.gameObject.activeSelf;

        /// <summary>供 PlayMode 测试读取当前展示的回合信息文本。</summary>
        public string TurnInfoText => _label.text;

        private void OnEndTurnClicked() => _intents.EndTurn();

        public override void RefreshUi()
        {
            var scheduler = _gameplay.TurnScheduler;
            if (scheduler == null)
            {
                _label.text = "（未启用回合制）";
                _endTurnButton.gameObject.SetActive(false);
                return;
            }

            var currentActor = scheduler.GetCurrentActor();
            _label.text = currentActor.HasValue
                ? $"行动者：{ShortId(currentActor.Value)}  轮次：{scheduler.RoundIndex}"
                : "（不在战斗中）";

            var awaitingInput = _gameplay.AppState.CurrentSubState.HasValue &&
                _gameplay.AppState.CurrentSubState.Value.Equals(_gameplay.AwaitingInputSubState);
            _endTurnButton.gameObject.SetActive(awaitingInput);
        }

        private void Update() => RefreshUi();

        private static string ShortId(Id id)
        {
            var v = id.Value;
            var idx = v.LastIndexOf('.');
            return idx >= 0 ? v.Substring(idx + 1) : v;
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
