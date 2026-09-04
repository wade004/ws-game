using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Spawn;
using Core.Gameplay.WorldState;
using Xunit;

namespace Tests.Gameplay.Spawn
{
    public sealed class SpawnHostTests
    {
        private static Id Map => new Id("world.sample_map");

        private sealed class Fixture
        {
            public IEventBus Bus = default!;
            public Core.Foundation.DataRegistry.IDataRegistry Registry = default!;
            public Core.Gameplay.WorldState.WorldState World = default!;
            public FakeCreatureFactory Creatures = default!;
            public FakeExprHostFactory Expr = default!;
            public SpawnOptions Options = default!;
            public SpawnHost Host = default!;
            public System.Collections.Generic.List<IEvent> Events = new System.Collections.Generic.List<IEvent>();
        }

        private static Fixture Build(params Core.Foundation.Common.Json.JsonObject[] rows)
        {
            var bus = SpawnTestSupport.NewEventBus();
            var registry = SpawnTestSupport.BuildRegistry(bus, rows);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("\n", report.Issues.Select(i => i.ToString())));

            var world = new Core.Gameplay.WorldState.WorldState(bus);
            var creatures = new FakeCreatureFactory(bus);
            var expr = new FakeExprHostFactory();
            var options = new SpawnOptions();
            var host = new SpawnHost(registry, world, creatures, bus, expr, options);

            var fx = new Fixture
            {
                Bus = bus, Registry = registry, World = world, Creatures = creatures, Expr = expr, Options = options, Host = host,
            };
            bus.Subscribe(SpawnEventKeys.Executed, evt => fx.Events.Add(evt));
            return fx;
        }

        [Fact]
        public void ApplyForMap_OnMapEnter_GeneratesOnce()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter"));

            var generated = fx.Host.ApplyForMap(Map);

