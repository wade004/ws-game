using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.WorldState
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json <c>world.flag_changed</c> 行）。惯例同
    /// <c>core/carriers/common</c> 的 <c>CarriersEventKeys</c>：模块自持一份常量，不依赖生成物。</summary>
    public static class WorldStateEventKeys
    {
        public static readonly Id FlagChanged = new Id("world.flag_changed");
    }

    /// <summary>
    /// <see cref="IWorldState.Set"/>/<see cref="IWorldState.Remove"/> 写入后触发（见 05 第 8.2 节
    /// "每次 set 触发 world.flag_changed 事件到事件总线，附带 flagKey、oldValue、newValue、writerId"、
    /// found.event_catalog <c>world.flag_changed</c> 行字段表原文）。
    /// </summary>
    public sealed class WorldFlagChangedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => WorldStateEventKeys.FlagChanged;

        public Id FlagKey { get; }

        /// <summary>变化前的值；标志此前未设置过时按"缺失"约定取
        /// <see cref="ExprValue.OfBool(bool)"/> <c>false</c>（见 <see cref="IWorldState.Get"/> 注释、
        /// 04 第 6.3 节"求值期引用对象暂缺按分组默认值处理"）。</summary>
        public ExprValue OldValue { get; }

        public ExprValue NewValue { get; }

        public Id WriterId { get; }

        public WorldFlagChangedEvent(Id flagKey, ExprValue oldValue, ExprValue newValue, Id writerId)
        {
            FlagKey = flagKey;
            OldValue = oldValue;
            NewValue = newValue;
            WriterId = writerId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "flagKey": value = ExprValue.OfId(FlagKey); return true;
                case "oldValue": value = OldValue; return true;
                case "newValue": value = NewValue; return true;
                case "writerId": value = ExprValue.OfId(WriterId); return true;
                default: value = default; return false;
            }
        }
    }
}
