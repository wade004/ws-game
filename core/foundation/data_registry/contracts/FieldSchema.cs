using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 单个字段的静态声明：类型、是否必填、枚举合法集合、引用目标（见 04 第 5 节校验器
    /// 检查项清单 "引用完整性" "枚举合法" "文本键存在" 与第 4 节 <c>declareReference</c>）。
    /// <para>
    /// ADR-0019 扩展（04 第 3.2 节"复合字段子结构登记"）：<see cref="FieldKind.Object"/>/
    /// <see cref="FieldKind.Array"/> 两种字段种类可选登记机器可读的子结构（<see cref="Fields"/>/
    /// <see cref="Item"/>/<see cref="Variants"/>），供 <c>DataRegistry</c> 递归校验；不登记时行为
    /// 与登记前完全一致（只检查"存在且类型匹配"），向后兼容。
    /// </para>
    /// </summary>
    public sealed class FieldSchema
    {
        public string Name { get; }

        public FieldKind Kind { get; }

        public bool Required { get; }

        /// <summary><see cref="FieldKind.Enum"/> 字段的合法取值集合；其余类型为 null。</summary>
        public IReadOnlyList<string>? EnumValues { get; }

        /// <summary><see cref="FieldKind.Reference"/> 字段指向的具体表名（与
        /// <see cref="ReferenceDomain"/> 至多设置一个）。</summary>
        public string? ReferenceTable { get; }

        /// <summary><see cref="FieldKind.Reference"/> 字段指向的 domain（值的 domain 段必须等于
        /// 它，且必须能在某张已加载、首段等于该 domain 的表里找到，见 04 第 2.2 节域名清单）；
        /// 与 <see cref="ReferenceTable"/> 至多设置一个。</summary>
        public string? ReferenceDomain { get; }

        public string? Description { get; }

        private FieldRange? _range;

        /// <summary>ADR-0021（04 第 4 节勘误）：仅对 <see cref="FieldKind.Number"/>/
        /// <see cref="FieldKind.Int"/> 字段有意义的数值范围约束，由 <see cref="WithRange"/> 事后登记
        /// （不进构造函数——见类型顶部 ABI 兼容判断记录，构造函数已因"新增参数即产生新物理签名"的
        /// 教训而冻结，新能力一律用新增成员/新构造重载承载，不再改动既有构造函数的参数列表）。
        /// 是否"仅数值种类可设"不在这里检查：<see cref="WithRange"/> 允许挂在任意 <see cref="Kind"/>
        /// 上，由 <c>SchemaAudit</c> 的自洽检查项负责事后报告——见 <see cref="FieldRange"/> 类型顶部
        /// 判断记录。</summary>
        public FieldRange? Range => _range;

        /// <summary>登记 <see cref="Range"/>；只能设置一次（重复设置抛异常，防止后来的登记代码静默
        /// 覆盖前面已登记的范围）。返回 <c>this</c> 便于链式调用，如
        /// <c>new FieldSchema("interval", FieldKind.Number, required: true, description: "...")
        /// .WithRange(FieldRange.Range(min: 0, minExclusive: true))</c>。</summary>
        public FieldSchema WithRange(FieldRange range)
        {
            if (range == null) throw new ArgumentNullException(nameof(range));
            if (_range != null) throw new InvalidOperationException($"字段 \"{Name}\"：Range 已设置，不可重复设置");
            _range = range;
            return this;
        }

        private FieldGroup? _group;

        /// <summary>ADR-0022（04 第 3.4 节"字段分组元数据"）：字段语义分组，供编辑器表单视图折叠/
        /// 排序（<c>editor/docs/编辑器产品文档.md</c> 5.4.1 节）。未经 <see cref="WithGroup"/> 显式
        /// 登记时，按 <see cref="FieldGroup"/> 类型判断记录所述规则计算默认值——见
        /// <see cref="DefaultGroup"/>。</summary>
        public FieldGroup Group => _group ?? DefaultGroup;

        private FieldGroup DefaultGroup
        {
            get
            {
                if (Name == "id" || Name == "key" || Name == "schema_version" || Name == "migrated_from")
                {
                    return FieldGroup.Basic;
                }
                if (Kind == FieldKind.TextKey)
                {
                    return FieldGroup.Basic;
                }
                if (Kind == FieldKind.Reference || Kind == FieldKind.IdList || Kind == FieldKind.Id)
                {
                    return FieldGroup.Reference;
                }
                if (Kind == FieldKind.Number || Kind == FieldKind.Int)
                {
                    return FieldGroup.Numeric;
                }
                for (var i = 0; i < PresentationNameHints.Length; i++)
                {
                    if (Name.Contains(PresentationNameHints[i]))
                    {
                        return FieldGroup.Presentation;
                    }
                }
                return FieldGroup.Advanced;
            }
        }

        /// <summary>见 <see cref="DefaultGroup"/>："名称含表现/资源类关键字=表现"这一条规则用到的
        /// 关键字集合，取自已登记字段里实际出现的表现层命名惯例（图标/精灵/模型/动画/特效/音效/
        /// UI/阴影/缩放/颜色/排序/挂点/渲染/粒子）。</summary>
        private static readonly string[] PresentationNameHints =
        {
            "icon", "sprite", "model", "anim", "vfx", "sfx", "shadow", "scale", "color",
            "sort", "socket", "slot_mesh", "render", "particle", "ui_", "layout", "camera",
            "shake", "screen", "menu_icon", "portrait",
        };

        /// <summary>登记 <see cref="Group"/>，覆盖 <see cref="DefaultGroup"/> 的启发式判定（用于
        /// 规则误判的少量字段）；只能设置一次。返回 <c>this</c> 便于链式调用。</summary>
        public FieldSchema WithGroup(FieldGroup group)
        {
            if (_group != null) throw new InvalidOperationException($"字段 \"{Name}\"：Group 已设置，不可重复设置");
            _group = group;
            return this;
        }

        /// <summary>ADR-0022（04 第 3.4 节"时间字段单位与作用域"）：字段取值单位，默认
        /// <see cref="FieldUnit.None"/>。<see cref="FieldUnit.Time"/> 标记 04 第 3.1 节列出的时间
        /// 字段，须由所属 <see cref="TableSchema.TimeScope"/> 非 <see cref="Core.Foundation.DataRegistry.TimeScope.None"/>
        /// 配合登记（见 <c>SchemaAudit</c>"time_scope_declared"检查）。</summary>
        public FieldUnit Unit { get; private set; } = FieldUnit.None;

        /// <summary>登记 <see cref="Unit"/>；只能设置一次。返回 <c>this</c> 便于链式调用。</summary>
        public FieldSchema WithUnit(FieldUnit unit)
        {
            if (Unit != FieldUnit.None) throw new InvalidOperationException($"字段 \"{Name}\"：Unit 已设置，不可重复设置");
            Unit = unit;
            return this;
        }

        /// <summary>ADR-0022（04 第 3.4 节"IdList 引用目标"）：仅 <see cref="FieldKind.IdList"/> 字段
        /// 有意义——显式标记该字段确属"自由 id 列表"（值不指向任何已登记表/domain，或分层边界不允许
        /// 静态耦合目标表，如 <c>arch.class.power_types</c>/<c>arch.race.passive_auras</c> 见
        /// <c>ArchSchemas</c> 判断记录），与 <see cref="ReferenceTable"/>/<see cref="ReferenceDomain"/>
        /// 三者恰好登记其一，供 <c>SchemaAudit</c>"idlist_reference_target"检查判定"IdList 二者必居
        /// 其一"。</summary>
        public bool FreeIds { get; private set; }

        /// <summary>登记 <see cref="FreeIds"/>；只能设置一次，且与 <see cref="ReferenceTable"/>/
        /// <see cref="ReferenceDomain"/> 互斥。<paramref name="reason"/> 必填，供门禁报告与人工审阅
        /// （同 <c>SchemaAuditAllowlistEntry.Reason</c> 惯例）。返回 <c>this</c> 便于链式调用。</summary>
        public FieldSchema WithFreeIds(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("reason 不能为空——需说明为何该 IdList 确属自由列表", nameof(reason));
            if (FreeIds) throw new InvalidOperationException($"字段 \"{Name}\"：FreeIds 已设置，不可重复设置");
            if (ReferenceTable != null || ReferenceDomain != null)
            {
                throw new InvalidOperationException($"字段 \"{Name}\"：FreeIds 与 ReferenceTable/ReferenceDomain 互斥");
            }
            FreeIds = true;
            FreeIdsReason = reason;
            return this;
        }

        /// <summary>见 <see cref="WithFreeIds"/>。</summary>
        public string? FreeIdsReason { get; private set; }

        /// <summary>仅 <see cref="FieldKind.Object"/> 可设：该对象的子字段清单，可递归嵌套
        /// <see cref="FieldKind.Object"/>/<see cref="FieldKind.Array"/>；与 <see cref="Variants"/>
        /// 互斥（见 ADR-0019）。</summary>
        public IReadOnlyList<FieldSchema>? Fields { get; }

        private readonly Func<FieldSchema>? _itemFactory;
        private FieldSchema? _item;
        private bool _itemResolved;

        /// <summary>仅 <see cref="FieldKind.Array"/> 可设：数组元素的结构；元素本身可以是任意
        /// <see cref="FieldKind"/>（含 <see cref="FieldKind.Object"/>/<see cref="FieldKind.Array"/>）。
        /// 若构造时传入 <c>itemFactory</c>，首次访问本属性时求值并缓存（见类型顶部"惰性递归"，
        /// 支持自引用结构如 <c>projectile.params.on_hit_effects</c> 复用 <c>effects</c> 自身的元素
        /// 结构）。</summary>
        public FieldSchema? Item
        {
            get
            {
                if (!_itemResolved)
                {
                    _item = _itemFactory != null ? _itemFactory() : _item;
                    _itemResolved = true;
                }
                return _item;
            }
        }

        private readonly Func<VariantSchema>? _variantsFactory;
        private VariantSchema? _variants;
        private bool _variantsResolved;

        /// <summary>仅 <see cref="FieldKind.Object"/> 可设，且与 <see cref="Fields"/> 互斥：按判别
        /// 字段分派的变体子结构（见 <see cref="VariantSchema"/>）。惰性求值语义同 <see cref="Item"/>。</summary>
        public VariantSchema? Variants
        {
            get
            {
                if (!_variantsResolved)
                {
                    _variants = _variantsFactory != null ? _variantsFactory() : _variants;
                    _variantsResolved = true;
                }
                return _variants;
            }
        }

        private MapSchema? _map;

        /// <summary>ADR-0024（04 第 3.3 节"映射登记"）：仅 <see cref="FieldKind.Object"/> 可设，且与
        /// <see cref="Fields"/>/<see cref="Variants"/> 互斥（登记在其它 <see cref="Kind"/> 上，或与
        /// <see cref="Fields"/>/<see cref="Variants"/> 同时登记，均由 <c>SchemaAudit</c> 的
        /// <c>field_map_kind</c>/<c>field_map_conflict</c> 检查项事后报告——刻意不在 <see cref="WithMap"/>
        /// 挂载时检查，同 <see cref="WithRange"/> 判断记录"让登记在错误种类字段上的错误停留在可枚举的
        /// 软失败"这条既有风格）：动态键映射子结构（键为某表/某 domain 引用或自由字符串 + 每个值的
        /// 登记，见 <see cref="MapSchema"/>）。</summary>
        public MapSchema? Map => _map;

        /// <summary>登记 <see cref="Map"/>；只能设置一次（重复设置抛异常，同 <see cref="WithRange"/>/
        /// <see cref="WithGroup"/>/<see cref="WithUnit"/> 惯例）。返回 <c>this</c> 便于链式调用。</summary>
        public FieldSchema WithMap(MapSchema map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (_map != null) throw new InvalidOperationException($"字段 \"{Name}\"：Map 已设置，不可重复设置");
            _map = map;
            return this;
        }

        public FieldSchema(
            string name,
            FieldKind kind,
            bool required,
            IReadOnlyList<string>? enumValues = null,
            string? referenceTable = null,
            string? referenceDomain = null,
            string? description = null,
            IReadOnlyList<FieldSchema>? fields = null,
            FieldSchema? item = null,
            VariantSchema? variants = null,
            Func<FieldSchema>? itemFactory = null,
            Func<VariantSchema>? variantsFactory = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("字段名不能为空", nameof(name));
            if (referenceTable != null && referenceDomain != null)
            {
                throw new ArgumentException("ReferenceTable 与 ReferenceDomain 至多设置一个", nameof(referenceDomain));
            }
            if (kind == FieldKind.Enum && (enumValues == null || enumValues.Count == 0))
            {
                throw new ArgumentException($"字段 \"{name}\" 声明为 Enum 但未提供 EnumValues", nameof(enumValues));
            }

            // ADR-0019：子结构登记的非法组合检查（04 第 3.2 节）。
            if (fields != null && kind != FieldKind.Object)
            {
                throw new ArgumentException($"字段 \"{name}\"：Fields 仅 Object 字段可设", nameof(fields));
            }
            if ((variants != null || variantsFactory != null) && kind != FieldKind.Object)
            {
                throw new ArgumentException($"字段 \"{name}\"：Variants 仅 Object 字段可设", nameof(variants));
            }
            if ((item != null || itemFactory != null) && kind != FieldKind.Array)
            {
                throw new ArgumentException($"字段 \"{name}\"：Item 仅 Array 字段可设", nameof(item));
            }
            if (item != null && itemFactory != null)
            {
                throw new ArgumentException($"字段 \"{name}\"：item 与 itemFactory 至多设置一个", nameof(itemFactory));
            }
            if (variants != null && variantsFactory != null)
            {
                throw new ArgumentException($"字段 \"{name}\"：variants 与 variantsFactory 至多设置一个", nameof(variantsFactory));
            }
            if (fields != null && (variants != null || variantsFactory != null))
            {
                throw new ArgumentException($"字段 \"{name}\"：Fields 与 Variants 互斥", nameof(variants));
            }

            Name = name;
            Kind = kind;
            Required = required;
            EnumValues = enumValues;
            ReferenceTable = referenceTable;
            ReferenceDomain = referenceDomain;
            Description = description;
            Fields = fields;

            _item = item;
            _itemFactory = itemFactory;
            _itemResolved = itemFactory == null;

            _variants = variants;
            _variantsFactory = variantsFactory;
            _variantsResolved = variantsFactory == null;
        }

        /// <summary>
        /// ABI/API 兼容 façade（P2-01 根治，外部审计 audit-c9ff301-20260909）：1.12 的物理构造签名
        /// 恰好是七个参数、没有 <c>fields</c>/<c>item</c>/<c>variants</c>/<c>itemFactory</c>/
        /// <c>variantsFactory</c> 五个 ADR-0019 新增参数。C# 的可选参数是编译期特性——上面主构造函数
        /// 虽然后五个参数都带默认值，但物理 IL 签名仍然是完整的十二个参数；1.12 编译产物里对七参数
        /// 构造函数的调用，在只替换 DLL、不重新编译的情况下，找不到匹配的物理方法而
        /// <c>MissingMethodException</c>（见 project-findings.md P2-01 "Actual"：正式 1.12 DLL 编译/
        /// 运行 exit 0；只替换 1.13 <c>Core.Foundation.dll</c> 不重编译，运行 <c>MissingMethodException</c>）。
        /// 本重载补回这个物理七参数签名，转发到主构造函数、后五个新参数一律传 null——旧调用方不会
        /// 得到 ADR-0019 子结构登记（<c>Fields</c>/<c>Item</c>/<c>Variants</c> 全部为 null），只保证
        /// 不再 <c>MissingMethodException</c>，行为与 1.12 完全一致。
        /// <para>
        /// 判断记录（不是可选参数）：本重载的七个参数全部不带默认值——若也写成可选参数，会与主
        /// 构造函数在"只传 3～6 个参数"的调用点产生重载二义性（两者都可以匹配、都需要为末尾参数
        /// 代入默认值）。全部必填后，C# 重载决议规则（"不需要为可选参数代入默认值的候选更优"）保证
        /// 恰好传 7 个位置参数时优先匹配本重载，其余任何参数个数的调用都落到主构造函数，不产生歧义。
        /// </para>
        /// </summary>
        [Obsolete("1.12 的七参数构造签名，仅为源码/二进制兼容保留；新代码请使用带 Fields/Item/Variants 的构造函数。")]
        public FieldSchema(
            string name,
            FieldKind kind,
            bool required,
            IReadOnlyList<string>? enumValues,
            string? referenceTable,
            string? referenceDomain,
            string? description)
            : this(name, kind, required, enumValues, referenceTable, referenceDomain, description,
                fields: null, item: null, variants: null, itemFactory: null, variantsFactory: null)
        {
        }
    }
}
