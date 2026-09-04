using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Gameplay.AreaTrigger;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    public sealed class AreaTriggerDefTests
    {
        private static DataRecord LoadRecord(Core.Foundation.Common.Json.JsonObject row)
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus, row);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("\n", report.Issues.Select(i => i.ToString())));
            var id = ((Core.Foundation.Common.Json.JsonString)row["id"]).Value;
            return registry.Get(AreaTriggerSchemas.TriggerDef.Name, id)!;
        }

        [Fact]
        public void FromRecord_MapTransition_ParsesParams()
        {
            var def = AreaTriggerDef.FromRecord(LoadRecord(
                AreaTriggerTestSupport.MapTransitionRow("area.sample_door", "world.sample_map", "world.other_map", "tp.sample_entry")));

            Assert.Equal(AreaTriggerType.MapTransition, def.TriggerType);
            Assert.NotNull(def.MapTransition);
            Assert.Equal(new Id("world.other_map"), def.MapTransition!.Value.TargetMap);
            Assert.Equal(new Id("tp.sample_entry"), def.MapTransition.Value.SpawnPoint);
        }

        [Fact]
        public void FromRecord_MapTransitionWithoutSpawnPoint_SpawnPointIsNull()
        {
            var def = AreaTriggerDef.FromRecord(LoadRecord(
                AreaTriggerTestSupport.MapTransitionRow("area.sample_door", "world.sample_map", "world.other_map")));

            Assert.Null(def.MapTransition!.Value.SpawnPoint);
        }

        [Fact]
        public void FromRecord_EncounterStart_ParsesEncounterRef()
        {
            var def = AreaTriggerDef.FromRecord(LoadRecord(
                AreaTriggerTestSupport.EncounterStartRow("area.sample_boss", "world.sample_map", "encounter.sample_boss")));

            Assert.Equal(new Id("encounter.sample_boss"), def.EncounterStart!.Value.EncounterRef);
        }

        [Fact]
        public void FromRecord_Script_ParsesHookId()
        {
            var def = AreaTriggerDef.FromRecord(LoadRecord(
                AreaTriggerTestSupport.ScriptRow("area.sample_trap_zone", "world.sample_map", "found.hook.sample_area")));

            Assert.Equal(new Id("found.hook.sample_area"), def.Script!.Value.HookId);
        }

        [Fact]
        public void FromRecord_QuestExplore_HasNoParams()
        {
            var def = AreaTriggerDef.FromRecord(LoadRecord(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map")));

            Assert.Null(def.MapTransition);
            Assert.Null(def.EncounterStart);
            Assert.Null(def.Script);
        }

        [Fact]
        public void FromRecord_ShapeCircle_ParsesCenterAndRadius()
        {
            var def = AreaTriggerDef.FromRecord(LoadRecord(
                AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map")));

            Assert.Equal(Core.Foundation.EngineAdapter.ShapeKind.Circle, def.Shape.Kind);
            Assert.Equal(5, def.Shape.Radius);
        }
    }
}
