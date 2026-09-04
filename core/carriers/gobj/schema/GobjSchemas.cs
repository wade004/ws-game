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
                new FieldSchema("type_data", FieldKind.Object, required: true,
                    description: "按 kind 解释，见 schema/README.md 类型数据表；字段组完整性见 GobjTypeDataFieldGroupRule"),
                new FieldSchema("lock_id", FieldKind.Reference, required: false, referenceTable: "gobj.lock",
                    description: "当前锁（可空），指向 gobj.lock"),
                new FieldSchema("on_use", FieldKind.Object, required: false,
                    description: "{kind: skill|dialog, ref: Id}，二者二选一，见 GobjOnUseKindRule"),
                new FieldSchema("display_ref", FieldKind.Id, required: true, description: "指向 display.map"),
                new FieldSchema("tags", FieldKind.IdList, required: false, description: "标签集合"),
            });

        public static TableSchema Lock { get; } = new TableSchema(
            name: "gobj.lock",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "gobj.lock.<name>"),
                new FieldSchema("requirement", FieldKind.Object, required: true,
                    description: "{kind: item_key|world_flag|skill_check, ...}，见 GobjLockRequirementFieldGroupRule"),
                new FieldSchema("consume_key", FieldKind.Bool, required: false,
                    description: "item_key 时是否消耗钥匙，缺省 false"),
            });
    }
}
