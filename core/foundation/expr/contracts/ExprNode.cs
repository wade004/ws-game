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
    }

    /// <summary>字面量：Bool、Int、Number、String，或不属于任何分组前缀的 Id。</summary>
    public sealed class ExprLiteralNode : ExprNode
    {
        public ExprValue Value { get; }

        public ExprLiteralNode(ExprValue value)
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
