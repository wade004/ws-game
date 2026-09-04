using System;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// <c>ExprParser.Parse</c> 解析失败时抛出的异常（见 04 第 6.4 节"解析期"错误）：
    /// 语法错误、未知运算符、字符串未闭合、非法标识符等词法/语法层错误。
    /// 不包含"未知分组/未知 key/类型不匹配"这类静态语义错误——那些由
    /// <c>ExprValidator.Validate</c> 产出 <see cref="ExprIssue"/> 列表报告，不属于解析失败。
    /// </summary>
    public sealed class ExprParseException : Exception
    {
        /// <summary>出错位置在原始表达式文本中的字符下标（从 0 开始）。</summary>
        public int Position { get; }

        public ExprParseException(int position, string message)
            : base(message)
        {
            Position = position;
        }
    }
}
