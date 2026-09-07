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

        /// <summary>N03 复现与根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// 同图读档——旧实体从未被销毁重建（<c>GameplayAssembly.RestoreFromSlot</c> 判定目标地图与
        /// 当前地图相同就不切场景，见该方法判断记录），本用例用"同一个 <see cref="SpawnHost"/> 实例
        /// 在实体存活期间 Save 再 Load"复现这个场景（对照上面
        /// <see cref="SaveThenLoad_RoundTripsRespawnRemainingAndSpawnCount"/> 用的是两个不同实例，
        /// 那条覆盖的是"读档不凭空恢复一个新实体"，本条覆盖"读档不能弄丢一个仍然存活的旧实体"）。
        /// 旧实现 <c>Load</c> 无条件清空 <c>_entityToSpawn</c>，读档后这个仍然存活的旧实体的
        /// <c>NotifyDespawn</c> 查不到映射、直接被忽略——刷新点从此不会重新计时/重生，永久卡死。
        /// 修复后：读档保留当前存活实体与刷新点的映射，之后杀死它仍能正常触发 timer 倒计时。</summary>
        [Fact]
        public void SameHost_SaveThenLoadWhileEntityStillAlive_PreservesMappingSoDespawnStillTriggersRespawn()
        {
            var row = SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "timer", respawnTimer: 20);
            var bus = SpawnTestSupport.NewEventBus();
            var registry = SpawnTestSupport.BuildRegistry(bus, row);
            registry.LoadAll();
            var world = new Core.Gameplay.WorldState.WorldState(bus);
            var creatures = new FakeCreatureFactory(bus);
            var host = new SpawnHost(registry, world, creatures, bus, new FakeExprHostFactory());

            var entityId = host.ApplyForMap(new Id("world.sample_map")).Single();
            var recordBeforeLoad = host.GetSpawnRecord(new Id("spawn.sample_wolf"));
            Assert.Equal(entityId, recordBeforeLoad!.EntityId);

            // 同图读档：不切场景、不销毁任何实体，entityId 原样继续存活；只是把 spawn_state 段的
            // 簿记数字（此处与存活时一致，不构成变化）重新走一遍 Load。
            var persistable = (Core.Foundation.SaveSystem.IPersistable)host;
            var saved = persistable.Save();
            persistable.Load(saved);

            // 读档后旧实体仍然存活，映射应当保留——不是"读档恢复了实体"，是"读档没有弄丢已经
            // 存在的映射"。
            var recordAfterLoad = host.GetSpawnRecord(new Id("spawn.sample_wolf"));
            Assert.Equal(entityId, recordAfterLoad!.EntityId);

            // 之后杀死这个（读档动作从未触碰过的）旧实体，NotifyDespawn 必须仍能查到映射，正常进入
            // timer 倒计时，而不是被静默忽略。
            creatures.Despawn(entityId, "died");
            var recordAfterDespawn = host.GetSpawnRecord(new Id("spawn.sample_wolf"));
            Assert.Null(recordAfterDespawn!.EntityId);
            Assert.Equal(20, recordAfterDespawn.RespawnRemaining);

            host.Update(20); // 倒计时归零，Update 内部直接触发重生（见 Update 判断记录 timer 分支）
            var recordAfterRespawn = host.GetSpawnRecord(new Id("spawn.sample_wolf"));
            Assert.NotNull(recordAfterRespawn!.EntityId);
            Assert.NotEqual(entityId, recordAfterRespawn.EntityId); // 新实体，不是同一个 id
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
