using Core.Foundation.AppLifecycle;
using Core.Foundation.EventBus;

namespace Tests.Foundation.AppLifecycle
{
    /// <summary>测试共用的 <see cref="IEventBus"/> 构造帮助方法：登记本模块发出的
    /// <c>app.state_changed</c> 事件 key（与 found.event_catalog.json 一致）（与
    /// sim_loop 的 <c>SimLoopTestSupport</c> 同一惯例）。</summary>
    internal static class AppLifecycleTestSupport
    {
        public static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(AppEventKeys.StateChanged, "app", new[] { "oldState", "newState" }),
            });

            return new EventBus(catalog);
        }
    }
}
