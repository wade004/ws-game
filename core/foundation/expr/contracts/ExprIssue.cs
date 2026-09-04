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

        /// <summary>
        /// 疑似引用拼写错误（ADR-0015）：某个 Id 字面量的域名与九个分组之一同名，但
        /// "域名.其余段" 并未在 <see cref="IExprSchema"/> 中登记为引用——这通常意味着调用方
        /// 原本想写一个引用，却因为 <c>group.key</c> 未登记而被 <see cref="ExprParser"/> 按
        /// Id 字面量归类。不阻断校验（<see cref="ExprIssueSeverity.Warning"/>），因为"和分组
        /// 同名的 Id 字面量"本身是合法用法（04 第 2.2 节域名清单允许 world/quest/target/combat
        /// 同时也是内容 id 的合法 domain）。
        /// </summary>
        SuspiciousReferenceSpelling,
    }

    /// <summary>问题严重级别：<see cref="Error"/> 阻断内容合入，<see cref="Warning"/> 不阻断。</summary>
    public enum ExprIssueSeverity
    {
        Error,
        Warning,
    }

    /// <summary>一条静态校验问题：种类 + 严重级别 + 可读消息。空列表即通过校验（见 04 第 6.4 节）。</summary>
    public readonly struct ExprIssue
    {
        public ExprIssueKind Kind { get; }

        public ExprIssueSeverity Severity { get; }

        public string Message { get; }

        /// <summary>构造一条 <see cref="ExprIssueSeverity.Error"/> 级别的问题（校验器里绝大多数问题都是错误）。</summary>
        public ExprIssue(ExprIssueKind kind, string message)
            : this(kind, ExprIssueSeverity.Error, message)
        {
        }

        public ExprIssue(ExprIssueKind kind, ExprIssueSeverity severity, string message)
        {
            Kind = kind;
            Severity = severity;
            Message = message;
        }

        public override string ToString() => $"[{Severity}] {Message}";
    }
}
