using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 测试覆盖梳理 T-M28：<see cref="SchemaFieldItemCountExport.Collect"/> 的直接用例（生产被
    /// <c>toolchain/validator/Program.cs</c> 的 <c>--list-tables --json</c> 使用）。构造最小 schema 覆盖
    /// 顶层/嵌套/映射/变体路径记法、只设下限/双设/下限为 0、自引用环、深度上限、错误输入，并对真实全量登记
    /// 跑一遍。期望值由 schema 对象本身算出，不写裸数。
    /// </summary>
    public class SchemaFieldItemCountExportTests
    {
        private static TableSchema Table(string name, params FieldSchema[] fields) =>
            new TableSchema(name, "id", 1, new[] { new FieldSchema("id", FieldKind.Id, required: true, description: "主键") }
                .Concat(fields).ToArray());

        private static FieldSchema IdList(string name, int? min, int? max)
        {
            var field = new FieldSchema(name, FieldKind.IdList, required: false, description: name);
            return min.HasValue ? field.WithItemCount(min.Value, max) : field;
        }

        private static FieldSchema StringArray(string name, int min, int? max = null) =>
            new FieldSchema(name, FieldKind.Array, required: false,
                item: new FieldSchema("<s>", FieldKind.String, required: true, description: "元素"), description: name)
            .WithItemCount(min, max);

        [Fact]
        public void Collect_TopLevelIdListAndArray_ReturnMinMaxAsDeclared_InFieldOrder()
        {
            var both = IdList("members", 1, 4);
            var minOnly = StringArray("tags", min: 2);
            var none = IdList("optional", null, null);
            var schema = Table("test.count_top", both, none, minOnly);

            var result = SchemaFieldItemCountExport.Collect(schema);

            Assert.Equal(new[] { "members", "tags" }, result.Select(r => r.FieldPath));
            Assert.Equal(new[] { FieldKind.IdList.ToString(), FieldKind.Array.ToString() }, result.Select(r => r.Kind));
            Assert.Equal(both.MinItems, result[0].MinItems);
            Assert.Equal(both.MaxItems, result[0].MaxItems);
            Assert.Equal(minOnly.MinItems, result[1].MinItems);
            Assert.Null(result[1].MaxItems); // 只设下限：上限保持 null，不被补成某个默认值。
        }

        [Fact]
        public void Collect_ZeroMinimumWithMaximum_IsStillReported()
        {
            // min=0 是合法取值（只约束上限）：MinItems.HasValue 为真，必须出现在导出里，而不是被当成"未设置"。
            var cap = IdList("capped", 0, 3);

            var hit = Assert.Single(SchemaFieldItemCountExport.Collect(Table("test.count_zero_min", cap)));

            Assert.Equal("capped", hit.FieldPath);
            Assert.Equal(cap.MinItems, hit.MinItems);
            Assert.Equal(cap.MaxItems, hit.MaxItems);
            Assert.True(hit.MinItems.HasValue);
        }

        [Fact]
        public void Collect_SchemaWithoutAnyItemCount_ReturnsEmpty()
        {
            var plain = new FieldSchema("plain", FieldKind.Array, required: false,
                item: new FieldSchema("<s>", FieldKind.String, required: true, description: "元素"), description: "无约束数组");

            Assert.Empty(SchemaFieldItemCountExport.Collect(Table("test.count_none", plain, IdList("ids", null, null))));
        }

        [Fact]
        public void Collect_NestedObjectArrayMapAndVariants_UseDocumentedPathNotation()
        {
            var inObject = IdList("ids", 1, null);
            var obj = new FieldSchema("holder", FieldKind.Object, required: false, fields: new[] { inObject }, description: "对象");

            var inArrayItem = IdList("refs", 2, 2);
            var arrayItem = new FieldSchema("<entry>", FieldKind.Object, required: true, fields: new[] { inArrayItem }, description: "元素");
            var array = new FieldSchema("entries", FieldKind.Array, required: false, item: arrayItem, description: "数组");

            var inMapValue = IdList("members", 1, 8);
            var mapValue = new FieldSchema("<value>", FieldKind.Object, required: true, fields: new[] { inMapValue }, description: "映射值");
            var mapField = new FieldSchema("groups", FieldKind.Object, required: false, description: "映射")
                .WithMap(MapSchema.FreeKeyed("测试：键为局部自由字符串", mapValue));

            var inCase = IdList("targets", 1, null);
            var inCommon = IdList("shared", 0, 5);
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                ["kind_a"] = new[] { inCase },
            };
            var variant = new FieldSchema("payload", FieldKind.Object, required: false,
                variants: new VariantSchema("kind", cases, commonFields: new[] { inCommon }), description: "变体");

            var result = SchemaFieldItemCountExport.Collect(Table("test.count_nested", obj, array, mapField, variant));
            var paths = result.Select(r => r.FieldPath).ToList();

            Assert.Equal(
                new[] { "holder.ids", "entries[].refs", "groups[*].members", "payload{kind=kind_a}.targets", "payload.shared" },
                paths);
            var byPath = result.ToDictionary(r => r.FieldPath);
            Assert.Equal(inMapValue.MaxItems, byPath["groups[*].members"].MaxItems);
            Assert.Equal(inCommon.MinItems, byPath["payload.shared"].MinItems);
        }

        [Fact]
        public void Collect_ConstrainedArrayContainingConstrainedArray_ReportsOuterThenInnerWithBracketPath()
        {
            // 数组本身带约束、且其元素又是带约束的数组：外层路径 "matrix"，内层 "matrix[]"。
            var inner = StringArray("<row>", min: 1, max: 3);
            var outer = new FieldSchema("matrix", FieldKind.Array, required: false, item: inner, description: "二维数组")
                .WithItemCount(1, 2);

            var result = SchemaFieldItemCountExport.Collect(Table("test.count_matrix", outer));

            Assert.Equal(new[] { "matrix", "matrix[]" }, result.Select(r => r.FieldPath));
            Assert.Equal(outer.MaxItems, result[0].MaxItems);
            Assert.Equal(inner.MaxItems, result[1].MaxItems);
        }

        [Fact]
        public void Collect_SelfReferencingSharedField_TerminatesAndReportsEachConstrainedFieldOnce()
        {
            FieldSchema? self = null;
            var ids = IdList("children", 1, null);
            self = new FieldSchema("<node>", FieldKind.Object, required: true,
                fields: new List<FieldSchema> { ids }, description: "自引用节点");
            var array = new FieldSchema("nodes", FieldKind.Array, required: false,
                itemFactory: () => self!, description: "自引用数组");

            var hit = Assert.Single(SchemaFieldItemCountExport.Collect(Table("test.count_self_ref", array)));

            Assert.Equal("nodes[].children", hit.FieldPath);
        }

        [Fact]
        public void Collect_UnboundedFreshInstanceRecursion_StopsAtDataRegistryDepthLimit()
        {
            var limit = (int)typeof(DataRegistry)
                .GetField("MaxSubstructureDepth", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

            // 每层都是一个带约束的 Array，其元素工厂每次新建下一层：祖先链拦不住，靠深度上限兜底。
            FieldSchema MakeLevel() => new FieldSchema("<level>", FieldKind.Array, required: false, itemFactory: MakeLevel, description: "层")
                .WithItemCount(1);
            var root = new FieldSchema("levels", FieldKind.Array, required: false, itemFactory: MakeLevel, description: "根")
                .WithItemCount(1);

            var result = SchemaFieldItemCountExport.Collect(Table("test.count_depth", root));

            // 根在深度 0，其后每层深度 +1；深度达到上限的那一层仍会被登记、但不再向下展开：总共上限 + 1 个命中。
            Assert.Equal(limit + 1, result.Count);
            Assert.Equal("levels", result[0].FieldPath);
            Assert.Equal(limit, result[result.Count - 1].FieldPath.Split(new[] { "[]" }, StringSplitOptions.None).Length - 1);
        }

        [Fact]
        public void Collect_UnschematizedTable_ReturnsEmpty()
        {
            Assert.Empty(SchemaFieldItemCountExport.Collect(TableSchema.Unschematized("test.count_unschematized", "id")));
        }

        [Fact]
        public void Collect_NullSchema_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SchemaFieldItemCountExport.Collect(null!));
        }

        [Fact]
        public void FieldItemCountInfo_NullArguments_ThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new FieldItemCountInfo(null!, "IdList", 1, null));
            Assert.Throws<ArgumentNullException>(() => new FieldItemCountInfo("a", null!, 1, null));
        }

        [Fact]
        public void Collect_RealRegistration_InputAction_ExportsDefaultBindingsMinimumFromTheRegisteredField()
        {
            var inputAction = SchemaAudit.EnumerateRegisteredSchemas().Single(s => s.Name == "found.input_action");
            var bindings = inputAction.Fields.Single(f => f.Name == "default_bindings");
            Assert.NotNull(bindings.MinItems);

            var result = SchemaFieldItemCountExport.Collect(inputAction);

            var hit = Assert.Single(result, r => r.FieldPath == "default_bindings");
            Assert.Equal(bindings.Kind.ToString(), hit.Kind);
            Assert.Equal(bindings.MinItems, hit.MinItems);
            Assert.Equal(bindings.MaxItems, hit.MaxItems);
        }

        [Fact]
        public void Collect_AllRegisteredSchemas_NeverThrow_PathsUniquePerTable_OnlyIdListOrArray_BoundsConsistent()
        {
            var total = 0;
            foreach (var schema in SchemaAudit.EnumerateRegisteredSchemas())
            {
                var result = SchemaFieldItemCountExport.Collect(schema);
                total += result.Count;
                Assert.Equal(result.Count, result.Select(r => r.FieldPath).Distinct(StringComparer.Ordinal).Count());
                foreach (var info in result)
                {
                    Assert.Contains(info.Kind, new[] { FieldKind.IdList.ToString(), FieldKind.Array.ToString() });
                    Assert.True(info.MinItems.HasValue || info.MaxItems.HasValue);
                    if (info.MinItems.HasValue && info.MaxItems.HasValue)
                    {
                        Assert.True(info.MaxItems.Value >= info.MinItems.Value, info.FieldPath);
                    }
                }
            }

            Assert.True(total > 0, "全量登记里应至少有一处元素数量约束被导出");
        }
    }
}
