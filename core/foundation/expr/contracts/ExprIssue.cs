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

        /// <summary>
        /// 消费方反馈第三批第 19 条（2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 19 条）：该问题对应的
        /// 源文本起始字符偏移（0 基，与 <see cref="ExprParseException.Position"/>/
        /// <see cref="ExprToken.Start"/> 同一套坐标系）；<c>-1</c> 表示未定位到具体源区间——旧
        /// 构造重载（不传位置）与不携带区间信息的问题种类（如某些无法归到单一子树的整体性问题）
        /// 都会落在这个默认值上，调用方须先判断 <c>Start &gt;= 0</c> 再使用区间信息。
        /// </summary>
        public int Start { get; }

        /// <summary>该问题对应源文本区间的字符长度（半开区间 <c>[Start, Start+Length)</c>）；
        /// <see cref="Start"/> 为 <c>-1</c> 时恒为 <c>0</c>，没有独立含义。</summary>
        public int Length { get; }

        /// <summary>构造一条 <see cref="ExprIssueSeverity.Error"/> 级别的问题（校验器里绝大多数问题都是错误）。</summary>
        public ExprIssue(ExprIssueKind kind, string message)
            : this(kind, ExprIssueSeverity.Error, message)
        {
        }

        /// <summary>构造一条 <see cref="ExprIssueSeverity.Error"/> 级别、带源区间的问题（消费方反馈
        /// 第三批第 19 条新增的可选参数重载；不传时等价于既有两参数构造，<see cref="Start"/> 落在
        /// 默认值 <c>-1</c>）。</summary>
        public ExprIssue(ExprIssueKind kind, string message, int start, int length = 0)
            : this(kind, ExprIssueSeverity.Error, message, start, length)
        {
        }

        public ExprIssue(ExprIssueKind kind, ExprIssueSeverity severity, string message)
            : this(kind, severity, message, -1, 0)
        {
        }

        /// <summary>消费方反馈第三批第 19 条新增的可选参数重载：既有三参数构造（kind/severity/message）
        /// 保持不变（<see cref="Start"/> 默认为 <c>-1</c>），本重载额外接受源区间。</summary>
        public ExprIssue(ExprIssueKind kind, ExprIssueSeverity severity, string message, int start, int length = 0)
        {
            Kind = kind;
            Severity = severity;
            Message = message;
            Start = start;
            Length = length;
        }

        public override string ToString() => Start >= 0
            ? $"[{Severity}] {Message}（位置 {Start}，长度 {Length}）"
            : $"[{Severity}] {Message}";
    }
}
