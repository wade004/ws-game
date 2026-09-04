using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <c>econ.currency</c>/<c>econ.vendor</c> 的 <see cref="TableSchema"/> 声明（见 08 第 7.1、7.2
    /// 节字段表）。<c>sell_items</c> 是嵌套数组套对象，同 <c>core/gameplay/loot.LootSchemas</c> 判断
    /// 记录，只声明为 <see cref="FieldKind.Array"/>，深层结构由 <see cref="EconomyDataParser"/>（运行期
    /// 与校验期共用）负责；<c>item_id</c>/<c>price_currency_id</c> 因此也不在此声明为顶层
    /// <see cref="FieldKind.Reference"/>——它们嵌在数组元素里，<see cref="EconomyContentValidationRule"/>
    /// 自行核对 <c>price_currency_id</c> 是否命中已加载的 <c>econ.currency</c> 记录。
    /// </summary>
    public static class EconomySchemas
    {
        public static readonly TableSchema Currency = new TableSchema(
            name: "econ.currency",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "econ.currency.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("cap", FieldKind.Int, required: false, description: "上限，空表示无上限"),
                new FieldSchema("display_ref", FieldKind.Id, required: true),
            });

        public static readonly TableSchema Vendor = new TableSchema(
            name: "econ.vendor",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "econ.vendor.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("sell_items", FieldKind.Array, required: true,
                    description: "List<{item_id, price_currency_id, price_amount, stock_limit?, restock_policy?, restock_timer?}>"),
                new FieldSchema("buy_price_rule", FieldKind.Expr, required: false,
                    description: "玩家出售物品给商人时的收购价规则；本版只支持返回数值的简单 Expr"),
                new FieldSchema("map_id", FieldKind.Id, required: false, description: "on_map_enter 补货用"),
            });
    }
}
