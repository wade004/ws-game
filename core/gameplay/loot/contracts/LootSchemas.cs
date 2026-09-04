using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// <c>loot.table</c> 的 <see cref="TableSchema"/> 声明（见 08 第 1.1 节字段表）。<c>groups</c>
    /// 内部结构（<see cref="LootGroup"/>/<see cref="LootEntry"/>）不是 <see cref="DataRegistry"/>
    /// 内置字段类型能表达的形状（嵌套数组套对象，且 <c>ref</c> 的引用目标表随其领域段变化，可能是
    /// <c>item.template</c> 也可能是同一张 <c>loot.table</c>），因此只声明为 <see
    /// cref="FieldKind.Array"/>（"存在且是数组"），深层结构、取值范围、成环检测均由本模块自己的
    /// <see cref="LootTableParser"/>（解析期）与 <see cref="LootContentValidationRule"/>（登记为
    /// <see cref="IValidationRule"/> 扩展点）负责，惯例同 <c>core/rules/skill</c> 对
    /// <c>skill.aura_def.effects</c> 的处理方式。
    /// </summary>
    public static class LootSchemas
    {
        public static readonly TableSchema Table = new TableSchema(
            name: "loot.table",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "loot.<name>"),
                new FieldSchema("groups", FieldKind.Array, required: true,
                    description: "List<LootGroup>，见 08 第 1.1 节，深层结构由 LootTableParser 解析"),
                new FieldSchema("guaranteed_min", FieldKind.Int, required: false,
                    description: "保底计数：本表整体至少掉落的条目数"),
            });
    }
}
