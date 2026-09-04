using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;

namespace Presentation.Ui
{
    /// <summary>
    /// UI 面板开关状态登记表（见任务书"UiPanelRegistry：面板开关状态（Open(panelId)/Close/
    /// IsOpen；打开 MenuOverlay 类面板时经 UiIntents.OpenMenu）"）。本类型只是一个"哪些面板当前
    /// 打开着"的登记簿，不做任何布局/绘制决策（那是引擎适配层 UI 承载面的职责）；面板 id 即
    /// <c>ui_layout_definition.id</c>（见 <c>schema/UiLayoutSchema.cs</c>）。
    /// <para>
    /// 判断记录（MenuOverlay 联动）：构造期传入的 <paramref name="menuOverlayPanelIds"/>
    /// 标出"属于 InWorld MenuOverlay 子状态"的那部分面板（如背包、任务日志、角色属性、设置——
    /// 玩家在探索/战斗中途打开会叠加在世界之上的面板），与 HUD/动作条/对话框（各自有自己的常驻
    /// 展示或 Dialog 子状态，不走 MenuOverlay）区分开。多个 MenuOverlay 类面板可以同时打开（例如
    /// 背包和角色属性一起开），本类型按"当前打开的 MenuOverlay 类面板数量"做 0→1/1→0 边沿检测，
    /// 只在真正"从没有到有"/"从有到没有"时才调用 <see cref="UiIntents.OpenMenu"/>/
    /// <see cref="UiIntents.CloseMenu"/> 一次，避免每次开关面板都重复 Push/Pop 子状态栈。
    /// </para>
    /// </summary>
    public sealed class UiPanelRegistry
    {
        private readonly UiIntents _intents;
        private readonly HashSet<Id> _menuOverlayPanelIds;
        private readonly HashSet<Id> _openPanels = new HashSet<Id>();

        public UiPanelRegistry(UiIntents intents, IEnumerable<Id> menuOverlayPanelIds)
        {
            _intents = intents ?? throw new ArgumentNullException(nameof(intents));
            if (menuOverlayPanelIds == null) throw new ArgumentNullException(nameof(menuOverlayPanelIds));
            _menuOverlayPanelIds = new HashSet<Id>(menuOverlayPanelIds);
        }

        public bool IsOpen(Id panelId) => _openPanels.Contains(panelId);

        public IReadOnlyCollection<Id> OpenPanels => _openPanels;

        /// <summary>打开一个面板；已打开时幂等（不重复调用 OpenMenu）。</summary>
        public void Open(Id panelId)
        {
            if (!_openPanels.Add(panelId))
            {
                return;
            }

            if (_menuOverlayPanelIds.Contains(panelId) && CountOpenMenuOverlayPanels() == 1)
            {
                _intents.OpenMenu();
            }
        }

        /// <summary>关闭一个面板；本就未打开时幂等。</summary>
        public void Close(Id panelId)
        {
            if (!_openPanels.Remove(panelId))
            {
                return;
            }

            if (_menuOverlayPanelIds.Contains(panelId) && CountOpenMenuOverlayPanels() == 0)
            {
                _intents.CloseMenu();
            }
        }

        private int CountOpenMenuOverlayPanels() => _openPanels.Count(p => _menuOverlayPanelIds.Contains(p));
    }
}
