using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// Expr 纯手写递归下降解析器（见 04 第 6.1 节 BNF）：
    /// <c>expr := or_expr</c>，优先级 or &lt; and &lt; not &lt; 比较 &lt; 括号/字面量/引用。
    /// 不引入第二种语法，不使用任何脚本引擎/反射/表达式树。
    /// </summary>
    public static class ExprParser
    {
        public static ExprNode Parse(string text)
        {
            if (text == null)
            {
                throw new ExprParseException(0, "表达式文本为 null");
            }

            var tokens = ExprLexer.Tokenize(text);
            var state = new ParserState(tokens);
            var node = ParseOr(state);
            state.Expect(ExprTokenKind.Eof, "表达式结尾存在多余内容");
            return node;
        }

        private static ExprNode ParseOr(ParserState s)
        {
            var first = ParseAnd(s);
            if (s.Peek().Kind != ExprTokenKind.Or) return first;

            var operands = new List<ExprNode> { first };
            while (s.Peek().Kind == ExprTokenKind.Or)
            {
                s.Advance();
                operands.Add(ParseAnd(s));
            }
            return new ExprOrNode(operands);
        }

        private static ExprNode ParseAnd(ParserState s)
        {
            var first = ParseUnary(s);
            if (s.Peek().Kind != ExprTokenKind.And) return first;

            var operands = new List<ExprNode> { first };
            while (s.Peek().Kind == ExprTokenKind.And)
            {
                s.Advance();
                operands.Add(ParseUnary(s));
            }
            return new ExprAndNode(operands);
        }

        private static ExprNode ParseUnary(ParserState s)
        {
            if (s.Peek().Kind == ExprTokenKind.Not)
            {
                s.Advance();
                var operand = ParseUnary(s);
                return new ExprNotNode(operand);
            }
            return ParseCompare(s);
        }

        private static ExprNode ParseCompare(ParserState s)
        {
            var left = ParseTerm(s);
            var op = TryReadCmpOp(s);
            if (op == null) return left;

            var right = ParseTerm(s);
            return new ExprCompareNode(left, op.Value, right);
        }

        private static ExprCompareOp? TryReadCmpOp(ParserState s)
        {
            switch (s.Peek().Kind)
            {
                case ExprTokenKind.Eq: s.Advance(); return ExprCompareOp.Eq;
                case ExprTokenKind.Ne: s.Advance(); return ExprCompareOp.Ne;
                case ExprTokenKind.Gt: s.Advance(); return ExprCompareOp.Gt;
                case ExprTokenKind.Ge: s.Advance(); return ExprCompareOp.Ge;
                case ExprTokenKind.Lt: s.Advance(); return ExprCompareOp.Lt;
                case ExprTokenKind.Le: s.Advance(); return ExprCompareOp.Le;
                default: return null;
            }
        }

        private static ExprNode ParseTerm(ParserState s)
        {
            var token = s.Peek();
            switch (token.Kind)
            {
                case ExprTokenKind.IntLiteral:
                    s.Advance();
                    return new ExprLiteralNode(ExprValue.OfInt(token.IntValue));

                case ExprTokenKind.NumberLiteral:
                    s.Advance();
                    return new ExprLiteralNode(ExprValue.OfNumber(token.NumberValue));

                case ExprTokenKind.StringLiteral:
                    s.Advance();
                    return new ExprLiteralNode(ExprValue.OfString(token.Text));

                case ExprTokenKind.True:
                    s.Advance();
                    return new ExprLiteralNode(ExprValue.OfBool(true));

                case ExprTokenKind.False:
                    s.Advance();
                    return new ExprLiteralNode(ExprValue.OfBool(false));

                case ExprTokenKind.LParen:
                {
                    s.Advance();
                    var inner = ParseOr(s);
                    s.Expect(ExprTokenKind.RParen, "缺少右括号 ')'");
                    return inner;
                }

                case ExprTokenKind.Ident:
                    return ParseIdentTerm(s);

                default:
                    throw new ExprParseException(token.Position, $"表达式语法错误：未预期的记号 \"{token.Text}\"（位置 {token.Position}）");
            }
        }

        /// <summary>
        /// 点分标识符的归类判定（BNF 未定，本模块拍板，见 README"语法细节"）：
        /// 第一段若是 04 第 6.2 节九个分组之一，整体按 &lt;reference&gt; 解析（group.key，可选带参）；
        /// 否则整体按 Id 字面量解析（构造 <see cref="Id"/> 校验格式，非法格式在词法层已被拦截，
        /// 因为标识符段规则与 Id 格式规则一致，这里不会失败）。
        /// </summary>
        private static ExprNode ParseIdentTerm(ParserState s)
        {
            var token = s.Peek();
            s.Advance();

            var segments = token.Text.Split('.');
            if (segments.Length < 2)
            {
                throw new ExprParseException(token.Position, $"标识符缺少 domain/分组前缀：\"{token.Text}\"（位置 {token.Position}）");
            }

            var first = segments[0];
            if (ExprGroups.IsKnown(first))
            {
                var key = string.Join(".", segments, 1, segments.Length - 1);
                var args = new List<ExprNode>();
                if (s.Peek().Kind == ExprTokenKind.LParen)
                {
                    var lparen = s.Peek();
                    s.Advance();
                    if (s.Peek().Kind == ExprTokenKind.RParen)
                    {
                        // arg_list ::= term ("," term)* 至少一个 term；空括号不合法（见 README"语法细节"第 6 条），
                        // 零参引用应省略括号而不是写 "()"。
                        throw new ExprParseException(lparen.Position, "空参数列表 '()' 不合法：零参引用应省略括号（位置 " + lparen.Position + "）");
                    }
                    args.Add(ParseTerm(s));
                    while (s.Peek().Kind == ExprTokenKind.Comma)
                    {
                        s.Advance();
                        args.Add(ParseTerm(s));
                    }
                    s.Expect(ExprTokenKind.RParen, "缺少右括号 ')'");
                }
                return new ExprReferenceNode(first, key, args);
            }

            return new ExprLiteralNode(ExprValue.OfId(new Id(token.Text)));
        }

        private sealed class ParserState
        {
            private readonly List<ExprToken> _tokens;
            private int _pos;

            public ParserState(List<ExprToken> tokens)
            {
                _tokens = tokens;
            }

            public ExprToken Peek() => _tokens[_pos];

            public void Advance()
            {
                if (_pos < _tokens.Count - 1) _pos++;
            }

            public void Expect(ExprTokenKind kind, string errorMessage)
            {
                var token = Peek();
                if (token.Kind != kind)
                {
                    throw new ExprParseException(token.Position, $"{errorMessage}（位置 {token.Position}，实际记号 \"{token.Text}\"）");
                }
                Advance();
            }
        }
    }
}
