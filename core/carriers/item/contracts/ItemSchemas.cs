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
                        "可引用的学派登记表（同 skill.def.school 惯例），按 Id 登记"),
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
                new FieldSchema("affixes", FieldKind.IdList, required: false,
                    description: "词缀引用（指向 item.affix，扩展位，本版不实现具体效果）"),
                new FieldSchema("set_id", FieldKind.Reference, required: false,
                    referenceTable: "item.set",
                    description: "所属套装"),
                WeaponProfileSchema,
                new FieldSchema("display_ref", FieldKind.Id, required: true,
                    description: "指向 display.map；本模块不引用 display_info 模块类型，不做引用完整性检查"),
                new FieldSchema("stack_size", FieldKind.Int, required: true,
                    description: "最大堆叠数量；装备类（slot 指向已登记 slot_definition）必须为 1" +
                        "（见 ItemStackSizeRule 判断记录）"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键（04 未展开，本模块实现期补录）"),
                RequirementsSchema,
                new FieldSchema("enchant_slot", FieldKind.Id, required: false,
                    description: "07 第 1.6 节扩展位：指向未来 item.enchant 表，本版不展开"),
                new FieldSchema("socket_count", FieldKind.Int, required: false,
                    description: "07 第 1.6 节扩展位：宝石镶嵌槽数，默认 0"),
                new FieldSchema("socket_ids", FieldKind.IdList, required: false,
                    description: "07 第 1.6 节扩展位：宝石镶嵌结果，本版不展开"),
                new FieldSchema("has_durability", FieldKind.Bool, required: false,
                    description: "07 第 1.6 节扩展位：是否具备耐久字段位，默认 false"),
                new FieldSchema("bind_type", FieldKind.Enum, required: false,
                    enumValues: new[] { "none", "on_pickup", "on_equip" },
                    description: "07 第 1.6 节扩展位：绑定方式，单机默认不产生实际限制"),
                new FieldSchema("stat_roll_ref", FieldKind.Id, required: false,
                    description: "07 第 1.6 节扩展位：指向未来的随机属性生成规则表，本版不展开"),
            });

        /// <summary><c>item.slot_definition</c>：槽位枚举定义（07 第 1.1 节原文 + 本模块实现期
        /// 补录 <c>is_weapon</c>/<c>accepts</c>/<c>is_equipment</c>，见 schema/README.md）。</summary>
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
                new FieldSchema("accepts", FieldKind.IdList, required: false,
                    description: "允许放入本槽位的物品 slot 取值列表（跨槽兼容，如\"左右戒指共用一个" +
                        "槽位定义\"一类场景）；未提供时只接受与本槽位 id 完全相同的 item.template.slot"),
                new FieldSchema("is_equipment", FieldKind.Bool, required: false,
                    description: "阶段 3 整理补录：该槽位是否为真正的装备位（缺省 true）。false 表示" +
                        "本槽位只是物品的分类桶（如消耗品/材料），不可经 EquipmentHost.Equip 装备" +
                        "（返回 SlotMismatch），堆叠数不受\"装备类 stack_size 必须为 1\"约束——见" +
                        "ItemStackSizeRule/EquipmentHost 判断记录"),
            });

        /// <summary><c>item.quality_definition</c>：品质分档定义（07 第 1.1 节原文 + 本模块实现期
        /// 补录 <c>budget_multiplier</c>，见 schema/README.md）。</summary>
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
                        "预算公式契约、ItemBudgetValidationRule）"),
            });

        /// <summary><c>item.budget_curve</c>：物品等级到属性预算上限的曲线（07 第 1.2 节，
        /// 线性插值，见 schema/README.md、<see cref="ItemBudgetCurve"/>）。</summary>
        public static readonly TableSchema BudgetCurve = new TableSchema(
            name: "item.budget_curve",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "item.budget.<name>"),
                new FieldSchema("entries", FieldKind.Array, required: true,
                    item: new FieldSchema("<budget_entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("item_level", FieldKind.Int, required: true,
                            description: "采样点对应的物品等级"),
                        new FieldSchema("budget", FieldKind.Number, required: true,
                            description: "该物品等级对应的属性预算上限"),
                    },
                    description: "{item_level, budget}，物品等级到属性预算上限曲线的一个采样点"),
                    description: "Array<{item_level:Int, budget:Number}>，按 item_level 线性插值" +
                        "（ItemBudgetCurve.ParseEntries 对缺失 item_level/budget 抛异常，故两者均必填）"),
            });

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
                new FieldSchema("pieces", FieldKind.IdList, required: true,
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
            });

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
            });
    }
}
