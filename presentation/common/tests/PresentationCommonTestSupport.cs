using System;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;

namespace Tests.PresentationCommon
{
    /// <summary>测试共用的最小 <see cref="Entity"/> 子类（本模块只提供公共基类，具体子类属于 L3；
    /// 惯例同 <c>core/foundation/sim_loop/tests/TestEntity.cs</c>）。</summary>
    internal sealed class TestEntity : Entity
    {
        public override string Kind { get; }

        public TestEntity(Id entityId, Id mapId, string kind = "test_entity")
            : base(entityId, mapId)
        {
            Kind = kind;
        }
    }

    /// <summary>构造一个非严格模式（<c>StrictCatalog=false</c>）的最小事件总线，供不关心事件目录
    /// 登记细节的测试直接使用（惯例同 <c>Tests.Foundation.SimLoop.SimLoopTestSupport</c>）。</summary>
    internal static class PresentationCommonTestSupport
    {
        public static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(Array.Empty<EventDefinition>());
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }

        public static IWorldSim CreateWorld(out IEventBus bus)
        {
            bus = CreateBus();
            return new WorldSim(bus);
        }
    }
}
