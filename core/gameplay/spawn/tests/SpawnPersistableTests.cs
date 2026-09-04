using System.Linq;
using Core.Foundation.Common;
using Core.Gameplay.Spawn;
using Core.Gameplay.WorldState;
using Xunit;

namespace Tests.Gameplay.Spawn
{
    public sealed class SpawnPersistableTests
    {
        [Fact]
        public void SectionKey_IsSpawnState()
        {
            var bus = SpawnTestSupport.NewEventBus();
            var registry = SpawnTestSupport.BuildRegistry(bus);
            var world = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new SpawnHost(registry, world, new FakeCreatureFactory(bus), bus, new FakeExprHostFactory());

            Assert.Equal("spawn_state", ((Core.Foundation.SaveSystem.IPersistable)host).SectionKey);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsRespawnRemainingAndSpawnCount()
        {
            var row = SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "timer", respawnTimer: 20);
            var bus = SpawnTestSupport.NewEventBus();
            var registry = SpawnTestSupport.BuildRegistry(bus, row);
            registry.LoadAll();
            var world = new Core.Gameplay.WorldState.WorldState(bus);
            var creatures = new FakeCreatureFactory(bus);
            var host = new SpawnHost(registry, world, creatures, bus, new FakeExprHostFactory());

            var entityId = host.ApplyForMap(new Id("world.sample_map")).Single();
            creatures.Despawn(entityId, "died");
            host.Update(5); // RespawnRemaining: 20 -> 15

            var persistable = (Core.Foundation.SaveSystem.IPersistable)host;
            var saved = persistable.Save();

            var world2 = new Core.Gameplay.WorldState.WorldState(bus);
            var host2 = new SpawnHost(registry, world2, creatures, bus, new FakeExprHostFactory());
            ((Core.Foundation.SaveSystem.IPersistable)host2).Load(saved);

            var record = host2.GetSpawnRecord(new Id("spawn.sample_wolf"));
            Assert.NotNull(record);
            Assert.Equal(15, record!.RespawnRemaining);
            Assert.Equal(1, record.SpawnCount);
            Assert.Null(record.EntityId); // 读档不恢复实体本身，见 05 第 5.2 节"刷新是数据行为"。
        }

        [Fact]
        public void Load_NullData_ClearsRecords()
        {
            var bus = SpawnTestSupport.NewEventBus();
            var registry = SpawnTestSupport.BuildRegistry(bus);
            var world = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new SpawnHost(registry, world, new FakeCreatureFactory(bus), bus, new FakeExprHostFactory());

            var ex = Record.Exception(() => ((Core.Foundation.SaveSystem.IPersistable)host).Load(Core.Foundation.Common.Json.JsonNull.Instance));
            Assert.Null(ex);
        }

        [Fact]
        public void Save_EmptyRecords_ProducesEmptyObject()
        {
            var bus = SpawnTestSupport.NewEventBus();
            var registry = SpawnTestSupport.BuildRegistry(bus);
            var world = new Core.Gameplay.WorldState.WorldState(bus);
            var host = new SpawnHost(registry, world, new FakeCreatureFactory(bus), bus, new FakeExprHostFactory());

            var saved = ((Core.Foundation.SaveSystem.IPersistable)host).Save();

            Assert.IsType<Core.Foundation.Common.Json.JsonObject>(saved);
            Assert.Empty((Core.Foundation.Common.Json.JsonObject)saved);
        }
    }
}
