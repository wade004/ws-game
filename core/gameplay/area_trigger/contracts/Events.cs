using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json <c>area.trigger_entered</c>/<c>area.trigger_left</c>
    /// 两行）。惯例同 <c>Core.Carriers.Common.CarriersEventKeys</c>：模块自持一份常量。</summary>
    public static class AreaTriggerEventKeys
    {
        public static readonly Id TriggerEntered = new Id("area.trigger_entered");

        public static readonly Id TriggerLeft = new Id("area.trigger_left");
    }

    /// <summary>单位进入触发范围且满足 <c>condition</c>（<c>one_shot</c> 已触发的除外）时发出（见
    /// 05 第 7.1 节、found.event_catalog <c>area.trigger_entered</c> 行字段原文）。</summary>
    public sealed class AreaTriggerEnteredEvent : IEvent, IExprReadableEvent
    {
        public Id Key => AreaTriggerEventKeys.TriggerEntered;

        public Id TriggerId { get; }

        public Id UnitId { get; }

        public AreaTriggerEnteredEvent(Id triggerId, Id unitId)
        {
            TriggerId = triggerId;
            UnitId = unitId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "triggerId": value = ExprValue.OfId(TriggerId); return true;
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>单位离开触发范围（此前已作为"进入"处理过）时发出（见 05 第 7.1 节、
    /// found.event_catalog <c>area.trigger_left</c> 行字段原文）。</summary>
    public sealed class AreaTriggerLeftEvent : IEvent, IExprReadableEvent
    {
        public Id Key => AreaTriggerEventKeys.TriggerLeft;

        public Id TriggerId { get; }

        public Id UnitId { get; }

        public AreaTriggerLeftEvent(Id triggerId, Id unitId)
        {
            TriggerId = triggerId;
            UnitId = unitId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "triggerId": value = ExprValue.OfId(TriggerId); return true;
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                default: value = default; return false;
            }
        }
    }
}
