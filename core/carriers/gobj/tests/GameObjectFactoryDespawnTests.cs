using System.Collections.Generic;
using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>
    /// T-L14（测试覆盖剩余项 2026-10-01）：<see cref="GameObjectFactory.Despawn"/> 的直接用例。
    /// 该方法只是 <c>MarkForDestruction</c> 的薄封装：标记后实体在下一次 tick 的生命周期清理阶段才真正移除，
    /// 并经 <c>entity.destroyed</c> 通用事件通知；对未知/重复 id 不抛异常。
    /// </summary>
    public sealed class GameObjectFactoryDespawnTests
    {
        private static readonly Id MapId = new Id("map.gobj_despawn");
        private static readonly Id TemplateId = new Id("gobj.gobj_despawn_template");

        private static GameObjectFactory Build(out WorldSim world, out Core.Foundation.EventBus.IEventBus bus)
        {
            bus = GobjWorldBuilder.CreateBus();
            world = new WorldSim(bus);
            return new GameObjectFactory(world);
        }

        [Fact]
        public void Despawn_MarksPending_ThenEntityDisappearsAfterNextTick()
        {
            var factory = Build(out var world, out _);
            var id = factory.Spawn(TemplateId, MapId, new Vec2(1, 2), 0);
            Assert.NotNull(world.GetEntity(id));
            Assert.False(world.IsPendingDestruction(id));

            factory.Despawn(id);

            // 标记当下实体仍在（生命周期清理在 tick 阶段 8），但已处于待销毁状态。
            Assert.NotNull(world.GetEntity(id));
            Assert.True(world.IsPendingDestruction(id));

            world.Tick(SimStep.Continuous(0.1));

            Assert.Null(world.GetEntity(id));
            Assert.False(world.IsPendingDestruction(id));
        }

        [Fact]
        public void Despawn_PublishesEntityDestroyedForThatEntityOnly()
        {
            var factory = Build(out var world, out var bus);
            var keep = factory.Spawn(TemplateId, MapId, new Vec2(0, 0), 0);
            var drop = factory.Spawn(TemplateId, MapId, new Vec2(5, 5), 0);
            var destroyed = new List<EntityDestroyedEvent>();
            bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e => destroyed.Add(e));

            factory.Despawn(drop);
            world.Tick(SimStep.Continuous(0.1));
            bus.DispatchPending();

            Assert.Contains(destroyed, e => e.EntityId.Equals(drop));
            Assert.DoesNotContain(destroyed, e => e.EntityId.Equals(keep));
            Assert.NotNull(world.GetEntity(keep));
        }

        [Fact]
        public void Despawn_UnknownOrRepeatedId_DoesNotThrow()
        {
            var factory = Build(out var world, out _);
            var id = factory.Spawn(TemplateId, MapId, new Vec2(0, 0), 0);

            var unknown = Record.Exception(() => factory.Despawn(new Id("gobj.never_spawned")));
            factory.Despawn(id);
            var repeated = Record.Exception(() => factory.Despawn(id));
            var afterTick = Record.Exception(() => world.Tick(SimStep.Continuous(0.1)));

            Assert.Null(unknown);
            Assert.Null(repeated);
            Assert.Null(afterTick);
            Assert.Null(world.GetEntity(id));
        }
    }
}
