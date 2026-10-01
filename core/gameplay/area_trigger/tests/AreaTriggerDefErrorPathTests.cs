using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Gameplay.AreaTrigger;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>
    /// T-M14（core 半，测试覆盖剩余项 2026-10-01）：<see cref="AreaTriggerDef.FromRecord"/> 与
    /// <see cref="AreaTriggerShapeJson.Parse"/> 的错误路径——缺必填字段、非法 shape.kind（解析器抛
    /// <see cref="ArgumentException"/>，由 <c>FromRecord</c> 包装成带 <c>shape</c> 字段的
    /// <see cref="DataFieldException"/>）、非法 trigger_type、按类型必填的 params 字段；以及
    /// 形状解析对缺省数值/缺失 center 的宽松取值、rect 全宽/全高折半的规则。
    /// </summary>
    public class AreaTriggerDefErrorPathTests
    {
        private const string Circle = "{\"kind\":\"circle\",\"radius\":3}";

        private static DataRecord Record(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(AreaTriggerSchemas.TriggerDef, "area.ep_sample", new Id("area.ep_sample"), raw);
        }

        private static string DefJson(string triggerType, string @params, string shape = Circle, bool includeShape = true) =>
            "{\"id\":\"area.ep_sample\",\"map_id\":\"world.ep_map\"" +
            (includeShape ? ",\"shape\":" + shape : "") +
            ",\"trigger_type\":\"" + triggerType + "\",\"params\":" + @params + "}";

        private static DataFieldException Fail(string json) =>
            Assert.Throws<DataFieldException>(() => AreaTriggerDef.FromRecord(Record(json)));

        // ------------------------------ AreaTriggerDef.FromRecord ------------------------------

        [Fact]
        public void FromRecord_NullRecord_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AreaTriggerDef.FromRecord(null!));
        }

        [Fact]
        public void FromRecord_MissingShape_ThrowsNamingShape()
        {
            Assert.Equal("shape", Fail(DefJson("quest_explore", "{}", includeShape: false)).Field);
        }

        [Theory]
        [InlineData("{}", "kind")]
        [InlineData("{\"kind\":4}", "kind")]
        [InlineData("{\"kind\":\"triangle\",\"radius\":1}", "triangle")]
        [InlineData("{\"kind\":\"Circle\",\"radius\":1}", "Circle")]
        public void FromRecord_BadShapeKind_IsWrappedAsDataFieldException_OnShape(string shape, string expectedInMessage)
        {
            var ex = Fail(DefJson("quest_explore", "{}", shape));

            Assert.Equal("shape", ex.Field);
            Assert.Contains(expectedInMessage, ex.Message);
        }

        [Fact]
        public void FromRecord_UnknownTriggerType_ThrowsListingLegalValues()
        {
            var ex = Fail(DefJson("teleport_everyone", "{}"));

            Assert.Equal("trigger_type", ex.Field);
            foreach (var type in (AreaTriggerType[])Enum.GetValues(typeof(AreaTriggerType)))
            {
                Assert.Contains(AreaTriggerTypeNames.ToText(type), ex.Message);
            }
        }

        [Fact]
        public void FromRecord_MissingTriggerTypeOrParams_Throws()
        {
            var noType = "{\"id\":\"area.ep_sample\",\"map_id\":\"world.ep_map\",\"shape\":" + Circle + ",\"params\":{}}";
            var noParams = "{\"id\":\"area.ep_sample\",\"map_id\":\"world.ep_map\",\"shape\":" + Circle + ",\"trigger_type\":\"quest_explore\"}";

            Assert.Equal("trigger_type", Fail(noType).Field);
            Assert.Equal("params", Fail(noParams).Field);
        }

        [Theory]
        [InlineData("map_transition", "{}", "params.target_map")]
        [InlineData("map_transition", "{\"target_map\":\"bad id\"}", "params.target_map")]
        [InlineData("map_transition", "{\"target_map\":7}", "params.target_map")]
        [InlineData("encounter_start", "{}", "params.encounter_ref")]
        [InlineData("encounter_start", "{\"encounter_ref\":false}", "params.encounter_ref")]
        [InlineData("script", "{}", "params.hook_id")]
        [InlineData("script", "{\"hook_id\":\"bad id\"}", "params.hook_id")]
        public void FromRecord_TypeSpecificRequiredParamMissingOrMalformed_ThrowsNamingTheParamPath(
            string triggerType, string @params, string expectedField)
        {
            Assert.Equal(expectedField, Fail(DefJson(triggerType, @params)).Field);
        }

        [Fact]
        public void FromRecord_ParamsOfOtherTypesAreNotRequired_AndQuestExploreAcceptsEmptyParams()
        {
            var quest = AreaTriggerDef.FromRecord(Record(DefJson("quest_explore", "{}")));

            Assert.Equal(AreaTriggerType.QuestExplore, quest.TriggerType);
            Assert.Null(quest.MapTransition);
            Assert.Null(quest.EncounterStart);
            Assert.Null(quest.Script);
        }

        [Fact]
        public void FromRecord_MalformedOptionalSpawnPoint_IsIgnoredNotRejected()
        {
            var def = AreaTriggerDef.FromRecord(Record(DefJson("map_transition", "{\"target_map\":\"world.other\",\"spawn_point\":\"bad id\"}")));

            Assert.Null(def.MapTransition!.Value.SpawnPoint);
        }

        [Fact]
        public void FromRecord_ErrorCarriesTableAndRecordKey()
        {
            var ex = Fail(DefJson("script", "{}"));

            Assert.Equal(AreaTriggerSchemas.TriggerDef.Name, ex.Table);
            Assert.Equal("area.ep_sample", ex.RecordKey);
        }

        // ------------------------------ AreaTriggerShapeJson.Parse ------------------------------

        private static Shape Shape(string json) => AreaTriggerShapeJson.Parse((JsonObject)JsonReader.Parse(json));

        [Fact]
        public void ShapeParse_NullObject_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AreaTriggerShapeJson.Parse(null!));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"kind\":null}")]
        [InlineData("{\"kind\":1}")]
        [InlineData("{\"kind\":\"hexagon\"}")]
        public void ShapeParse_MissingOrIllegalKind_ThrowsArgumentException(string json)
        {
            Assert.Throws<ArgumentException>(() => Shape(json));
        }

        [Fact]
        public void ShapeParse_Circle_ReadsRadiusAndCenter_MissingCenterMeansOrigin()
        {
            var withCenter = Shape("{\"kind\":\"circle\",\"radius\":4,\"center\":{\"x\":2,\"y\":-3}}");
            var noCenter = Shape(Circle);
            var partialCenter = Shape("{\"kind\":\"circle\",\"radius\":4,\"center\":{\"x\":2}}");

            Assert.Equal(ShapeKind.Circle, withCenter.Kind);
            Assert.Equal(4.0, withCenter.Radius);
            Assert.Equal(new Vec2(2, -3), withCenter.Origin);
            Assert.Equal(Vec2.Zero, noCenter.Origin);
            Assert.Equal(Vec2.Zero, partialCenter.Origin);
        }

        [Fact]
        public void ShapeParse_MissingOrNonNumericDimensions_DefaultToZero()
        {
            var circle = Shape("{\"kind\":\"circle\"}");
            var rect = Shape("{\"kind\":\"rect\",\"length\":\"long\"}");

            Assert.Equal(0.0, circle.Radius);
            Assert.Equal(Vec2.Zero, rect.HalfExtents);
        }

        [Fact]
        public void ShapeParse_RectLengthAndWidthAreFullExtents_HalvedIntoHalfExtents()
        {
            var rect = Shape("{\"kind\":\"rect\",\"length\":10,\"width\":4,\"rotation\":0.5,\"center\":{\"x\":1,\"y\":1}}");

            Assert.Equal(ShapeKind.Rect, rect.Kind);
            Assert.Equal(new Vec2(10 / 2.0, 4 / 2.0), rect.HalfExtents);
            Assert.Equal(0.5, rect.Rotation);
            Assert.Equal(new Vec2(1, 1), rect.Origin);
        }

        [Fact]
        public void ShapeParse_ConeAndLine_UseRotationAsDirection()
        {
            var cone = Shape("{\"kind\":\"cone\",\"radius\":6,\"angle\":1.2,\"rotation\":0.75}");
            var line = Shape("{\"kind\":\"line\",\"length\":8,\"width\":2,\"rotation\":-0.25}");

            Assert.Equal(ShapeKind.Cone, cone.Kind);
            Assert.Equal(0.75, cone.Direction);
            Assert.Equal(1.2, cone.Angle);
            Assert.Equal(6.0, cone.Radius);
            Assert.Equal(ShapeKind.Line, line.Kind);
            Assert.Equal(-0.25, line.Direction);
            Assert.Equal(8.0, line.Length);
            Assert.Equal(2.0, line.Width);
        }
    }
}
