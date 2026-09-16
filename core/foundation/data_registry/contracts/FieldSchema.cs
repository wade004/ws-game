using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Core.Foundation.Common;

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

        private IReadOnlyList<Id>? _allowedValues;

        /// <summary>消费方反馈第 28 条（04 第 3.4 节勘误"IdList/Id 固定取值登记"）：<see cref="FieldKind.IdList"/>
        /// （及单值 <see cref="FieldKind.Id"/>）字段的固定合法取值集合，有序（供编辑器下拉/自动补全按
        /// 该顺序展示，如 <c>creature.template.npc_flags</c> 照抄 07 表格行序）。登记后
        /// <c>DataRegistry</c> 加载期对每个取值做 <c>field_allowed_value</c> 检查——用于"取值是固定
        /// 词汇表，但不指向任何已登记表的具体记录"这类场景（<see cref="EnumValues"/> 只对
        /// <see cref="FieldKind.Enum"/> 种类可用，覆盖不到 IdList/Id；<see cref="ReferenceTable"/>/
        /// <see cref="ReferenceDomain"/> 面向的是"表里的记录"，不是"代码里的固定枚举"）。
        /// <para>
        /// 判断记录：与 <see cref="FreeIds"/>/<see cref="ReferenceTable"/> 语义上互斥——三者都是
        /// "声明该字段值从哪来"的不同方式，但 <see cref="WithAllowedValues"/> 刻意不在挂载时检查冲突
        /// （同 <see cref="WithRange"/>/<see cref="WithMap"/> 既有风格"让登记在错误组合上的错误停留在
        /// 可枚举的软失败"），冲突由 <c>SchemaAudit</c> 的 <c>idlist_allowed_values_conflict</c> 检查项
        /// 事后报告。
        /// </para>
        /// </summary>
        public IReadOnlyList<Id>? AllowedValues => _allowedValues;

        /// <summary>登记 <see cref="AllowedValues"/>；只能设置一次（重复设置抛异常，同
        /// <see cref="WithRange"/>/<see cref="WithGroup"/>/<see cref="WithUnit"/> 惯例）。
        /// <paramref name="values"/> 不能为空集合。返回 <c>this</c> 便于链式调用。</summary>
        public FieldSchema WithAllowedValues(IReadOnlyList<Id> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Count == 0) throw new ArgumentException($"字段 \"{Name}\"：AllowedValues 不能是空集合", nameof(values));
            if (_allowedValues != null) throw new InvalidOperationException($"字段 \"{Name}\"：AllowedValues 已设置，不可重复设置");
            _allowedValues = values;
            return this;
        }

        private string? _softReferenceTable;

        /// <summary>见 <see cref="WithSoftReference"/>。</summary>
        public string? SoftReferenceTable => _softReferenceTable;

        private string? _softReferenceDomain;

        /// <summary>见 <see cref="WithSoftReference"/>。</summary>
        public string? SoftReferenceDomain => _softReferenceDomain;

        /// <summary>消费方反馈第 29 条（04 第 3.4 节勘误"软引用元数据"）：<see cref="FieldKind.Id"/>/
        /// <see cref="FieldKind.IdList"/> 字段指向别的表/domain 的"软"提示——只传达跨层引用语义
        /// （供内容工具做自动补全/跳转），刻意不像 <see cref="FieldKind.Reference"/>/
        /// <see cref="ReferenceTable"/>/<see cref="ReferenceDomain"/> 那样参与
        /// <c>DataRegistry</c> 加载期的 <c>reference_integrity</c> 校验，也不改变 00/04 既定的分层
        /// 边界（典型场景：L3 字段语义上指向 L2/L4 的表，但分层规则不允许声明为
        /// <see cref="FieldKind.Reference"/>，见 <c>CreatureSchemas</c> 判断记录）。
        /// <paramref name="table"/> 与 <paramref name="domain"/> 至多设置一个，至少设置一个。</summary>
        public FieldSchema WithSoftReference(string? table = null, string? domain = null)
        {
            if (table != null && domain != null)
            {
                throw new ArgumentException($"字段 \"{Name}\"：SoftReference 的 table 与 domain 至多设置一个", nameof(domain));
            }
            if (table == null && domain == null)
            {
                throw new ArgumentException($"字段 \"{Name}\"：SoftReference 的 table 与 domain 至少设置一个");
            }
            if (_softReferenceTable != null || _softReferenceDomain != null)
            {
                throw new InvalidOperationException($"字段 \"{Name}\"：SoftReference 已设置，不可重复设置");
            }
            _softReferenceTable = table;
            _softReferenceDomain = domain;
            return this;
        }

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

        private CurveSchema? _curve;

        /// <summary>分阶段落地计划 T-N0-1（落地清单 2.1 C1、数值总纲第 3 节原则 1）：该字段是一条曲线的
        /// 形态标记（断点表 / 二元饱和 + 横轴语义，见 <see cref="CurveSchema"/>），供通用曲线校验规则、
        /// 内容工具与运行时解析只认这一份登记。形态与 <see cref="Kind"/>/子结构是否相符（断点表须是
        /// <see cref="FieldKind.Array"/> 且元素为 <c>{x, y}</c>，饱和须是 <see cref="FieldKind.Object"/>
        /// 且含 <c>k</c>）刻意不在 <see cref="WithCurve"/> 挂载时检查，由 <c>SchemaAudit</c> 的
        /// <c>field_curve_shape</c> 检查项事后报告（同 <see cref="WithRange"/>/<see cref="WithMap"/>
        /// "让登记在错误组合上的错误停留在可枚举的软失败"这条既有风格）；按标准形态生成字段请用
        /// <see cref="CurveSchema.BreakpointsField"/>/<see cref="CurveSchema.SaturationField"/>。</summary>
        public CurveSchema? Curve => _curve;

        /// <summary>登记 <see cref="Curve"/>；只能设置一次（重复设置抛异常，同 <see cref="WithRange"/>/
        /// <see cref="WithMap"/> 惯例）。返回 <c>this</c> 便于链式调用。</summary>
        public FieldSchema WithCurve(CurveSchema curve)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            if (_curve != null) throw new InvalidOperationException($"字段 \"{Name}\"：Curve 已设置，不可重复设置");
            _curve = curve;
            return this;
        }

        /// <summary>消费方反馈第 46 条（04 第 3.4 节勘误"字段废弃元数据"）："X.Y.Z" 版本号格式，与
        /// <c>docs/升级指南/*.md</c>、<c>CHANGELOG.md</c> 记录版本号的既有记法一致；同
        /// <see cref="Core.Foundation.Common.Id"/> 的构造期格式校验惯例，编译为静态只读字段避免
        /// 每次 <see cref="WithDeprecated"/> 调用重新构造。</summary>
        private static readonly Regex DeprecatedSinceVersionRegex = new Regex(
            "^\\d+\\.\\d+\\.\\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>消费方反馈第 46 条（04 第 3.4 节勘误"字段废弃元数据"）：该字段是否已废弃——结构化
        /// 标记，供内容工具（<c>--list-tables --json</c> 导出）与 <c>SchemaAudit</c> 门禁区分"这个字段
        /// 废弃了"与"这个字段只是 Description 里恰好提到了别的字段名"这两种此前无法区分的情形（原始
        /// 案例：编辑器侧 <c>Editor.Core.Validation.DeprecatedFieldHints</c> 静态清单与升级指南附录 C
        /// 人工核对，见该反馈"影响"一节）。只能经 <see cref="WithDeprecated"/> 登记一次；未登记时为
        /// <c>false</c>，<see cref="DeprecatedSince"/>/<see cref="ReplacedBy"/>/<see cref="DeprecationNote"/>
        /// 均为 <c>null</c>（向后兼容，同 <see cref="Range"/>/<see cref="Curve"/> 等既有登记惯例）。</summary>
        public bool IsDeprecated { get; private set; }

        /// <summary>见 <see cref="WithDeprecated"/>。字段被标记废弃起始生效的版本号（<c>"X.Y.Z"</c>），
        /// 与 <c>docs/升级指南</c> 记录该字段废弃时间点的版本号一致。</summary>
        public string? DeprecatedSince { get; private set; }

        /// <summary>见 <see cref="WithDeprecated"/>。同表内取代本字段的字段名；<c>null</c> 表示"废弃后
        /// 无同表替代字段"（如 <c>StatHostOptions.EnableRatingConversion</c> 一类"无替代——功能始终
        /// 启用"的历史案例，见升级指南附录 C）。非空时须是同一级字段清单里已登记的字段名——校验时机见
        /// <see cref="WithDeprecated"/> 判断记录（登记顺序问题，改在字段清单组装处校验：本类型
        /// <c>fields</c> 参数构造分支、<see cref="TableSchema"/> 构造函数）。</summary>
        public string? ReplacedBy { get; private set; }

        /// <summary>见 <see cref="WithDeprecated"/>。可选的补充说明（如迁移注意事项），供内容工具原样
        /// 展示；不替代 <see cref="Description"/>，只是废弃这一件事本身的额外说明。</summary>
        public string? DeprecationNote { get; private set; }

        /// <summary>登记 <see cref="IsDeprecated"/>/<see cref="DeprecatedSince"/>/<see cref="ReplacedBy"/>/
        /// <see cref="DeprecationNote"/>（消费方反馈第 46 条，04 第 3.4 节勘误"字段废弃元数据"）；同
        /// <see cref="WithCurve"/>/<see cref="WithSoftReference"/> 等既有"修饰方法追加可选元数据"惯例，
        /// 不改动既有构造函数签名。只能设置一次（重复设置抛异常，同 <see cref="WithRange"/> 惯例）。
        /// <para>
        /// 判断记录（<paramref name="sinceVersion"/> 格式在此校验，<paramref name="replacedBy"/> 存在性
        /// 不在此校验）：<paramref name="sinceVersion"/> 的格式合法性只依赖它自己的字符串内容，不依赖
        /// 任何登记顺序，因此像 <see cref="WithFreeIds"/> 的 <c>reason</c> 必填检查一样在挂载时直接拒绝
        /// 非法值。<paramref name="replacedBy"/> 则不同——它必须是"同一级字段清单里已登记的字段名"，但
        /// <see cref="WithDeprecated"/> 调用发生在单个 <see cref="FieldSchema"/> 实例构造完成之后、被
        /// 装进它所属的兄弟字段清单（<see cref="Fields"/>/<c>TableSchema.Fields</c> 等）之前——此时这个
        /// 字段对象既不知道自己最终会被放进哪份清单，也看不到清单里的其它兄弟字段，无法在这里判断
        /// <paramref name="replacedBy"/> 是否存在。因此这条存在性校验推迟到"兄弟字段清单第一次被完整
        /// 组装"的地方，见本类型构造函数 <c>fields</c> 参数分支与 <see cref="TableSchema"/> 构造函数——
        /// 两处都已经拿到完整的兄弟清单，能一次性校验完，且报错发生在装配期（而不是留到
        /// <c>SchemaAudit</c> 门禁才发现），出错位置更接近登记代码本身。<c>SchemaAudit</c> 仍对
        /// <c>IsDeprecated</c>/<c>DeprecatedSince</c>/<c>ReplacedBy</c> 的自洽性做一次冗余复查
        /// （<c>field_deprecated_metadata</c> 检查，同 <c>field_group</c> 判断记录"理论上不可能触发、
        /// 仍留一道可枚举的软失败防线"这条既有风格）。
        /// </para>
        /// </summary>
        public FieldSchema WithDeprecated(string sinceVersion, string? replacedBy, string? note = null)
        {
            if (IsDeprecated) throw new InvalidOperationException($"字段 \"{Name}\"：Deprecated 已设置，不可重复设置");
            if (string.IsNullOrWhiteSpace(sinceVersion) || !DeprecatedSinceVersionRegex.IsMatch(sinceVersion))
            {
                throw new ArgumentException(
                    $"字段 \"{Name}\"：DeprecatedSince \"{sinceVersion ?? "<null>"}\" 格式非法，须形如 \"X.Y.Z\"",
                    nameof(sinceVersion));
            }
            if (replacedBy != null && string.IsNullOrWhiteSpace(replacedBy))
            {
                throw new ArgumentException($"字段 \"{Name}\"：ReplacedBy 若传入不能是空白字符串，无同表替代字段请传 null", nameof(replacedBy));
            }
            if (replacedBy == Name)
            {
                throw new ArgumentException($"字段 \"{Name}\"：ReplacedBy 不能指向自身", nameof(replacedBy));
            }

            IsDeprecated = true;
            DeprecatedSince = sinceVersion;
            ReplacedBy = replacedBy;
            DeprecationNote = note;
            return this;
        }

        /// <summary>见 <see cref="WithDeprecated"/> 判断记录：在某份"兄弟字段清单"（本类型 <c>fields</c>
        /// 参数、<see cref="TableSchema.Fields"/>）第一次被完整组装时，校验清单内每个已标记
        /// <see cref="IsDeprecated"/> 且 <see cref="ReplacedBy"/> 非空的字段，其 <see cref="ReplacedBy"/>
        /// 取值必须是同一份清单里某个字段的 <see cref="Name"/>。<paramref name="ownerLabel"/> 只用于
        /// 报错信息定位（"字段 X 的 Fields" 或 "表 Y"）。</summary>
        internal static void ValidateDeprecatedReplacedBy(string ownerLabel, IReadOnlyList<FieldSchema> siblings)
        {
            for (var i = 0; i < siblings.Count; i++)
            {
                var field = siblings[i];
                if (!field.IsDeprecated || field.ReplacedBy == null) continue;

                var found = false;
                for (var j = 0; j < siblings.Count; j++)
                {
                    if (siblings[j].Name == field.ReplacedBy) { found = true; break; }
                }

                if (!found)
                {
                    throw new ArgumentException(
                        $"{ownerLabel}：字段 \"{field.Name}\" 的 ReplacedBy \"{field.ReplacedBy}\" 未在同级字段清单中找到（消费方反馈第 46 条）");
                }
            }
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

            // 消费方反馈第 46 条：见 WithDeprecated 判断记录——本次构造把 fields 这份兄弟字段清单
            // 第一次完整组装出来，是校验清单内 ReplacedBy 存在性的第一个可行时机。
            if (fields != null)
            {
                ValidateDeprecatedReplacedBy($"字段 \"{name}\" 的 Fields", fields);
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
