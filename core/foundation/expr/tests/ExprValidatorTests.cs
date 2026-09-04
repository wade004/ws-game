using System;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Expr
{
    public class ExprValidatorTests
    {
        private static ExprSchema BuildSchema() => new ExprSchema()
            .Register("self", "hp_pct", ExprValueKind.Number)
            .Register("self", "has_aura", ExprValueKind.Bool, ExprValueKind.Id)
            .Register("combat", "in_combat", ExprValueKind.Bool)
            .Register("target", "faction", ExprValueKind.Id)
            .Register("target", "hp_pct", ExprValueKind.Number)
            .Register("player", "level", ExprValueKind.Int)
            .Register("event", "school", ExprValueKind.String)
            .Register("time", "since_combat_start", ExprValueKind.Number);

        [Fact]
        public void ValidExpression_ProducesNoIssues()
        {
            var node = ExprParser.Parse("combat.in_combat and target.hp_pct < 0.3", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Empty(issues);
        }

        [Fact]
        public void ValidExpression_IntNumberCompareIsAllowed()
        {
            // player.level 声明为 Int，与 Number 字面量比较应视为合法（Int/Number 互相可比，04 6.3 节）。
            var node = ExprParser.Parse("player.level >= 10.0", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Empty(issues);
        }

        [Fact]
        public void UnknownGroup_IsReported()
        {
            // 解析器本身不会产出未知分组的引用节点，这里手工构造以验证校验器独立检查。
            var node = new ExprReferenceNode("bogus", "foo", Array.Empty<ExprNode>());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.UnknownGroup);
        }

        [Fact]
        public void UnknownKey_IsReported()
        {
            // ADR-0015 之后，ExprParser 对未在 schema 登记的点分标识符一律归类为 Id 字面量，
            // 不会再产出"分组已知、key 未登记"的 ExprReferenceNode——所以和 UnknownGroup 一样，
            // UnknownKey 只能在手工构造的 AST（如来自非 ExprParser 产出路径）上触发；
            // 校验器仍然独立检查这一条，保证"未知 key"这条 04 第 5 节校验项在任意 AST 输入下都成立。
            var node = new ExprReferenceNode("self", "no_such_key", Array.Empty<ExprNode>());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.UnknownKey);
        }

        [Fact]
        public void ArgCountMismatch_IsReported()
        {
            var node = ExprParser.Parse("self.has_aura(skill.aura.burning, skill.aura.frost)", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.ArgCountMismatch);
        }

        [Fact]
        public void ArgTypeMismatch_IsReported()
        {
            // self.has_aura 期望 Id 参数，这里传一个 String。
            var node = ExprParser.Parse("self.has_aura(\"not_an_id\")", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.ArgTypeMismatch);
        }

        [Fact]
        public void CompareTypeMismatch_BoolVsInt_IsReported()
        {
            var node = ExprParser.Parse("combat.in_combat == 1", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.CompareTypeMismatch);
        }

        [Fact]
        public void CompareTypeMismatch_StringVsId_IsReported()
        {
            var node = ExprParser.Parse("event.school == fac.wildlife", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.CompareTypeMismatch);
        }

        [Fact]
        public void InvalidCompareForType_IdWithLessThan_IsReported()
        {
            var node = ExprParser.Parse("target.faction < fac.wildlife", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.InvalidCompareForType);
        }

        [Fact]
        public void InvalidCompareForType_BoolWithGreaterThan_IsReported()
        {
            var node = ExprParser.Parse("combat.in_combat > combat.in_combat", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.InvalidCompareForType);
        }

        [Fact]
        public void LogicalOperandNotBool_IsReported()
        {
            var node = ExprParser.Parse("self.hp_pct and true", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.LogicalOperandNotBool);
        }

        [Fact]
        public void LogicalOperandNotBool_NotOperator_IsReported()
        {
            var node = ExprParser.Parse("not self.hp_pct", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.LogicalOperandNotBool);
        }

        [Fact]
        public void MultipleIssues_AreAllCollected_NotJustFirst()
        {
            // 左侧逻辑操作数非 Bool，右侧参数类型错误：校验器应尽量收集而不是遇错即停。
            var node = ExprParser.Parse("self.hp_pct and self.has_aura(\"nope\")", BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.True(issues.Count >= 2);
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.LogicalOperandNotBool);
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.ArgTypeMismatch);
        }
    }
}
