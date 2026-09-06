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
using Core.Foundation.InputMap;
using Core.Gameplay.Assembly;
using Presentation.Assembly;
using Presentation.Ui;
using TMPro;
using UnityEngine;

namespace Adapter.Unity.Ui.Panels
{
    /// <summary>
    /// HUD：生命/资源条 + 等级 + 目标框（09 §7.1、<see cref="HudViewModel"/>），并入技术债 17 收口
    /// 后的回合状态展示（回合顺序条/行动点显示的最小实现——当前行动者与轮次文本——与"结束回合"
    /// 意图按钮，09 §7.1 离散模式三个单元）。
    /// <para>
    /// 判断记录（原 <c>TurnStatusPanel</c> 迁入本类型，不新增第十一个 <see cref="UiPanel"/> 枚举值）：
    /// 见 <see cref="HudViewModel"/> 类型注释"技术债 17 收口"一节——回合状态此前由不经
    /// <see cref="Adapter.Unity.Ui.UiPanelHost"/> 登记的独立引擎侧面板 <c>TurnStatusPanel</c> 承载，
    /// 现随视图模型一起并入 <see cref="UiPanel.Hud"/>，本类型因此额外持有 <see cref="UiIntents"/>
    /// （转发"结束回合"意图）与可选的 <see cref="IInputMapHost"/>（键盘/手柄绑定，见
    /// <see cref="Update"/>），构造签名与既有 <c>Construct(parent, vm)</c> 调用方不兼容——这是一次
    /// 蓄意的破坏性签名变更（原类型已删除，不保留过渡重载），仅有的调用方
    /// （<c>UiPanelHost.Initialize</c>、PlayMode 测试）随本次改动一并更新。
    /// </para>
    /// </summary>
    public sealed class HudPanel : UiPanelBehaviour
    {
        /// <summary>H4 新增：结束回合的键盘/手柄输入动作 id（原 <c>TurnStatusPanel</c> 常量，见
        /// <c>data/_framework/found/found.input_action.json</c> 的 <c>input.action.end_turn</c> 行，
        /// 默认绑定 key:t / pad:select）。</summary>
        private const string EndTurnActionName = "input.action.end_turn";

        private HudViewModel _vm = null!;
        private UiIntents _intents = null!;
        private IInputMapHost? _inputMap;
        private bool _endTurnActionWasActive;
        private TextMeshProUGUI _levelLabel = null!;
        private TextMeshProUGUI _powerLabel = null!;
        private TextMeshProUGUI _targetLabel = null!;
        private TextMeshProUGUI _turnInfoLabel = null!;

        /// <summary>GP-PRES-09 收口新增（09 §7.1"回合顺序条（仅离散模式）""行动点显示（仅离散
        /// 模式且启用 action_points 先攻策略）"）：此前本面板只展示当前行动者与轮次
        /// （<see cref="_turnInfoLabel"/>），完整队列与行动点完全没有落地——见
        /// architecture/落地计划/audit-20260907/gameplay-presentation.md GP-PRES-09。</summary>
        private TextMeshProUGUI _turnOrderLabel = null!;

        private TextMeshProUGUI _actionPointsLabel = null!;
        private UnityEngine.UI.Button _endTurnButton = null!;

        /// <summary>
        /// <paramref name="intents"/>：新增，"结束回合"按钮点击转发（原 <c>TurnStatusPanel.Construct</c>
        /// 同名参数）。<paramref name="inputMap"/>：可选（默认 null，同原 <c>TurnStatusPanel</c>
        /// H4 判断记录）——非空时本面板在 <see cref="HudViewModel.CanEndTurn"/> 为真时额外监听
        /// <c>input.action.end_turn</c> 的按下沿，触发时与点击"结束回合"按钮完全等价。
        /// </summary>
        public void Construct(RectTransform parent, HudViewModel vm, UiIntents intents, IInputMapHost? inputMap = null)
        {
            _vm = vm;
            _intents = intents;
            _inputMap = inputMap;
            var root = UiWidgets.CreatePanelBackground("HudPanel", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(320f, 140f), new Vector2(170f, -70f));
            var list = UiWidgets.CreateVerticalList("List", root, 2f);
            UiWidgets.SetRect(list, Vector2.zero, Vector2.one, new Vector2(8, 6), new Vector2(-8, -6));
            _levelLabel = UiWidgets.CreateLabel("Level", list, "Lv.-", 18);
            _powerLabel = UiWidgets.CreateLabel("Power", list, "-", 16);
            _targetLabel = UiWidgets.CreateLabel("Target", list, "目标：无", 16);
            _turnInfoLabel = UiWidgets.CreateLabel("TurnInfo", list, "（未启用回合制）", 16);
            _turnOrderLabel = UiWidgets.CreateLabel("TurnOrder", list, "", 14);
            _actionPointsLabel = UiWidgets.CreateLabel("ActionPoints", list, "", 14);
            var (_, button, _) = UiWidgets.CreateButton("EndTurnButton", list, "结束回合", OnEndTurnClicked);
            _endTurnButton = button;

            RefreshUi();
        }

        /// <summary>供 PlayMode 测试直接调用（等价于用户真实点击"结束回合"按钮），同
        /// <see cref="ActionBarPanel.ClickSlot"/> 惯例；原 <c>TurnStatusPanel.ClickEndTurn</c>。</summary>
        public void ClickEndTurn() => OnEndTurnClicked();

        /// <summary>供 PlayMode 测试直接读取当前"结束回合"按钮的可见性；原
        /// <c>TurnStatusPanel.IsEndTurnButtonVisible</c>，惯例不变（读 <c>activeSelf</c>，不依赖
        /// <c>Transform.Find</c>）。</summary>
        public bool IsEndTurnButtonVisible => _endTurnButton.gameObject.activeSelf;

