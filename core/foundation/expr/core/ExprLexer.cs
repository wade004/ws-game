using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// Expr 词法 token 的种类（消费方反馈 E5 根治，ADR-0020：词法切分入口纳入公开契约，
    /// 2026-09-10，见 architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E5）：随
    /// <see cref="ExprToken"/>/<see cref="ExprLexer"/> 一并从 <c>internal</c> 转为公开——此前
    /// 只有 <see cref="ExprParser"/> 内部消费，语法高亮等编辑器工具拿不到 token 种类/位置信息，
    /// 只能自己另写一套词法规则，与解析器实际认得的语法必然逐渐漂移（关键字集合、转义规则、点分
    /// 标识符边界等任何一处后续演进都得两处同步改，历史上没有强制约束保证这一点）。枚举成员/取值
    /// 未变，纯粹放宽可见性，不影响既有编译产物的行为。
    /// </summary>
    public enum ExprTokenKind
    {
        Ident,
        IntLiteral,
        NumberLiteral,
        StringLiteral,
        True,
        False,
        And,
        Or,
        Not,
        LParen,
        RParen,
        Comma,
        Eq,
        Ne,
        Gt,
        Ge,
        Lt,
        Le,
        Eof,
    }

    /// <summary>
    /// 一个 Expr 词法单元（消费方反馈 E5 根治，ADR-0020，见 <see cref="ExprTokenKind"/> 类型
    /// 注释判断记录）。<see cref="Start"/>/<see cref="Length"/> 是该 token 在源文本里的原始字符
    /// 区间（半开区间 <c>[Start, Start+Length)</c>），供编辑器做高亮/悬浮提示时定位；
    /// <see cref="Text"/> 是解码后的值——多数 token 种类下 <c>Text.Length == Length</c>，但
    /// <see cref="ExprTokenKind.StringLiteral"/> 例外：源文本里的引号与转义序列（<c>\"</c>/<c>\\</c>）
    /// 在 <see cref="Text"/> 里已被解码/去除（如源文本 <c>"a\"b"</c> 长度 6，解码后 <c>Text</c> 是
    /// <c>a"b</c> 长度 3），因此两者不保证相等——需要"这段 token 在源文本里占多少字符"时用
    /// <see cref="Length"/>，需要"这个字符串字面量的实际值"时用 <see cref="Text"/>。
    /// </summary>
    public readonly struct ExprToken
    {
        /// <summary>token 种类。</summary>
        public ExprTokenKind Kind { get; }

        /// <summary>解码后的文本值（字符串字面量已去除引号/转义；其余种类等于源文本原样切片）。</summary>
        public string Text { get; }

        /// <summary>该 token 在源文本里的起始字符偏移（0 基），与 <see cref="ExprParseException"/>
        /// 报告的位置同一套坐标系。</summary>
        public int Start { get; }

        /// <summary>该 token 在源文本里占用的原始字符数（见类型注释"<c>Text</c> 与 <c>Length</c>
        /// 的关系"）。<see cref="ExprTokenKind.Eof"/> 固定为 0。</summary>
        public int Length { get; }

        /// <summary><see cref="ExprTokenKind.IntLiteral"/> 的解析值；其它种类恒为 0。</summary>
        public long IntValue { get; }

        /// <summary><see cref="ExprTokenKind.NumberLiteral"/> 的解析值；其它种类恒为 0。</summary>
        public double NumberValue { get; }

        public ExprToken(ExprTokenKind kind, string text, int start, int length, long intValue = 0, double numberValue = 0)
        {
            Kind = kind;
            Text = text;
            Start = start;
            Length = length;
            IntValue = intValue;
            NumberValue = numberValue;
        }
    }

    /// <summary>
    /// Expr 纯手写词法器：空白分隔，识别括号/逗号/比较运算符/字符串/数字/关键字/点分标识符
    /// （见 04 第 6.1 节 BNF 与本模块 README 的"语法细节"补充约定）。不使用任何正则/反射，
    /// 单遍扫描字符数组。
    /// <para>
    /// 判断记录（消费方反馈 E5 根治，ADR-0020：词法切分入口纳入公开契约，2026-09-10，见
    /// architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E5）：本类型与
    /// <see cref="ExprToken"/>/<see cref="ExprTokenKind"/> 一并从 <c>internal</c> 改为
    /// <c>public</c>，成为 <c>core/foundation/expr</c> 公开契约面的一部分——<see cref="Tokenize"/>
    /// 是 Expr 解析器契约新增的公开词法切分入口，与 <see cref="ExprParser.Parse"/> 共用同一套词法
    /// 规则（<see cref="ExprParser.Parse"/> 内部就是先调用本方法拿到 token 序列再语法分析，这里
    /// 改动前后是同一份实现，没有派生出第二套判断逻辑）。编辑器等工具做语法高亮/token 级诊断时应
    /// 调用本方法，不得另写一套独立的词法规则——两套实现会随语言演进逐渐漂移（关键字集合、转义
    /// 规则、点分标识符边界等任何一处变化都需要保证两处同步，另写一套等于放弃这个保证）。非法字符/
    /// 未闭合字符串等词法错误抛出的 <see cref="ExprParseException"/> 与 <see cref="ExprParser.Parse"/>
    /// 对同一段非法输入报出的异常位置/消息一致（同一份实现，不可能不一致）。见
    /// architecture/adr/0020-表达式词法器纳入公开契约.md。
    /// </para>
    /// </summary>
    public static class ExprLexer
    {
        /// <summary>
        /// 把 Expr 源文本切分为只读 token 序列（含结尾的 <see cref="ExprTokenKind.Eof"/> token）。
        /// 非法字符/未闭合字符串/非法转义等词法错误抛出 <see cref="ExprParseException"/>（位置与
        /// <see cref="ExprParser.Parse"/> 对同一输入的报错位置一致，见类型注释）。
        /// </summary>
        public static IReadOnlyList<ExprToken> Tokenize(string text)
        {
            var tokens = new List<ExprToken>();
            int i = 0;
            int n = text.Length;

            while (i < n)
            {
                char c = text[i];

                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    i++;
                    continue;
                }

                if (c == '(') { tokens.Add(new ExprToken(ExprTokenKind.LParen, "(", i, 1)); i++; continue; }
                if (c == ')') { tokens.Add(new ExprToken(ExprTokenKind.RParen, ")", i, 1)); i++; continue; }
                if (c == ',') { tokens.Add(new ExprToken(ExprTokenKind.Comma, ",", i, 1)); i++; continue; }

                if (c == '=')
                {
                    if (i + 1 < n && text[i + 1] == '=') { tokens.Add(new ExprToken(ExprTokenKind.Eq, "==", i, 2)); i += 2; continue; }
                    throw new ExprParseException(i, $"非法字符 '='：比较运算符只能是 == != > >= < <=（位置 {i}）");
                }

                if (c == '!')
                {
                    if (i + 1 < n && text[i + 1] == '=') { tokens.Add(new ExprToken(ExprTokenKind.Ne, "!=", i, 2)); i += 2; continue; }
                    throw new ExprParseException(i, $"非法字符 '!'：只支持 '!=' 运算符（位置 {i}）");
                }

                if (c == '>')
                {
                    if (i + 1 < n && text[i + 1] == '=') { tokens.Add(new ExprToken(ExprTokenKind.Ge, ">=", i, 2)); i += 2; continue; }
                    tokens.Add(new ExprToken(ExprTokenKind.Gt, ">", i, 1));
                    i++;
                    continue;
                }

                if (c == '<')
                {
                    if (i + 1 < n && text[i + 1] == '=') { tokens.Add(new ExprToken(ExprTokenKind.Le, "<=", i, 2)); i += 2; continue; }
                    tokens.Add(new ExprToken(ExprTokenKind.Lt, "<", i, 1));
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    int start = i;
                    i++;
                    var sb = new StringBuilder();
                    bool closed = false;
                    while (i < n)
                    {
                        char ch = text[i];
                        if (ch == '"') { closed = true; i++; break; }
                        if (ch == '\\')
                        {
                            if (i + 1 >= n)
                            {
                                throw new ExprParseException(start, $"字符串未闭合：转义符位于末尾（起始位置 {start}）");
                            }
                            char next = text[i + 1];
                            if (next == '"') { sb.Append('"'); i += 2; continue; }
                            if (next == '\\') { sb.Append('\\'); i += 2; continue; }
                            throw new ExprParseException(i, $"非法转义序列 '\\{next}'：只支持 \\\" 与 \\\\（位置 {i}）");
                        }
                        sb.Append(ch);
                        i++;
                    }
                    if (!closed)
                    {
                        throw new ExprParseException(start, $"字符串未闭合：缺少结尾双引号（起始位置 {start}）");
                    }
                    // 判断记录：Length 取原始源文本区间（含引号与转义序列本身的反斜杠字符），不是
                    // 解码后 sb.ToString() 的长度——见 ExprToken 类型注释"Text 与 Length 的关系"。
                    tokens.Add(new ExprToken(ExprTokenKind.StringLiteral, sb.ToString(), start, i - start));
                    continue;
                }

                bool isNegativeNumberStart = c == '-' && i + 1 < n && text[i + 1] >= '0' && text[i + 1] <= '9';
                if ((c >= '0' && c <= '9') || isNegativeNumberStart)
                {
                    int start = i;
                    if (c == '-') i++;
                    while (i < n && text[i] >= '0' && text[i] <= '9') i++;

                    bool isNumber = false;
                    if (i < n && text[i] == '.' && i + 1 < n && text[i + 1] >= '0' && text[i + 1] <= '9')
                    {
                        isNumber = true;
                        i++;
                        while (i < n && text[i] >= '0' && text[i] <= '9') i++;
                    }

                    string numText = text.Substring(start, i - start);
                    if (isNumber)
                    {
                        // F-03 根治：JSON 侧 F-01 的同源问题——数字语法合法（如 500 位十进制小数）但
                        // double.Parse 静默饱和为 Infinity，不抛异常。词法层必须拒绝，否则非有限数值
                        // 字面量会以 ExprLiteralNode(ExprValue.OfNumber(Infinity)) 的形式混进解析树，
                        // 后续求值/静态校验都不会再检查这一点。
                        double value = double.Parse(numText, NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (!double.IsFinite(value))
                        {
                            throw new ExprParseException(start, $"数字字面量 \"{numText}\" 语法合法，但解析结果不是有限浮点数（位置 {start}）");
                        }
                        tokens.Add(new ExprToken(ExprTokenKind.NumberLiteral, numText, start, i - start, numberValue: value));
                    }
                    else
                    {
                        // F-03 根治：整数字面量超出 long 范围时，long.Parse 会抛出没有位置信息的
                        // OverflowException，逃出本词法器"非法输入统一抛 ExprParseException"的契约
                        // （见类型/方法注释）。改用 TryParse，失败时按同一契约抛带位置的 ExprParseException。
                        if (!long.TryParse(numText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                        {
                            throw new ExprParseException(start, $"整数字面量 \"{numText}\" 超出可表示范围（位置 {start}）");
                        }
                        tokens.Add(new ExprToken(ExprTokenKind.IntLiteral, numText, start, i - start, intValue: value));
                    }
                    continue;
                }

                if (c >= 'a' && c <= 'z')
                {
                    int start = i;
                    i++;
                    while (i < n && IsIdentBodyChar(text[i])) i++;

                    // 点分多段：仅当 '.' 后紧跟一个合法段起始字符（小写字母）才继续消费为同一个标识符 token。
                    while (i < n && text[i] == '.' && i + 1 < n && text[i + 1] >= 'a' && text[i + 1] <= 'z')
                    {
                        i++; // consume '.'
                        i++; // consume 段首字符（已确认是 a-z）
                        while (i < n && IsIdentBodyChar(text[i])) i++;
                    }

                    string ident = text.Substring(start, i - start);
                    int identLength = i - start;
                    switch (ident)
                    {
                        case "and": tokens.Add(new ExprToken(ExprTokenKind.And, ident, start, identLength)); break;
                        case "or": tokens.Add(new ExprToken(ExprTokenKind.Or, ident, start, identLength)); break;
                        case "not": tokens.Add(new ExprToken(ExprTokenKind.Not, ident, start, identLength)); break;
                        case "true": tokens.Add(new ExprToken(ExprTokenKind.True, ident, start, identLength)); break;
                        case "false": tokens.Add(new ExprToken(ExprTokenKind.False, ident, start, identLength)); break;
                        default: tokens.Add(new ExprToken(ExprTokenKind.Ident, ident, start, identLength)); break;
                    }
                    continue;
                }

                throw new ExprParseException(i, $"非法字符 '{c}'（位置 {i}）");
            }

            tokens.Add(new ExprToken(ExprTokenKind.Eof, string.Empty, n, 0));
            return tokens;
        }

        private static bool IsIdentBodyChar(char ch) => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_';
    }
}
