using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 消费方反馈第四批第 25/26 条验收测试（2026-09-10）：
    /// <list type="bullet">
    /// <item><description>第 25 条——<see cref="FieldKind.Expr"/> 字段不再被
    /// <see cref="RecordExprSchema.For"/> 登记为 <c>self.&lt;field&gt;</c> 引用（全仓 grep
    /// <c>data/</c> 与既有测试未发现 <c>self.&lt;Expr 字段名&gt;</c> 的现存使用，见
    /// <c>RecordExprSchema</c> 类型注释判断记录）。</description></item>
    /// <item><description>第 26 条——<see cref="RecordExprMapping"/>/<see cref="RecordExprMapping.ToExprValueKind"/>
    /// 放宽为 <c>public static</c> 后，逐一断言全部 13 种 <see cref="FieldKind"/> 的映射结果。</description></item>
    /// </list>
    /// </summary>
    public class RecordExprSchemaExprFieldExclusionTests
    {
        private static TableSchema TableWithExprField() => new TableSchema(
            "test.expr_holder", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("visible_if", FieldKind.Expr, required: false),
            });

        [Fact]
        public void For_ExprField_NotRegisteredAsSelfReference()
        {
            var schema = RecordExprSchema.For(TableWithExprField());

            var found = schema.TryGetSignature(ExprGroups.Self, "visible_if", out _);

            Assert.False(found);
        }

        [Fact]
        public void ExprParser_SelfDotExprField_FallsBackToIdLiteral()
        {
            // 未登记为 <reference> 时，ADR-0015 回退规则把 self.visible_if 整体解析为 <id_literal>；
            // self.visible_if 满足第 2 节 id 格式（形如 domain.segment...），解析不应抛异常。
            var schema = RecordExprSchema.For(TableWithExprField());

            var node = ExprParser.Parse("self.visible_if", schema);

            Assert.NotNull(node);
        }

        [Fact]
        public void For_NonExprFields_StillRegisteredAsSelfReference()
        {
            var table = new TableSchema(
                "test.expr_holder_mixed", "id", 1,
                new[]
                {
                    new FieldSchema("id", FieldKind.Id, required: true),
                    new FieldSchema("name", FieldKind.String, required: true),
                    new FieldSchema("condition", FieldKind.Expr, required: false),
                });
            var schema = RecordExprSchema.For(table);

            Assert.True(schema.TryGetSignature(ExprGroups.Self, "id", out var idSig));
            Assert.Equal(ExprValueKind.Id, idSig.ReturnKind);

            Assert.True(schema.TryGetSignature(ExprGroups.Self, "name", out var nameSig));
            Assert.Equal(ExprValueKind.String, nameSig.ReturnKind);

            Assert.False(schema.TryGetSignature(ExprGroups.Self, "condition", out _));
        }

        // 判断记录：MemberData 用 object[] 承载测试数据，装箱 (ExprValueKind?)null 会触发可空引用
        // 分析器的 CS8625（TreatWarningsAsErrors=true 下即编译错误）。改用"是否有值 + 有值时的取值"
        // 两个非可空参数拆开表达，规避这个装箱陷阱，不改变断言意图。
        public static IEnumerable<object[]> AllFieldKindMappings()
        {
            yield return new object[] { FieldKind.Bool, true, ExprValueKind.Bool };
            yield return new object[] { FieldKind.Int, true, ExprValueKind.Int };
            yield return new object[] { FieldKind.Number, true, ExprValueKind.Number };
            yield return new object[] { FieldKind.String, true, ExprValueKind.String };
            yield return new object[] { FieldKind.Id, true, ExprValueKind.Id };
            yield return new object[] { FieldKind.IdList, false, default(ExprValueKind) };
            yield return new object[] { FieldKind.Reference, true, ExprValueKind.Id };
            yield return new object[] { FieldKind.TextKey, true, ExprValueKind.Id };
            yield return new object[] { FieldKind.Expr, false, default(ExprValueKind) };
            yield return new object[] { FieldKind.Enum, true, ExprValueKind.String };
            yield return new object[] { FieldKind.Vec2, false, default(ExprValueKind) };
            yield return new object[] { FieldKind.Object, false, default(ExprValueKind) };
            yield return new object[] { FieldKind.Array, false, default(ExprValueKind) };
        }

        [Theory]
        [MemberData(nameof(AllFieldKindMappings))]
        public void ToExprValueKind_CoversAllThirteenFieldKinds(FieldKind kind, bool expectHasValue, ExprValueKind expectedValue)
        {
            var actual = RecordExprMapping.ToExprValueKind(kind);

            Assert.Equal(expectHasValue, actual.HasValue);
            if (expectHasValue)
            {
                Assert.Equal(expectedValue, actual!.Value);
            }
        }

        [Fact]
        public void AllFieldKindMappings_CoversExactlyThirteenKinds()
        {
            // 防止 FieldKind 未来新增枚举值时本测试静默漏检——13 是本次任务书写定的当前种类数。
            var kinds = (FieldKind[])System.Enum.GetValues(typeof(FieldKind));

            Assert.Equal(13, kinds.Length);
        }
    }
}
