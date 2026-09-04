using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Spawn
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json <c>spawn.executed</c> 行）。</summary>
    public static class SpawnEventKeys
    {
        public static readonly Id Executed = new Id("spawn.executed");
    }

    /// <summary><see cref="ISpawnHost.ApplyForMap"/>/<see cref="ISpawnHost.TriggerNever"/> 按策略生成
    /// 实体完成时发出（见 05 第 5.3 节、found.event_catalog <c>spawn.executed</c> 行字段原文）。</summary>
    public sealed class SpawnExecutedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => SpawnEventKeys.Executed;

        public Id SpawnId { get; }

        public Id EntityId { get; }

        public SpawnExecutedEvent(Id spawnId, Id entityId)
        {
            SpawnId = spawnId;
            EntityId = entityId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "spawnId": value = ExprValue.OfId(SpawnId); return true;
                case "entityId": value = ExprValue.OfId(EntityId); return true;
                default: value = default; return false;
            }
        }
    }
}
