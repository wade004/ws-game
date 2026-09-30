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
    /// 测试覆盖梳理 T-M28：<see cref="SchemaFieldRangeExport.Collect"/> 的直接用例（生产被
    /// <c>toolchain/validator/Program.cs</c> 的 <c>--list-tables --json</c> 使用；<c>AngleFieldRadianUnitTests</c>
    /// 只是复刻了遍历器，不经过本入口）。既覆盖构造出来的最小 schema（顶层/嵌套对象/数组/映射/变体各路径记法、
    /// 自引用环、深度上限、错误输入），也对真实全量登记跑一遍。期望值由 schema 对象本身（字段顺序、同一 Range
    /// 实例、DataRegistry 的递归深度常量）算出，不写裸数。
    /// </summary>
    public class SchemaFieldRangeExportTests
    {
        private static TableSchema Table(string name, params FieldSchema[] fields) =>
            new TableSchema(name, "id", 1, new[] { new FieldSchema("id", FieldKind.Id, required: true, description: "主键") }
                .Concat(fields).ToArray());

        private static FieldSchema Number(string name, FieldRange? range = null, FieldKind kind = FieldKind.Number)
        {
            var field = new FieldSchema(name, kind, required: false, description: name);
            return range == null ? field : field.WithRange(range);
        }

        [Fact]
        public void Collect_TopLevelNumberAndIntFields_ReturnBareNamePathsInFieldOrder_WithKindAndSameRangeInstance()
        {
            var hpRange = FieldRange.Range(min: 0, max: 100);
            var countRange = FieldRange.Range(min: 1, minExclusive: true);
            var hp = Number("hp", hpRange);
            var unranged = Number("regen");
            var count = Number("count", countRange, FieldKind.Int);
            var schema = Table("test.range_top", hp, unranged, count);

            var result = SchemaFieldRangeExport.Collect(schema);

            // 顺序 = TableSchema.Fields 出现顺序；未登记 Range 的字段不出现。
            Assert.Equal(new[] { "hp", "count" }, result.Select(r => r.FieldPath));
            Assert.Equal(new[] { FieldKind.Number.ToString(), FieldKind.Int.ToString() }, result.Select(r => r.Kind));
            Assert.Same(hpRange, result[0].Range);
            Assert.Same(countRange, result[1].Range);
            Assert.Equal(hpRange.Max, result[0].Range.Max);
            Assert.True(result[1].Range.MinExclusive);
        }

        [Fact]
        public void Collect_SchemaWithoutAnyRange_ReturnsEmpty()
        {
            var result = SchemaFieldRangeExport.Collect(Table("test.range_none", Number("a"), Number("b", kind: FieldKind.Int)));

            Assert.Empty(result);
        }

        [Fact]
        public void Collect_RangeOnNonNumericKind_IsIgnored()
        {
            // 导出只面向 Number/Int（类型注释）：挂在其它种类上的 Range 不会被导出。
            var label = new FieldSchema("label", FieldKind.String, required: false, description: "字符串")
                .WithRange(FieldRange.Range(min: 0, max: 1));
            var ranked = Number("score", FieldRange.Range(max: 10));

            var result = SchemaFieldRangeExport.Collect(Table("test.range_non_numeric", label, ranked));

            Assert.Equal("score", Assert.Single(result).FieldPath);
        }

        [Fact]
        public void Collect_NestedObjectArrayMapAndVariants_UseDocumentedPathNotation()
        {
            var inObject = FieldRange.Range(min: 0);
            var inArrayItem = FieldRange.Range(max: 5);
            var inMapValue = FieldRange.Range(min: -1, max: 1);
            var inCase = FieldRange.Range(min: 2);
            var inCommon = FieldRange.Range(max: 9);

            var obj = new FieldSchema("stats", FieldKind.Object, required: false,
                fields: new[] { Number("speed", inObject) }, description: "对象");
            var arrayItem = new FieldSchema("<entry>", FieldKind.Object, required: true,
                fields: new[] { Number("weight", inArrayItem) }, description: "元素");
            var array = new FieldSchema("entries", FieldKind.Array, required: false, item: arrayItem, description: "数组");
            var mapValue = new FieldSchema("<value>", FieldKind.Object, required: true,
                fields: new[] { Number("factor", inMapValue) }, description: "映射值");
            var mapField = new FieldSchema("table", FieldKind.Object, required: false, description: "映射")
                .WithMap(MapSchema.FreeKeyed("测试：键为局部自由字符串", mapValue));
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                ["kind_a"] = new[] { Number("param", inCase) },
            };
            var variant = new FieldSchema("payload", FieldKind.Object, required: false,
                variants: new VariantSchema("kind", cases, commonFields: new[] { Number("shared", inCommon) }),
                description: "变体");

            var result = SchemaFieldRangeExport.Collect(Table("test.range_nested", obj, array, mapField, variant));

            var byPath = result.ToDictionary(r => r.FieldPath, r => r.Range);
            Assert.Same(inObject, byPath["stats.speed"]);
            Assert.Same(inArrayItem, byPath["entries[].weight"]);
            Assert.Same(inMapValue, byPath["table[*].factor"]);
            Assert.Same(inCase, byPath["payload{kind=kind_a}.param"]);
            Assert.Same(inCommon, byPath["payload.shared"]);
            Assert.Equal(5, result.Count);
            // 变体：先各 case（按判别值）、后公共字段。
            var paths = result.Select(r => r.FieldPath).ToList();
            Assert.True(paths.IndexOf("payload{kind=kind_a}.param") < paths.IndexOf("payload.shared"));
        }

        [Fact]
        public void Collect_ArrayWhoseItemIsRangedInt_ReportsBracketPathWithItemKind()
        {
            // 数组元素本身是带 Range 的 Int：路径 "<array>[]"。
            var itemRange = FieldRange.Range(min: 0, max: 3);
            var item = new FieldSchema("<n>", FieldKind.Int, required: true, description: "元素").WithRange(itemRange);
            var array = new FieldSchema("levels", FieldKind.Array, required: false, item: item, description: "数组");

            var result = SchemaFieldRangeExport.Collect(Table("test.range_array_scalar", array));

            var hit = Assert.Single(result);
            Assert.Equal("levels[]", hit.FieldPath);
            Assert.Equal(FieldKind.Int.ToString(), hit.Kind);
            Assert.Same(itemRange, hit.Range);
        }

        [Fact]
        public void Collect_SelfReferencingSharedField_TerminatesAndReportsEachRangedFieldOnce()
        {
            FieldSchema? self = null;
            var leaf = Number("leaf", FieldRange.Range(min: 0));
            self = new FieldSchema("<node>", FieldKind.Object, required: true,
                fields: new List<FieldSchema> { leaf }, description: "自引用节点");
            var array = new FieldSchema("nodes", FieldKind.Array, required: false,
                itemFactory: () => self!, description: "自引用数组");

            var result = SchemaFieldRangeExport.Collect(Table("test.range_self_ref", array));

            var hit = Assert.Single(result);
            Assert.Equal("nodes[].leaf", hit.FieldPath);
        }

        [Fact]
        public void Collect_UnboundedFreshInstanceRecursion_StopsAtDataRegistryDepthLimit()
        {
            // 每层都新建实例的无限递归（itemFactory 不复用实例）：祖先链按对象引用比较拦不住，靠深度上限兜底。
            // 深度上限与 DataRegistry.MaxSubstructureDepth 同值（类型注释承诺），这里从它读出来算期望。
            var limit = (int)typeof(DataRegistry)
                .GetField("MaxSubstructureDepth", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetRawConstantValue()!;

            FieldSchema MakeNode() => new FieldSchema("<node>", FieldKind.Object, required: true,
                fields: new[]
                {
                    Number("v", FieldRange.Range(min: 0)),
                    new FieldSchema("next", FieldKind.Array, required: false, itemFactory: MakeNode, description: "下一层"),
                }, description: "递归节点");
            var root = new FieldSchema("chain", FieldKind.Array, required: false, itemFactory: MakeNode, description: "链");

            var result = SchemaFieldRangeExport.Collect(Table("test.range_depth", root));

            Assert.NotEmpty(result);
            // 每个命中的 "v" 位于递归链上的某一层；路径随层数严格加长，且最深命中不超过深度上限。
            var lengths = result.Select(r => r.FieldPath.Length).ToList();
            Assert.Equal(lengths.OrderBy(l => l), lengths);
            Assert.Equal(lengths.Distinct().Count(), lengths.Count);
            // 链上每层递归占两级深度（数组 -> 节点），首个 "v" 在深度 2；深度达到上限的那一层仍会被登记、但不再展开：
            // 命中深度为 2、4、……、不超过上限的最大偶数，共 limit / 2 个。
            Assert.Equal(limit / 2, result.Count);
        }

        [Fact]
        public void Collect_UnschematizedTable_ReturnsEmpty()
        {
            Assert.Empty(SchemaFieldRangeExport.Collect(TableSchema.Unschematized("test.range_unschematized", "id")));
        }

        [Fact]
        public void Collect_NullSchema_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SchemaFieldRangeExport.Collect(null!));
        }

        [Fact]
        public void FieldRangeInfo_NullArguments_ThrowArgumentNullException()
        {
            var range = FieldRange.Range(min: 0);
            Assert.Throws<ArgumentNullException>(() => new FieldRangeInfo(null!, "Number", range));
            Assert.Throws<ArgumentNullException>(() => new FieldRangeInfo("a", null!, range));
            Assert.Throws<ArgumentNullException>(() => new FieldRangeInfo("a", "Number", null!));
        }

        [Fact]
        public void Collect_RealRegistration_ItemAffix_ExportsTopLevelAndNestedRangesOfTheRegisteredFieldObjects()
        {
            var affix = SchemaAudit.EnumerateRegisteredSchemas().Single(s => s.Name == "item.affix");
            var budgetShare = affix.Fields.Single(f => f.Name == "budget_share");
            var statMixItem = affix.Fields.Single(f => f.Name == "stat_mix").Item!;
            var ratio = statMixItem.Fields!.Single(f => f.Name == "ratio");
            Assert.NotNull(budgetShare.Range);
            Assert.NotNull(ratio.Range);

            var result = SchemaFieldRangeExport.Collect(affix);
            var byPath = result.ToDictionary(r => r.FieldPath, r => r.Range);

            Assert.Same(budgetShare.Range, byPath["budget_share"]);
            Assert.Same(ratio.Range, byPath["stat_mix[].ratio"]);
        }

        [Fact]
        public void Collect_AllRegisteredSchemas_NeverThrow_PathsUniquePerTable_OnlyNumericKinds_AtLeastOneHit()
        {
            var total = 0;
            foreach (var schema in SchemaAudit.EnumerateRegisteredSchemas())
            {
                var result = SchemaFieldRangeExport.Collect(schema);
                total += result.Count;
                Assert.Equal(result.Count, result.Select(r => r.FieldPath).Distinct(StringComparer.Ordinal).Count());
                foreach (var info in result)
                {
                    Assert.Contains(info.Kind, new[] { FieldKind.Number.ToString(), FieldKind.Int.ToString() });
                    Assert.NotNull(info.Range);
                }
            }

            Assert.True(total > 0, "全量登记里应至少有一处 Number/Int 范围约束被导出");
        }
    }
}
