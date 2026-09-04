using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Numbers.StatBlock
{
    /// <summary>本模块发出的事件 key 常量（见 06 第 1.3 节"事件：发出
    /// <c>stat.changed { unitId, stat, oldValue, newValue }</c>"）。任务书要求：模块内自建
    /// 常量，不依赖 <c>core/foundation/event_bus/generated/EventKeys.g.cs</c> 是否已登记
    /// <c>stat.changed</c>——该生成文件由 <c>found.event_catalog.json</c> 驱动，登记与否不是
    /// 本模块能控制的时序，调用方（把本模块接入某个具体 <see cref="Core.Foundation.EventBus.IEventBus"/>
    /// 实例的一方）负责在自己的 <c>IEventCatalog</c> 中登记本 key，或关闭
    /// <c>EventBusOptions.StrictCatalog</c>。</summary>
    public static class StatBlockEventKeys
    {
        public static readonly Id StatChanged = new Id("stat.changed");
    }

    /// <summary>
    /// 属性最终值发生变化时发出（见 06 第 1.3 节）。<see cref="StatHost"/> 只在
    /// <see cref="IStatHost.SetBase"/>/<see cref="IStatHost.AddModifier"/>/
    /// <see cref="IStatHost.RemoveModifiersBySource"/> 导致 <see cref="OldValue"/> 与
    /// <see cref="NewValue"/> 确实不同（按 <c>double</c> 精确比较）时才
    /// <see cref="Core.Foundation.EventBus.IEventBus.Enqueue"/> 一次（tick 内批处理，属性变化
    /// 发生在 tick 内，见 04_数据与内容管线.md 第 3.1 节与落地方案.md 第 4.2 节"同步派发 +
    /// tick 末批处理"）。
    /// </summary>
    public sealed class StatChangedEvent : IEvent
    {
        public Id Key => StatBlockEventKeys.StatChanged;

        public Id UnitId { get; }

        public Id Stat { get; }

        public double OldValue { get; }

        public double NewValue { get; }

        public StatChangedEvent(Id unitId, Id stat, double oldValue, double newValue)
        {
            UnitId = unitId;
            Stat = stat;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
}
