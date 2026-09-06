using System;
using Core.Foundation.EventBus;

namespace Tests.Gameplay.Death
{
    /// <summary>本模块测试的最小公共帮助方法（惯例同 <c>core/gameplay/spawn/tests/TestSupport.cs</c>
    /// 的 <c>SpawnTestSupport</c>）。</summary>
    internal static class DeathTestSupport
    {
        /// <summary>非严格事件目录的事件总线：本模块测试只关心
        /// <c>unit.died</c>/<c>unit.respawned</c> 两个事件的收发，不为其余无关表格逐一登记目录项。</summary>
        public static IEventBus NewEventBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
    }
}
