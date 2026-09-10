using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Core.Foundation.Expr
{
    /// <summary>比较运算符（见 04 第 6.1 节 BNF <c>cmp_op</c>）。</summary>
    public enum ExprCompareOp
    {
        Eq,
        Ne,
        Gt,
        Ge,
        Lt,
        Le,
    }

    /// <summary>
    /// Expr 语法树的不可变节点基类（见 04 第 6.1 节 BNF）。具体形态：
    /// <see cref="ExprOrNode"/>、<see cref="ExprAndNode"/>、<see cref="ExprNotNode"/>、
    /// <see cref="ExprCompareNode"/>、<see cref="ExprLiteralNode"/>、<see cref="ExprReferenceNode"/>。
    /// <see cref="ToString"/> 把节点还原为规范化的 Expr 文本，保证按最低必要原则插入括号
    /// （运算符优先级：or &lt; and &lt; not &lt; 比较 &lt; 字面量/引用，括号仅用于改变优先级）。
    /// </summary>
    public abstract class ExprNode
    {
        /// <summary>
        /// 消费方反馈第三批第 19 条（2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 19 条）：该节点在源文本里
        /// 的起始字符偏移（0 基，与 <see cref="ExprToken.Start"/>/<see cref="ExprParseException.Position"/>
        /// 同一套坐标系）。<c>-1</c> 表示未知——本类型的既有构造方式（不传位置信息，供既有测试与
        /// 除 <see cref="ExprParser"/> 之外的调用方直接手写语法树时使用）都落在这个默认值上；只有
        /// <see cref="ExprParser.Parse"/> 解析源文本产出的节点会带上精确区间。
        /// </summary>
        public int Start { get; }

        /// <summary>该节点覆盖的源文本长度（半开区间 <c>[Start, Start+Length)</c>）；<see cref="Start"/>
        /// 为 <c>-1</c> 时恒为 <c>0</c>，没有独立含义。</summary>
        public int Length { get; }

        /// <summary>
        /// 硬规则（公开 API 表面差异门禁）：本类型原先没有任何显式构造函数（抽象类的隐式默认构造
        /// 是 <c>protected</c>），不能给它加可选参数——那会把物理签名从 <c>.ctor()</c> 改成
        /// <c>.ctor(int, int)</c>，让已编译消费方对旧 <c>.ctor()</c> 的调用在运行期
        /// <see cref="System.MissingMethodException"/>（toolchain/abi_probe 门禁真实拦到过这个
        /// 问题，见修复判断记录）。这里改为两个物理上各自独立的构造函数：无参的保留原物理签名，
        /// 新增的双参数版本是纯增量。</summary>
        protected ExprNode()
            : this(-1, 0)
        {
        }

        /// <summary>消费方反馈第三批第 19 条新增：带源区间的构造——与无参 <see cref="ExprNode()"/>
        /// 物理上是两个不同的构造函数（真正的重载新增），不是给原有 <c>.ctor()</c> 追加可选参数。</summary>
        protected ExprNode(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public abstract override string ToString();

        public abstract override bool Equals(object? obj);

        public abstract override int GetHashCode();

        /// <summary>把节点打印为 term 位置合法的文本：只有字面量/引用本身是原子 term，其余一律加括号。</summary>
        internal static string PrintAsTerm(ExprNode node) =>
            node is ExprLiteralNode || node is ExprReferenceNode ? node.ToString() : "(" + node + ")";

        /// <summary>把节点打印为 unary_expr（not/and 操作数）位置合法的文本：or 需要加括号，其余不需要。</summary>
        internal static string PrintAsUnaryOperand(ExprNode node) =>
            node is ExprOrNode || node is ExprAndNode ? "(" + node + ")" : node.ToString();

        /// <summary>把节点打印为 and_expr（or 操作数）位置合法的文本：只有 or 本身需要加括号。</summary>
        internal static string PrintAsAndOperand(ExprNode node) =>
            node is ExprOrNode ? "(" + node + ")" : node.ToString();

        /// <summary>消费方反馈第三批第 19 条：由两个已知端点节点推出覆盖两者的源区间——两端点
        /// 任一 <see cref="Start"/> 为 <c>-1</c>（未知）时，结果也是未知（<c>(-1, 0)</c>），不猜测。
        /// 供 <see cref="ExprParser"/> 构造复合节点（比较/and/or/not）时计算自身区间。</summary>
        internal static (int Start, int Length) SpanOf(ExprNode first, ExprNode last)
        {
            if (first.Start < 0 || last.Start < 0) return (-1, 0);
            return (first.Start, last.Start + last.Length - first.Start);
        }
    }

    /// <summary>字面量：Bool、Int、Number、String，或不属于任何分组前缀的 Id。</summary>
    public sealed class ExprLiteralNode : ExprNode
    {
        public ExprValue Value { get; }

        public ExprLiteralNode(ExprValue value)
            : this(value, -1, 0)
        {
        }

        /// <summary>消费方反馈第三批第 19 条新增：与 <see cref="ExprLiteralNode(ExprValue)"/> 物理上
        /// 是两个不同的构造函数（见 <see cref="ExprNode"/> 类型级判断记录，不给既有构造加可选参数）。</summary>
        public ExprLiteralNode(ExprValue value, int start, int length)
            : base(start, length)
        {
            Value = value;
        }

        public override string ToString()
        {
            if (Value.Kind == ExprValueKind.String)
            {
                var sb = new StringBuilder();
                sb.Append('"');
                foreach (var c in Value.AsString)
                {
                    if (c == '"') sb.Append("\\\"");
                    else if (c == '\\') sb.Append("\\\\");
                    else sb.Append(c);
                }
                sb.Append('"');
                return sb.ToString();
            }

            return Value.ToString();
        }

        public override bool Equals(object? obj) => obj is ExprLiteralNode other && Value.Equals(other.Value);

        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>宿主引用：<c>group.key</c>，可选带参数列表（见 04 第 6.1、6.2 节）。</summary>
    public sealed class ExprReferenceNode : ExprNode
    {
        public string Group { get; }

        public string Key { get; }

        public IReadOnlyList<ExprNode> Args { get; }

        public ExprReferenceNode(string group, string key, IReadOnlyList<ExprNode> args)
            : this(group, key, args, -1, 0)
        {
        }

        /// <summary>消费方反馈第三批第 19 条新增：与三参数构造物理上是两个不同的构造函数
        /// （见 <see cref="ExprNode"/> 类型级判断记录）。</summary>
        public ExprReferenceNode(string group, string key, IReadOnlyList<ExprNode> args, int start, int length)
            : base(start, length)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Args = args ?? Array.Empty<ExprNode>();
        }

        public override string ToString()
        {
            if (Args.Count == 0) return Group + "." + Key;
            return Group + "." + Key + "(" + string.Join(", ", Args.Select(PrintAsTerm)) + ")";
        }

        public override bool Equals(object? obj)
        {
            if (!(obj is ExprReferenceNode other)) return false;
            if (!string.Equals(Group, other.Group, StringComparison.Ordinal)) return false;
            if (!string.Equals(Key, other.Key, StringComparison.Ordinal)) return false;
            if (Args.Count != other.Args.Count) return false;
            for (int i = 0; i < Args.Count; i++)
            {
                if (!Args[i].Equals(other.Args[i])) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Group) * 397 ^ StringComparer.Ordinal.GetHashCode(Key);
                foreach (var arg in Args)
                {
                    hash = hash * 397 ^ arg.GetHashCode();
                }
                return hash;
            }
        }
    }

    /// <summary><c>not</c> 一元否定，操作数是另一个 unary_expr（可链式嵌套）。</summary>
    public sealed class ExprNotNode : ExprNode
    {
        public ExprNode Operand { get; }

        public ExprNotNode(ExprNode operand)
            : this(operand, -1, 0)
        {
        }

        /// <summary>消费方反馈第三批第 19 条新增：与单参数构造物理上是两个不同的构造函数
        /// （见 <see cref="ExprNode"/> 类型级判断记录）。</summary>
        public ExprNotNode(ExprNode operand, int start, int length)
            : base(start, length)
        {
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        }

        public override string ToString() => "not " + PrintAsUnaryOperand(Operand);

        public override bool Equals(object? obj) => obj is ExprNotNode other && Operand.Equals(other.Operand);

        public override int GetHashCode() => unchecked(typeof(ExprNotNode).GetHashCode() * 397 ^ Operand.GetHashCode());
    }

    /// <summary>比较表达式：两个 term 之间的六种比较运算符之一（见 04 第 6.1 节）。</summary>
    public sealed class ExprCompareNode : ExprNode
    {
        public ExprNode Left { get; }

        public ExprCompareOp Op { get; }

        public ExprNode Right { get; }

        public ExprCompareNode(ExprNode left, ExprCompareOp op, ExprNode right)
            : this(left, op, right, -1, 0)
        {
        }

        /// <summary>消费方反馈第三批第 19 条新增：与三参数构造物理上是两个不同的构造函数
        /// （见 <see cref="ExprNode"/> 类型级判断记录）。</summary>
        public ExprCompareNode(ExprNode left, ExprCompareOp op, ExprNode right, int start, int length)
            : base(start, length)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Op = op;
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }

        public override string ToString() => PrintAsTerm(Left) + " " + OpText(Op) + " " + PrintAsTerm(Right);

        internal static string OpText(ExprCompareOp op)
        {
            switch (op)
            {
                case ExprCompareOp.Eq: return "==";
                case ExprCompareOp.Ne: return "!=";
                case ExprCompareOp.Gt: return ">";
                case ExprCompareOp.Ge: return ">=";
                case ExprCompareOp.Lt: return "<";
                case ExprCompareOp.Le: return "<=";
                default: throw new ArgumentOutOfRangeException(nameof(op), op, "未知比较运算符");
            }
        }

        public override bool Equals(object? obj) =>
            obj is ExprCompareNode other && Op == other.Op && Left.Equals(other.Left) && Right.Equals(other.Right);

        public override int GetHashCode()
        {
            unchecked
            {
                return (((int)Op * 397) ^ Left.GetHashCode()) * 397 ^ Right.GetHashCode();
            }
        }
    }

    /// <summary><c>and</c> 链：扁平化的操作数列表（见 04 第 6.1 节 <c>and_expr</c>）。</summary>
    public sealed class ExprAndNode : ExprNode
    {
        public IReadOnlyList<ExprNode> Operands { get; }

        public ExprAndNode(IReadOnlyList<ExprNode> operands)
            : this(operands, -1, 0)
        {
        }

        /// <summary>消费方反馈第三批第 19 条新增：与单参数构造物理上是两个不同的构造函数
        /// （见 <see cref="ExprNode"/> 类型级判断记录）。</summary>
        public ExprAndNode(IReadOnlyList<ExprNode> operands, int start, int length)
            : base(start, length)
        {
            if (operands == null || operands.Count < 2)
            {
                throw new ArgumentException("ExprAndNode 至少需要两个操作数", nameof(operands));
            }
            Operands = operands;
        }

        public override string ToString() => string.Join(" and ", Operands.Select(PrintAsUnaryOperand));

        public override bool Equals(object? obj)
        {
            if (!(obj is ExprAndNode other) || Operands.Count != other.Operands.Count) return false;
            for (int i = 0; i < Operands.Count; i++)
            {
                if (!Operands[i].Equals(other.Operands[i])) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = typeof(ExprAndNode).GetHashCode();
                foreach (var op in Operands) hash = hash * 397 ^ op.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary><c>or</c> 链：扁平化的操作数列表（见 04 第 6.1 节 <c>or_expr</c>）。</summary>
    public sealed class ExprOrNode : ExprNode
    {
        public IReadOnlyList<ExprNode> Operands { get; }

        public ExprOrNode(IReadOnlyList<ExprNode> operands)
            : this(operands, -1, 0)
        {
        }

        /// <summary>消费方反馈第三批第 19 条新增：与单参数构造物理上是两个不同的构造函数
        /// （见 <see cref="ExprNode"/> 类型级判断记录）。</summary>
        public ExprOrNode(IReadOnlyList<ExprNode> operands, int start, int length)
            : base(start, length)
        {
            if (operands == null || operands.Count < 2)
            {
                throw new ArgumentException("ExprOrNode 至少需要两个操作数", nameof(operands));
            }
            Operands = operands;
        }

        public override string ToString() => string.Join(" or ", Operands.Select(PrintAsAndOperand));

        public override bool Equals(object? obj)
        {
            if (!(obj is ExprOrNode other) || Operands.Count != other.Operands.Count) return false;
            for (int i = 0; i < Operands.Count; i++)
            {
                if (!Operands[i].Equals(other.Operands[i])) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = typeof(ExprOrNode).GetHashCode();
                foreach (var op in Operands) hash = hash * 397 ^ op.GetHashCode();
                return hash;
            }
        }
    }
}
