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
            var node = ExprParser.Parse("combat.in_combat and target.hp_pct < 0.3");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Empty(issues);
        }

        [Fact]
        public void ValidExpression_IntNumberCompareIsAllowed()
        {
            // player.level 声明为 Int，与 Number 字面量比较应视为合法（Int/Number 互相可比，04 6.3 节）。
            var node = ExprParser.Parse("player.level >= 10.0");
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
            var node = ExprParser.Parse("self.no_such_key");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.UnknownKey);
        }

        [Fact]
        public void ArgCountMismatch_IsReported()
        {
            var node = ExprParser.Parse("self.has_aura(skill.aura.burning, skill.aura.frost)");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.ArgCountMismatch);
        }

        [Fact]
        public void ArgTypeMismatch_IsReported()
        {
            // self.has_aura 期望 Id 参数，这里传一个 String。
            var node = ExprParser.Parse("self.has_aura(\"not_an_id\")");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.ArgTypeMismatch);
        }

        [Fact]
        public void CompareTypeMismatch_BoolVsInt_IsReported()
        {
            var node = ExprParser.Parse("combat.in_combat == 1");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.CompareTypeMismatch);
        }

        [Fact]
        public void CompareTypeMismatch_StringVsId_IsReported()
        {
            var node = ExprParser.Parse("event.school == fac.wildlife");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.CompareTypeMismatch);
        }

        [Fact]
        public void InvalidCompareForType_IdWithLessThan_IsReported()
        {
            var node = ExprParser.Parse("target.faction < fac.wildlife");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.InvalidCompareForType);
        }

        [Fact]
        public void InvalidCompareForType_BoolWithGreaterThan_IsReported()
        {
            var node = ExprParser.Parse("combat.in_combat > combat.in_combat");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.InvalidCompareForType);
        }

        [Fact]
        public void LogicalOperandNotBool_IsReported()
        {
            var node = ExprParser.Parse("self.hp_pct and true");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.LogicalOperandNotBool);
        }

        [Fact]
        public void LogicalOperandNotBool_NotOperator_IsReported()
        {
            var node = ExprParser.Parse("not self.hp_pct");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.LogicalOperandNotBool);
        }

        [Fact]
        public void MultipleIssues_AreAllCollected_NotJustFirst()
        {
            // 左侧未知 key，右侧参数类型错误：校验器应尽量收集而不是遇错即停。
            var node = ExprParser.Parse("self.no_such_key and self.has_aura(\"nope\")");
            var issues = ExprValidator.Validate(node, BuildSchema());
            Assert.True(issues.Count >= 2);
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.UnknownKey);
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.ArgTypeMismatch);
        }
    }
}
