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
        /// <summary>
        /// 解析一段 Expr 文本。<paramref name="schema"/> 是必填的宿主引用登记表：点分标识符
        /// 归类为引用还是 Id 字面量，以它为准（见 ADR-0015、README"语法细节"第 5 条）。
        /// </summary>
        public static ExprNode Parse(string text, IExprSchema schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (text == null)
            {
                throw new ExprParseException(0, "表达式文本为 null");
            }

            var tokens = ExprLexer.Tokenize(text);
            var state = new ParserState(tokens, schema);
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
            var span = ExprNode.SpanOf(operands[0], operands[operands.Count - 1]);
            return new ExprOrNode(operands, span.Start, span.Length);
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
            var span = ExprNode.SpanOf(operands[0], operands[operands.Count - 1]);
            return new ExprAndNode(operands, span.Start, span.Length);
        }

        private static ExprNode ParseUnary(ParserState s)
        {
            if (s.Peek().Kind == ExprTokenKind.Not)
            {
                var notToken = s.Peek();
                s.Advance();
                var operand = ParseUnary(s);
                var length = operand.Start >= 0 ? (operand.Start + operand.Length) - notToken.Start : 0;
                return new ExprNotNode(operand, notToken.Start, length);
            }
            return ParseCompare(s);
        }

        private static ExprNode ParseCompare(ParserState s)
        {
            var left = ParseTerm(s);
            var op = TryReadCmpOp(s);
            if (op == null) return left;

            var right = ParseTerm(s);
            var span = ExprNode.SpanOf(left, right);
            return new ExprCompareNode(left, op.Value, right, span.Start, span.Length);
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
                    return new ExprLiteralNode(ExprValue.OfInt(token.IntValue), token.Start, token.Length);

                case ExprTokenKind.NumberLiteral:
                    s.Advance();
                    return new ExprLiteralNode(ExprValue.OfNumber(token.NumberValue), token.Start, token.Length);

                case ExprTokenKind.StringLiteral:
                    s.Advance();
                    return new ExprLiteralNode(ExprValue.OfString(token.Text), token.Start, token.Length);

                case ExprTokenKind.True:
                    s.Advance();
                    return new ExprLiteralNode(ExprValue.OfBool(true), token.Start, token.Length);

                case ExprTokenKind.False:
                    s.Advance();
                    return new ExprLiteralNode(ExprValue.OfBool(false), token.Start, token.Length);

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
                    throw new ExprParseException(token.Start, $"表达式语法错误：未预期的记号 \"{token.Text}\"（位置 {token.Start}）");
            }
        }

        /// <summary>
        /// 点分标识符的归类判定（BNF 未定，ADR-0015 拍板，见 README"语法细节"）：以宿主引用
        /// 登记表 <see cref="IExprSchema"/> 为准——<c>segments[0]</c> 与"其余段以点连接"合起来
        /// 若在 <paramref name="s"/> 携带的 schema 中登记了签名，整体按 &lt;reference&gt; 解析
        /// （group 取第一段，key 取剩余部分，可选带参数列表）；否则整体按 Id 字面量解析（构造
        /// <see cref="Id"/> 校验格式，非法格式在词法层已被拦截，因为标识符段规则严格蕴含
        /// Id 格式规则，这里不会失败）。未登记的标识符若紧跟 <c>(</c>，说明调用方误以为它是
        /// 一个引用，报解析错误而不是把参数列表悄悄丢弃。
        /// </summary>
        private static ExprNode ParseIdentTerm(ParserState s)
        {
            var token = s.Peek();
            s.Advance();

            var segments = token.Text.Split('.');
            if (segments.Length < 2)
            {
                throw new ExprParseException(token.Start, $"标识符缺少 domain/分组前缀：\"{token.Text}\"（位置 {token.Start}）");
            }

            var group = segments[0];
            var key = string.Join(".", segments, 1, segments.Length - 1);

            // 判断记录（阶段 3 集成"事项二"）：event 分组的具体 key 随触发事件类型动态变化，任何一份
            // IExprSchema 都不可能穷举登记；同时 event 不是内容域名（04 §2.2 域名清单不含 event），
            // 不存在 quest.deliver_letter 那类"未登记即合理回退为内容 Id 字面量"的场景——把未登记的
            // event.<key> 回退成 Id 字面量会让 ExprEvaluator 将其当作一个字面 Id 常量参与运算，而不是
            // 查询触发事件的字段，语义整体错误（见 core/gameplay/achievement/core/AchievementHost.cs
            // 构造函数判断记录记录的同一个契约缺口）。因此：schema 未登记的 event.<key> 仍整体解析为
            // <reference>（group="event"，签名未知——ExprValidator 对此跳过静态类型检查，见该类型
            // ValidateReference 判断记录），而不是退回 Id 字面量分支；schema 显式登记过的 event.<key>
            // （如测试/游戏层为已知事件字段声明的精确签名）优先沿用原有强类型校验路径，本改动不影响。
            var isKnownReference = s.Schema.TryGetSignature(group, key, out _);
            var isEventFallbackReference = !isKnownReference && group == ExprGroups.Event;

            if (isKnownReference || isEventFallbackReference)
            {
                var args = new List<ExprNode>();
                var identEnd = token.Start + token.Length;
                if (s.Peek().Kind == ExprTokenKind.LParen)
                {
                    var lparen = s.Peek();
                    s.Advance();
                    if (s.Peek().Kind == ExprTokenKind.RParen)
                    {
                        // arg_list ::= term ("," term)* 至少一个 term；空括号不合法（见 README"语法细节"第 6 条），
                        // 零参引用应省略括号而不是写 "()"。
                        throw new ExprParseException(lparen.Start, "空参数列表 '()' 不合法：零参引用应省略括号（位置 " + lparen.Start + "）");
                    }
                    args.Add(ParseTerm(s));
                    while (s.Peek().Kind == ExprTokenKind.Comma)
                    {
                        s.Advance();
                        args.Add(ParseTerm(s));
                    }
                    var closeParen = s.Peek();
                    s.Expect(ExprTokenKind.RParen, "缺少右括号 ')'");
                    // 消费方反馈第三批第 19 条：引用节点的源区间须覆盖到闭括号（若有参数列表），
                    // 不只是 "group.key" 这一段标识符本身。
                    identEnd = closeParen.Start + closeParen.Length;
                }
                return new ExprReferenceNode(group, key, args, token.Start, identEnd - token.Start);
            }

            // 未在登记表中命中 -> 整体作为 Id 字面量（ADR-0015）。
            var idValue = new Id(token.Text);

            if (s.Peek().Kind == ExprTokenKind.LParen)
            {
                var lparen = s.Peek();
                throw new ExprParseException(lparen.Start,
                    $"未登记的引用不能带参数列表：\"{token.Text}\"（位置 {lparen.Start}）——如果这本应是一个引用，请先在 IExprSchema 中登记 \"{group}.{key}\" 的签名");
            }

            return new ExprLiteralNode(ExprValue.OfId(idValue), token.Start, token.Length);
        }

        private sealed class ParserState
        {
            private readonly IReadOnlyList<ExprToken> _tokens;
            private int _pos;

            public IExprSchema Schema { get; }

            public ParserState(IReadOnlyList<ExprToken> tokens, IExprSchema schema)
            {
                _tokens = tokens;
                Schema = schema;
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
                    throw new ExprParseException(token.Start, $"{errorMessage}（位置 {token.Start}，实际记号 \"{token.Text}\"）");
                }
                Advance();
            }
        }
    }
}
