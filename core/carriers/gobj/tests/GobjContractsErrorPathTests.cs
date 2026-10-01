using System;
using Core.Carriers.Common;
using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>
    /// T-M14（core 半，测试覆盖剩余项 2026-10-01）：<see cref="GameObjectTemplate.FromRecord"/> 与
    /// <see cref="LockDef.FromRecord"/> 的错误路径——缺必填字段、类型不符、未知枚举取值抛
    /// <see cref="DataFieldException"/>，且异常携带表名/记录键/字段名便于定位。
    /// 这些解析器假设记录已过校验规则；此处直接构造"绕过校验"的坏记录，钉住解析层自身的兜底行为。
    /// 同时给出对应的正向基线（合法记录能解析），避免错误用例因基线本身坏掉而空转。
    /// </summary>
    public class GobjContractsErrorPathTests
    {
        private static string IdOf(JsonObject raw) => ((JsonString)raw["id"]).Value;

        private static DataRecord Template(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(GobjSchemas.Template, IdOf(raw), new Id(IdOf(raw)), raw);
        }

        private static DataRecord LockRecord(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(GobjSchemas.Lock, IdOf(raw), new Id(IdOf(raw)), raw);
        }

        private static string TemplateJson(string kind, string typeData, string extra = "") =>
            "{\"id\":\"gobj.cp_sample\",\"name_key\":\"l10n.gobj.cp_sample.name\",\"kind\":\"" + kind + "\"," +
            "\"type_data\":" + typeData + ",\"display_ref\":\"display.cp_sample\"" + extra + "}";

        private static DataFieldException Fail(Action action) => Assert.Throws<DataFieldException>(action);

        // ------------------------------ GameObjectTemplate ------------------------------

        [Fact]
        public void Template_ValidBaseline_Parses()
        {
            var t = GameObjectTemplate.FromRecord(Template(TemplateJson("sign", "{\"text_key\":\"l10n.cp.text\"}")));

            Assert.Equal(new Id("gobj.cp_sample"), t.Id);
            Assert.Equal(GobjKind.Sign, t.Kind);
        }

        [Theory]
        [InlineData("name_key")]
        [InlineData("kind")]
        [InlineData("type_data")]
        [InlineData("display_ref")]
        public void Template_MissingTopLevelRequiredField_ThrowsDataFieldException_NamingTheField(string missing)
        {
            var raw = (JsonObject)JsonReader.Parse(TemplateJson("sign", "{\"text_key\":\"l10n.cp.text\"}"));
            var builder = new JsonObjectBuilder();
            foreach (var pair in raw)
            {
                if (pair.Key != missing) builder.Add(pair.Key, pair.Value);
            }

            var record = new DataRecord(GobjSchemas.Template, "gobj.cp_sample", new Id("gobj.cp_sample"), builder.Build());

            var ex = Fail(() => GameObjectTemplate.FromRecord(record));
            Assert.Equal(missing, ex.Field);
            Assert.Equal("gobj.cp_sample", ex.RecordKey);
        }

        [Fact]
        public void Template_UnknownKind_ThrowsArgumentException_NamingTheText()
        {
            var record = Template(TemplateJson("nonexistent_kind", "{}"));

            var ex = Assert.Throws<ArgumentException>(() => GameObjectTemplate.FromRecord(record));

            Assert.Contains("nonexistent_kind", ex.Message);
        }

        [Fact]
        public void Template_KindFieldNotAString_ThrowsDataFieldException()
        {
            var record = Template(
                "{\"id\":\"gobj.cp_sample\",\"name_key\":\"l10n.x\",\"kind\":7,\"type_data\":{},\"display_ref\":\"display.x\"}");

            Assert.Equal("kind", Fail(() => GameObjectTemplate.FromRecord(record)).Field);
        }

        [Theory]
        [InlineData("chest", "{}", "loot_table_ref")]
        [InlineData("chest", "{\"loot_table_ref\":123}", "loot_table_ref")]
        [InlineData("chest", "{\"loot_table_ref\":\"not an id\"}", "loot_table_ref")]
        [InlineData("quest_object", "{}", "quest_action_ref")]
        [InlineData("trap", "{\"skill_id\":\"skill.cp\"}", "trigger_shape")]
        [InlineData("trap", "{\"skill_id\":\"skill.cp\",\"trigger_shape\":\"circle\"}", "trigger_shape")]
        [InlineData("trap", "{\"trigger_shape\":{}}", "skill_id")]
        [InlineData("spell_focus", "{}", "required_skill_tag")]
        [InlineData("gather_node", "{\"loot_table_ref\":\"loot.cp\"}", "respawn_after_use")]
        [InlineData("gather_node", "{\"loot_table_ref\":\"loot.cp\",\"respawn_after_use\":\"soon\"}", "respawn_after_use")]
        [InlineData("teleporter", "{}", "teleport_target_ref")]
        [InlineData("lever", "{}", "linked_object_ids")]
        [InlineData("lever", "{\"linked_object_ids\":\"gobj.a\"}", "linked_object_ids")]
        [InlineData("lever", "{\"linked_object_ids\":[\"gobj.a\",5]}", "linked_object_ids")]
        [InlineData("sign", "{}", "text_key")]
        [InlineData("sign", "{\"text_key\":42}", "text_key")]
        public void Template_TypeDataMissingOrMistypedRequiredProperty_ThrowsDataFieldException_OnTypeData(
            string kind, string typeData, string property)
        {
            var record = Template(TemplateJson(kind, typeData));

            var ex = Fail(() => GameObjectTemplate.FromRecord(record));

            Assert.Equal("type_data", ex.Field);
            Assert.Contains(property, ex.Message);
        }

        [Fact]
        public void Template_LeverWithBadIdElement_NamesTheElementIndex()
        {
            var record = Template(TemplateJson("lever", "{\"linked_object_ids\":[\"gobj.a\",\"gobj.b\",5]}"));

            var ex = Fail(() => GameObjectTemplate.FromRecord(record));

            Assert.Contains("2", ex.Message);
        }

        [Fact]
        public void Template_OptionalLockIdInTypeData_NullIsAccepted_ButMalformedIsRejected()
        {
            var nullLock = GameObjectTemplate.FromRecord(Template(TemplateJson("door", "{\"lock_id\":null}")));
            var absentLock = GameObjectTemplate.FromRecord(Template(TemplateJson("door", "{}")));
            Assert.NotNull(nullLock);
            Assert.NotNull(absentLock);

            var bad = Template(TemplateJson("door", "{\"lock_id\":12}"));
            Assert.Equal("type_data", Fail(() => GameObjectTemplate.FromRecord(bad)).Field);
        }

        [Theory]
        [InlineData("{}", "kind")]
        [InlineData("{\"kind\":3,\"ref\":\"skill.cp\"}", "kind")]
        [InlineData("{\"kind\":\"explode\",\"ref\":\"skill.cp\"}", "explode")]
        [InlineData("{\"kind\":\"skill\"}", "ref")]
        [InlineData("{\"kind\":\"dialog\",\"ref\":\"not an id\"}", "ref")]
        public void Template_OnUseMalformed_ThrowsDataFieldException_OnOnUse(string onUse, string expectedInMessage)
        {
            var record = Template(TemplateJson("sign", "{\"text_key\":\"l10n.cp.text\"}", ",\"on_use\":" + onUse));

            var ex = Fail(() => GameObjectTemplate.FromRecord(record));

            Assert.Equal("on_use", ex.Field);
            Assert.Contains(expectedInMessage, ex.Message);
        }

        // ------------------------------ LockDef ------------------------------

        private static string LockJson(string requirement, string extra = "") =>
            "{\"id\":\"gobj.lock.cp_sample\",\"requirement\":" + requirement + extra + "}";

        [Fact]
        public void Lock_ValidBaselines_ParseForAllThreeKinds()
        {
            var key = LockDef.FromRecord(LockRecord(LockJson("{\"kind\":\"item_key\",\"item_id\":\"item.cp_key\"}", ",\"consume_key\":true")));
            var flag = LockDef.FromRecord(LockRecord(LockJson("{\"kind\":\"world_flag\",\"flag_key\":\"world.flag.cp\",\"expected\":3}")));
            var check = LockDef.FromRecord(LockRecord(LockJson("{\"kind\":\"skill_check\",\"skill_tag\":\"skill.tag.cp\",\"min_value\":2.5}")));

            Assert.Equal(LockRequirementKind.ItemKey, key.Requirement.Kind);
            Assert.True(key.ConsumeKey);
            Assert.Equal(LockRequirementKind.WorldFlag, flag.Requirement.Kind);
            Assert.False(flag.ConsumeKey);
            Assert.Equal(LockRequirementKind.SkillCheck, check.Requirement.Kind);
        }

        [Fact]
        public void Lock_MissingRequirement_ThrowsDataFieldException_NamingRequirement()
        {
            var record = LockRecord("{\"id\":\"gobj.lock.cp_sample\"}");

            Assert.Equal("requirement", Fail(() => LockDef.FromRecord(record)).Field);
        }

        [Theory]
        [InlineData("{}", "kind")]
        [InlineData("{\"kind\":5}", "kind")]
        [InlineData("{\"kind\":\"retina_scan\"}", "retina_scan")]
        [InlineData("{\"kind\":\"item_key\"}", "item_id")]
        [InlineData("{\"kind\":\"item_key\",\"item_id\":\"not an id\"}", "item_id")]
        [InlineData("{\"kind\":\"world_flag\",\"expected\":true}", "flag_key")]
        [InlineData("{\"kind\":\"world_flag\",\"flag_key\":\"world.flag.cp\"}", "expected")]
        [InlineData("{\"kind\":\"world_flag\",\"flag_key\":\"world.flag.cp\",\"expected\":\"yes\"}", "expected")]
        [InlineData("{\"kind\":\"skill_check\",\"min_value\":1}", "skill_tag")]
        [InlineData("{\"kind\":\"skill_check\",\"skill_tag\":\"skill.tag.cp\"}", "min_value")]
        [InlineData("{\"kind\":\"skill_check\",\"skill_tag\":\"skill.tag.cp\",\"min_value\":\"high\"}", "min_value")]
        public void Lock_MalformedRequirement_ThrowsDataFieldException_OnRequirement(string requirement, string expectedInMessage)
        {
            var record = LockRecord(LockJson(requirement));

            var ex = Fail(() => LockDef.FromRecord(record));

            Assert.Equal("requirement", ex.Field);
            Assert.Contains(expectedInMessage, ex.Message);
        }
    }
}
