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

        /// <summary>R08 收边补齐（外部审计 5e779c6，P2）：见 <see cref="ProgressionRestoredEvent"/>
        /// 判断记录。</summary>
        public static readonly Id StateRestored = new Id("progression.state_restored");
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
    /// R08 收边补齐（外部审计 5e779c6，P2；见 <c>ProgressionHost.RestoreState</c>/
    /// <c>Core.Rules.Assembly.RulesAssembly</c> 构造函数"RC-06 收边补齐"判断记录）：读档恢复等级时
    /// 发出，专供依赖等级的下游缓存（当前唯一消费方：<c>StatHost.RecomputeRatingStats</c>，见
    /// <see cref="Core.Numbers.StatBlock.StatHostOptions.LevelLookup"/>）失效重算。
    /// <para>
    /// 判断记录（为什么不复用 <see cref="LevelUpEvent"/>）：<c>ProgressionHost.RestoreState</c>
    /// 是"按存档快照直接置位"，不是"通过 <see cref="IProgressionHost.AddXp"/> 逐级真实升级"——语义
    /// 上不是同一件事：<see cref="LevelUpEvent.OldLevel"/>/<see cref="LevelUpEvent.NewLevel"/> 表达
    /// "刚刚从哪一级升到哪一级"，读档场景没有真实的"旧等级"（存档快照本身就是这个单位当前唯一
    /// 已知的状态，读档前该单位在本次进程里可能压根还没注册），且读档可能一次性跨越多级（如从
    /// 等级 1 直接恢复到等级 50），若复用 <see cref="LevelUpEvent"/> 要么伪造一串虚假的逐级
    /// OldLevel/NewLevel 事件（未来若有内容层订阅 <c>progression.level_up</c> 播放"升级"音效/特效/
    /// 弹窗、发放升级奖励，读档会被误当成"玩家在这一刻连升 49 级"重复触发这些副作用），要么只发一次
    /// 语义不自洽的"跨级"LevelUpEvent（<c>OldLevel</c> 该填几没有唯一正确答案）。改发一个语义诚实、
    /// 专用的独立事件，读档消费者（本次只有 <c>RulesAssembly</c> 一处）明确知道这是"状态被恢复"而
    /// 不是"发生了一次升级"，不会与未来任何"真实升级"订阅者的假设冲突。
    /// </para>
    /// </summary>
    public sealed class ProgressionRestoredEvent : IEvent
    {
        public Id Key => ProgressionEventKeys.StateRestored;

        public Id UnitId { get; }

        public int Level { get; }

        public ProgressionRestoredEvent(Id unitId, int level)
        {
            UnitId = unitId;
            Level = level;
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
