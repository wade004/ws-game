using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary>手感字段的值类型。</summary>
    public enum FeelFieldKind
    {
        /// <summary>实数，登记范围必填（限幅依据）。</summary>
        Number,

        /// <summary>整数（运算按实数进行，结果取整后限幅），登记范围必填。</summary>
        Int,

        /// <summary>布尔，只允许 <c>set</c>。</summary>
        Bool,

        /// <summary>枚举字符串，取值固定，只允许 <c>set</c>。</summary>
        Enum,

        /// <summary>自由文本（曲线引用、材质标签等），只允许 <c>set</c>。</summary>
        Text,

        /// <summary>逻辑 id（引用其它表的行，可带软引用目标表），只允许 <c>set</c>。</summary>
        Id,

        /// <summary>文本列表：缺省整体替换，显式 <c>remove</c> 删元素，<c>add</c> 追加不重复元素。</summary>
        List,
    }

    /// <summary>
    /// 字段的落地状态（手感设计/05 第 4 节，ADR-0146）：<see cref="Active"/> = 至少有一个生产消费方读取，改它会改变运行结果；
    /// <see cref="Planned"/> = 已登记（预设可以写、数据可以校验）但尚无消费方，写了也不生效。
    /// 约束由测试守住：每个 active 字段在非测试代码里都有消费方引用，每个没有消费方引用的字段必须标 planned。
    /// </summary>
    public enum FeelFieldStatus
    {
        /// <summary>已生效（缺省）。</summary>
        Active = 0,

        /// <summary>已登记、尚未生效：调参面板灰显并注明"尚未生效"。</summary>
        Planned = 1,
    }

    /// <summary>
    /// 一个手感字段的登记（手感设计/05 第 4 节的"字段登记元数据"）：名称、类型、范围、半属、分组、允许操作、
    /// 合成来源、单位、副手可叠加、是否可选。一份 <see cref="FeelFieldSet"/> 同时驱动数据校验、解析器合成、
    /// 调参面板自动生成与测试自动枚举——新增一个手感字段只改登记表与消费方，工具与测试不动（00 第 4 节）。
    /// </summary>
    public sealed class FeelFieldDef
    {
        public string Name { get; }

        public FeelFieldKind Kind { get; }

        public string Description { get; }

        /// <summary><see cref="FeelFieldKind.Enum"/> 字段的合法取值。</summary>
        public IReadOnlyList<string>? EnumValues { get; }

        /// <summary>数值字段的最小值（含），限幅与校验依据。</summary>
        public double? Min { get; }

        /// <summary>数值字段的最大值（含）。</summary>
        public double? Max { get; }

        public FeelFieldMeta Meta { get; }

        /// <summary>可选字段：预设里可以缺省（解析结果为 <see cref="FeelValue.None"/>），不要求"预设全字段齐全"。</summary>
        public bool Optional { get; }

        /// <summary><see cref="FeelFieldKind.Id"/> 字段的软引用目标表（只供工具补全/跳转，不做加载期引用校验）。</summary>
        public string? SoftReferenceTable { get; }

        /// <summary>
        /// 非空表示该字段已经由属性系统承载（如移动速度、急速），光环 <c>feel_modifiers</c> 不得重复表达
        /// （手感设计/05 第 6 节；校验错误 <c>feel_modifier_attribute_duplicate</c>）。值为说明文本。
        /// </summary>
        public string? AttributeBackedReason { get; }

        /// <summary>落地状态：缺省 <see cref="FeelFieldStatus.Active"/>；<see cref="FeelFieldStatus.Planned"/> 表示已登记但尚无消费方（见 <see cref="FeelFieldStatus"/>）。</summary>
        public FeelFieldStatus Status { get; private set; }

        /// <summary><see cref="FeelFieldStatus.Planned"/> 字段的说明（缺什么才能生效），active 字段为 null。</summary>
        public string? StatusNote { get; private set; }

        public FeelHalf Half => Meta.Half;

        public FeelGroup Group => Meta.Group;

        public FeelOpSet Ops => Meta.Ops;

        public FeelComposition Composition => Meta.Composition;

        public FeelUnit Unit => Meta.Unit;

        public bool OffhandStackable => Meta.OffhandStackable;

        /// <summary>相对量字段：标定表换算前的取值（身高倍数、基础移速倍数、画面高度比例等）。</summary>
        public bool IsRelativeQuantity
        {
            get
            {
                switch (Meta.Unit)
                {
                    case FeelUnit.BodyHeights:
                    case FeelUnit.BaseSpeedSeconds:
                    case FeelUnit.BaseSpeedRatio:
                    case FeelUnit.Ratio:
                    case FeelUnit.ClipRatio:
                    case FeelUnit.ScreenHeightRatio:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>判定型时间字段（毫秒）：解析结果同时给出换算后的 tick 数。</summary>
        public bool HasTicks => Meta.Unit == FeelUnit.Milliseconds && Meta.Half == FeelHalf.Judging;

        public bool IsNumeric => Kind == FeelFieldKind.Number || Kind == FeelFieldKind.Int;

        public FeelFieldDef(
            string name,
            FeelFieldKind kind,
            FeelFieldMeta meta,
            string description,
            double? min = null,
            double? max = null,
            IReadOnlyList<string>? enumValues = null,
            bool optional = false,
            string? softReferenceTable = null,
            string? attributeBackedReason = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("字段名不能为空", nameof(name));
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException($"字段 \"{name}\" 缺说明", nameof(description));

            if (kind == FeelFieldKind.Number || kind == FeelFieldKind.Int)
            {
                if (min == null || max == null)
                {
                    throw new ArgumentException($"字段 \"{name}\"：数值字段必须登记范围（限幅依据）", nameof(min));
                }
                if (!double.IsFinite(min.Value) || !double.IsFinite(max.Value) || min.Value > max.Value)
                {
                    throw new ArgumentException($"字段 \"{name}\"：范围端点非法", nameof(min));
                }
            }
            else if (min != null || max != null)
            {
                throw new ArgumentException($"字段 \"{name}\"：只有数值字段可以登记范围", nameof(min));
            }

            if (kind == FeelFieldKind.Enum)
            {
                if (enumValues == null || enumValues.Count == 0)
                {
                    throw new ArgumentException($"字段 \"{name}\"：枚举字段必须登记取值", nameof(enumValues));
                }
            }
            else if (enumValues != null)
            {
                throw new ArgumentException($"字段 \"{name}\"：只有枚举字段可以登记取值", nameof(enumValues));
            }

            if (softReferenceTable != null && kind != FeelFieldKind.Id)
            {
                throw new ArgumentException($"字段 \"{name}\"：只有 Id 字段可以登记软引用目标", nameof(softReferenceTable));
            }

            // 允许操作与类型相容：布尔/枚举/文本/Id 只允许 set（05 第 3.3 节）；列表只允许 set/add/remove；数值只允许 set/multiply/add。
            var legal = LegalOpsFor(kind);
            if ((meta.Ops & ~legal) != FeelOpSet.None)
            {
                throw new ArgumentException(
                    $"字段 \"{name}\"（{kind}）登记了该类型不允许的覆盖操作：{meta.Ops}", nameof(meta));
            }

            // 分组与半属一致：输入/动作/受击三组是判定型，镜头/特效/音频三组是呈现型，移动组两种都有（05 第 3.1 节）。
            switch (meta.Group)
            {
                case FeelGroup.Input:
                case FeelGroup.Action:
                case FeelGroup.Reaction:
                    if (meta.Half != FeelHalf.Judging) throw new ArgumentException($"字段 \"{name}\"：{meta.Group} 组必须是判定型", nameof(meta));
                    break;
                case FeelGroup.Camera:
                case FeelGroup.Effects:
                case FeelGroup.Audio:
                    if (meta.Half != FeelHalf.Presenting) throw new ArgumentException($"字段 \"{name}\"：{meta.Group} 组必须是呈现型", nameof(meta));
                    break;
            }

            Name = name;
            Kind = kind;
            Meta = meta;
            Description = description;
            Min = min;
            Max = max;
            EnumValues = enumValues;
            Optional = optional;
            SoftReferenceTable = softReferenceTable;
            AttributeBackedReason = attributeBackedReason;
        }

        /// <summary>
        /// 返回带指定落地状态的副本（本对象不可变，不被修改）。<paramref name="note"/> 只对 <see cref="FeelFieldStatus.Planned"/>
        /// 有意义：说明还缺什么消费方才能生效，调参面板灰显时展示。
        /// </summary>
        public FeelFieldDef WithStatus(FeelFieldStatus status, string? note = null)
        {
            if (status == FeelFieldStatus.Active && note != null)
            {
                throw new ArgumentException($"字段 \"{Name}\"：active 字段不带状态说明", nameof(note));
            }

            var copy = (FeelFieldDef)MemberwiseClone();
            copy.Status = status;
            copy.StatusNote = status == FeelFieldStatus.Planned ? note : null;
            return copy;
        }

        /// <summary>某值类型允许登记的覆盖操作全集。</summary>
        public static FeelOpSet LegalOpsFor(FeelFieldKind kind)
        {
            switch (kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int:
                    return FeelOpSet.Set | FeelOpSet.Multiply | FeelOpSet.Add;
                case FeelFieldKind.List:
                    return FeelOpSet.Set | FeelOpSet.Add | FeelOpSet.Remove;
                default:
                    return FeelOpSet.Set;
            }
        }

        /// <summary>该字段是否允许某覆盖操作。</summary>
        public bool Allows(FeelOp op)
        {
            switch (op)
            {
                case FeelOp.Set: return (Ops & FeelOpSet.Set) != 0;
                case FeelOp.Multiply: return (Ops & FeelOpSet.Multiply) != 0;
                case FeelOp.Add: return (Ops & FeelOpSet.Add) != 0;
                case FeelOp.Remove: return (Ops & FeelOpSet.Remove) != 0;
                default: return false;
            }
        }

        /// <summary>
        /// 构造登记表用的 <see cref="FieldSchema"/>（预设行 <c>values</c> 子字段）：类型、范围、元数据与本登记一致。
        /// </summary>
        public FieldSchema ToFieldSchema()
        {
            var schema = BuildSchema(Name, required: false, withRange: true);
            schema.WithFeel(Meta);
            return schema;
        }

        /// <summary>
        /// 构造写入条目 <c>value</c> 子字段的 <see cref="FieldSchema"/>：类型与本字段一致，但<b>不</b>登记范围——
        /// <c>multiply</c> 的倍数、<c>add</c> 的增量与字段取值范围语义不同，按操作区分的范围检查由
        /// <c>FeelProfileChecker</c> 负责（检查名 <c>feel_value_out_of_range</c>）。
        /// </summary>
        public FieldSchema ToWriteValueSchema() => BuildSchema("value", required: true, withRange: false);

        private FieldSchema BuildSchema(string schemaName, bool required, bool withRange)
        {
            FieldSchema schema;
            switch (Kind)
            {
                case FeelFieldKind.Number:
                    schema = new FieldSchema(schemaName, FieldKind.Number, required, description: Description);
                    break;
                case FeelFieldKind.Int:
                    schema = new FieldSchema(schemaName, FieldKind.Int, required, description: Description);
                    break;
                case FeelFieldKind.Bool:
                    schema = new FieldSchema(schemaName, FieldKind.Bool, required, description: Description);
                    break;
                case FeelFieldKind.Enum:
                    schema = new FieldSchema(schemaName, FieldKind.Enum, required, enumValues: EnumValues, description: Description);
                    break;
                case FeelFieldKind.Text:
                    schema = new FieldSchema(schemaName, FieldKind.String, required, description: Description);
                    break;
                case FeelFieldKind.Id:
                    schema = new FieldSchema(schemaName, FieldKind.Id, required, description: Description);
                    if (SoftReferenceTable != null) schema.WithSoftReference(table: SoftReferenceTable);
                    break;
                case FeelFieldKind.List:
                    schema = new FieldSchema(schemaName, FieldKind.Array, required,
                        item: new FieldSchema("<element>", FieldKind.String, required: true, description: "列表元素"),
                        description: Description);
                    break;
                default:
                    throw new InvalidOperationException("未知字段类型 " + Kind);
            }

            if (withRange && IsNumeric)
            {
                schema.WithRange(FieldRange.Range(min: Min, max: Max));
            }

            return schema;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// 手感字段登记集合（不可变、有序）：顺序即登记顺序，是解析结果、溯源、面板生成与测试枚举的唯一遍历顺序
    /// （不依赖字典枚举顺序，保持确定性）。
    /// </summary>
    public sealed class FeelFieldSet
    {
        private readonly FeelFieldDef[] _fields;
        private readonly Dictionary<string, int> _index;

        public int Count => _fields.Length;

        public FeelFieldDef this[int index] => _fields[index];

        public IReadOnlyList<FeelFieldDef> Fields => _fields;

        public FeelFieldSet(IEnumerable<FeelFieldDef> fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            _fields = new List<FeelFieldDef>(fields).ToArray();
            _index = new Dictionary<string, int>(_fields.Length, StringComparer.Ordinal);
            for (var i = 0; i < _fields.Length; i++)
            {
                if (_fields[i] == null) throw new ArgumentException("字段登记不能为 null", nameof(fields));
                if (_index.ContainsKey(_fields[i].Name))
                {
                    throw new ArgumentException($"字段 \"{_fields[i].Name}\" 重复登记", nameof(fields));
                }
                _index.Add(_fields[i].Name, i);
            }
        }

        public bool Contains(string name) => name != null && _index.ContainsKey(name);

        public int IndexOf(string name) => name != null && _index.TryGetValue(name, out var i) ? i : -1;

        public bool TryGet(string name, out FeelFieldDef def)
        {
            if (name != null && _index.TryGetValue(name, out var i))
            {
                def = _fields[i];
                return true;
            }
            def = null!;
            return false;
        }

        public FeelFieldDef Get(string name)
        {
            if (!TryGet(name, out var def)) throw new KeyNotFoundException($"手感字段 \"{name}\" 未登记");
            return def;
        }
    }
}
