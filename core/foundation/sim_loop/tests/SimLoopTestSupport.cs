using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;

namespace Tests.Foundation.SimLoop
{
    /// <summary>测试共用的 <see cref="IEventBus"/> 构造帮助方法：登记本模块自己会发出的
    /// 四个事件 key（与 found.event_catalog.json 一致），并关闭严格模式，允许测试用例
    /// 额外发一些未登记的临时标记事件（见 EventBusOptions.StrictCatalog 注释）。</summary>
    internal static class SimLoopTestSupport
    {
        public static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(SimEventKeys.TickStarted, "sim", new[] { "tickIndex", "dt" }),
                new EventDefinition(SimEventKeys.TickFinished, "sim", new[] { "tickIndex" }),
                new EventDefinition(SimEventKeys.EntityCreated, "entity", new[] { "entityId", "kind", "displayId" }),
                new EventDefinition(SimEventKeys.EntityDestroyed, "entity", new[] { "entityId" }),
                new EventDefinition(SimEventKeys.TurnStarted, "sim", new[] { "actorId", "roundIndex" }),
                new EventDefinition(SimEventKeys.TurnEnded, "sim", new[] { "actorId" }),
                new EventDefinition(SimEventKeys.RoundEnded, "sim", new[] { "roundIndex" }),
                new EventDefinition(SimEventKeys.AwaitingInput, "sim", new[] { "actorId" }),
            });

            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }
    }
}
