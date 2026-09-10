using System;
using System.Linq;
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

        // -----------------------------------------------------------------
        // 消费方反馈第三批第 19 条（2026-09-10，见
        // architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 19 条）：
        // ExprIssue.Start/Length 与源文本精确对应。
        // -----------------------------------------------------------------

        [Fact]
        public void UnknownKey_IssueSpan_MatchesHandBuiltReferenceNodeSpan()
        {
            // 与既有 UnknownKey_IsReported 用例同样的理由（见该用例注释）：ADR-0015 之后
            // ExprParser 不会为未登记的非 event 分组 key 产出 <reference>，UnknownKey 只能在
            // 手工构造的 AST 上触发——这里额外验证 ExprReferenceNode 的可选 start/length 构造
            // 参数会被 ExprValidator 原样透传到产出的 ExprIssue 上。
            var node = new ExprReferenceNode("self", "no_such_key", Array.Empty<ExprNode>(), start: 5, length: 20);
            var issues = ExprValidator.Validate(node, BuildSchema());

            var issue = Assert.Single(issues, i => i.Kind == ExprIssueKind.UnknownKey);
            Assert.Equal(5, issue.Start);
            Assert.Equal(20, issue.Length);
        }

        [Fact]
        public void ArgTypeMismatch_IssueSpan_MatchesOffendingArgumentText()
        {
            var text = "self.has_aura(\"nope\")";
            var argStart = text.IndexOf("\"nope\"", StringComparison.Ordinal);
            var node = ExprParser.Parse(text, BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());

            var issue = Assert.Single(issues, i => i.Kind == ExprIssueKind.ArgTypeMismatch);
            Assert.Equal(argStart, issue.Start);
            Assert.Equal("\"nope\"".Length, issue.Length);
        }

        [Fact]
        public void CompareTypeMismatch_IssueSpan_CoversWholeCompareExpression()
        {
            var text = "target.faction == target.hp_pct";
            var node = ExprParser.Parse(text, BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());

            var issue = Assert.Single(issues, i => i.Kind == ExprIssueKind.CompareTypeMismatch);
            Assert.Equal(0, issue.Start);
            Assert.Equal(text.Length, issue.Length);
        }

        [Fact]
        public void SuspiciousReferenceSpelling_IssueSpan_MatchesIdLiteralText()
        {
            var text = "self.hp_pct > 0 and world.bridge_repaired";
            var idStart = text.IndexOf("world.bridge_repaired", StringComparison.Ordinal);
            var node = ExprParser.Parse(text, BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());

            var issue = Assert.Single(issues, i => i.Kind == ExprIssueKind.SuspiciousReferenceSpelling);
            Assert.Equal(idStart, issue.Start);
            Assert.Equal("world.bridge_repaired".Length, issue.Length);
        }

        [Fact]
        public void SameGroupKey_AppearingTwice_ProducesIssuesWithDistinctSpans()
        {
            // "bogus" 不是 04 第 6.2 节九个分组之一——但只要 schema 里登记了 "bogus.a" 的签名，
            // ExprParser（只认 schema 是否命中，不检查是否属于九个分组）就会把它解析成
            // <reference>，交给 ExprValidator 报 UnknownGroup（见 ValidateReference 判断记录）。
            var schema = new ExprSchema()
                .Register("self", "hp_pct", ExprValueKind.Number)
                .Register("bogus", "a", ExprValueKind.Bool);
            var text = "bogus.a or bogus.a";
            var node = ExprParser.Parse(text, schema);
            var issues = ExprValidator.Validate(node, schema);

            var unknownGroupIssues = issues.Where(i => i.Kind == ExprIssueKind.UnknownGroup).ToList();
            Assert.Equal(2, unknownGroupIssues.Count);
            Assert.NotEqual(unknownGroupIssues[0].Start, unknownGroupIssues[1].Start);
            Assert.Equal(0, unknownGroupIssues[0].Start);
            Assert.Equal(text.IndexOf("bogus.a", 5, StringComparison.Ordinal), unknownGroupIssues[1].Start);
        }

        [Fact]
        public void LogicalOperandNotBool_IssueSpan_MatchesOffendingOperandText()
        {
            var text = "self.has_aura(\"self.a\") and self.hp_pct";
            var node = ExprParser.Parse(text, BuildSchema());
            var issues = ExprValidator.Validate(node, BuildSchema());

            var operandStart = text.IndexOf("self.hp_pct", 10, StringComparison.Ordinal);
            var issue = Assert.Single(issues, i => i.Kind == ExprIssueKind.LogicalOperandNotBool);
            Assert.Equal(operandStart, issue.Start);
            Assert.Equal("self.hp_pct".Length, issue.Length);
        }

        [Fact]
        public void LegacyTwoArgConstructor_LeavesStartAtMinusOne()
        {
            var issue = new ExprIssue(ExprIssueKind.UnknownGroup, "手写问题，没有源位置");
            Assert.Equal(-1, issue.Start);
            Assert.Equal(0, issue.Length);
        }
    }
}