        /// <summary>供 PlayMode 测试读取当前展示的回合信息文本；原 <c>TurnStatusPanel.TurnInfoText</c>。</summary>
        public string TurnInfoText => _turnInfoLabel.text;

        /// <summary>GP-PRES-09 收口新增：供 PlayMode 测试读取当前展示的回合顺序条文本。</summary>
        public string TurnOrderText => _turnOrderLabel.text;

        /// <summary>GP-PRES-09 收口新增：供 PlayMode 测试读取当前展示的行动点文本。</summary>
        public string ActionPointsText => _actionPointsLabel.text;

        private void OnEndTurnClicked() => _intents.EndTurn();

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

            if (!_vm.IsTurnBased)
            {
                _turnInfoLabel.text = "（未启用回合制）";
                _turnOrderLabel.text = "";
                _actionPointsLabel.text = "";
                _endTurnButton.gameObject.SetActive(false);
                return;
            }

            _turnInfoLabel.text = _vm.CurrentActorId.HasValue
                ? $"行动者：{ShortId(_vm.CurrentActorId.Value)}  轮次：{_vm.RoundIndex}"
                : "（不在战斗中）";

            // GP-PRES-09 收口：回合顺序条——完整队列，按 HudViewModel.TurnOrder（转发
            // TurnScheduler.GetOrder，稳定排序）原样展示，当前行动者用方括号标出，不额外重排。
            _turnOrderLabel.text = _vm.TurnOrder.Count > 0
                ? "顺序：" + string.Join(" ", _vm.TurnOrder.Select(id =>
                    _vm.CurrentActorId.HasValue && id.Equals(_vm.CurrentActorId.Value) ? $"[{ShortId(id)}]" : ShortId(id)))
                : "";

            // 行动点显示：09 §7.1 文档字面只要求 action_points 先攻策略下展示，但
            // HudViewModel.ActionPointsRemaining 在全部策略下都有意义（账本无条件维护，见该属性
            // 判断记录）——本面板选择"有当前行动者就展示"，不按策略过滤，更简单也不算过度展示
            // （多显示一个数字不违反 09 铁律，09 只规定"至少要展示什么"，不禁止展示更多只读信息）。
            _actionPointsLabel.text = _vm.CurrentActorId.HasValue
                ? $"行动点：{_vm.ActionPointsRemaining}"
                : "";

            _endTurnButton.gameObject.SetActive(_vm.CanEndTurn);
        }

        /// <summary>
        /// 判断记录（键盘轮询不重复调用 <see cref="RefreshUi"/>）：本面板注册在
        /// <see cref="Adapter.Unity.Ui.UiPanelHost"/> 下时，<c>UiPanelHost.Update</c> 已经对"当前
        /// 处于打开状态"的面板逐帧调用一次 <see cref="RefreshUi"/>（见 <c>IUiPanel.cs</c> 顶部判断
        /// 记录），本 <see cref="Update"/> 因此只做键盘轮询，不重复刷新视觉；<see cref="SkillBookPanel"/>
        /// 自带 <c>Update</c> 处理数字键绑定是同一惯例的既有先例。可见性判断读
        /// <see cref="HudViewModel.CanEndTurn"/>（视图模型自身经事件订阅维持的最新值，见该类型
        /// 判断记录），不读 <see cref="IsEndTurnButtonVisible"/>（按钮 <c>activeSelf</c>）——本面板
        /// 未必总是注册在 <see cref="Adapter.Unity.Ui.UiPanelHost"/> 下（PlayMode 测试常见"独立
        /// GameObject 不挂进任何 UiPanelHost"的用法，见 <c>DiscreteCombatTests</c>/
        /// <c>SharedBootstrapDiscreteTests</c>），此时没有任何人每帧调用 <see cref="RefreshUi"/>，
        /// 若可见性判断依赖 <c>activeSelf</c>（只在 <see cref="RefreshUi"/> 被调用时才更新），键盘
        /// 绑定会在这类场景下失效；直接读视图模型属性不依赖 <see cref="RefreshUi"/> 是否被调用过。
        /// </summary>
        private void Update()
        {
            HandleEndTurnKeybinding();
        }

        /// <summary>H4 新增：结束回合按钮可见（<see cref="HudViewModel.CanEndTurn"/>）时，
        /// <c>input.action.end_turn</c> 出现按下沿即等价于点击按钮；不可见（不是轮到玩家/不在回合制
        /// 中）时不响应，且清空"上一帧是否激活"状态——避免"按下沿恰好跨越可见性切换那一帧"误触发，
        /// 也避免玩家长按期间恰好轮到自己时立即被上一次残留的"已激活"状态误判为一次新按下。原
        /// <c>TurnStatusPanel.HandleEndTurnKeybinding</c>，逻辑原样搬入。</summary>
        private void HandleEndTurnKeybinding()
        {
            if (_inputMap == null)
            {
                return;
            }

            if (!_vm.CanEndTurn)
            {
                _endTurnActionWasActive = false;
                return;
            }

            var active = _inputMap.IsActionActive(EndTurnActionName);
            var wasActive = _endTurnActionWasActive;
            _endTurnActionWasActive = active;
            if (active && !wasActive)
            {
                OnEndTurnClicked();
            }
        }

        private static string ShortId(Id id)
        {
            var v = id.Value;
            var idx = v.LastIndexOf('.');
            return idx >= 0 ? v.Substring(idx + 1) : v;
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
