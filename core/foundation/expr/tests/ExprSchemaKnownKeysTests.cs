using System.Linq;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Expr
{
    /// <summary>
    /// 消费方反馈第三批第 18 条（2026-09-10，见
    /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 18 条）：
    /// <see cref="IExprSchema.KnownKeys(string)"/>/<see cref="IExprSchema.KnownGroups"/>。
    /// </summary>
    public class ExprSchemaKnownKeysTests
    {
        [Fact]
        public void KnownKeys_ReturnsAllRegisteredKeys_ForGroup_OrderedDeterministically()
        {
            var schema = new ExprSchema()
                .Register("self", "hp_pct", ExprValueKind.Number)
                .Register("self", "has_aura", ExprValueKind.Bool, ExprValueKind.Id)
                .Register("self", "level", ExprValueKind.Int)
                .Register("target", "faction", ExprValueKind.Id);

            var keys = schema.KnownKeys("self");

            Assert.Equal(new[] { "has_aura", "hp_pct", "level" }, keys.ToArray());
        }

        [Fact]
        public void KnownKeys_UnknownGroup_ReturnsEmpty()
        {
            var schema = new ExprSchema().Register("self", "hp_pct", ExprValueKind.Number);

            Assert.Empty(schema.KnownKeys("bogus"));
        }

        [Fact]
        public void KnownKeys_ReRegisteringSameKey_DoesNotDuplicate()
        {
            var schema = new ExprSchema()
                .Register("self", "hp_pct", ExprValueKind.Number)
                .Register("self", "hp_pct", ExprValueKind.Number); // 覆盖式重登记，签名可能变化但 key 集合不应重复。

            Assert.Equal(new[] { "hp_pct" }, schema.KnownKeys("self").ToArray());
        }

        [Fact]
        public void KnownKeys_CallResultsAreIndependentSnapshots()
        {
            var schema = new ExprSchema().Register("self", "hp_pct", ExprValueKind.Number);

            var first = schema.KnownKeys("self");
            schema.Register("self", "level", ExprValueKind.Int);
            var second = schema.KnownKeys("self");

            Assert.Single(first);
            Assert.Equal(2, second.Count);
        }

        [Fact]
        public void KnownGroups_ReturnsAllGroupsWithAtLeastOneKey_OrderedDeterministically()
        {
            var schema = new ExprSchema()
                .Register("target", "faction", ExprValueKind.Id)
                .Register("self", "hp_pct", ExprValueKind.Number)
                .Register("combat", "in_combat", ExprValueKind.Bool);

            Assert.Equal(new[] { "combat", "self", "target" }, schema.KnownGroups.ToArray());
        }

        [Fact]
        public void KnownGroups_EmptySchema_ReturnsEmpty()
        {
            var schema = new ExprSchema();
            Assert.Empty(schema.KnownGroups);
        }

        private sealed class LegacyFakeSchema : IExprSchema
        {
            public bool TryGetSignature(string group, string key, out ExprSignature signature)
            {
                signature = default;
                return false;
            }
        }

        [Fact]
        public void DefaultInterfaceImplementation_UsedByLegacyImplementer_ReturnsEmpty()
        {
            // 旧的 IExprSchema 实现方（本任务之前就存在、没有实现 KnownKeys/KnownGroups）不需要
            // 改动即可继续编译，且走接口默认实现——恒返回空集合，不抛异常。
            IExprSchema legacy = new LegacyFakeSchema();

            Assert.Empty(legacy.KnownKeys("self"));
            Assert.Empty(legacy.KnownGroups);
        }
    }
}
