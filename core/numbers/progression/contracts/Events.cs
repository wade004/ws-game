using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Numbers.Progression
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// 01_分层与依赖.md L1 模块表 <c>progression</c> 行"主要事件：progression.level_up、
    /// progression.xp_gained"）。事件登记表本身的补全由并行任务负责，本模块只声明常量与事件
    /// 类型，测试用 <c>EventCatalog.FromDefinitions</c> 自行搭建最小登记表。</summary>
    public static class ProgressionEventKeys
    {
        public static readonly Id LevelUp = new Id("progression.level_up");
        public static readonly Id XpGained = new Id("progression.xp_gained");
    }

    /// <summary>
    /// <see cref="IProgressionHost.AddXp"/> 处理经验时，单位每提升一级发一次（见 IProgressionHost
    /// 判断记录"一次 AddXp 内可连升多级，每级发一次"）。用 <see cref="IEventBus.PublishImmediate"/>：
    /// 升级不是 tick 内的高频批处理事件，属于一次性状态跃迁通知。
    /// </summary>
    public sealed class LevelUpEvent : IEvent
    {
        public Id Key => ProgressionEventKeys.LevelUp;

        public Id UnitId { get; }

        public int OldLevel { get; }

        public int NewLevel { get; }

        public LevelUpEvent(Id unitId, int oldLevel, int newLevel)
        {
            UnitId = unitId;
            OldLevel = oldLevel;
            NewLevel = newLevel;
        }
    }

    /// <summary>
    /// <see cref="IProgressionHost.AddXp"/> 成功记入经验时发出（单位已处于满级、经验被丢弃的
    /// 分支不发本事件，见 IProgressionHost 判断记录）。
    /// </summary>
    public sealed class XpGainedEvent : IEvent
    {
        public Id Key => ProgressionEventKeys.XpGained;

        public Id UnitId { get; }

        public Id SourceId { get; }

        public long Amount { get; }

        public XpGainedEvent(Id unitId, Id sourceId, long amount)
        {
            UnitId = unitId;
            SourceId = sourceId;
            Amount = amount;
        }
    }
}
