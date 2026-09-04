using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Numbers.Archetype
{
    /// <summary>本模块发出的事件 key 常量（见 01_分层与依赖.md L1 模块表 <c>archetype</c> 行
    /// "主要事件：archetype.applied"）。</summary>
    public static class ArchetypeEventKeys
    {
        public static readonly Id Applied = new Id("archetype.applied");
    }

    /// <summary><see cref="IArchetypeRegistry.ApplyTo"/> 完成全部写入步骤后发出（见该方法文档
    /// "按顺序 StatBaseWriter 写 base_stats → StatModifierWriter 写种族修正 → PowerRegistrar
    /// 注册资源 → 发 archetype.applied"）。用 <see cref="IEventBus.PublishImmediate"/>：
    /// 应用职业模板是初始化/转职一类一次性操作，不是 tick 内的高频事件。</summary>
    public sealed class ArchetypeAppliedEvent : IEvent
    {
        public Id Key => ArchetypeEventKeys.Applied;

        public Id UnitId { get; }

        public Id ClassId { get; }

        public Id? RaceId { get; }

        public ArchetypeAppliedEvent(Id unitId, Id classId, Id? raceId)
        {
            UnitId = unitId;
            ClassId = classId;
            RaceId = raceId;
        }
    }
}
