using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.InputMap;

namespace Presentation.Ui
{
    /// <summary>
    /// 参考界面面板的开关热键（P4 备忘 5，样板游戏 A 反馈）。此前面板宿主把 I/U/J/K/C/N/L 写死且无法关闭，与游戏自己的战斗键冲突。
    /// 现在热键由输入绑定数据声明：数据里有名为 <c>input.action.ui_toggle_&lt;面板&gt;</c> 的按钮动作（<see cref="ActionName"/>，如
    /// <c>input.action.ui_toggle_inventory</c>）就用该动作的当前绑定（可改键、可存设置）；数据里没声明的面板，在
    /// <see cref="LegacyEnabled"/> 为真（默认）时沿用原来的写死键，设为 false 即全部关闭。声明了动作的面板不再响应写死键，两套不叠加。
    /// 本类型只做"哪个面板该翻转"的判断，不依赖任何引擎类型；引擎侧（面板宿主）把每帧的写死键按下状态用委托传进来。
    /// </summary>
    public sealed class UiPanelHotkeys
    {
        /// <summary>面板开关动作名前缀。</summary>
        public const string ActionPrefix = "input.action.ui_toggle_";

        /// <summary>带热键的面板（与此前写死键的覆盖范围一致）。</summary>
        public static readonly IReadOnlyList<UiPanel> TogglePanels = new[]
        {
            UiPanel.Inventory, UiPanel.Equipment, UiPanel.QuestLog, UiPanel.SkillBook,
            UiPanel.CharacterStats, UiPanel.Settings, UiPanel.SaveSlots,
        };

        private readonly IInputMapHost? _inputMap;
        private readonly Dictionary<UiPanel, bool> _wasActive = new Dictionary<UiPanel, bool>();

        /// <summary>数据里没声明开关动作的面板，是否仍响应原写死键。默认 true（既有行为不变）；游戏有自己的键位时设 false。</summary>
        public bool LegacyEnabled { get; set; } = true;

        public UiPanelHotkeys(IInputMapHost? inputMap, bool legacyEnabled = true)
        {
            _inputMap = inputMap;
            LegacyEnabled = legacyEnabled;
        }

        /// <summary>面板对应的开关动作名，如 <c>UiPanel.QuestLog</c> → <c>input.action.ui_toggle_quest_log</c>。</summary>
        public static string ActionName(UiPanel panel) => ActionPrefix + SnakeCase(panel.ToString());

        /// <summary>数据里是否声明了该面板的开关动作。输入映射不支持枚举动作时一律视为未声明。</summary>
        public bool IsDeclared(UiPanel panel)
        {
            var names = _inputMap?.GetDeclaredActionNames();
            if (names == null)
            {
                return false;
            }

            var wanted = ActionName(panel);
            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], wanted, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 每帧调用一次：对每个带热键的面板，声明了动作的看动作的按下沿，没声明的（且 <see cref="LegacyEnabled"/>）问
        /// <paramref name="legacyKeyPressedThisFrame"/>；该翻转时调 <paramref name="toggle"/>。
        /// </summary>
        public void Poll(Func<UiPanel, bool> legacyKeyPressedThisFrame, Action<UiPanel> toggle)
        {
            if (legacyKeyPressedThisFrame == null) throw new ArgumentNullException(nameof(legacyKeyPressedThisFrame));
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));

            for (var i = 0; i < TogglePanels.Count; i++)
            {
                var panel = TogglePanels[i];
                if (IsDeclared(panel))
                {
                    bool active;
                    try
                    {
                        active = _inputMap!.IsActionActive(ActionName(panel));
                    }
                    catch (InvalidOperationException)
                    {
                        // 声明成了轴类动作：不是按钮，没法当开关用；按未声明处理（走写死键）。
                        active = false;
                    }

                    _wasActive.TryGetValue(panel, out var was);
                    _wasActive[panel] = active;
                    if (active && !was)
                    {
                        toggle(panel);
                    }
                }
                else if (LegacyEnabled && legacyKeyPressedThisFrame(panel))
                {
                    toggle(panel);
                }
            }
        }

        private static string SnakeCase(string pascal)
        {
            var sb = new System.Text.StringBuilder(pascal.Length + 4);
            for (var i = 0; i < pascal.Length; i++)
            {
                var c = pascal[i];
                if (char.IsUpper(c) && i > 0)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 模态面板的输入上下文（P4 备忘 8 的界面侧）：对话、商店、暂停菜单、设置、存档槽任一打开时，向输入映射压入一层上下文，把移动与战斗等
    /// 动作挡掉，只放行界面类动作（确认/取消/交互/菜单/暂停，以及数据里声明的面板开关动作）；全部关闭时弹出。每帧调用 <see cref="Sync"/> 即可，
    /// 状态没变时什么也不做。放行表在每次压入时按当时已声明的动作重新计算。
    /// </summary>
    public sealed class UiModalInputContext
    {
        /// <summary>本类型压入的上下文 id。</summary>
        public static readonly Id ContextId = new Id("input.context.ui_modal");

        /// <summary>默认放行的界面类动作名（只放行实际已声明的那些）。</summary>
        public static readonly IReadOnlyList<string> DefaultAllowedActions = new[]
        {
            "input.action.confirm", "input.action.cancel", "input.action.interact", "input.action.open_menu", "input.action.pause",
        };

        private readonly IInputMapHost? _inputMap;
        private bool _pushed;

        /// <summary>false 时本类型不压入任何上下文（游戏自己管输入上下文时用）。默认 true。</summary>
        public bool Enabled { get; set; } = true;

        public bool IsPushed => _pushed;

        public UiModalInputContext(IInputMapHost? inputMap)
        {
            _inputMap = inputMap;
        }

        public void Sync(bool anyModalOpen)
        {
            if (_inputMap == null)
            {
                return;
            }

            var want = Enabled && anyModalOpen;
            if (want && !_pushed)
            {
                _inputMap.PushInputContext(ContextId, ComputeAllowed());
                _pushed = true;
            }
            else if (!want && _pushed)
            {
                _inputMap.PopInputContext(ContextId);
                _pushed = false;
            }
        }

        private List<string> ComputeAllowed()
        {
            var allowed = new List<string>();
            var declared = _inputMap!.GetDeclaredActionNames();
            if (declared == null)
            {
                return allowed;
            }

            for (var i = 0; i < declared.Count; i++)
            {
                var name = declared[i];
                if (name.StartsWith(UiPanelHotkeys.ActionPrefix, StringComparison.Ordinal))
                {
                    allowed.Add(name);
                    continue;
                }

                for (var j = 0; j < DefaultAllowedActions.Count; j++)
                {
                    if (string.Equals(name, DefaultAllowedActions[j], StringComparison.Ordinal))
                    {
                        allowed.Add(name);
                        break;
                    }
                }
            }

            return allowed;
        }
    }
}
