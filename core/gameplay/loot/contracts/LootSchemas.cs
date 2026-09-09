using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// <c>loot.table</c> 的 <see cref="TableSchema"/> 声明（见 08 第 1.1 节字段表）。
    /// <para>
    /// ADR-0019 / F1b：<c>groups</c> 的深层结构（<see cref="LootGroup"/>/<see cref="LootEntry"/>）
    /// 现登记为 <see cref="FieldKind.Array"/> 的 <see cref="FieldSchema.Item"/>——
    /// <c>groups[]</c> 是 Object，子字段 <c>roll_mode</c>（<see cref="FieldKind.Enum"/>，取值
    /// <c>chance_each</c>/<c>weighted_pick_one</c>，对应 <see cref="LootRollMode"/>）、
    /// <c>pick_count</c>（<see cref="FieldKind.Int"/>，可选）、<c>entries</c>（<see
    /// cref="FieldKind.Array"/>，元素 Object，子字段 <c>ref</c>/<c>weight_or_chance</c>/
    /// <c>condition</c>/<c>count_range</c>）。判断记录（<c>ref</c> 退回 <see cref="FieldKind.Id"/>
    /// 而非 <see cref="FieldKind.Reference"/>）：<c>ref</c> 的引用目标表随其 <c>Id.Domain</c>
    /// 变化——<c>item</c> 域指向 <c>item.template</c>（L3，本模块 L4 的下层，层次合法），<c>loot</c>
    /// 域指向同一张 <c>loot.table</c>（同层自引用）；<see cref="FieldSchema"/> 的 <c>Reference</c>
    /// 只能声明单一目标表/域，表达不了"按值动态切换目标表"，因此退回 <c>Id</c>，域校验 +
    /// 存在性校验保留为 <see cref="LootContentValidationRule"/> 的手写业务判断（惯例同
    /// <c>core/gameplay/spawn.SpawnContentRefRule</c> 对 <c>spawn.table.content_ref</c> 的处理）。
    /// <c>count_range</c> 登记为带 <c>Fields</c> 的 Object（<c>min</c>/<c>max</c> 均
    /// <see cref="FieldKind.Int"/> 必填）；<c>min&lt;=max</c>、<c>min&gt;=1</c> 属于登记表达不了的
    /// 数值范围约束，同样保留为业务判断。<c>weight_or_chance</c> 的合法区间随所属分组的
    /// <c>roll_mode</c>（同一 <c>entries</c> 元素登记里看不到父级 <c>roll_mode</c> 取值）变化，
    /// 同理保留为业务判断。<c>pick_count&gt;=1</c>、<c>guaranteed_min&gt;=0</c> 同属数值范围约束，
    /// 保留为业务判断。嵌套 <c>loot.*</c> 引用成环检测（DFS）是跨记录判断，登记无法表达，保留。
    /// 详见 README"子结构登记表（ADR-0019 / F1b）"一节、<see cref="LootContentValidationRule"/>
    /// 类型注释"退役说明"。
    /// </para>
    /// </summary>
    public static class LootSchemas
    {
        private static readonly FieldSchema CountRange = new FieldSchema(
            "count_range", FieldKind.Object, required: true,
            description: "{min: Int, max: Int}；1<=min<=max 是登记表达不了的数值范围约束，见 LootContentValidationRule",
            fields: new[]
            {
                new FieldSchema("min", FieldKind.Int, required: true),
                new FieldSchema("max", FieldKind.Int, required: true),
            });

        private static readonly FieldSchema EntryItem = new FieldSchema(
            "<entry>", FieldKind.Object, required: true,
            description: "LootEntry：ref 退回 Id（跨域引用，见类型注释），weight_or_chance 的区间随父级 roll_mode 变化",
            fields: new[]
            {
                new FieldSchema("ref", FieldKind.Id, required: true,
                    description: "item.<template> 或 loot.<table>；域名+存在性校验见 LootContentValidationRule"),
                new FieldSchema("weight_or_chance", FieldKind.Number, required: true,
                    description: "chance_each: [0,1] 概率；weighted_pick_one: >=0 相对权重（区间校验见 LootContentValidationRule）"),
                new FieldSchema("condition", FieldKind.Expr, required: false,
                    description: "可选前置条件；缺省/未提供表示恒真"),
                CountRange,
            });

        private static readonly FieldSchema GroupItem = new FieldSchema(
            "<group>", FieldKind.Object, required: true,
            description: "LootGroup",
            fields: new[]
            {
                new FieldSchema("roll_mode", FieldKind.Enum, required: true,
                    enumValues: new[] { "chance_each", "weighted_pick_one" }),
                new FieldSchema("pick_count", FieldKind.Int, required: false,
                    description: "weighted_pick_one 下可选多次抽取；>=1 是登记表达不了的数值范围约束，见 LootContentValidationRule"),
                new FieldSchema("entries", FieldKind.Array, required: true, item: EntryItem),
            });

        public static readonly TableSchema Table = new TableSchema(
            name: "loot.table",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "loot.<name>"),
                new FieldSchema("groups", FieldKind.Array, required: true, item: GroupItem,
                    description: "List<LootGroup>，见 08 第 1.1 节，子结构登记见本类型注释"),
                new FieldSchema("guaranteed_min", FieldKind.Int, required: false,
                    description: "保底计数：本表整体至少掉落的条目数；>=0 是登记表达不了的数值范围约束，见 LootContentValidationRule"),
            });
    }
}
