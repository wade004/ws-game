using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Gobj;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// <see cref="EntitySpatialSyncHost"/> 独立单元测试（不经过完整 <see cref="CarriersAssembly"/>
    /// 装配、不需要任何数据表——直接用 <see cref="WorldSim"/> + <see cref="StubSpatialQuery"/>，
    /// 覆盖 ADR-0016 决策 7"创建时 Register、销毁时 Unregister"两个时机与标签区分）。
    /// </summary>
    public class EntitySpatialSyncHostTests
    {
        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.player");

        [Fact]
        public void EntityCreated_RegistersUnitWithUnitTag()
        {
            var bus = NewBus();
            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            _ = new EntitySpatialSyncHost(bus, world, spatial, CarriersAssembly.DefaultSpatialSyncKinds);

            var creature = new CreatureUnit(new Id("unit.wolf"), MapId, FactionId, new Id("creature.grey_wolf"))
            {
                Position = new Vec2(3, 4)
            };
            world.AddEntity(creature);
            bus.DispatchPending();

            var nearest = spatial.Nearest(new Vec2(3, 4), QueryFilter.None);
            Assert.Equal(creature.EntityId, nearest);

            // 打了 "unit" 标签：用 RequiredTags=["gobj"] 查询应该查不到。
            var gobjOnly = spatial.QueryRadius(new Vec2(3, 4), 1.0, new QueryFilter(requiredTags: new[] { "gobj" }));
            Assert.Empty(gobjOnly);

            var unitOnly = spatial.QueryRadius(new Vec2(3, 4), 1.0, new QueryFilter(requiredTags: new[] { "unit" }));
            Assert.Single(unitOnly);
        }

        [Fact]
        public void EntityCreated_RegistersGobjWithGobjTag_WhenExplicitlyConfigured()
        {
            // CarriersAssembly.DefaultSpatialSyncKinds 默认不含 "gobj"（见该属性判断记录：
            // core/rules/targeting 的 nearest_in_shape 策略用 QueryFilter.None 且不防御性检查
            // IUnitAccess.Exists，登记 gobj 会有被误捞的风险）；本测试直接验证机制本身——
            // 显式配置 "gobj" 时，EntitySpatialSyncHost 确实会按配置的标签登记，供确有需要且已经
            // 自行审计过查询调用方的游戏使用。
            var bus = NewBus();
            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var kinds = new Dictionary<string, EntitySpatialSyncHost.KindConfig>(StringComparer.Ordinal)
            {
                ["gobj"] = new EntitySpatialSyncHost.KindConfig(0.1, new[] { "gobj" }),
            };
            _ = new EntitySpatialSyncHost(bus, world, spatial, kinds);

            var gobj = new GameObjectEntity(new Id("gobj.chest_1"), MapId, new Id("gobj.template.chest"))
            {
                Position = new Vec2(10, 10)
            };
            world.AddEntity(gobj);
            bus.DispatchPending();

            var unitOnly = spatial.QueryRadius(new Vec2(10, 10), 1.0, new QueryFilter(requiredTags: new[] { "unit" }));
            Assert.Empty(unitOnly);

            var gobjOnly = spatial.QueryRadius(new Vec2(10, 10), 1.0, new QueryFilter(requiredTags: new[] { "gobj" }));
            Assert.Single(gobjOnly);
            Assert.Equal(gobj.EntityId, gobjOnly[0]);
        }

        [Fact]
        public void EntityDestroyed_UnregistersFromSpatialIndex()
        {
            var bus = NewBus();
            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            _ = new EntitySpatialSyncHost(bus, world, spatial, CarriersAssembly.DefaultSpatialSyncKinds);

            var creature = new CreatureUnit(new Id("unit.wolf"), MapId, FactionId, new Id("creature.grey_wolf"))
            {
                Position = new Vec2(3, 4)
            };
            world.AddEntity(creature);
            bus.DispatchPending();
            Assert.NotNull(spatial.Nearest(new Vec2(3, 4), QueryFilter.None));

            world.ClearAll();
            bus.DispatchPending();

            Assert.Null(spatial.Nearest(new Vec2(3, 4), QueryFilter.None));
        }

        [Fact]
        public void DefaultSpatialSyncKinds_DoesNotIncludeGobj()
        {
            // 回归防护：见 CarriersAssembly.DefaultSpatialSyncKinds 判断记录——默认清单不含 "gobj"，
            // 避免 core/rules/targeting 的 nearest_in_shape 一类不做标签过滤的策略把物件误当单位。
            Assert.False(CarriersAssembly.DefaultSpatialSyncKinds.ContainsKey("gobj"));
        }

        [Fact]
        public void EntityCreated_GobjWithDefaultKinds_IsNotRegistered()
        {
            var bus = NewBus();
            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            _ = new EntitySpatialSyncHost(bus, world, spatial, CarriersAssembly.DefaultSpatialSyncKinds);

            var gobj = new GameObjectEntity(new Id("gobj.chest_1"), MapId, new Id("gobj.template.chest"))
            {
                Position = new Vec2(10, 10)
            };
            world.AddEntity(gobj);
            bus.DispatchPending();

            Assert.Null(spatial.Nearest(new Vec2(10, 10), QueryFilter.None));
        }

        [Fact]
        public void EntityCreated_UnconfiguredKind_IsIgnored()
        {
            var bus = NewBus();
            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            // 只配置 "unit"，不配置 "gobj"——GameObjectEntity(Kind="gobj") 应被忽略。
            var kinds = new Dictionary<string, EntitySpatialSyncHost.KindConfig>(StringComparer.Ordinal)
            {
                ["creature"] = new EntitySpatialSyncHost.KindConfig(0.1, new[] { "unit" }),
            };
            _ = new EntitySpatialSyncHost(bus, world, spatial, kinds);

            var gobj = new GameObjectEntity(new Id("gobj.chest_1"), MapId, new Id("gobj.template.chest"))
            {
                Position = new Vec2(10, 10)
            };
            world.AddEntity(gobj);
            bus.DispatchPending();

            Assert.Null(spatial.Nearest(new Vec2(10, 10), QueryFilter.None));
        }
    }
}
