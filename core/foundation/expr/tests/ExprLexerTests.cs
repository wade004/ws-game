using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Expr
{
    /// <summary>
    /// <see cref="ExprLexer"/> 公开契约回归测试（消费方反馈 E5 根治，ADR-0020：词法切分入口纳入
    /// 公开契约，2026-09-10，见 architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E5）。覆盖
    /// 各 token 种类、<see cref="ExprToken.Start"/>/<see cref="ExprToken.Length"/> 位置区间、
    /// 非法字符/未闭合字符串报错位置与 <see cref="ExprParser.Parse"/> 对同一输入的报错位置一致。
    /// </summary>
    public class ExprLexerTests
    {
        [Fact]
        public void Tokenize_Punctuation_And_Operators()
        {
            var tokens = ExprLexer.Tokenize("( ) , == != > >= < <=");
            Assert.Equal(ExprTokenKind.LParen, tokens[0].Kind);
            Assert.Equal(ExprTokenKind.RParen, tokens[1].Kind);
            Assert.Equal(ExprTokenKind.Comma, tokens[2].Kind);
            Assert.Equal(ExprTokenKind.Eq, tokens[3].Kind);
            Assert.Equal(ExprTokenKind.Ne, tokens[4].Kind);
            Assert.Equal(ExprTokenKind.Gt, tokens[5].Kind);
            Assert.Equal(ExprTokenKind.Ge, tokens[6].Kind);
            Assert.Equal(ExprTokenKind.Lt, tokens[7].Kind);
            Assert.Equal(ExprTokenKind.Le, tokens[8].Kind);
            Assert.Equal(ExprTokenKind.Eof, tokens[9].Kind);
            // 两字符运算符 Length 应为 2，单字符为 1。
            Assert.Equal(2, tokens[3].Length); // ==
            Assert.Equal(2, tokens[4].Length); // !=
            Assert.Equal(1, tokens[5].Length); // >
            Assert.Equal(2, tokens[6].Length); // >=
        }

        [Fact]
        public void Tokenize_Keywords()
        {
            var tokens = ExprLexer.Tokenize("and or not true false");
            Assert.Equal(ExprTokenKind.And, tokens[0].Kind);
            Assert.Equal(ExprTokenKind.Or, tokens[1].Kind);
            Assert.Equal(ExprTokenKind.Not, tokens[2].Kind);
            Assert.Equal(ExprTokenKind.True, tokens[3].Kind);
            Assert.Equal(ExprTokenKind.False, tokens[4].Kind);
        }

        [Fact]
        public void Tokenize_DottedIdent_Start_And_Length()
        {
            var tokens = ExprLexer.Tokenize("  self.hp_pct");
            var token = tokens[0];
            Assert.Equal(ExprTokenKind.Ident, token.Kind);
            Assert.Equal("self.hp_pct", token.Text);
            Assert.Equal(2, token.Start); // 跳过前导两个空格
            Assert.Equal(11, token.Length); // "self.hp_pct".Length
            Assert.Equal(token.Text.Length, token.Length);
        }

        [Fact]
        public void Tokenize_IntLiteral_And_NumberLiteral()
        {
            var tokens = ExprLexer.Tokenize("42 -7 3.5 -2.25");
            Assert.Equal(ExprTokenKind.IntLiteral, tokens[0].Kind);
            Assert.Equal(42, tokens[0].IntValue);
            Assert.Equal(2, tokens[0].Length);

            Assert.Equal(ExprTokenKind.IntLiteral, tokens[1].Kind);
            Assert.Equal(-7, tokens[1].IntValue);
            Assert.Equal(2, tokens[1].Length); // "-7"

            Assert.Equal(ExprTokenKind.NumberLiteral, tokens[2].Kind);
            Assert.Equal(3.5, tokens[2].NumberValue);
            Assert.Equal(3, tokens[2].Length); // "3.5"

            Assert.Equal(ExprTokenKind.NumberLiteral, tokens[3].Kind);
            Assert.Equal(-2.25, tokens[3].NumberValue);
            Assert.Equal(5, tokens[3].Length); // "-2.25"
        }

        [Fact]
        public void Tokenize_StringLiteral_Text_Decoded_Length_Is_Raw_Span()
        {
            // 源文本 "a\"b" 共 6 个字符（含首尾引号与转义反斜杠）；解码后的值是 a"b，长度 3。
            var tokens = ExprLexer.Tokenize("\"a\\\"b\"");
            var token = tokens[0];
            Assert.Equal(ExprTokenKind.StringLiteral, token.Kind);
            Assert.Equal("a\"b", token.Text);
            Assert.Equal(3, token.Text.Length);
            Assert.Equal(6, token.Length);
            Assert.NotEqual(token.Text.Length, token.Length);
        }

        [Fact]
        public void Tokenize_Eof_Token_At_End()
        {
            var tokens = ExprLexer.Tokenize("true");
            var eof = tokens[tokens.Count - 1];
            Assert.Equal(ExprTokenKind.Eof, eof.Kind);
            Assert.Equal(4, eof.Start);
            Assert.Equal(0, eof.Length);
            Assert.Equal(string.Empty, eof.Text);
        }

        [Fact]
        public void Tokenize_IllegalCharacter_Throws_At_Same_Position_As_Parser()
        {
            const string source = "self.hp_pct == 1 & 2";
            var lexerEx = Assert.Throws<ExprParseException>(() => ExprLexer.Tokenize(source));
            var parserEx = Assert.Throws<ExprParseException>(() => ExprParser.Parse(source, TestSchema.Build()));
            Assert.Equal(lexerEx.Position, parserEx.Position);
            Assert.Equal(source.IndexOf('&'), lexerEx.Position);
        }

        [Fact]
        public void Tokenize_UnterminatedString_Throws()
        {
            var ex = Assert.Throws<ExprParseException>(() => ExprLexer.Tokenize("\"unterminated"));
            Assert.Equal(0, ex.Position);
        }

        [Fact]
        public void Tokenize_Returns_ReadOnly_Sequence()
        {
            System.Collections.Generic.IReadOnlyList<ExprToken> tokens = ExprLexer.Tokenize("true");
            Assert.NotEmpty(tokens);
        }
    }
}
