using Core.Foundation.Common;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Expr
{
    public class ExprParserTests
    {
        // ---------- 语法集：解析成功且 AST 形状符合预期（>= 20 条） ----------

        [Fact]
        public void Compare_Eq()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("self.hp_pct == 1"));
            Assert.Equal(ExprCompareOp.Eq, node.Op);
            var left = Assert.IsType<ExprReferenceNode>(node.Left);
            Assert.Equal("self", left.Group);
            Assert.Equal("hp_pct", left.Key);
            Assert.Empty(left.Args);
            var right = Assert.IsType<ExprLiteralNode>(node.Right);
            Assert.Equal(ExprValue.OfInt(1), right.Value);
        }

        [Fact]
        public void Compare_Ne()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("self.hp_pct != 1"));
            Assert.Equal(ExprCompareOp.Ne, node.Op);
        }

        [Fact]
        public void Compare_Gt()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("self.hp_pct > 1"));
            Assert.Equal(ExprCompareOp.Gt, node.Op);
        }

        [Fact]
        public void Compare_Ge()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("self.hp_pct >= 1"));
            Assert.Equal(ExprCompareOp.Ge, node.Op);
        }

        [Fact]
        public void Compare_Lt()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("self.hp_pct < 1"));
            Assert.Equal(ExprCompareOp.Lt, node.Op);
        }

        [Fact]
        public void Compare_Le()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("self.hp_pct <= 1"));
            Assert.Equal(ExprCompareOp.Le, node.Op);
        }

        [Fact]
        public void And_Combination()
        {
            var node = Assert.IsType<ExprAndNode>(ExprParser.Parse("combat.in_combat and target.hp_pct < 0.3"));
            Assert.Equal(2, node.Operands.Count);
            Assert.IsType<ExprReferenceNode>(node.Operands[0]);
            Assert.IsType<ExprCompareNode>(node.Operands[1]);
        }

        [Fact]
        public void Or_Combination()
        {
            var node = Assert.IsType<ExprOrNode>(ExprParser.Parse("combat.in_combat or player.level >= 10"));
            Assert.Equal(2, node.Operands.Count);
        }

        [Fact]
        public void Not_Simple()
        {
            var node = Assert.IsType<ExprNotNode>(ExprParser.Parse("not combat.in_combat"));
            Assert.IsType<ExprReferenceNode>(node.Operand);
        }

        [Fact]
        public void Not_ChainedDouble()
        {
            var node = Assert.IsType<ExprNotNode>(ExprParser.Parse("not not combat.in_combat"));
            var inner = Assert.IsType<ExprNotNode>(node.Operand);
            Assert.IsType<ExprReferenceNode>(inner.Operand);
        }

        [Fact]
        public void Not_ParenGroup()
        {
            var node = Assert.IsType<ExprNotNode>(ExprParser.Parse("not (combat.in_combat and target.hp_pct < 0.3)"));
            Assert.IsType<ExprAndNode>(node.Operand);
        }

        [Fact]
        public void Parens_ChangePrecedence()
        {
            var withParens = Assert.IsType<ExprAndNode>(
                ExprParser.Parse("(combat.in_combat or player.level >= 10) and target.hp_pct < 0.3"));
            Assert.Equal(2, withParens.Operands.Count);
            Assert.IsType<ExprOrNode>(withParens.Operands[0]);

            // 去掉括号后语义完全不同：整体应变成 or(结合优先级更低)。
            var withoutParens = Assert.IsType<ExprOrNode>(
                ExprParser.Parse("combat.in_combat or player.level >= 10 and target.hp_pct < 0.3"));
            Assert.Equal(2, withoutParens.Operands.Count);
            Assert.IsType<ExprAndNode>(withoutParens.Operands[1]);
        }

        [Fact]
        public void StringLiteral_Compare()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("event.school == \"fire\""));
            var right = Assert.IsType<ExprLiteralNode>(node.Right);
            Assert.Equal(ExprValue.OfString("fire"), right.Value);
        }

        [Fact]
        public void StringLiteral_WithEscapes()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("event.school == \"a\\\"b\\\\c\""));
            var right = Assert.IsType<ExprLiteralNode>(node.Right);
            Assert.Equal("a\"b\\c", right.Value.AsString);
        }

        [Fact]
        public void IdLiteral_Equality()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("target.faction == fac.wildlife"));
            var right = Assert.IsType<ExprLiteralNode>(node.Right);
            Assert.Equal(ExprValueKind.Id, right.Value.Kind);
            Assert.Equal(new Id("fac.wildlife"), right.Value.AsId);
        }

        [Fact]
        public void Reference_WithSingleArg()
        {
            var node = Assert.IsType<ExprReferenceNode>(ExprParser.Parse("self.has_aura(skill.aura.burning)"));
            Assert.Equal("self", node.Group);
            Assert.Equal("has_aura", node.Key);
            var arg = Assert.IsType<ExprLiteralNode>(Assert.Single(node.Args));
            Assert.Equal(new Id("skill.aura.burning"), arg.Value.AsId);
        }

        [Fact]
        public void Reference_WithMultipleArgs()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("quest.objective_progress(quest.a, 1) >= 3"));
            Assert.Equal(ExprCompareOp.Ge, node.Op);
            var left = Assert.IsType<ExprReferenceNode>(node.Left);
            Assert.Equal("quest", left.Group);
            Assert.Equal("objective_progress", left.Key);
            Assert.Equal(2, left.Args.Count);
            var firstArg = Assert.IsType<ExprReferenceNode>(left.Args[0]);
            Assert.Equal("quest", firstArg.Group);
            Assert.Equal("a", firstArg.Key);
            var secondArg = Assert.IsType<ExprLiteralNode>(left.Args[1]);
            Assert.Equal(ExprValue.OfInt(1), secondArg.Value);
        }

        [Fact]
        public void NumberVsIntLiteral_Distinguished()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("self.hp_pct < 0.3"));
            var right = Assert.IsType<ExprLiteralNode>(node.Right);
            Assert.Equal(ExprValueKind.Number, right.Value.Kind);
            Assert.Equal(0.3, right.Value.AsNumber);
        }

        [Fact]
        public void NestedReferenceAsArg()
        {
            var node = Assert.IsType<ExprReferenceNode>(ExprParser.Parse("enemies.count_in_range(self.range)"));
            Assert.Equal("enemies", node.Group);
            Assert.Equal("count_in_range", node.Key);
            var arg = Assert.IsType<ExprReferenceNode>(Assert.Single(node.Args));
            Assert.Equal("self", arg.Group);
            Assert.Equal("range", arg.Key);
        }

        [Fact]
        public void NegativeIntLiteral()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("event.damage_amount > -5"));
            var right = Assert.IsType<ExprLiteralNode>(node.Right);
            Assert.Equal(ExprValue.OfInt(-5), right.Value);
        }

        [Fact]
        public void NegativeNumberLiteral()
        {
            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("event.damage_amount > -1.5"));
            var right = Assert.IsType<ExprLiteralNode>(node.Right);
            Assert.Equal(ExprValueKind.Number, right.Value.Kind);
            Assert.Equal(-1.5, right.Value.AsNumber);
        }

        [Fact]
        public void TopLevelReference_NoOperator_ReturnsBareTerm()
        {
            var node = Assert.IsType<ExprReferenceNode>(ExprParser.Parse("time.since_combat_start"));
            Assert.Equal("time", node.Group);
            Assert.Equal("since_combat_start", node.Key);
        }

        [Fact]
        public void TopLevelBoolLiteral_True()
        {
            var node = Assert.IsType<ExprLiteralNode>(ExprParser.Parse("true"));
            Assert.Equal(ExprValue.OfBool(true), node.Value);
        }

        [Fact]
        public void TopLevelBoolLiteral_False()
        {
            var node = Assert.IsType<ExprLiteralNode>(ExprParser.Parse("false"));
            Assert.Equal(ExprValue.OfBool(false), node.Value);
        }

        [Fact]
        public void MultiSegmentKey_BareReference()
        {
            var node = Assert.IsType<ExprReferenceNode>(ExprParser.Parse("quest.objective_progress"));
            Assert.Equal("quest", node.Group);
            Assert.Equal("objective_progress", node.Key);
            Assert.Empty(node.Args);
        }

        [Fact]
        public void WhitespaceVariety_Tolerated()
        {
            var a = ExprParser.Parse("self.hp_pct == 1");
            var b = ExprParser.Parse("  self.hp_pct\t==\n1  ");
            Assert.Equal(a, b);
        }

        [Fact]
        public void MultipleAndOperands_AreFlattened()
        {
            var node = Assert.IsType<ExprAndNode>(
                ExprParser.Parse("combat.in_combat and player.level >= 1 and target.hp_pct > 0"));
            Assert.Equal(3, node.Operands.Count);
        }

        [Fact]
        public void MultipleOrOperands_AreFlattened()
        {
            var node = Assert.IsType<ExprOrNode>(
                ExprParser.Parse("combat.in_combat or player.level >= 1 or target.hp_pct > 0"));
            Assert.Equal(3, node.Operands.Count);
        }

        // ---------- 解析错误（>= 6 条） ----------

        [Fact]
        public void ParseError_MissingRParen()
        {
            Assert.Throws<ExprParseException>(() => ExprParser.Parse("self.has_aura(skill.aura.burning"));
        }

        [Fact]
        public void ParseError_IllegalIdentifier_Uppercase()
        {
            Assert.Throws<ExprParseException>(() => ExprParser.Parse("Self.hp_pct"));
        }

        [Fact]
        public void ParseError_MissingDomain_BareWord()
        {
            Assert.Throws<ExprParseException>(() => ExprParser.Parse("fireball"));
        }

        [Fact]
        public void ParseError_UnknownOperator()
        {
            Assert.Throws<ExprParseException>(() => ExprParser.Parse("self.hp_pct =< 1"));
        }

        [Fact]
        public void ParseError_StringNotClosed()
        {
            Assert.Throws<ExprParseException>(() => ExprParser.Parse("event.school == \"fire"));
        }

        [Fact]
        public void ParseError_IllegalHyphenIdentifier()
        {
            Assert.Throws<ExprParseException>(() => ExprParser.Parse("skill-fireball"));
        }

        [Fact]
        public void ParseError_EmptyArgList()
        {
            Assert.Throws<ExprParseException>(() => ExprParser.Parse("self.has_aura()"));
        }

        [Fact]
        public void ParseError_ExposesPosition()
        {
            var ex = Assert.Throws<ExprParseException>(() => ExprParser.Parse("fireball"));
            Assert.Equal(0, ex.Position);
        }

        // ---------- ToString 往返：解析 -> 打印 -> 再解析，AST 相等 ----------

        [Theory]
        [InlineData("combat.in_combat and target.hp_pct < 0.3")]
        [InlineData("combat.in_combat or player.level >= 10")]
        [InlineData("not (combat.in_combat and target.hp_pct < 0.3)")]
        [InlineData("(combat.in_combat or player.level >= 10) and target.hp_pct < 0.3")]
        [InlineData("self.has_aura(skill.aura.burning)")]
        [InlineData("quest.objective_progress(quest.a, 1) >= 3")]
        [InlineData("event.school == \"a\\\"b\\\\c\"")]
        [InlineData("event.damage_amount > -5")]
        [InlineData("self.hp_pct < 0.3")]
        [InlineData("not not combat.in_combat")]
        [InlineData("true")]
        [InlineData("enemies.count_in_range(self.range)")]
        public void ToString_RoundTrips_ToEqualAst(string text)
        {
            var first = ExprParser.Parse(text);
            var printed = first.ToString();
            var second = ExprParser.Parse(printed);

            Assert.Equal(first, second);
        }
    }
}
