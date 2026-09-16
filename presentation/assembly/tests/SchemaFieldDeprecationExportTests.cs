using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 消费方反馈第 46 条收口（验收报告"必须修项"根治，1.38.0）：<see cref="SchemaFieldDeprecationExport"/>
    /// 回归测试——既覆盖构造出来的最小 schema（自引用环、Map/Variants/Array 各分支、顶层与嵌套混合），
    /// 也对真实全量登记（<see cref="SchemaAudit.EnumerateRegisteredSchemas"/>）跑一遍，锁死
    /// <c>quest.def.rewards.xp</c>、<c>skill.def</c> 效果参数 <c>scaling_stat</c>/<c>coefficient</c>
    /// 这两处验收报告点名核实过、CLI 此前吐不出来的真实嵌套废弃字段（见
    /// <c>toolchain/validator/Program.cs</c> <c>PrintJson</c> 判断记录"deprecated_paths"）。
    /// </summary>
    public class SchemaFieldDeprecationExportTests
    {
        private static TableSchema SingleFieldTable(string tableName, FieldSchema field) =>
            new TableSchema(tableName, "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                field,
            });

        [Fact]
        public void Collect_TopLevelDeprecatedField_ReturnsBareFieldNamePath()
        {
            // ReplacedBy 必须能在同级兄弟字段清单里找到（FieldSchema.ValidateDeprecatedReplacedBy，
            // 消费方反馈第 46 条），这里额外登记一个 "new_value" 顶层字段作为替代目标。
            var field = new FieldSchema("legacy_value", FieldKind.Number, required: false, description: "旧字段")
                .WithDeprecated("1.20.0", "new_value", "迁移说明");
            var replacement = new FieldSchema("new_value", FieldKind.Number, required: false, description: "替代字段");
            var schema = new TableSchema("test.top_level", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                field,
                replacement,
            });

            var result = SchemaFieldDeprecationExport.Collect(schema);

            var hit = Assert.Single(result.Where(r => r.FieldPath == "legacy_value"));
            Assert.Equal("1.20.0", hit.Since);
            Assert.Equal("new_value", hit.ReplacedBy);
            Assert.Equal("迁移说明", hit.Note);
        }

        [Fact]
        public void Collect_NestedObjectField_AppendsDotPath()
        {
            var nested = new FieldSchema("old_child", FieldKind.Number, required: false, description: "废弃子字段")
                .WithDeprecated("1.21.0", null);
            var replacement = new FieldSchema("new_child", FieldKind.Number, required: false, description: "替代子字段");
            var parent = new FieldSchema("container", FieldKind.Object, required: false,
                fields: new[] { nested, replacement }, description: "对象字段");
            var schema = SingleFieldTable("test.nested_object", parent);

            var result = SchemaFieldDeprecationExport.Collect(schema);

            var hit = Assert.Single(result);
            Assert.Equal("container.old_child", hit.FieldPath);
            Assert.Equal("1.21.0", hit.Since);
            Assert.Null(hit.ReplacedBy);
        }

        [Fact]
        public void Collect_ArrayItemField_AppendsBracketPath()
        {
            var itemChild = new FieldSchema("old_flag", FieldKind.Number, required: false, description: "废弃")
                .WithDeprecated("1.22.0", "new_flag");
            var replacement = new FieldSchema("new_flag", FieldKind.Number, required: false, description: "替代");
            var item = new FieldSchema("<entry>", FieldKind.Object, required: true,
                fields: new[] { itemChild, replacement }, description: "数组元素");
            var array = new FieldSchema("entries", FieldKind.Array, required: false, item: item, description: "数组字段");
            var schema = SingleFieldTable("test.array_item", array);

            var result = SchemaFieldDeprecationExport.Collect(schema);

            var hit = Assert.Single(result);
            Assert.Equal("entries[].old_flag", hit.FieldPath);
        }

        [Fact]
        public void Collect_MapValueField_AppendsStarBracketPath()
        {
            var valueChild = new FieldSchema("old_weight", FieldKind.Number, required: false, description: "废弃")
                .WithDeprecated("1.23.0", "new_weight");
            var replacement = new FieldSchema("new_weight", FieldKind.Number, required: false, description: "替代");
            var valueSchema = new FieldSchema("<value>", FieldKind.Object, required: true,
                fields: new[] { valueChild, replacement }, description: "映射值");
            var map = MapSchema.FreeKeyed("测试：键为局部自由字符串", valueSchema);
            var mapField = new FieldSchema("weights", FieldKind.Object, required: false, description: "映射字段")
                .WithMap(map);
            var schema = SingleFieldTable("test.map_value", mapField);

            var result = SchemaFieldDeprecationExport.Collect(schema);

            var hit = Assert.Single(result);
            Assert.Equal("weights[*].old_weight", hit.FieldPath);
        }

        [Fact]
        public void Collect_VariantCaseField_AppendsDiscriminatorBracePath()
        {
            var caseChild = new FieldSchema("old_param", FieldKind.Number, required: false, description: "废弃")
                .WithDeprecated("1.24.0", null, "case 内废弃字段");
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                ["kind_a"] = new[] { caseChild },
            };
            var variants = new VariantSchema("kind", cases);
            var variantField = new FieldSchema("payload", FieldKind.Object, required: false,
                variants: variants, description: "变体字段");
            var schema = SingleFieldTable("test.variant_case", variantField);

            var result = SchemaFieldDeprecationExport.Collect(schema);

            var hit = Assert.Single(result);
            Assert.Equal("payload{kind=kind_a}.old_param", hit.FieldPath);
            Assert.Equal("case 内废弃字段", hit.Note);
        }

        [Fact]
        public void Collect_SelfReferencingField_DoesNotInfiniteLoop()
        {
            // 判断记录同 SchemaFieldRangeExport 既有覆盖：自引用 Item 工厂（复用自身实例）不应导致
            // 无限递归——祖先链命中后直接停止展开，不重复计数。
            FieldSchema? self = null;
            var deprecatedLeaf = new FieldSchema("leaf", FieldKind.Number, required: false, description: "叶子")
                .WithDeprecated("1.25.0", null);
            self = new FieldSchema("<node>", FieldKind.Object, required: true,
                fields: new List<FieldSchema> { deprecatedLeaf },
                itemFactory: null, description: "自引用节点");
            var array = new FieldSchema("nodes", FieldKind.Array, required: false,
                itemFactory: () => self!, description: "自引用数组");
            var schema = SingleFieldTable("test.self_reference", array);

            var result = SchemaFieldDeprecationExport.Collect(schema);

            var hit = Assert.Single(result);
            Assert.Equal("nodes[].leaf", hit.FieldPath);
        }

        [Fact]
        public void Collect_UnschematizedTable_ReturnsEmpty()
        {
            var schema = TableSchema.Unschematized("test.unschematized", "id");

            var result = SchemaFieldDeprecationExport.Collect(schema);

            Assert.Empty(result);
        }

        [Fact]
        public void Collect_RealQuestDef_IncludesNestedRewardsXpPath()
        {
            // 验收报告"必须修项"核实场景：quest.def.rewards.xp 的 FieldSchema.IsDeprecated 已登记，
            // 但既有 --list-tables --json 的 field_meta 只覆盖顶层字段，读不到这个嵌套废弃字段——
            // 本用例断言新增的 deprecated_paths 能读到（见 Program.cs PrintJson 判断记录）。
            var schemas = SchemaAudit.EnumerateRegisteredSchemas();
            var questDef = schemas.Single(s => s.Name == "quest.def");

            var result = SchemaFieldDeprecationExport.Collect(questDef);

            var hit = result.SingleOrDefault(r => r.FieldPath == "rewards.xp");
            Assert.True(hit != null, "quest.def 的 deprecated_paths 里未找到 \"rewards.xp\"");
            Assert.Equal("1.34.0", hit!.Since);
            Assert.Equal("xp_equivalent", hit.ReplacedBy);
        }

        [Fact]
        public void Collect_RealSkillDef_IncludesNestedScalingStatAndCoefficientPaths()
        {
            // 同上，验收报告核实场景：skill.def 效果参数 scaling_stat/coefficient（DamageOrHealParams，
            // school_damage/heal 两个变体 case 各自展开一份，见 SkillSchemas.EffectsItemSchema 判断
            // 记录）——两个字段都挂在 "params" 之下，实际路径形如
            // "effects[]{kind=school_damage}.params.scaling_stat"。
            var schemas = SchemaAudit.EnumerateRegisteredSchemas();
            var skillDef = schemas.Single(s => s.Name == "skill.def");

            var result = SchemaFieldDeprecationExport.Collect(skillDef);
            var paths = result.Select(r => r.FieldPath).ToList();

            Assert.Contains("effects[]{kind=school_damage}.params.scaling_stat", paths);
            Assert.Contains("effects[]{kind=school_damage}.params.coefficient", paths);
            Assert.Contains("effects[]{kind=heal}.params.scaling_stat", paths);
            Assert.Contains("effects[]{kind=heal}.params.coefficient", paths);

            var scalingStatHit = result.Single(r => r.FieldPath == "effects[]{kind=school_damage}.params.scaling_stat");
            Assert.Equal("1.33.0", scalingStatHit.Since);
            Assert.Equal("scaling", scalingStatHit.ReplacedBy);
        }
    }
}
