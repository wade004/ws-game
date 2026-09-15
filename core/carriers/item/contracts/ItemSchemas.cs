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
                    description: "词缀引用（指向 item.affix，扩展位，本版不实现具体效果）"),
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
        /// 契约未明文规定、按同类字段既有口径类推的实现期判断，非契约条文明文规定——上报待设计层确认，
        /// 见本任务汇报"契约疑点"一节。
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
        /// 覆盖本表。</para></summary>
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
        /// 武器槽位系数</c>、<c>伤害范围 = 武器秒伤 × weapon_profile.speed × (1 ± 浮动)</c> 随
        /// T-N2-4/E8 落地（同一提交替换 <c>EquipmentHost.GetWeaponBaseDamage</c> 的 <c>(min+max)/2</c>
        /// 实现），本任务只登记 schema 与注册。</summary>
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

        /// <summary><c>item.affix</c>：词缀（07 第 1.6 节扩展位，只登记 schema，不实现具体效果，
        /// 见 schema/README.md 判断记录）。</summary>
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
                new FieldSchema("effects", FieldKind.Array, required: false,
                    description: "扩展位占位字段，本版不解析、不实现"),
            }).WithOwnership(SchemaLayer.Carriers, "item");
    }
}
