using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// 本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// 01_分层与依赖.md L1 模块表 <c>power_set</c> 行"主要事件：power.changed、
    /// power.depleted"）。与 sim_loop 的 <c>SimEventKeys</c>、hook_registry 的
    /// <c>HookEventKeys</c> 同一惯例：模块自持一份常量，不依赖跨模块生成物。
    /// </summary>
    public static class PowerEventKeys
    {
        public static readonly Id Changed = new Id("power.changed");
        public static readonly Id Depleted = new Id("power.depleted");
    }

    /// <summary>
    /// 资源值发生变化时触发（见 06 第 2.2 节"事件：power.changed { unitId, powerType,
    /// oldValue, newValue }"）。由 <see cref="PowerHost"/> 在 <see cref="IPowerHost.ModifyPower"/>、
    /// <see cref="IPowerHost.Advance"/>/<see cref="IPowerHost.AdvanceAll"/> 的回复/衰减、
    /// <see cref="IPowerHost.SetInCombat"/> 的脱战回满、<see cref="IPowerHost.RecomputeMax"/>
    /// 的上限下降夹取五处产生实际变化时 <c>Enqueue</c>；值未变化不发。
    /// </summary>
    public sealed class PowerChangedEvent : IEvent
    {
        public Id Key => PowerEventKeys.Changed;

        public Id UnitId { get; }

        public Id PowerType { get; }

        public double OldValue { get; }

        public double NewValue { get; }

        public PowerChangedEvent(Id unitId, Id powerType, double oldValue, double newValue)
        {
            UnitId = unitId;
            PowerType = powerType;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    /// <summary>
    /// 资源值从大于 <c>min</c> 变为等于 <c>min</c> 的那一次触发（见 01_分层与依赖.md L1 模块表
    /// <c>power_set</c> 行；06 第 2.2 节未给出该事件字段，本类型按"事件词汇表"惯例携带
    /// <c>{unitId, powerType}</c>——任务书拍板字段）。连续多次修改仍停留在 <c>min</c> 只在
    /// 首次触发时发一次，不重复发。
    /// </summary>
    public sealed class PowerDepletedEvent : IEvent
    {
        public Id Key => PowerEventKeys.Depleted;

        public Id UnitId { get; }

        public Id PowerType { get; }

        public PowerDepletedEvent(Id unitId, Id powerType)
        {
            UnitId = unitId;
            PowerType = powerType;
        }
    }
}
