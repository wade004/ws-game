using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// <c>gobj.*</c> 两张表的 <see cref="TableSchema"/> 声明（见 07_载体层_物品生物物件.md 第 3.1/3.2
    /// 节字段表、schema/README.md 字段表）。调用方在构造 <see cref="Core.Foundation.DataRegistry.IDataRegistry"/>
    /// 后需要依次 <c>RegisterSchema(GobjSchemas.Template)</c>/<c>RegisterSchema(GobjSchemas.Lock)</c>
    /// 才能加载对应数据文件（本模块不自动注册，与 <c>core/rules/skill</c> 的 <c>SkillSchemas</c> 同一
    /// 惯例）。
    /// </summary>
    public static class GobjSchemas
    {
        /// <summary><c>kind</c> 字段合法取值（07 第 3.1 节十种类型，见 <see cref="GobjKindNames"/>）。</summary>
        public static readonly string[] KindValues =
        {
            "door", "chest", "quest_object", "trap", "spell_focus",
            "gather_node", "teleporter", "save_point", "lever", "sign",
        };

        /// <summary><c>on_use.kind</c> 字段合法取值（07 第 3.3 节"二者二选一"）。</summary>
        public static readonly string[] OnUseKindValues = { "skill", "dialog" };

        /// <summary><c>gobj.lock.requirement.kind</c> 字段合法取值（07 第 3.2 节三种变体）。</summary>
        public static readonly string[] LockRequirementKindValues = { "item_key", "world_flag", "skill_check" };

        // -----------------------------------------------------------------
        // type_data：GameObjectTemplate.ParseTypeData 唯一权威，按 gobj.template.kind 分派十种
        // 类型数据结构（07 第 3.1 节）。
        // 判断记录（Variants 不适用于本字段，同 AreaTriggerSchemas.ParamsSchema 判断记录）：判别
        // 字段 kind 是 gobj.template 行内与 type_data 平级的字段，不在 type_data 对象内部；
        // VariantSchema.Discriminator 要求判别字段与被判别的子字段处在同一个 JsonObject 里
        // （DataRegistry.ValidateVariantObject 在 type_data 自身的 JsonObject 里找 Discriminator），
        // 若把 Discriminator 设为 "kind"，每条记录的 type_data 对象内都不会有这个键，会恒报
        // variant_discriminator 缺失。改用 Fields 登记十种 kind 分别用到的字段并集，全部标记非
        // 必填——真正"哪些字段必填视 kind 而定"这条业务判断，登记层表达不了，继续保留在
        // GobjTypeDataFieldGroupRule（不退役，见 schema/README.md"退役规则"一节）；本次登记新增的
        // 是此前完全没有的"存在时类型必须合法"校验（Id 格式/Reference 存在性/数字），与
        // GobjTypeDataFieldGroupRule 的必填性检查互不重叠，不会对同一缺陷双报。
        // -----------------------------------------------------------------
        public static readonly FieldSchema TypeDataSchema = new FieldSchema(
            "type_data", FieldKind.Object, required: true, fields: new[]
            {
                // door/chest 共用：type_data.lock_id 是任务书给内容作者的可读性冗余（真正生效的是
                // 顶层 lock_id），不参与运行期逻辑，见 DoorTypeData/ChestTypeData 判断记录，故不登记
                // 为 Reference（避免暗示"这里也会做引用完整性检查"这一并不成立的语义），退回 Id。
                new FieldSchema("lock_id", FieldKind.Id, required: false,
                    description: "door/chest 可选：可读性冗余，不参与运行期逻辑（真正生效的是顶层 lock_id）"),
                new FieldSchema("loot_table_ref", FieldKind.Id, required: false,
                    description: "chest/gather_node 必填（GobjTypeDataFieldGroupRule 校验，本登记只管类型）；" +
                        "loot.table 属 L4，本模块（L3）不可 Reference（依赖方向），退回 Id"),
                new FieldSchema("quest_action_ref", FieldKind.Id, required: false,
                    description: "quest_object 必填（同上，本登记只管类型）；quest.* 属 L4，退回 Id"),
                new FieldSchema("skill_id", FieldKind.Reference, required: false, referenceTable: "skill.def",
                    description: "trap 必填（同上，本登记只管类型）；skill.def 属 L2，本模块（L3）依赖方向合法"),
                new FieldSchema("trigger_shape", FieldKind.Object, required: false,
                    description: "trap 必填（同上，本登记只管类型）；Shape 联合类型的具体形状不属于本模块" +
                        "解析范围（TrapTypeData.TriggerShape 原样透传给 L4），不展开子结构"),
                new FieldSchema("required_skill_tag", FieldKind.Id, required: false,
                    description: "spell_focus 必填（同上，本登记只管类型）；标签而非表引用，按 Id 登记"),
                new FieldSchema("respawn_after_use", FieldKind.Number, required: false,
                    description: "gather_node 必填（同上，本登记只管类型）"),
                new FieldSchema("teleport_target_ref", FieldKind.Id, required: false,
                    description: "teleporter 必填（同上，本登记只管类型）；经 GobjOptions.TeleportResolver" +
                        "运行期解析，非静态数据表引用，按 Id 登记"),
                new FieldSchema("linked_object_ids", FieldKind.IdList, required: false,
                    description: "lever 必填（同上，本登记只管类型）；ToggleLever 按运行期实体 id 索引" +
                        "（非 gobj.template 数据表引用），按 IdList 登记，不加 Reference")
                    .WithFreeIds("按运行期实体 id 索引（非 gobj.template 数据表引用），见字段描述判断记录"),
                new FieldSchema("text_key", FieldKind.TextKey, required: false,
                    description: "sign 必填（同上，本登记只管类型）；本次登记补齐嵌套 TextKey 存在性校验" +
                        "（原判断记录\"契约缺口\"——DataRegistry 内建 text_key_exists 只覆盖表顶层字段——" +
                        "已由 ADR-0019 递归校验关闭，见 SignTypeData 顶部注释）"),
            },
            description: "按 kind 分派：door{lock_id?}；chest{loot_table_ref,lock_id?}；" +
                "quest_object{quest_action_ref}；trap{skill_id,trigger_shape}；" +
                "spell_focus{required_skill_tag}；gather_node{loot_table_ref,respawn_after_use}；" +
                "teleporter{teleport_target_ref}；save_point{}；lever{linked_object_ids}；" +
                "sign{text_key}；字段组必填性见 GobjTypeDataFieldGroupRule（Variants 不适用，见本字段判断记录）");

        /// <summary><c>gobj.template.on_use</c>：判别字段 <c>kind</c> 与被判别的 <c>ref</c> 同处
        /// <c>on_use</c> 对象内，Variants 适用（不同于 <see cref="TypeDataSchema"/)）。原
        /// <c>GobjOnUseKindRule</c> 的三项检查（kind 存在且合法字符串/取值在二选一集合内/ref 是合法
        /// Id）已被 <c>variant_discriminator</c>/<c>field_type</c>/<c>required_field</c> 内建校验
        /// 完全覆盖，整条退役删除（见 schema/README.md"退役规则"）。</summary>
        public static readonly FieldSchema OnUseSchema = new FieldSchema(
            "on_use", FieldKind.Object, required: false, variants: BuildOnUseVariants(),
            description: "{kind: skill|dialog, ref}，二者二选一");

        private static VariantSchema BuildOnUseVariants()
        {
            var cases = new System.Collections.Generic.Dictionary<string, IReadOnlyList<FieldSchema>>(System.StringComparer.Ordinal)
            {
                ["skill"] = new[]
                {
                    new FieldSchema("ref", FieldKind.Reference, required: true, referenceTable: "skill.def",
                        description: "指向 skill.def 的待触发技能"),
                },
                ["dialog"] = new[]
                {
                    // dialog.gossip_menu 属 L4，本模块（L3）不可 Reference（依赖方向），退回 Id。
                    new FieldSchema("ref", FieldKind.Id, required: true,
                        description: "指向 dialog.gossip_menu 的动作项；L4 高于本模块 L3，退回 Id"),
                },
            };
            return new VariantSchema("kind", cases);
        }

        public static TableSchema Template { get; } = new TableSchema(
            name: "gobj.template",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "gobj.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true, description: "显示名文本键"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: KindValues,
                    description: "十种类型之一，见 07 第 3.1 节"),
                TypeDataSchema,
                new FieldSchema("lock_id", FieldKind.Reference, required: false, referenceTable: "gobj.lock",
                    description: "当前锁（可空），指向 gobj.lock"),
                OnUseSchema,
                new FieldSchema("display_ref", FieldKind.Id, required: true, description: "指向 display.map"),
                new FieldSchema("tags", FieldKind.IdList, required: false, description: "标签集合")
                    .WithFreeIds("标签当前没有独立登记表，是内容作者自由声明的分类标签"),
            }).WithOwnership(SchemaLayer.Carriers, "gameobject");

        /// <summary><c>gobj.lock.requirement</c>：判别字段 <c>kind</c> 与三种变体的专属字段同处
        /// <c>requirement</c> 对象内，Variants 适用。原 <c>GobjLockRequirementFieldGroupRule</c> 的
        /// 全部检查（kind 存在且合法字符串/取值在三选一集合内/各变体专属字段必填）已被
        /// <c>variant_discriminator</c>/<c>required_field</c> 内建校验完全覆盖，整条退役删除。
        /// <c>world_flag.expected</c> 取值为 <c>Bool｜Number</c>（<c>LockDef.RequireExprValue</c>），
        /// <see cref="FieldKind"/> 无法表达联合类型，本次不在这里登记其类型（同 <c>SkillSchemas</c>
        /// <c>set_world_flag.value</c> 判断记录）；P2-03 根治：必填与形状改由独立的
        /// <see cref="GobjLockWorldFlagExpectedRule"/> 在 report 阶段兜底，不再是"未登记子字段
        /// 默认不报错"——缺失或类型不是 Bool/Number 会被判为 blocking error，见该规则判断记录。</summary>
        public static readonly FieldSchema RequirementSchema = new FieldSchema(
            "requirement", FieldKind.Object, required: true, variants: BuildRequirementVariants(),
            description: "{kind: item_key|world_flag|skill_check, ...}");

        private static VariantSchema BuildRequirementVariants()
        {
            var cases = new System.Collections.Generic.Dictionary<string, IReadOnlyList<FieldSchema>>(System.StringComparer.Ordinal)
            {
                ["item_key"] = new[]
                {
                    // item.template 与本模块同属 Core.Carriers 程序集（同层），登记为 Reference。
                    new FieldSchema("item_id", FieldKind.Reference, required: true, referenceTable: "item.template",
                        description: "指向 item.template 的钥匙物品"),
                },
                ["world_flag"] = new[]
                {
                    // world.flag_schema 属 L4（core/gameplay/world_state），本模块（L3）不可
                    // Reference（依赖方向），退回 Id；expected 判断记录见本字段顶部注释——不在这里
                    // 登记，改由 GobjLockWorldFlagExpectedRule 校验。
                    new FieldSchema("flag_key", FieldKind.Id, required: true,
                        description: "指向 world.flag_schema 的世界标志键，退回 Id 登记（跨层不做引用完整性校验）"),
                },
                ["skill_check"] = new[]
                {
                    new FieldSchema("skill_tag", FieldKind.Id, required: true,
                        description: "标签而非表引用，按 Id 登记"),
                    new FieldSchema("min_value", FieldKind.Number, required: true,
                        description: "技能检定需要达到的最小值"),
                },
            };
            return new VariantSchema("kind", cases);
        }

        public static TableSchema Lock { get; } = new TableSchema(
            name: "gobj.lock",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "gobj.lock.<name>"),
                RequirementSchema,
                new FieldSchema("consume_key", FieldKind.Bool, required: false,
                    description: "item_key 时是否消耗钥匙，缺省 false"),
            }).WithOwnership(SchemaLayer.Carriers, "gameobject");
    }
}
