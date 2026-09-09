using Core.Foundation.DataRegistry;

namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// <c>world.flag_schema</c> 的 <see cref="TableSchema"/>（见 01 第 L4 模块表 <c>world_state</c>
    /// 行"主要数据表：world.flag_schema"、04 第 1.1 节表清单该行"世界状态命名空间与标志的合法取值
    /// 说明（非运行态数据，仅作文档化 schema）"）。本表不参与运行期加载（本模块不实现任何
    /// <see cref="IValidationRule"/>，见 README 判断记录"该表只文档化"），只声明结构，供内容作者
    /// 按命名空间登记标志含义、供 <see cref="WorldStateOptions.SchemaEntries"/>（运行期可选校验）
    /// 由游戏组装根从本表加载结果转换而来。
    /// </summary>
    public static class WorldStateSchemas
    {
        private static readonly string[] FlagKindValues = { "bool", "int", "number", "string", "id" };

        /// <summary>世界状态命名空间/标志的合法取值说明（见 04 第 1.1 节表清单）。</summary>
        public static readonly TableSchema FlagSchema = new TableSchema(
            name: "world.flag_schema",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "world.<命名空间...>：登记的是标志的命名空间前缀或具体标志键（04 第 2.4 节、05 第 8.2 节 flagKey 命名空间格式，如 world.gobj 覆盖整个物件状态命名空间，或 world.bridge.repaired 精确到单个标志）"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: FlagKindValues,
                    description: "该前缀/标志键下取值的 Expr 标量类型，对应 IWorldState.Set 的 ExprValueKind（10 第 2.3 节 world_state_flags 字段表原文只列 Bool|Int，本模块拍板放宽到 Expr 全部五种标量并向后兼容，见 README 判断记录）"),
                new FieldSchema("description", FieldKind.String, required: true,
                    description: "该命名空间/标志的含义说明（本表非运行态数据，纯供内容作者与人工评审参考）"),
                // ADR-0019 / F1b 判断记录：allowed_values 的元素理论上应是与同记录 kind 取值一致的
                // JSON 标量（bool/int/number/string/id 之一），但判别字段 kind 是 allowed_values 的
                // 父级同层字段而非数组元素内部字段——FieldSchema.Item 只能声明单一 FieldKind，
                // VariantSchema 的判别字段又必须与其所属 Object 同层（见 ValidateVariantObject 实现），
                // 两者都表达不了"元素类型跟随父级同层字段动态变化"。且 core/gameplay/world_state.WorldState
                // 运行期从未读取本字段（只读 kind 做类型匹配，见该类型 ValidateAgainstSchema；本表本身
                // 也不参与运行期加载，见类型注释"仅作文档化 schema"）——没有运行时解析代码可作登记依据
                // （04 第 3.2 节"以运行时解析代码为唯一依据"）。保持登记前行为不变（Item 不设，只检查
                // "存在且是数组"），详见 schema/README.md"子结构登记表"一节判断记录。
                new FieldSchema("allowed_values", FieldKind.Array, required: false,
                    description: "可选：该标志允许的取值枚举，元素应与同记录 kind 取值同型（主要用于收窄 int/string/id 类型的合法取值范围；缺省表示不限制，本模块不对其做任何运行期强制，元素子结构未登记，见类型注释判断记录）"),
            });
    }
}
