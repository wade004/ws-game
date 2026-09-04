namespace Core.Foundation.Expr
{
    /// <summary>静态校验发现的问题种类，对应 04 第 5 节"表达式可解析"与第 6.4 节"解析期"错误情形。</summary>
    public enum ExprIssueKind
    {
        /// <summary>引用的分组不是 6.2 节九个分组之一。</summary>
        UnknownGroup,

        /// <summary>分组已知，但 key 未在 <see cref="IExprSchema"/> 中登记。</summary>
        UnknownKey,

        /// <summary>引用的实参个数与登记的签名不符。</summary>
        ArgCountMismatch,

        /// <summary>某个实参的静态类型与登记签名的期望类型不符。</summary>
        ArgTypeMismatch,

        /// <summary>比较运算符两侧的静态类型不一致（且都不是可互相比较的数值类型）。</summary>
        CompareTypeMismatch,

        /// <summary>比较运算符两侧类型一致，但该类型不支持所用的运算符（如 Id/Bool/String 参与 &lt; &gt;）。</summary>
        InvalidCompareForType,

        /// <summary><c>and</c>/<c>or</c>/<c>not</c> 的操作数静态类型不是 Bool。</summary>
        LogicalOperandNotBool,
    }

    /// <summary>一条静态校验问题：种类 + 可读消息。空列表即通过校验（见 04 第 6.4 节）。</summary>
    public readonly struct ExprIssue
    {
        public ExprIssueKind Kind { get; }

        public string Message { get; }

        public ExprIssue(ExprIssueKind kind, string message)
        {
            Kind = kind;
            Message = message;
        }

        public override string ToString() => Message;
    }
}
