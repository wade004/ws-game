using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Numbers.Faction
{
    /// <summary>本模块发出的事件 key 常量（见 01_分层与依赖.md L1 模块表 <c>faction</c> 行
    /// "主要事件：faction.relation_changed"）。</summary>
    public static class FactionEventKeys
    {
        public static readonly Id RelationChanged = new Id("faction.relation_changed");
    }

    /// <summary><see cref="IFactionMatrix.SetReaction"/> 把 <c>(from, to)</c> 的运行期反应改成
    /// 与改动前不同的值时发出（未变化不发，见 <see cref="FactionMatrix"/> 判断记录）。用
    /// <see cref="IEventBus.PublishImmediate"/>：阵营关系变化是一次性状态跃迁，不是 tick 内的
    /// 高频事件。</summary>
    public sealed class FactionRelationChangedEvent : IEvent
    {
        public Id Key => FactionEventKeys.RelationChanged;

        public Id From { get; }

        public Id To { get; }

        public Reaction OldReaction { get; }

        public Reaction NewReaction { get; }

        public FactionRelationChangedEvent(Id from, Id to, Reaction oldReaction, Reaction newReaction)
        {
            From = from;
            To = to;
            OldReaction = oldReaction;
            NewReaction = newReaction;
        }
    }
}
