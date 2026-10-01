using System;
using System.Collections.Generic;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// 八层覆盖的层号（手感设计/05 第 3.2 节），从粗到细；解析按层号从低到高逐层应用。
    /// 数值即层号，溯源里的 <see cref="FeelProvenanceEntry.Layer"/> 直接取它。
    /// </summary>
    public enum FeelLayer
    {
        /// <summary>1 基础预设 <c>feel.preset</c>（标定表指定）。</summary>
        BasePreset = 1,

        /// <summary>2 体型原型 <c>feel.archetype</c>（单位模板 <c>feel_archetype_ref</c>）。</summary>
        Archetype = 2,

        /// <summary>3 标签映射 <c>feel.tag_map</c>（游戏层：性别/种族/职业 → 原型或覆盖）。</summary>
        TagMap = 3,

        /// <summary>4 武器 <c>feel.weapon</c>（主手；副手只叠加打击层）。</summary>
        Weapon = 4,

        /// <summary>5 角色 <c>feel.character</c>（单位模板 <c>feel_ref</c>）。</summary>
        Character = 5,

        /// <summary>6 当前动作 <c>feel.action</c>（进行中动作的 <c>feel_ref</c>；动作结束即撤）。</summary>
        Action = 6,

        /// <summary>7 临时状态（光环携带的手感修饰，以光环实例 id 为键）。</summary>
        Temporary = 7,

        /// <summary>8 调试覆盖（实验室热调参，仅开发期）。</summary>
        Debug = 8,
    }

    /// <summary>覆盖操作（手感设计/05 第 3.3 节）。<c>Remove</c> 只用于列表字段。</summary>
    public enum FeelOp
    {
        Set,
        Multiply,
        Add,
        Remove,
    }

    /// <summary>溯源条目里 <see cref="FeelProvenanceEntry.Op"/> 的取值（除四种覆盖操作外还有解析末尾的限幅与取整）。</summary>
    public static class FeelProvenanceOps
    {
        public const string Set = "set";
        public const string Multiply = "multiply";
        public const string Add = "add";
        public const string Remove = "remove";

        /// <summary>全部覆盖应用完毕后按登记范围限幅（只在值被改变时记录）。</summary>
        public const string Clamp = "clamp";

        /// <summary>整数字段在限幅前取整（只在值被改变时记录）。</summary>
        public const string Round = "round";

        public static string FromOp(FeelOp op)
        {
            switch (op)
            {
                case FeelOp.Set: return Set;
                case FeelOp.Multiply: return Multiply;
                case FeelOp.Add: return Add;
                case FeelOp.Remove: return Remove;
                default: throw new ArgumentOutOfRangeException(nameof(op));
            }
        }
    }

    /// <summary>一条覆盖写入：对字段 <see cref="Field"/> 做一次 <see cref="Op"/>，参数为 <see cref="Value"/>。</summary>
    public readonly struct FeelWrite : IEquatable<FeelWrite>
    {
        public string Field { get; }

        public FeelOp Op { get; }

        public FeelValue Value { get; }

        public FeelWrite(string field, FeelOp op, FeelValue value)
        {
            if (string.IsNullOrEmpty(field)) throw new ArgumentException("字段名不能为空", nameof(field));
            Field = field;
            Op = op;
            Value = value;
        }

        public bool Equals(FeelWrite other) =>
            string.Equals(Field, other.Field, StringComparison.Ordinal) && Op == other.Op && Value.Equals(other.Value);

        public override bool Equals(object? obj) => obj is FeelWrite other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = Field == null ? 0 : StringComparer.Ordinal.GetHashCode(Field);
                h = (h * 397) ^ (int)Op;
                return (h * 397) ^ Value.GetHashCode();
            }
        }

        public override string ToString() => Field + " " + FeelProvenanceOps.FromOp(Op) + " " + Value;

        public static bool operator ==(FeelWrite left, FeelWrite right) => left.Equals(right);

        public static bool operator !=(FeelWrite left, FeelWrite right) => !left.Equals(right);
    }

    /// <summary>
    /// 溯源条目（手感设计/05 第 5 节 <c>provenance</c>）：某字段在某层被某来源以某操作从
    /// <see cref="ValueBefore"/> 改成 <see cref="ValueAfter"/>。值是标定前的相对值；标定后的绝对值见
    /// <see cref="ResolvedFeel"/>。
    /// </summary>
    public sealed class FeelProvenanceEntry
    {
        /// <summary>层号 1～8（<see cref="FeelLayer"/>）；限幅/取整条目记为 0（不属于任何层）。</summary>
        public int Layer { get; }

        /// <summary>来源 id：预设/原型/武器/角色/动作行 id、光环实例键，或调试覆盖的 <c>debug:global</c>/<c>debug:unit</c>。</summary>
        public string SourceId { get; }

        /// <summary>取值见 <see cref="FeelProvenanceOps"/>。</summary>
        public string Op { get; }

        public FeelValue ValueBefore { get; }

        public FeelValue ValueAfter { get; }

        public FeelProvenanceEntry(int layer, string sourceId, string op, FeelValue valueBefore, FeelValue valueAfter)
        {
            Layer = layer;
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            Op = op ?? throw new ArgumentNullException(nameof(op));
            ValueBefore = valueBefore;
            ValueAfter = valueAfter;
        }

        public override string ToString() =>
            "L" + Layer + " " + SourceId + " " + Op + " " + ValueBefore + " -> " + ValueAfter;
    }

    /// <summary>一个临时状态条目（第 7 层）：以键（光环实例 id）标识的一组覆盖写入。</summary>
    public sealed class FeelTemporaryEntry
    {
        /// <summary>条目键（光环实例 id）；解析时按键的序数序排序，保证同层多条目的应用顺序确定。</summary>
        public string Key { get; }

        public IReadOnlyList<FeelWrite> Writes { get; }

        public FeelTemporaryEntry(string key, IReadOnlyList<FeelWrite> writes)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("条目键不能为空", nameof(key));
            Key = key;
            Writes = writes ?? throw new ArgumentNullException(nameof(writes));
        }
    }
}
