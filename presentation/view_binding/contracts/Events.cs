using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Presentation.ViewBinding
{
    /// <summary>
    /// ADR-0078：本模块（<c>presentation/view_binding</c>）发出的事件 key 常量（对应
    /// <c>found.event_catalog</c> 登记表，见 <c>data/_framework/found/found.event_catalog.json</c>
    /// 的 <c>unit.stride_completed</c> 行）。惯例同 <c>core/foundation/input_map</c> 的
    /// <c>InputMapEventKeys</c>：模块自持一份发出事件的 key 常量，不依赖 event_bus 的生成物
    /// <c>EventKeys.g.cs</c>。
    /// <para>
    /// 判断记录（为什么不违反 09 第 1 节铁律 P2"表现层只订阅"）：见 <see cref="StrideEmitter"/>
    /// 类型注释与 <c>architecture/adr/0078-单位步幅位移事件.md</c>"决策"一节——本事件是由已发布的
    /// <c>unit.moved</c> 在表现层派生出的节奏通知，不携带、也不允许携带任何会被 L0～L4 解释为
    /// "逻辑判定"的数据，不写回仿真、不影响仿真基线，09 第 1 节 P2 已同步收窄措辞为"表现层被允许
    /// 发出的事件限定在一个明确枚举的小集合内"。
    /// </para>
    /// </summary>
    public static class ViewBindingEventKeys
    {
        public static readonly Id UnitStrideCompleted = new Id("unit.stride_completed");
    }

    /// <summary>
    /// <see cref="StrideEmitter"/> 累计某单位经 <c>unit.moved</c> 报告的位移，达到该单位
    /// <c>display.map.stride_distance</c> 声明的一个步幅距离时发出（见该类型注释）。本事件只陈述
    /// "该单位的累计位移达到了一个步幅距离"这一几何事实，不预设、也不携带任何呈现含义（是否配
    /// 音效、特效、地面痕迹等均由具体游戏的 <c>feedback.binding</c>/表现代码决定，框架不登记默认
    /// 绑定，见判断记录）。用 <see cref="IEventBus.Enqueue"/> 入队——本事件在 <c>unit.moved</c>
    /// 的事件分发过程中产生，同一批 <c>DispatchPending</c> 内继续派发，惯例同 <c>ViewBinder</c> 等
    /// 其它在事件分发中级联产生事件的表现层组件。
    /// </summary>
    public sealed class UnitStrideCompletedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => ViewBindingEventKeys.UnitStrideCompleted;

        public Id UnitId { get; }

        /// <summary>触发本次步幅事件那一刻的单位逻辑位置（同 <c>UnitMovedEvent.Position</c> 惯例，
        /// 不经 Expr 暴露——Vec2 是复合类型，见 <c>IExprReadableEvent</c> 覆盖范围说明）。</summary>
        public Vec2 Position { get; }

        public UnitStrideCompletedEvent(Id unitId, Vec2 position)
        {
            UnitId = unitId;
            Position = position;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                default: value = default; return false;
            }
        }
    }
}
