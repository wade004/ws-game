using Core.Foundation.DataRegistry;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 本模块拥有的数据表 schema（见 07_载体层_物品生物物件.md 第 1.1/1.6 节、
    /// 04_数据与内容管线.md 第 1.1 节 <c>item.*</c> 六张表清单、schema/README.md 字段表与判断
    /// 记录）。调用方需要 <c>registry.RegisterSchema(ItemSchemas.Template)</c> 等逐一注册才会
    /// 加载对应数据文件——本模块不自动注册（同 <c>power_set</c>/<c>progression</c> 惯例，注册
    /// 时机由宿主统一掌控）。
    /// </summary>
    public static class ItemSchemas
    {
        /// <summary><c>stats[].op</c> 合法取值（<c>EquipmentHost.ParseOp</c> 权威解析，
        /// <c>flat|pct|mult</c> 三种，非 07 原文示例的 <c>flat|pct</c> 两种——以运行时代码为准，
        /// 见 ADR-0019 通用规则 1）。</summary>
        public static readonly string[] StatOpValues = { "flat", "pct", "mult" };

        /// <summary><c>item.template.stats</c> 元素结构（<c>EquipmentHost.ApplyGrants</c>
        /// 第一段：<c>RequireId(obj,"stat")</c>/<c>ParseOp(GetString(obj,"op","flat"))</c>/
        /// <c>GetNumber(obj,"value",0)</c>）。<c>stat</c> 登记为 <c>Reference(stat.definition)</c>：
        /// stat.definition 属 L1，本模块（L3）依赖方向合法（同 <c>SkillSchemas.AuraEffectsItemSchema</c>
        /// 的 <c>mod_stat.stat</c> 判断记录）。</summary>
        public static readonly FieldSchema StatsItemSchema = new FieldSchema(
            "<stat_mod>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("stat", FieldKind.Reference, required: true, referenceTable: "stat.definition",
                    description: "指向 stat.definition 的属性类型"),
                new FieldSchema("op", FieldKind.Enum, required: false, enumValues: StatOpValues,
                    description: "缺省 flat"),
                new FieldSchema("value", FieldKind.Number, required: false, description: "缺省 0"),
            },
            description: "{stat:Reference(stat.definition), op?:flat|pct|mult, value?:Number}，单条属性调整词条");

        /// <summary><c>item.template.grants</c> 结构（<c>EquipmentHost.ApplyGrants</c> 第二段）。
        /// <c>skills</c>/<c>auras</c> 登记为 <c>Reference(skill.def)</c>/<c>Reference(skill.aura_def)</c>
        /// （任务书额外要求 1：L3 引用 L2 程序集合法）。</summary>
        public static readonly FieldSchema GrantsSchema = new FieldSchema(
            "grants", FieldKind.Object, required: false, fields: new[]
            {
                new FieldSchema("skills", FieldKind.Array, required: false,
                    item: new FieldSchema("<skill_id>", FieldKind.Reference, required: true, referenceTable: "skill.def",
                        description: "指向 skill.def 的技能 id"),
                    description: "缺省 []，装备后授予的主动技能（来源计数按装备实例 id，见 EquipmentHost.ApplyGrants）"),
                new FieldSchema("auras", FieldKind.Array, required: false,
                    item: new FieldSchema("<aura_id>", FieldKind.Reference, required: true, referenceTable: "skill.aura_def",
                        description: "指向 skill.aura_def 的光环 id"),
                    description: "缺省 []，装备后授予的被动光环；重复引用见 ItemGrantsAurasDuplicateRule（Warning）"),
            },
            description: "{skills:[Reference(skill.def)], auras:[Reference(skill.aura_def)]}");

        /// <summary><c>item.affix.stat_mix</c> 元素结构（分阶段落地计划 T-N2-2；ADR-0032 决策 7；
        /// 07 第 1.6 节修订段"<c>stat_mix</c>（属性组合与内部分配比例，之和不超过一）"）。<c>stat</c>
        /// 登记为 <c>Reference(stat.definition)</c>，与 <see cref="StatsItemSchema"/> 同一判断记录
        /// （L3 依赖 L1 合法）。<c>ratio</c> 只登记"这条属性在词缀内部预算分配中的比例"，禁止出现任何
        /// 绝对数值字段（任务书硬性规则："禁止词缀写绝对数值"）——具体落值在掉落那一刻由预算反解
        /// （<c>IBudgetSolver.solve</c>，随 T-N2-4 落地）按该件预算 × <c>budget_share</c> × 本比例算出。
        /// 单个 <c>ratio</c> 范围登记为 <c>(0,1]</c>：0 没有意义（等价于该属性不在组合内，应从数组里
        /// 删掉这一条而不是写 0），上限 1 只是单元素本身的物理上界，"同一条词缀 <c>stat_mix[]</c> 之和
        /// 不超过一"是跨元素约束，登记表达不了，见 <see cref="ItemAffixStatMixRatioSumRule"/>。</summary>
        public static readonly FieldSchema AffixStatMixEntrySchema = new FieldSchema(
            "<affix_stat_mix_entry>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("stat", FieldKind.Reference, required: true, referenceTable: "stat.definition",
                    description: "指向 stat.definition 的属性类型，具体数值不在本表登记"),
                new FieldSchema("ratio", FieldKind.Number, required: true,
                    description: "该属性在本条词缀内部预算分配中的比例；同一条词缀 stat_mix[].ratio 之和" +
                        "不超过一（阻断，见 ItemAffixStatMixRatioSumRule）")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true, max: 1)),
            },
            description: "{stat:Reference(stat.definition), ratio:Number(0,1]}，词缀内部属性组合与分配" +
                "比例（不含绝对数值）");

        /// <summary><c>item.template.weapon_profile</c> 结构（<c>EquipmentHost.GetWeaponProfile</c>：
        /// <c>GetNumber(profile,"damage_min",0)</c>/<c>"damage_max"</c>/<c>"speed"</c>/
        /// <c>GetIdOpt(profile,"weapon_school")</c>）。</summary>
        public static readonly FieldSchema WeaponProfileSchema = new FieldSchema(
            "weapon_profile", FieldKind.Object, required: false, fields: new[]
            {
                new FieldSchema("damage_min", FieldKind.Number, required: false, description: "缺省 0"),
                new FieldSchema("damage_max", FieldKind.Number, required: false, description: "缺省 0"),
                new FieldSchema("speed", FieldKind.Number, required: false, description: "缺省 0"),
                new FieldSchema("weapon_school", FieldKind.Id, required: false,
                    description: "缺省无（WeaponProfile.WeaponSchool 为 null）；判断记录：无独立跨层" +
                        "登记表可挂载的学派标签（同 skill.def.school 惯例），按 Id 登记，不登记 SoftReferenceTable"),
            },
            description: "{damage_min,damage_max,speed:Number, weapon_school:Id?}；当且仅当 " +
                "slot_definition.is_weapon 为 true 时必须存在（ItemWeaponProfileRule）");

        /// <summary><c>item.template.requirements</c> 结构（<c>EquipmentHost.TryGetRequiredLevel</c>：
        /// 仅读取 <c>level</c>，非 <c>JsonNumber</c> 或缺失按"无等级限制"处理）。</summary>
        public static readonly FieldSchema RequirementsSchema = new FieldSchema(
            "requirements", FieldKind.Object, required: false, fields: new[]
            {
                new FieldSchema("level", FieldKind.Int, required: false, description: "缺省不限等级"),
            },
            description: "{level:Int?}");


        /// <summary><c>item.template</c>：物品模板（07 第 1.1 节全部字段 + 第 1.6 节扩展位留位 +
        /// 本模块实现期补录字段，见 schema/README.md）。</summary>
        public static readonly TableSchema Template = new TableSchema(
            name: "item.template",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.<name>"),
                new FieldSchema("slot", FieldKind.Reference, required: true,
                    referenceTable: "item.slot_definition",
                    description: "槽位枚举，指向 item.slot_definition"),
                new FieldSchema("quality", FieldKind.Reference, required: true,
                    referenceTable: "item.quality_definition",
                    description: "品质分档，指向 item.quality_definition"),
                new FieldSchema("item_level", FieldKind.Int, required: true,
                    description: "物品等级，用于预算公式与掉落/商店等级匹配"),
                new FieldSchema("stats", FieldKind.Array, required: false, item: StatsItemSchema,
                    description: "Array<{stat:Reference(stat.definition), op:flat|pct|mult, value:Number}>，" +
                        "装备后提供的固定/百分比属性"),
                GrantsSchema,
                new FieldSchema("affixes", FieldKind.IdList, required: false, referenceTable: "item.affix",
                    description: "分阶段落地计划 T-N2-2（ADR-0032 决策 7；落地改动点清单 E2"
                        + "\"item.template.affixes 语义改为可抽词缀池约束\"）：语义由留位期的\"词缀引用\"" +
                        "改写为\"该模板掉落时可抽取的词缀候选白名单\"——掉落三次掷骰的第三骰（词缀骰，" +
                        "T-N2-8 落地）先按品质骰结果匹配 item.affix.quality_pool，再与本字段交集；" +
                        "缺省 []（未登记）按\"不额外收窄\"处理，即该品质池下全部词缀均可抽，与登记前的" +
                        "缺省行为一致，不破坏现有样例。本任务只登记字段语义，消费实现（交集运算）随" +
                        "T-N2-8 掉落三次掷骰落地——设计层裁定（2026-09-15）：采纳，契约原文\"该模板可挂的词缀/或固定" +
                        "词缀\"（任务书用语）与\"可抽词缀池约束\"（落地改动点清单 E2 用语）二选一，" +
                        "本任务按后者（更具体、更晚落笔）实现，前者\"固定词缀\"（装备时必然带、不参与" +
                        "随机）在契约里没有独立字段位，若设计层确认需要该语义，需另开字段而非复用本字段"),
                new FieldSchema("set_id", FieldKind.Reference, required: false,
                    referenceTable: "item.set",
                    description: "所属套装"),
                WeaponProfileSchema,
                new FieldSchema("display_ref", FieldKind.Id, required: true,
                    description: "指向 display.map；本模块不引用 display_info 模块类型，不做引用完整性检查（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "display.map"),
                new FieldSchema("stack_size", FieldKind.Int, required: true,
                    description: "最大堆叠数量；装备类（slot 指向已登记 slot_definition）必须为 1" +
                        "（见 ItemStackSizeRule 判断记录）；下限见同类型 ItemStackSizeRule.CheckMin")
                    // 依据（ADR-0021）：ItemValidationRules.cs ItemStackSizeRule 既有判断
                    // "stack_size ({stackSize}) 必须 >= 1"（CheckMin），装备类额外要求 == 1 是跨字段
                    // 条件约束（依赖 slot 是否为已登记装备槽），登记表达不了，保留在该规则里。
                    .WithRange(FieldRange.Range(min: 1)),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键（04 未展开，本模块实现期补录）"),
                RequirementsSchema,
                new FieldSchema("enchant_slot", FieldKind.Id, required: false,
                    description: "07 第 1.6 节扩展位：预留 item.enchant 扩展表的挂载位，本版未定义该表，不登记 SoftReferenceTable（消费方反馈第 30 条核实）"),
                new FieldSchema("socket_count", FieldKind.Int, required: false,
                    description: "07 第 1.6 节扩展位：宝石镶嵌槽数，默认 0"),
                new FieldSchema("socket_ids", FieldKind.IdList, required: false,
                    description: "07 第 1.6 节扩展位：宝石镶嵌结果，本版不展开")
                    .WithFreeIds("07 第 1.6 节扩展位，本版不展开镶嵌结果指向哪张表，暂无目标可引用"),
                new FieldSchema("has_durability", FieldKind.Bool, required: false,
                    description: "07 第 1.6 节扩展位：是否具备耐久字段位，默认 false"),
                new FieldSchema("bind_type", FieldKind.Enum, required: false,
                    enumValues: new[] { "none", "on_pickup", "on_equip" },
                    description: "07 第 1.6 节扩展位：绑定方式，单机默认不产生实际限制"),
                new FieldSchema("stat_roll_ref", FieldKind.Id, required: false,
                    description: "兼容位（分阶段落地计划 T-N2-1；ADR-0032；07 第 1.6 节修订段"
                        + "\"stat_roll_ref 留位由 affixes 正式字段取代，保留兼容位不再规划\"）：随机属性"
                        + "由本版起改经 item.affix（budget_share/stat_mix/quality_pool/weight，见 T-N2-2）"
                        + "承担，本字段不再是待展开的扩展位，只作历史数据兼容读取位，不登记 SoftReferenceTable"),
                new FieldSchema("value_override", FieldKind.Number, required: false,
                    description: "基准价值覆盖（ADR-0032 决策；07 第 1.1 节修订段；ADR-0034）：未填按 "
                        + "econ.value_curve 公式（08 第 7.4 节，未落地）算出基准价值；填了且偏离公式超带宽"
                        + "报警告（\"手填价格偏离公式\"，04 第 5 节警告级校验项，消费与校验规则随 ADR-0034 "
                        + "落地任务实现，本任务只登记字段）；范围 >= 0（价值不可为负）")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("budget_note", FieldKind.String, required: false,
                    description: "超模说明（ADR-0032 决策 6："
                        + "\"橙装独特技能凭 budget_note 免检\"）：登记作者对预算/授予校验豁免的说明原文，"
                        + "供警告报告的\"已确认/待确认\"分组使用（04 第 2.2 节 ValidationIssue.note 同源，"
                        + "消费与接线随 T-N2-4/T-N2-6 授予与预算校验任务落地，本任务只登记字段）"),
            }).WithOwnership(SchemaLayer.Carriers, "item");

        /// <summary><c>item.slot_definition</c>：槽位枚举定义（07 第 1.1 节原文 + 本模块实现期
        /// 补录 <c>is_weapon</c>/<c>accepts</c>/<c>is_equipment</c> + 分阶段落地计划 T-N2-1（ADR-0032
        /// 决策 1）补录 <c>budget_coefficient</c>/<c>price_coefficient</c>，见 schema/README.md）。</summary>
        public static readonly TableSchema SlotDefinition = new TableSchema(
            name: "item.slot_definition",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.slot.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "槽位显示名文本键"),
                new FieldSchema("sort_weight", FieldKind.Int, required: false,
                    description: "排序权重，缺省 0"),
                new FieldSchema("is_weapon", FieldKind.Bool, required: false,
                    description: "本模块实现期补录：该槽位是否为武器槽（决定 weapon_profile 是否" +
                        "必填，见 ItemWeaponProfileRule），缺省 false"),
                new FieldSchema("accepts", FieldKind.IdList, required: false, referenceTable: "item.slot_definition",
                    description: "允许放入本槽位的物品 slot 取值列表（跨槽兼容，如\"左右戒指共用一个" +
                        "槽位定义\"一类场景）；未提供时只接受与本槽位 id 完全相同的 item.template.slot"),
                new FieldSchema("is_equipment", FieldKind.Bool, required: false,
                    description: "阶段 3 整理补录：该槽位是否为真正的装备位（缺省 true）。false 表示" +
                        "本槽位只是物品的分类桶（如消耗品/材料），不可经 EquipmentHost.Equip 装备" +
                        "（返回 SlotMismatch），堆叠数不受\"装备类 stack_size 必须为 1\"约束——见" +
                        "ItemStackSizeRule/EquipmentHost 判断记录"),
                new FieldSchema("budget_coefficient", FieldKind.Number, required: false,
                    description: "分阶段落地计划 T-N2-1（ADR-0032 决策 1/3；07 第 1.2 节修订段）：槽位" +
                        "预算系数，预算上限 = 预算曲线(item_level) × 品质预算倍率 × 本系数；缺省 1" +
                        "（未登记视为全额槽位）；消费实现随 T-N2-4 落地，本任务只登记字段。范围 > 0" +
                        "（系数应为正数，0 会让该槽位永远无法通过预算利用率）")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
                new FieldSchema("price_coefficient", FieldKind.Number, required: false,
                    description: "分阶段落地计划 T-N2-1（ADR-0032 决策 1；ADR-0034 价格公式\"买价 = " +
                        "基准价值 × 品质价格倍率 × 槽位价格倍率\"）：槽位价格系数，缺省 1；消费实现随 " +
                        "ADR-0034 落地任务接入，本任务只登记字段。范围 > 0")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
                new FieldSchema("has_armor", FieldKind.Bool, required: false,
                    description: "分阶段落地计划 T-N2-6（设计层裁定，07 第 1.2 节修订段\"护甲值……仅" +
                        "护甲位\"）：该槽位的装备是否提供护甲值（ADR-0032 决策 4 的护甲位），缺省 " +
                        "false。判断记录：T-N2-5 曾用\"非武器位（is_weapon != true）且是真正装备位" +
                        "（is_equipment != false）\"推断护甲位，代价是戒指/项链/饰品一类传统意义上不该" +
                        "有护甲值的槽位也会被写入护甲修正——本字段是随 T-N2-6 一并落地的设计层裁定：" +
                        "改为显式字段，EquipmentHost.IsArmorSlot 只看 has_armor == true，不再从" +
                        "is_weapon/is_equipment 推断，见该方法判断记录。"),
            }).WithOwnership(SchemaLayer.Carriers, "item");

        /// <summary><c>item.quality_definition</c>：品质分档定义（07 第 1.1 节原文 + 本模块实现期
        /// 补录 <c>budget_multiplier</c> + 分阶段落地计划 T-N2-1（ADR-0032 决策 2）补录
        /// <c>affix_count</c>/<c>grant_budget_share</c>/<c>price_multiplier</c>，见 schema/README.md）。
        /// <para>
        /// 判断记录（<c>grant_budget_share</c>/<c>price_multiplier</c> 是否必填）：ADR-0032 决策 2 原文
        /// 只对 <c>affix_count</c> 明文标注"可选"，对另两个新列未明确标注必填/可选。本任务按既有
        /// <c>budget_multiplier</c>（同为品质倍率类字段，<c>required: false</c> + 缺省 1）的登记口径
        /// 类推处理：<c>price_multiplier</c> 与 <c>budget_multiplier</c> 同为"倍率"，缺省 1；
        /// <c>grant_budget_share</c> 是"占比"，缺省 0（未登记则视为不可带授予/特效预算为零，与 07 第
        /// 1.2 节"紫以上品质才可带 grants"的门槛语义一致——白绿蓝品质通常不需要显式填写该列）。均为
        /// 契约未明文规定、按同类字段既有口径类推的实现期判断，非契约条文明文规定——设计层裁定
        /// （2026-09-15）：采纳。
        /// </para>
        /// </summary>
        public static readonly TableSchema QualityDefinition = new TableSchema(
            name: "item.quality_definition",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.quality.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "品质显示名文本键"),
                new FieldSchema("sort_weight", FieldKind.Int, required: false,
                    description: "排序权重，缺省 0"),
                new FieldSchema("budget_multiplier", FieldKind.Number, required: false,
                    description: "本模块实现期补录：预算曲线值的品质系数，缺省 1（见 07 第 1.2 节" +
                        "预算公式契约、ItemBudgetValidationRule）；大小顺序须与 sort_weight 一致，见 " +
                        "ItemQualityMultiplierOrderRule（T-N2-1）"),
                new FieldSchema("affix_count", FieldKind.Int, required: false,
                    description: "分阶段落地计划 T-N2-1（ADR-0032 决策 2：\"副属性条目数，不填不限\"）：" +
                        "该品质允许的副属性条目数上限，可选，不填不限；消费实现随词缀转正（T-N2-2）落地，" +
                        "本任务只登记字段。范围 >= 0")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("grant_budget_share", FieldKind.Number, required: false,
                    description: "分阶段落地计划 T-N2-1（ADR-0032 决策 2/6；07 第 1.2 节修订段"
                        + "\"授予规则\"）：该品质授予的技能/光环预算 = 该件预算 × 本占比，警告级校验"
                        + "（\"授予价值超特效占比\"）；缺省 0（见本表类型判断记录）；消费实现随 T-N2-6 "
                        + "落地，本任务只登记字段。范围 [0,1]")
                    .WithRange(FieldRange.Range(min: 0, max: 1)),
                new FieldSchema("price_multiplier", FieldKind.Number, required: false,
                    description: "分阶段落地计划 T-N2-1（ADR-0032 决策 2；ADR-0034 价格公式\"买价 = " +
                        "基准价值 × 品质价格倍率 × 槽位价格倍率\"）：品质价格系数，缺省 1（见本表类型" +
                        "判断记录）；大小顺序须与 sort_weight 一致，见 ItemQualityMultiplierOrderRule" +
                        "（T-N2-1）；消费实现随 ADR-0034 落地任务接入，本任务只登记字段。范围 > 0")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
            }).WithOwnership(SchemaLayer.Carriers, "item");

        /// <summary><c>item.budget_curve</c>：物品等级到属性预算上限的曲线（07 第 1.2 节，
        /// 线性插值，见 schema/README.md、<see cref="ItemBudgetCurve"/>）。
        /// <para>
        /// 分阶段落地计划 T-N0-4（落地清单 2.1 C4）：<c>entries</c> 迁移到 04 第 3.6 节通用断点表形态
        /// <c>{x, y}</c>（横轴语义 <see cref="CurveAxis.ItemLevel"/>），schema 版本 1→2，迁移环节把
        /// v1 的 <c>{item_level, budget}</c> 逐元素改名为 <c>{x, y}</c>（其余键原样保留）。运行时解析
        /// 与插值委托 <see cref="CurveSchema.ReadBreakpoints(DataRecord, string)"/>/
        /// <c>Core.Foundation.Common.PiecewiseCurve</c>；通用规则 <c>curve_monotonic_finite</c> 自动
        /// 覆盖本表。</para>
        /// <para>
        /// 判断记录（T-N2-3，<c>exponent</c> 字段登记位置）：ADR-0032 决策 3/07 第 1.2 节修订段/数值
        /// 总纲第 4.4 节三处原文均只给出"实际消耗 = (Σ(属性值×权重)^k)^(1/k)，k 默认 1.5，数据配置"，
        /// 没有指明 k 具体登记在哪张表的哪个字段——设计层裁定（2026-09-15）：采纳，登记为任务书给出的候选
        /// 位置之一实现：登记为 <c>item.budget_curve</c> 记录上的可选字段 <c>exponent</c>（缺省 1.5，
        /// 与消耗曲线同表意味着"哪条预算曲线用哪个指数"可以按曲线各自配置，同一游戏若有多条预算曲线
        /// 服务不同物品档位时不必共用一个 k）。范围 <c>&gt; 0</c>（非正指数在 <c>(...)^(1/k)</c> 意义
        /// 下无定义或退化）。消费实现（<see cref="ItemBudgetCurve.ComputeConsumed"/>）见该类型判断
        /// 记录。
        /// </para></summary>
        public static readonly TableSchema BudgetCurve = new TableSchema(
            name: "item.budget_curve",
            primaryKey: "id",
            currentSchemaVersion: 2,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.budget.<name>"),
                CurveSchema.BreakpointsField("entries", CurveAxis.ItemLevel, required: true,
                    description: "断点表 [{x: 物品等级(Int), y: 预算上限(Number)}]，按 x 线性插值、越界夹取到端点" +
                        "（04 第 3.6 节通用曲线形态；v1 字段名 item_level/budget 经 1→2 迁移改名）",
                    xDescription: "采样点对应的物品等级",
                    yDescription: "该物品等级对应的属性预算上限"),
                new FieldSchema("exponent", FieldKind.Number, required: false,
                    description: "分阶段落地计划 T-N2-3（ADR-0032 决策 3；07 第 1.2 节修订段；数值总纲" +
                        "第 4.4 节）：消耗公式 (Σ(属性值×权重)^k)^(1/k) 的指数 k，缺省 1.5（见本表类型" +
                        "判断记录，登记位置设计层裁定（2026-09-15）：采纳）。范围 > 0")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
            },
            migrations: new[]
            {
                new TableMigration(1, 2, row => CurveSchema.MigrateBreakpointsFieldNames(row, "entries", "item_level", "budget")),
            }).WithOwnership(SchemaLayer.Carriers, "item");

        /// <summary><c>item.armor_curve</c>：物品等级到护甲值的曲线（分阶段落地计划 T-N2-1；
        /// ADR-0032 决策 4；04 第 1.1 节表清单 <c>item.armor_curve</c> 行"护甲位专用，不占预算，乘
        /// 槽位系数"）。新表，直接按 04 第 3.6 节通用断点表形态登记（无需迁移，不同于
        /// <see cref="BudgetCurve"/> 那种从私有形态迁移过来的既有表）。消费公式
        /// <c>护甲值 = item.armor_curve(item_level) × 槽位系数</c> 随 T-N2-4/E7 落地，本任务只登记
        /// schema 与注册；<c>curve_monotonic_finite</c>（T-N0-3）自动覆盖本表 <c>entries</c>。</summary>
        public static readonly TableSchema ArmorCurve = new TableSchema(
            name: "item.armor_curve",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.armor.<name>"),
                CurveSchema.BreakpointsField("entries", CurveAxis.ItemLevel, required: true,
                    description: "断点表 [{x: 物品等级(Int), y: 护甲值(Number)}]，按 x 线性插值、越界" +
                        "夹取到端点（04 第 3.6 节通用曲线形态；ADR-0032 决策 4）",
                    xDescription: "采样点对应的物品等级",
                    yDescription: "该物品等级对应的护甲值，仅护甲位使用，不占预算",
                    yRange: FieldRange.Range(min: 0)),
            }).WithOwnership(SchemaLayer.Carriers, "item");

        /// <summary><c>item.weapon_dps_curve</c>：物品等级到武器秒伤的曲线（分阶段落地计划 T-N2-1；
        /// ADR-0032 决策 4；04 第 1.1 节表清单 <c>item.weapon_dps_curve</c> 行"乘品质预算倍率与武器
        /// 槽位系数"）。消费公式 <c>武器秒伤 = item.weapon_dps_curve(item_level) × 品质预算倍率 ×
        /// 武器槽位系数</c> 随 T-N2-6（<see cref="Core.Carriers.Item.EquipmentHost.GetWeaponDps"/>）
        /// 落地；<c>伤害范围 = 武器秒伤 × weapon_profile.speed × (1 ± 浮动)</c> 同一任务接入"手填
        /// damage_min/max 偏离秒伤曲线"警告（见 <c>Core.Carriers.Item.
        /// ItemWeaponDamageDeviatesDpsCurveRule</c>）——该警告只核对 <c>(damage_min+damage_max)/2</c>
        /// 与"秒伤 × speed"的偏离比例，不消费本表新增的 <c>variance</c> 字段；<c>GetWeaponBaseDamage</c> 仍保留
        /// <c>(min+max)/2</c> 语义不变（硬性规则"禁止改既有签名"，见该接口判断记录）。</summary>
        public static readonly TableSchema WeaponDpsCurve = new TableSchema(
            name: "item.weapon_dps_curve",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.weapon_dps.<name>"),
                CurveSchema.BreakpointsField("entries", CurveAxis.ItemLevel, required: true,
                    description: "断点表 [{x: 物品等级(Int), y: 武器秒伤(Number)}]，按 x 线性插值、" +
                        "越界夹取到端点（04 第 3.6 节通用曲线形态；ADR-0032 决策 4）",
                    xDescription: "采样点对应的物品等级",
                    yDescription: "该物品等级对应的武器基准秒伤，乘品质预算倍率与武器槽位系数后得到" +
                        "实际武器秒伤",
                    yRange: FieldRange.Range(min: 0, minExclusive: true)),
                new FieldSchema("variance", FieldKind.Number, required: false,
                    description: "分阶段落地计划 T-N2-6（ADR-0032 决策 4"
                        + "\"伤害范围 = 武器秒伤 × 初始攻速 × (1 ± 浮动)\"；拍板 6"
                        + "\"一拍常数与浮动比例为数据项\"）：伤害范围浮动比例，缺省 0.1（±10%）。"
                        + "判断记录（登记位置——设计层裁定（2026-09-15）：采纳）：07/ADR-0032 均未指明具体字段位，"
                        + "落地改动点清单第 10 节第 6 条给出候选\"一拍常数与浮动比例放 skill.budget_rule "
                        + "与 item.weapon_dps_curve 旁\"——一拍常数登记在 skill.budget_rule（该表要到 "
                        + "T-N3-9 才创建，见落地改动点清单 S12/E8），浮动比例按同一候选落在本表；本任务" +
                        "只登记字段，尚无消费者（\"手填偏离秒伤曲线\"警告只比较均值与\"秒伤×speed\"，不" +
                        "展开到 (1±浮动) 的上下界，见 ItemWeaponDamageDeviatesDpsCurveRule 判断记录）。" +
                        "范围 [0,1)（0 表示无浮动、伤害范围退化为定值；>=1 会让下界降到 0 或以下，" +
                        "无意义）")
                    .WithRange(FieldRange.Range(min: 0, max: 1, maxExclusive: true)),
            }).WithOwnership(SchemaLayer.Carriers, "item");

        /// <summary><c>item.req_level_curve</c>：物品等级到需求等级的曲线（分阶段落地计划 T-N2-1；
        /// ADR-0032 决策 5；04 第 1.1 节表清单 <c>item.req_level_curve</c> 行"<c>requirements.level</c>
        /// 未填时按此反推"）。消费实现（<c>EquipmentHost.TryGetRequiredLevel</c> 缺省分支改按本曲线
        /// 反推，替代当前"缺省不限等级"）随 T-N2-9/E9 落地，本任务只登记 schema 与注册。</summary>
        public static readonly TableSchema ReqLevelCurve = new TableSchema(
            name: "item.req_level_curve",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.req_level.<name>"),
                CurveSchema.BreakpointsField("entries", CurveAxis.ItemLevel, required: true,
                    description: "断点表 [{x: 物品等级(Int), y: 需求等级(Number)}]，按 x 线性插值、" +
                        "越界夹取到端点（04 第 3.6 节通用曲线形态；ADR-0032 决策 5）",
                    xDescription: "采样点对应的物品等级",
                    yDescription: "该物品等级对应的角色需求等级；requirements.level 未填时由此反推，" +
                        "填了以手填为准",
                    yRange: FieldRange.Range(min: 0)),
            }).WithOwnership(SchemaLayer.Carriers, "item");

        /// <summary><c>item.set</c>：套装定义（07 第 1.1/1.5 节，件数门槛 → apply_aura）。</summary>
        public static readonly TableSchema Set = new TableSchema(
            name: "item.set",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.set.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "套装显示名文本键"),
                new FieldSchema("pieces", FieldKind.IdList, required: true, referenceTable: "item.template",
                    description: "所属物品模板 id 列表（指向 item.template）；反向一致性见 " +
                        "ItemSetMembershipRule"),
                new FieldSchema("bonuses", FieldKind.Array, required: true,
                    item: new FieldSchema("<bonus_entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("count", FieldKind.Int, required: false, description: "缺省 0"),
                        new FieldSchema("aura_ref", FieldKind.Reference, required: true, referenceTable: "skill.aura_def",
                            description: "达到门槛件数后授予的光环，指向 skill.aura_def"),
                    },
                    description: "{count?, aura_ref}，件数门槛到套装光环的一条映射"),
                    description: "Array<{count:Int, aura_ref:Reference(skill.aura_def)}>，件数门槛到" +
                        "套装光环的映射（EquipmentHost.ParseSetBonuses；aura_ref 登记为 Reference：" +
                        "skill.aura_def 属 L2，本模块 L3 依赖方向合法）"),
            }).WithOwnership(SchemaLayer.Carriers, "item");

        /// <summary><c>item.affix</c>：词缀，分阶段落地计划 T-N2-2（ADR-0032 决策 7；07 第 1.6 节
        /// 修订段"随机词缀由留位转正"）由留位转正为预算份额包正式表。
        /// <para>
        /// 判断记录（是否升级 schema 版本）：本任务书"实现要点"给出的判定条件——"若 item.affix 此前是
        /// 留位、样例为空或只有占位，且 games/_template 没有该表数据，可以不升版本"——三项前提均成立：
        /// 留位期字段只有 <c>id</c>/<c>name_key</c>/<c>effects</c>（<c>effects</c> 从未被任何运行时
        /// 代码解析，见 schema/README.md 旧判断记录"任务书额外要求 1……全仓库搜索 item.affix
        /// 只有 ItemSchemas.cs 自身的 schema 声明，没有任何运行时解析代码读取过 effects"）；
        /// <c>data/_sample/item/item.affix.json</c> 三条样例行此前只有 <c>id</c>/<c>name_key</c>
        /// 两个字段（无 <c>effects</c> 实值，纯占位）；<c>games/_template/data/game/item/</c> 目录
        /// 下没有 <c>item.affix.json</c>（见本任务勘察，T-N2-10 才补空壳表）。三项前提俱在，本表
        /// <c>currentSchemaVersion</c> 保持 1，不新增迁移链——新增的四个必填字段（<c>budget_share</c>/
        /// <c>stat_mix</c>/<c>quality_pool</c>/<c>weight</c>）对"没有旧数据需要兼容"的场景无需迁移
        /// 函数，样例改写为新形态即可（见 <c>data/_sample/item/item.affix.json</c>）。
        /// </para>
        /// <para>
        /// 判断记录（<c>effects</c> 留位废弃，不删除）：硬性规则 5（ABI 只允许新增）与任务书"去掉占位
        /// effects（保留读取兼容一个周期）"——字段本身不删除，改写 <see cref="FieldKind.Array"/>
        /// 描述为废弃说明；不登记 <c>required: true</c>，允许旧数据文件（若外部游戏仓库已写过
        /// <c>effects</c>）继续通过加载，一个版本周期后（跟随分阶段落地计划下一次词缀相关任务）再
        /// 物理删除。本版起新样例不再写 <c>effects</c>，新增内容也不应该再填它。
        /// </para>
        /// </summary>
        public static readonly TableSchema Affix = new TableSchema(
            name: "item.affix",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.affix.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "词缀显示名文本键"),
                new FieldSchema("budget_share", FieldKind.Number, required: true,
                    description: "分阶段落地计划 T-N2-2（ADR-0032 决策 7；07 第 1.6 节修订段）：占该件" +
                        "预算的比例；具体数值在掉落那一刻按该件预算 × 本比例经预算反解" +
                        "（IBudgetSolver.solve，随 T-N2-4 落地）算出，一条词缀适用于全部等级；" +
                        "\"模板属性 + 可抽词缀最大份额 ≤ 预算\"为阻断校验（消费实现随 T-N2-3/T-N2-4 落地，" +
                        "本任务只登记字段）。范围 [0,1]（占比，同 item.quality_definition." +
                        "grant_budget_share 既有登记口径）")
                    .WithRange(FieldRange.Range(min: 0, max: 1)),
                new FieldSchema("stat_mix", FieldKind.Array, required: true, item: AffixStatMixEntrySchema,
                    description: "Array<{stat:Reference(stat.definition), ratio:Number(0,1]}>，属性组合与" +
                        "内部分配比例；同一条词缀本字段内部 ratio 之和不超过一（阻断，" +
                        "ItemAffixStatMixRatioSumRule，本任务落地）；禁止出现任何绝对数值字段" +
                        "（任务书硬性规则）"),
                new FieldSchema("quality_pool", FieldKind.Reference, required: true,
                    referenceTable: "item.quality_definition",
                    description: "所属品质池，指向 item.quality_definition；掉落时先掷品质骰，" +
                        "再从该品质对应的词缀池里抽词缀骰（07 第 1.6 节修订段"
                        + "\"品质来自掉落表品质权重，词缀从该品质对应的池里抽\"），消费实现随 T-N2-8 落地"),
                new FieldSchema("weight", FieldKind.Number, required: true,
                    description: "池内权重（07 第 1.6 节修订段）；范围 >= 0（同 loot.table." +
                        "weighted_pick_one 既有 weight_or_chance 登记口径\"相对权重\"，见 LootSchemas；" +
                        "0 表示登记了但当前不参与抽取，而非非法值），消费实现（加权抽取）随 T-N2-8 落地")
                    .WithRange(FieldRange.Range(min: 0)),
                GrantsSchema,
                new FieldSchema("effects", FieldKind.Array, required: false,
                    description: "已废弃占位字段（分阶段落地计划 T-N2-2 起）：由 stat_mix/grants 取代，" +
                        "保留一个版本周期仅作历史数据读取兼容，本版起不再解析、新数据不应再填写，一个" +
                        "版本周期后随后续词缀相关任务物理删除")
                    // 消费方反馈第 46 条：ReplacedBy 只接受单个同级字段名，但本字段实际由 stat_mix 与
                    // grants 两个字段共同取代（见上方 Description、升级指南附录 C 同一行"budget_share/
                    // stat_mix/quality_pool/weight"——附录 C 的表述口径是"item.affix 整表 T-N2-2 新增的
                    // 四个必填字段"，比本字段 Description 给出的"stat_mix/grants"更宽，两处表述不完全
                    // 一致，如实记录在 note 里，不强行只挑一个塞进 ReplacedBy）；登记 ReplacedBy=null
                    // （不代表"无替代"，代表"替代关系是多字段，无法用单个字段名表达"），完整取代关系
                    // 见 DeprecationNote。
                    .WithDeprecated("1.32.0", replacedBy: null,
                        note: "由 stat_mix + grants 两个字段共同取代（升级指南附录 C 一行给出更宽口径" +
                            "\"budget_share/stat_mix/quality_pool/weight\"，指整表 T-N2-2 新增字段，" +
                            "非本字段的一一对应替代）"),
            }).WithOwnership(SchemaLayer.Carriers, "item");
    }
}
