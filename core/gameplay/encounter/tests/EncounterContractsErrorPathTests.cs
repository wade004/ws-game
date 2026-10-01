using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Encounter;
using Xunit;

namespace Tests.Gameplay.Encounter
{
    /// <summary>
    /// T-M14（core 半，测试覆盖剩余项 2026-10-01）：<see cref="EncounterDefinition.FromRecord"/> 与
    /// <see cref="EncounterLevelDefinition.FromRecord"/> 的错误路径——空单位表、元素形状不符、坏 Id、
    /// 构造期互斥规则被包装为 <see cref="DataFieldException"/>（并带出数组下标路径），缺必填字段等。
    /// 解析器假设记录已过校验规则；这里直接用"绕过校验"的坏记录钉住解析层兜底。
    /// </summary>
    public class EncounterContractsErrorPathTests
    {
        private const string GoodUnit = "{\"template_ref\":\"creature.ec_wolf\"}";

        private static DataRecord Def(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(EncounterSchemas.Def, "encounter.ec_sample", new Id("encounter.ec_sample"), raw);
        }

        private static string DefJson(
            string units = "[" + GoodUnit + "]",
            string extra = "",
            bool withVictory = true,
            bool withDefeat = true) =>
            "{\"id\":\"encounter.ec_sample\",\"units\":" + units +
            (withVictory ? ",\"victory_condition\":\"all_dead\"" : "") +
            (withDefeat ? ",\"defeat_condition\":\"player_dead\"" : "") + extra + "}";

        private static DataFieldException Fail(DataRecord record) =>
            Assert.Throws<DataFieldException>(() => EncounterDefinition.FromRecord(record));

        [Fact]
        public void Def_ValidBaseline_Parses()
        {
            var def = EncounterDefinition.FromRecord(Def(DefJson()));

            Assert.Equal(new Id("encounter.ec_sample"), def.Id);
            Assert.Single(def.Units);
        }

        [Fact]
        public void Def_EmptyUnits_Throws_NamingUnits()
        {
            Assert.Equal("units", Fail(Def(DefJson(units: "[]"))).Field);
        }

        [Fact]
        public void Def_MissingUnitsOrConditions_ThrowsOnThatField()
        {
            Assert.Equal("units", Fail(Def("{\"id\":\"encounter.ec_sample\",\"victory_condition\":\"a\",\"defeat_condition\":\"b\"}")).Field);
            Assert.Equal("victory_condition", Fail(Def(DefJson(withVictory: false))).Field);
            Assert.Equal("defeat_condition", Fail(Def(DefJson(withDefeat: false))).Field);
        }

        [Theory]
        [InlineData("[\"not an object\"]", "units[0]")]
        [InlineData("[" + GoodUnit + ",5]", "units[1]")]
        [InlineData("[{\"spawn_ref\":\"not an id\"}]", "units[0].spawn_ref")]
        [InlineData("[{\"template_ref\":\"not an id\"}]", "units[0].template_ref")]
        public void Def_BadUnitShape_ThrowsWithIndexedFieldPath(string units, string expectedField)
        {
            Assert.Equal(expectedField, Fail(Def(DefJson(units: units))).Field);
        }

        [Theory]
        [InlineData("[{}]")]
        [InlineData("[{\"spawn_ref\":\"spawn.ec_a\",\"template_ref\":\"creature.ec_wolf\"}]")]
        public void Def_UnitWithNeitherOrBothRefs_IsWrappedAsDataFieldException_OnThatUnit(string units)
        {
            var ex = Fail(Def(DefJson(units: units)));

            Assert.Equal("units[0]", ex.Field);
            Assert.Contains("二选一", ex.Message);
        }

        [Theory]
        [InlineData(",\"waves\":[3]", "waves[0]")]
        [InlineData(",\"waves\":[{\"spawn_refs\":[]}]", "waves[0].trigger_condition")]
        [InlineData(",\"waves\":[{\"trigger_condition\":7}]", "waves[0].trigger_condition")]
        [InlineData(",\"waves\":[{\"trigger_condition\":\"true\",\"spawn_refs\":[\"spawn.ec_a\",\"bad id\"]}]", "waves[0].spawn_refs[1]")]
        [InlineData(",\"waves\":[{\"trigger_condition\":\"true\",\"spawn_refs\":[9]}]", "waves[0].spawn_refs[0]")]
        public void Def_BadWave_ThrowsWithIndexedFieldPath(string extra, string expectedField)
        {
            Assert.Equal(expectedField, Fail(Def(DefJson(extra: extra))).Field);
        }