            Assert.Single(generated);
            Assert.Single(fx.Creatures.SpawnCalls);
        }

        [Fact]
        public void ApplyForMap_OnMapEnter_SecondCallSkipsWhileAlive()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter"));

            fx.Host.ApplyForMap(Map);
            var second = fx.Host.ApplyForMap(Map);

            Assert.Empty(second);
            Assert.Single(fx.Creatures.SpawnCalls);
        }

        [Fact]
        public void ApplyForMap_OnMapEnter_RegeneratesAfterDespawn()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter"));

            var first = fx.Host.ApplyForMap(Map).Single();
            fx.Creatures.Despawn(first, "died");
            fx.Bus.DispatchPending();
            var second = fx.Host.ApplyForMap(Map);

            Assert.Single(second);
            Assert.Equal(2, fx.Creatures.SpawnCalls.Count);
        }

        [Fact]
        public void ApplyForMap_Once_SecondApplyDoesNotRegenerate()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_hero", "world.sample_map", "creature.sample_hero", "once"));

            var first = fx.Host.ApplyForMap(Map);
            Assert.Single(first);

            // 模拟重新进图：即便清空运行期实例引用（如死亡），once 标志已持久化在 WorldState，
            // 不应再次生成。
            fx.Creatures.Despawn(first[0], "died");
            fx.Bus.DispatchPending();
            var second = fx.Host.ApplyForMap(Map);

            Assert.Empty(second);
            Assert.Single(fx.Creatures.SpawnCalls);
        }

        [Fact]
        public void ApplyForMap_Never_DoesNotGenerate()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_boss", "world.sample_map", "creature.sample_boss", "never"));

            var generated = fx.Host.ApplyForMap(Map);

            Assert.Empty(generated);
            Assert.Empty(fx.Creatures.SpawnCalls);
        }

        [Fact]
        public void TriggerNever_GeneratesExplicitly()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_boss", "world.sample_map", "creature.sample_boss", "never"));
            fx.Host.ApplyForMap(Map);

            fx.Host.TriggerNever(new Id("spawn.sample_boss"));

            Assert.Single(fx.Creatures.SpawnCalls);
        }

        [Fact]
        public void TriggerNever_AlreadyAlive_WarnsAndSkips()
        {
            var diagnostics = new Core.Gameplay.Spawn.InMemorySpawnDiagnostics();
            var fx = Build(SpawnTestSupport.Row("spawn.sample_boss", "world.sample_map", "creature.sample_boss", "never"));
            var host = new SpawnHost(fx.Registry, fx.World, fx.Creatures, fx.Bus, fx.Expr, fx.Options, diagnostics);

            host.TriggerNever(new Id("spawn.sample_boss")); // 第一次：正常生成
            host.TriggerNever(new Id("spawn.sample_boss")); // 第二次：已有存活实例，跳过

            Assert.Single(fx.Creatures.SpawnCalls);
            Assert.NotEmpty(diagnostics.Warnings);
        }

        [Fact]
        public void Update_TimerExpires_RespawnsWhenMapLoaded()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "timer", respawnTimer: 5));

            var first = fx.Host.ApplyForMap(Map).Single();
            fx.Creatures.Despawn(first, "died");
            fx.Bus.DispatchPending();

            fx.Host.Update(3); // 未到期
            Assert.Single(fx.Creatures.SpawnCalls);

            fx.Host.Update(3); // 累计 6 > 5，到期
            Assert.Equal(2, fx.Creatures.SpawnCalls.Count);
        }

        [Fact]
        public void Update_TimerExpires_MapNotLoaded_DoesNotRespawn()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "timer", respawnTimer: 1));

            var first = fx.Host.ApplyForMap(Map).Single();
            fx.Creatures.Despawn(first, "died");
            fx.Bus.DispatchPending();
            fx.Host.UnloadMap(Map);

            fx.Host.Update(10);

            Assert.Single(fx.Creatures.SpawnCalls);
        }

        [Fact]
        public void ApplyForMap_ConditionFalse_DoesNotGenerate()
        {
            var fx = Build(SpawnTestSupport.Row(
                "spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter", condition: "self.is_alive"));
            fx.Expr.Host.Set("self", "is_alive", ExprValue.OfBool(false));

            var generated = fx.Host.ApplyForMap(Map);

            Assert.Empty(generated);
        }

        [Fact]
        public void ApplyForMap_ConditionTrue_Generates()
        {
            var fx = Build(SpawnTestSupport.Row(
                "spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter", condition: "self.is_alive"));
            fx.Expr.Host.Set("self", "is_alive", ExprValue.OfBool(true));

            var generated = fx.Host.ApplyForMap(Map);

            Assert.Single(generated);
        }

        [Fact]
        public void ApplyForMap_GobjContentRef_UsesGobjSpawnerDelegate()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_chest", "world.sample_map", "gobj.sample_chest", "on_map_enter"));
            Id? gotTemplate = null;
            fx.Options.GobjSpawner = (templateId, mapId, position, facing) => { gotTemplate = templateId; return new Id("gobj.inst_1"); };

            var generated = fx.Host.ApplyForMap(Map);

            Assert.Single(generated);
            Assert.Equal(new Id("gobj.sample_chest"), gotTemplate);
            Assert.Empty(fx.Creatures.SpawnCalls);
        }

        [Fact]
        public void ApplyForMap_GobjContentRefWithoutSpawner_WarnsAndSkips()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_chest", "world.sample_map", "gobj.sample_chest", "on_map_enter"));

            var generated = fx.Host.ApplyForMap(Map);

            Assert.Empty(generated);
        }

        [Fact]
        public void SpawnExecutedEvent_CarriesSpawnIdAndEntityId()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter"));

            var entityId = fx.Host.ApplyForMap(Map).Single();
            fx.Bus.DispatchPending();

            var evt = Assert.Single(fx.Events.OfType<SpawnExecutedEvent>());
            Assert.Equal(new Id("spawn.sample_wolf"), evt.SpawnId);
            Assert.Equal(entityId, evt.EntityId);
        }

        [Fact]
        public void GetSpawnRecord_ReflectsSpawnCountAndEntity()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter"));
            var entityId = fx.Host.ApplyForMap(Map).Single();

            var record = fx.Host.GetSpawnRecord(new Id("spawn.sample_wolf"));

            Assert.NotNull(record);
            Assert.Equal(entityId, record!.EntityId);
            Assert.Equal(1, record!.SpawnCount);
        }

        [Fact]
        public void UnloadMap_ClearsEntityReferenceButKeepsSpawnCount()
        {
            var fx = Build(SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter"));
            fx.Host.ApplyForMap(Map);

            fx.Host.UnloadMap(Map);

            var record = fx.Host.GetSpawnRecord(new Id("spawn.sample_wolf"));
            Assert.Null(record!.EntityId);
            Assert.Equal(1, record!.SpawnCount);
        }
    }
}
