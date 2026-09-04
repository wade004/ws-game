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
                new FieldSchema("stats", FieldKind.Array, required: false,
                    description: "Array<{stat:Id, op:flat|pct|mult, value:Number}>，装备后提供的" +
                        "固定/百分比属性；结构由本模块自行解析（field_type 对 Array 只做\"是数组\"检查）"),
                new FieldSchema("grants", FieldKind.Object, required: false,
                    description: "{skills:List<Id>, auras:List<Id>}，装备后授予的主动技能与被动光环"),
                new FieldSchema("affixes", FieldKind.IdList, required: false,
                    description: "词缀引用（指向 item.affix，扩展位，本版不实现具体效果）"),
                new FieldSchema("set_id", FieldKind.Reference, required: false,
                    referenceTable: "item.set",
                    description: "所属套装"),
                new FieldSchema("weapon_profile", FieldKind.Object, required: false,
                    description: "{damage_min:Number, damage_max:Number, speed:Number, weapon_school:Id}，" +
                        "当且仅当 slot 指向的 slot_definition.is_weapon 为 true 时必须存在" +
                        "（见 ItemWeaponProfileRule）"),
                new FieldSchema("display_ref", FieldKind.Id, required: true,
                    description: "指向 display.map；本模块不引用 display_info 模块类型，不做引用完整性检查"),
                new FieldSchema("stack_size", FieldKind.Int, required: true,
                    description: "最大堆叠数量；装备类（slot 指向已登记 slot_definition）必须为 1" +
                        "（见 ItemStackSizeRule 判断记录）"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键（04 未展开，本模块实现期补录）"),
                new FieldSchema("requirements", FieldKind.Object, required: false,
                    description: "{level:Int?}，可空；本模块实现期补录，供 EquipmentHost.Equip 的" +
                        "RequirementNotMet 判定使用"),
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
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
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
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
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
                    description: "Array<{item_level:Int, budget:Number}>，按 item_level 线性插值；" +
                        "结构由本模块自行解析"),
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
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("pieces", FieldKind.IdList, required: true,
                    description: "所属物品模板 id 列表（指向 item.template）；反向一致性见 " +
                        "ItemSetMembershipRule"),
                new FieldSchema("bonuses", FieldKind.Array, required: true,
                    description: "Array<{count:Int, aura_ref:Id}>，件数门槛到套装光环的映射；结构" +
                        "由本模块自行解析"),
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
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("effects", FieldKind.Array, required: false,
                    description: "扩展位占位字段，本版不解析、不实现"),
            });
    }
}
