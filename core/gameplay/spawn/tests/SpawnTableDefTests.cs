using System.Linq;
using Core.Foundation.Common;
using Core.Gameplay.Spawn;
using Xunit;

namespace Tests.Gameplay.Spawn
{
    public sealed class SpawnTableDefTests
    {
        private static Core.Foundation.DataRegistry.DataRecord LoadRecord(Core.Foundation.Common.Json.JsonObject row)
        {
            var bus = SpawnTestSupport.NewEventBus();
            var registry = SpawnTestSupport.BuildRegistry(bus, row);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("\n", report.Issues.Select(i => i.ToString())));
            var id = ((Core.Foundation.Common.Json.JsonString)row["id"]).Value;
            return registry.Get(SpawnSchemas.Table.Name, id)!;
        }

        [Fact]
        public void FromRecord_ParsesBasicFields()
        {
            var def = SpawnTableDef.FromRecord(LoadRecord(
                SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "timer", x: 3, y: 4, facing: 1.5, respawnTimer: 30)));

            Assert.Equal(new Id("spawn.sample_wolf"), def.Id);
            Assert.Equal(new Id("world.sample_map"), def.MapId);
            Assert.Equal(new Id("creature.sample_wolf"), def.ContentRef);
            Assert.Equal(3, def.Position.X);
            Assert.Equal(4, def.Position.Y);
            Assert.Equal(1.5, def.Facing);
            Assert.Equal(RespawnPolicy.Timer, def.RespawnPolicy);
            Assert.Equal(30, def.RespawnTimer);
        }

        [Fact]
        public void FromRecord_FacingDefaultsToZero()
        {
            var def = SpawnTableDef.FromRecord(LoadRecord(
                SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter")));

            Assert.Equal(0, def.Facing);
        }

        [Fact]
        public void FromRecord_ConditionAbsent_IsNull()
        {
            var def = SpawnTableDef.FromRecord(LoadRecord(
                SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter")));

            Assert.Null(def.ConditionText);
        }

        [Fact]
        public void FromRecord_GobjContentRef_DomainIsGobj()
        {
            var def = SpawnTableDef.FromRecord(LoadRecord(
                SpawnTestSupport.Row("spawn.sample_chest", "world.sample_map", "gobj.sample_chest", "once")));

            Assert.Equal("gobj", def.ContentRef.Domain);
        }
    }
}