        [Theory]
        [InlineData(",\"phases\":[\"x\"]", "phases[0]")]
        [InlineData(",\"phases\":[{}]", "phases[0].enter_condition")]
        [InlineData(",\"phases\":[{\"enter_condition\":1}]", "phases[0].enter_condition")]
        [InlineData(",\"phases\":[{\"enter_condition\":\"true\",\"on_enter_hook\":\"not an id\"}]", "phases[0].on_enter_hook")]
        [InlineData(",\"phases\":[{\"enter_condition\":\"true\",\"ai_rotation_override\":{\"ai.rotation.a\":5}}]", "phases[0].ai_rotation_override")]
        [InlineData(",\"phases\":[{\"enter_condition\":\"true\",\"ai_rotation_override\":{\"bad key\":\"ai.rotation.b\"}}]", "phases[0].ai_rotation_override")]
        public void Def_BadPhase_ThrowsWithIndexedFieldPath(string extra, string expectedField)
        {
            Assert.Equal(expectedField, Fail(Def(DefJson(extra: extra))).Field);
        }

        [Fact]
        public void Def_ArenaRulesWithoutBoundsShape_Throws()
        {
            var ex = Fail(Def(DefJson(extra: ",\"arena_rules\":{\"reset_if_leave\":true}")));

            Assert.Equal("arena_rules.bounds_shape", ex.Field);
        }

        [Fact]
        public void Def_ArenaRulesBoundsShapeNotAnObject_Throws()
        {
            var ex = Fail(Def(DefJson(extra: ",\"arena_rules\":{\"bounds_shape\":\"circle\"}")));

            Assert.Equal("arena_rules.bounds_shape", ex.Field);
        }

        [Fact]
        public void Def_ErrorCarriesTableAndRecordKey()
        {
            var ex = Fail(Def(DefJson(units: "[]")));

            Assert.Equal("encounter.def", ex.Table);
            Assert.Equal("encounter.ec_sample", ex.RecordKey);
        }

        // ------------------------------ EncounterLevelDefinition ------------------------------

        private static DataRecord Level(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(EncounterSchemas.Level, "encounter.level.ec_sample", new Id("encounter.level.ec_sample"), raw);
        }

        [Fact]
        public void Level_ValidBaseline_ParsesWithDefaultEmptyEntryOptions()
        {
            var def = EncounterLevelDefinition.FromRecord(Level(
                "{\"id\":\"encounter.level.ec_sample\",\"map_ref\":\"world.ec_map\",\"encounter_sequence\":[\"encounter.ec_a\"]}"));

            Assert.Single(def.EncounterSequence);
            Assert.Empty(def.EntryDifficultyOptions);
        }

        [Fact]
        public void Level_EmptySequence_Throws_NamingEncounterSequence()
        {
            var ex = Assert.Throws<DataFieldException>(() => EncounterLevelDefinition.FromRecord(Level(
                "{\"id\":\"encounter.level.ec_sample\",\"map_ref\":\"world.ec_map\",\"encounter_sequence\":[]}")));

            Assert.Equal("encounter_sequence", ex.Field);
        }

        [Theory]
        [InlineData("{\"id\":\"encounter.level.ec_sample\",\"encounter_sequence\":[\"encounter.ec_a\"]}", "map_ref")]
        [InlineData("{\"id\":\"encounter.level.ec_sample\",\"map_ref\":\"world.ec_map\"}", "encounter_sequence")]
        [InlineData("{\"id\":\"encounter.level.ec_sample\",\"map_ref\":\"bad id\",\"encounter_sequence\":[\"encounter.ec_a\"]}", "map_ref")]
        [InlineData("{\"id\":\"encounter.level.ec_sample\",\"map_ref\":\"world.ec_map\",\"encounter_sequence\":\"encounter.ec_a\"}", "encounter_sequence")]
        public void Level_MissingOrMistypedRequiredField_ThrowsNamingTheField(string json, string field)
        {
            var ex = Assert.Throws<DataFieldException>(() => EncounterLevelDefinition.FromRecord(Level(json)));

            Assert.Equal(field, ex.Field);
        }

        [Fact]
        public void Level_NullRecord_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => EncounterLevelDefinition.FromRecord(null!));
        }
    }
}
