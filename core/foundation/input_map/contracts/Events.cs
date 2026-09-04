using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.InputMap
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json 的 <c>input.action_triggered</c>、
    /// <c>input.rebind_conflict</c> 两行）。与 hook_registry 的 <c>HookEventKeys</c>、sim_loop
    /// 的 <c>SimEventKeys</c> 同一惯例：模块自持一份发出事件的 key 常量，不依赖 event_bus 的
    /// 生成物 <c>EventKeys.g.cs</c>。</summary>
    public static class InputMapEventKeys
    {
        public static readonly Id ActionTriggered = new Id("input.action_triggered");
        public static readonly Id RebindConflict = new Id("input.rebind_conflict");
    }

    /// <summary>
    /// 物理输入经 InputMap 转译出的按下型动作被触发时发出（按钮"按下沿"，见 03 第 7 节、
    /// 01 模块表 <c>input_map</c> 行、<see cref="IInputMapHost.Update"/>）。由
    /// <see cref="IEventBus.Enqueue"/> 入队（tick 内产生，非立即派发）。
    /// </summary>
    public sealed class InputActionTriggeredEvent : IEvent
    {
        public Id Key => InputMapEventKeys.ActionTriggered;

        /// <summary>被触发的动作名（<c>ActionDefinition.ActionId.Value</c>），与
        /// <c>found.event_catalog.json</c> 登记的字段 <c>actionName</c> 对应。</summary>
        public string ActionName { get; }

        public InputActionTriggeredEvent(string actionName)
        {
            ActionName = actionName;
        }
    }

    /// <summary>
    /// <see cref="IInputMapHost.Rebind"/> 检测到同一重绑分组内的绑定冲突时发出（见 03 第 7 节
    /// "冲突检测"）。用 <see cref="IEventBus.PublishImmediate"/> 立即派发（重绑定是玩家在设置界面
    /// 发起的一次性交互操作，不是 tick 内产生的高频事件，不必走 <see cref="IEventBus.Enqueue"/> 排队）。
    /// </summary>
    public sealed class InputRebindConflictEvent : IEvent
    {
        public Id Key => InputMapEventKeys.RebindConflict;

        /// <summary>发起重绑定、检测到冲突的动作名。</summary>
        public string ActionName { get; }

        /// <summary>发生冲突的绑定字符串。</summary>
        public string Binding { get; }

        public InputRebindConflictEvent(string actionName, string binding)
        {
            ActionName = actionName;
            Binding = binding;
        }
    }
}
