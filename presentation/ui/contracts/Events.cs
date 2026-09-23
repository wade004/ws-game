using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Presentation.Ui
{
    /// <summary>
    /// ADR-0077：UI 核心层（<c>presentation/ui</c>）发出的事件 key 常量（对应
    /// <c>found.event_catalog</c> 登记表，见 <c>data/_framework/found/found.event_catalog.json</c>
    /// 的 <c>ui.panel_opened</c>/<c>ui.panel_closed</c>/<c>ui.action_invoked</c> 三行）。惯例同
    /// <c>core/foundation/input_map</c> 的 <c>InputMapEventKeys</c>：模块自持一份发出事件的 key
    /// 常量，不依赖 event_bus 的生成物 <c>EventKeys.g.cs</c>。
    /// <para>
    /// 判断记录（这三个事件为什么不违反 09 第 1 节铁律 P2"表现层只订阅"）：见
    /// <see cref="UiPanelRegistry"/>/<see cref="UiIntents"/> 类型注释与
    /// <c>architecture/adr/0077-ui交互域事件.md</c>"决策"一节——本模块是引擎无关的 UI 核心层，这三
    /// 个事件是"UI 交互本身"的转译（语义类比 <c>Core.Foundation.InputMap</c> 把物理输入转译为
    /// <c>input.action_triggered</c>，只是转译层级从物理输入提高到 UI 意图），不携带、也不允许携带
    /// 任何会被 L0～L4 解释为"逻辑判定"的数据，不改变任何逻辑状态、不被规则层订阅，09 第 1 节 P2 已
    /// 同步收窄措辞为"表现层被允许发出的事件限定在一个明确枚举的小集合内"。
    /// </para>
    /// </summary>
    public static class UiEventKeys
    {
        public static readonly Id PanelOpened = new Id("ui.panel_opened");
        public static readonly Id PanelClosed = new Id("ui.panel_closed");
        public static readonly Id ActionInvoked = new Id("ui.action_invoked");
    }

    /// <summary>
    /// <see cref="UiPanelRegistry.Open"/> 把一个此前未打开的面板登记为打开时发出（见该方法判断记录
    /// "0→1 边沿检测"）；已打开的面板再次 <c>Open</c> 不重复触发。用
    /// <see cref="IEventBus.PublishImmediate"/> 立即派发（面板开关是玩家一次性交互操作，同
    /// <c>InputRebindConflictEvent</c> 一贯惯例，不必走 <see cref="IEventBus.Enqueue"/> 排队）。
    /// </summary>
    public sealed class UiPanelOpenedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => UiEventKeys.PanelOpened;

        /// <summary>被打开的面板 id，即 <c>ui_layout_definition.id</c>（见
        /// <see cref="UiPanelRegistry"/> 类型注释）。</summary>
        public Id PanelId { get; }

        public UiPanelOpenedEvent(Id panelId)
        {
            PanelId = panelId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "panelId": value = ExprValue.OfId(PanelId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>
    /// <see cref="UiPanelRegistry.Close"/> 把一个此前处于打开状态的面板登记为关闭时发出（见该方法
    /// 判断记录"1→0 边沿检测"）；未打开的面板再次 <c>Close</c> 不重复触发。同
    /// <see cref="UiPanelOpenedEvent"/> 一样用 <see cref="IEventBus.PublishImmediate"/> 立即派发。
    /// </summary>
    public sealed class UiPanelClosedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => UiEventKeys.PanelClosed;

        public Id PanelId { get; }

        public UiPanelClosedEvent(Id panelId)
        {
            PanelId = panelId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "panelId": value = ExprValue.OfId(PanelId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>
    /// <see cref="UiIntents"/> 携带 <c>panelId</c> 参数的意图方法重载被调用时发出（见该类型
    /// "判断记录（ui.action_invoked）"）。用 <see cref="IEventBus.PublishImmediate"/> 立即派发，
    /// 同 <see cref="UiPanelOpenedEvent"/> 一贯惯例。
    /// </summary>
    public sealed class UiActionInvokedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => UiEventKeys.ActionInvoked;

        /// <summary>发起本次意图调用的面板 id，即 <c>ui_layout_definition.id</c>，由调用方传入
        /// （<see cref="UiIntents"/> 本身不持有任何面板数据，见其类型注释）。</summary>
        public Id PanelId { get; }

        /// <summary>框架自持的 UI 意图词汇（如 <c>"cast_skill"</c>/<c>"equip"</c>/<c>"buy"</c>），
        /// 对应 <see cref="UiIntents"/> 具体调用的方法本身，不是具体游戏的按钮 id（见
        /// <c>architecture/adr/0077-ui交互域事件.md</c>"决策"一节"不采纳 buttonId"判断记录）。</summary>
        public string ActionName { get; }

        public UiActionInvokedEvent(Id panelId, string actionName)
        {
            PanelId = panelId;
            ActionName = actionName;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "panelId": value = ExprValue.OfId(PanelId); return true;
                case "actionName": value = ExprValue.OfString(ActionName); return true;
                default: value = default; return false;
            }
        }
    }
}
