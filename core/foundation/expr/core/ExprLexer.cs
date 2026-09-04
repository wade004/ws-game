using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Core.Foundation.Expr
{
    internal enum ExprTokenKind
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

    internal readonly struct ExprToken
    {
        public ExprTokenKind Kind { get; }

        public string Text { get; }

        public int Position { get; }

        public long IntValue { get; }

        public double NumberValue { get; }

        public ExprToken(ExprTokenKind kind, string text, int position, long intValue = 0, double numberValue = 0)
        {
            Kind = kind;
            Text = text;
            Position = position;
            IntValue = intValue;
            NumberValue = numberValue;
        }
    }

    /// <summary>
    /// Expr 纯手写词法器：空白分隔，识别括号/逗号/比较运算符/字符串/数字/关键字/点分标识符
    /// （见 04 第 6.1 节 BNF 与本模块 README 的"语法细节"补充约定）。不使用任何正则/反射，
    /// 单遍扫描字符数组。
    /// </summary>
    internal static class ExprLexer
    {
        public static List<ExprToken> Tokenize(string text)
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

                if (c == '(') { tokens.Add(new ExprToken(ExprTokenKind.LParen, "(", i)); i++; continue; }
                if (c == ')') { tokens.Add(new ExprToken(ExprTokenKind.RParen, ")", i)); i++; continue; }
                if (c == ',') { tokens.Add(new ExprToken(ExprTokenKind.Comma, ",", i)); i++; continue; }

                if (c == '=')
                {
                    if (i + 1 < n && text[i + 1] == '=') { tokens.Add(new ExprToken(ExprTokenKind.Eq, "==", i)); i += 2; continue; }
                    throw new ExprParseException(i, $"非法字符 '='：比较运算符只能是 == != > >= < <=（位置 {i}）");
                }

                if (c == '!')
                {
                    if (i + 1 < n && text[i + 1] == '=') { tokens.Add(new ExprToken(ExprTokenKind.Ne, "!=", i)); i += 2; continue; }
                    throw new ExprParseException(i, $"非法字符 '!'：只支持 '!=' 运算符（位置 {i}）");
                }

                if (c == '>')
                {
                    if (i + 1 < n && text[i + 1] == '=') { tokens.Add(new ExprToken(ExprTokenKind.Ge, ">=", i)); i += 2; continue; }
                    tokens.Add(new ExprToken(ExprTokenKind.Gt, ">", i));
                    i++;
                    continue;
                }

                if (c == '<')
                {
                    if (i + 1 < n && text[i + 1] == '=') { tokens.Add(new ExprToken(ExprTokenKind.Le, "<=", i)); i += 2; continue; }
                    tokens.Add(new ExprToken(ExprTokenKind.Lt, "<", i));
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
                    tokens.Add(new ExprToken(ExprTokenKind.StringLiteral, sb.ToString(), start));
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
                        double value = double.Parse(numText, CultureInfo.InvariantCulture);
                        tokens.Add(new ExprToken(ExprTokenKind.NumberLiteral, numText, start, numberValue: value));
                    }
                    else
                    {
                        long value = long.Parse(numText, CultureInfo.InvariantCulture);
                        tokens.Add(new ExprToken(ExprTokenKind.IntLiteral, numText, start, intValue: value));
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
                    switch (ident)
                    {
                        case "and": tokens.Add(new ExprToken(ExprTokenKind.And, ident, start)); break;
                        case "or": tokens.Add(new ExprToken(ExprTokenKind.Or, ident, start)); break;
                        case "not": tokens.Add(new ExprToken(ExprTokenKind.Not, ident, start)); break;
                        case "true": tokens.Add(new ExprToken(ExprTokenKind.True, ident, start)); break;
                        case "false": tokens.Add(new ExprToken(ExprTokenKind.False, ident, start)); break;
                        default: tokens.Add(new ExprToken(ExprTokenKind.Ident, ident, start)); break;
                    }
                    continue;
                }

                throw new ExprParseException(i, $"非法字符 '{c}'（位置 {i}）");
            }

            tokens.Add(new ExprToken(ExprTokenKind.Eof, string.Empty, n));
            return tokens;
        }

        private static bool IsIdentBodyChar(char ch) => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_';
    }
}
