using System.Linq;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Gameplay.AreaTrigger;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>
    /// ADR-0019 / F1b 落地形式：<c>AreaTriggerSchemas.ShapeSchema</c>（<c>Variants</c>，判别字段
    /// <c>kind</c>）/<c>AreaTriggerSchemas.ParamsSchema</c>（<c>Fields</c>）子结构登记的命中/坏形状
    /// 用例，以及"变体键集合与运行时合法集合一致"锁定测试（惯例同
    /// <c>Tests.Rules.Skill.SkillSchemaCoverageTests</c>）。
    /// <para>
    /// <c>ShapeKindRule_IllegalKind_ReportsError</c>/<c>ShapeKindRule_LegalKind_NoIssue</c>
    /// 两条用例从 <c>AreaTriggerValidationRuleTests</c> 迁移到本文件：原 <c>AreaTriggerShapeKindRule</c>
    /// 已整条退役（见 <c>AreaTriggerValidationRules.cs</c> 顶部退役记录），"shape.kind 合法性"这条
    /// 检查现在完全由 <c>DataRegistry</c> 加载期对 <c>Variants</c> 的内置 <c>variant_discriminator</c>
    /// 检查产生，不再需要注册任何 <c>IValidationRule</c> 即可复现同等断言（检查名从
    /// <c>area_trigger_shape_kind</c> 变为 <c>variant_discriminator</c>，字段路径变为 <c>shape.kind</c>）。
    /// </para>
    /// </summary>
    public sealed class AreaTriggerSchemaCoverageTests
    {
        private static ValidationReport LoadRows(params JsonObject[] rows)
        {
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus, rows);
            return registry.LoadAll();
        }

        // -----------------------------------------------------------------
        // shape：Variants 判别字段 kind（迁移自 AreaTriggerValidationRuleTests.ShapeKindRule_*）
        // -----------------------------------------------------------------

        [Fact]
        public void ShapeKindRule_IllegalKind_ReportsError()
        {
            var row = AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map");
            var builder = new JsonObjectBuilder();
            foreach (var kv in row)
            {
                builder.Add(kv.Key, kv.Key == "shape" ? J.O(("kind", J.S("triangle"))) : kv.Value);
            }

            var report = LoadRows(builder.Build());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator" && i.Field == "shape.kind"
                && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void ShapeKindRule_LegalKind_NoIssue()
        {
            var row = AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map");

            var report = LoadRows(row);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == "variant_discriminator");
        }

        [Fact]
        public void ShapeVariantKeys_MatchShapeKindValuesFullSet()
        {
            var shapeField = AreaTriggerSchemas.TriggerDef.GetField("shape");
            Assert.NotNull(shapeField);
            var variants = shapeField!.Variants;
            Assert.NotNull(variants);

            var registered = new System.Collections.Generic.HashSet<string>(variants!.Cases.Keys, System.StringComparer.Ordinal);
            var expected = new System.Collections.Generic.HashSet<string>(AreaTriggerSchemas.ShapeKindValues, System.StringComparer.Ordinal);

            Assert.Equal(expected, registered);
            Assert.Equal("kind", variants.Discriminator);
        }

        [Theory]
        [InlineData("cone")]
        [InlineData("line")]
        [InlineData("rect")]
        public void ShapeVariant_NonCircleKinds_WellFormed_Passes(string kind)
        {
            var shape = J.O(
                ("kind", J.S(kind)),
                ("center", J.O(("x", J.N(1)), ("y", J.N(2)))),
                ("rotation", J.N(0.5)),
                ("angle", J.N(0.7)),
                ("length", J.N(4)),
                ("width", J.N(2)),
                ("radius", J.N(3)));
            var row = J.O(
                ("id", J.S("area.sample_shape")),
                ("map_id", J.S("world.sample_map")),
                ("shape", shape),
                ("trigger_type", J.S("quest_explore")),
                ("params", J.O()));

            var report = LoadRows(row);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ShapeVariant_RadiusWrongType_ReportsFieldTypeWithNestedPath()
        {
            var row = J.O(
                ("id", J.S("area.sample_shape")),
                ("map_id", J.S("world.sample_map")),
                ("shape", J.O(("kind", J.S("circle")), ("radius", J.S("not_a_number")))),
                ("trigger_type", J.S("quest_explore")),
                ("params", J.O()));

            var report = LoadRows(row);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "shape.radius");
        }

        [Fact]
        public void ShapeVariant_MissingNumericFields_NoRequiredFieldIssue()
        {
            // 判断记录：AreaTriggerShapeJson.GetNumber/ParseCenter 对缺失字段一律兜底为 0/Vec2.Zero，
            // 不抛异常，因此 shape 的数值/center 子字段全部登记为非必填——只有 kind 仍强制存在。
            var row = J.O(
                ("id", J.S("area.sample_shape")),
                ("map_id", J.S("world.sample_map")),
                ("shape", J.O(("kind", J.S("circle")))),
                ("trigger_type", J.S("quest_explore")),
                ("params", J.O()));

            var report = LoadRows(row);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == "required_field" && i.Field != null && i.Field.StartsWith("shape."));
        }

        // -----------------------------------------------------------------
        // params：Fields（非 Variants，见 AreaTriggerSchemas.ParamsSchema 判断记录）
        // -----------------------------------------------------------------

        [Fact]
        public void ParamsFields_WellFormedMapTransition_Passes()
        {
            var row = AreaTriggerTestSupport.MapTransitionRow(
                "area.sample_door", "world.sample_map", "world.sample_target", spawnPoint: "world.sample_target.spawn_a");

            var report = LoadRows(row);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ParamsFields_TargetMapBadIdFormat_ReportsFieldTypeWithNestedPath()
        {
            var row = J.O(
                ("id", J.S("area.sample_door")),
                ("map_id", J.S("world.sample_map")),
                ("shape", AreaTriggerTestSupport.CircleShape(0, 0, 5)),
                ("trigger_type", J.S("map_transition")),
                ("params", J.O(("target_map", J.S("NOT VALID")))));

            var report = LoadRows(row);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "params.target_map");
        }

        [Fact]
        public void ParamsFields_QuestExploreEmptyParams_NoFieldTypeOrUnknownSubfieldIssue()
        {
            var row = AreaTriggerTestSupport.QuestExploreRow("area.sample_grove", "world.sample_map");

            var report = LoadRows(row);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == "unknown_subfield");
        }

        [Fact]
        public void ParamsSchema_AllCandidateSubfieldsAreOptional()
        {
            // 判断记录：params 的判别字段 trigger_type 与 params 平级、不在 params 对象内部，
            // VariantSchema 机制无法覆盖（见 AreaTriggerSchemas.ParamsSchema 判断记录）；因此改用
            // Fields 登记四种 trigger_type 用到的子字段并集，全部必须是非必填——"哪些字段按
            // trigger_type 必填"这条业务判断继续由 AreaTriggerParamsFieldGroupRule 独家负责，不与
            // 本登记的类型检查重复报告同一缺陷。
            var paramsField = AreaTriggerSchemas.TriggerDef.GetField("params");
            Assert.NotNull(paramsField);
            Assert.Null(paramsField!.Variants);
            Assert.NotNull(paramsField.Fields);
            Assert.All(paramsField.Fields!, f => Assert.False(f.Required));

            var names = paramsField.Fields!.Select(f => f.Name).ToList();
            Assert.Contains("target_map", names);
            Assert.Contains("spawn_point", names);
            Assert.Contains("encounter_ref", names);
            Assert.Contains("hook_id", names);
        }
    }
}
