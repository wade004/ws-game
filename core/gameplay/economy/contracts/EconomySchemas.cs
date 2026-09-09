using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <c>econ.currency</c>/<c>econ.vendor</c> 的 <see cref="TableSchema"/> 声明（见 08 第 7.1、7.2
    /// 节字段表）。
    /// <para>
    /// ADR-0019 / F1b：<c>sell_items</c> 现登记为 <see cref="FieldKind.Array"/> 的
    /// <see cref="FieldSchema.Item"/>——元素 Object，子字段 <c>item_id</c>/<c>price_currency_id</c>/
    /// <c>price_amount</c>/<c>stock_limit</c>/<c>restock_policy</c>/<c>restock_timer</c>（见
    /// <see cref="VendorSellItem"/>）。判断记录（两个 Id 字段各自的登记选择）：
    /// <c>price_currency_id</c> 登记为 <see cref="FieldKind.Reference"/>（<c>ReferenceTable</c> =
    /// <see cref="Currency"/>）——单一目标表、同模块同层，<c>EconomyTestSupport.MakeRegistry</c>（测试）与
    /// <c>GameplaySchemaCatalog.RegisterEconomySchemas</c>（生产装配）均总是把 <c>econ.currency</c>
    /// 与 <c>econ.vendor</c> 登记进同一个 <see cref="DataRegistry"/> 实例并一起加载，<c>reference_integrity</c>
    /// 检查项因此能可靠工作，此前 <see cref="EconomyContentValidationRule"/> 手写的"核对
    /// <c>price_currency_id</c> 是否命中已加载的 <c>econ.currency</c> 记录"业务判断随之退役（见该
    /// 类型注释）。<c>item_id</c> 则退回 <see cref="FieldKind.Id"/>——虽然层次上 <c>item.template</c>
    /// （L3）在 <c>econ.vendor</c>（L4）之下、层次合法，但本模块自身的测试装配（<c>EconomyTestSupport.MakeRegistry</c>、
    /// <c>CR130_01_BuyFailureTransactionTests</c>）历来把
    /// <c>item.template</c> 登记进另一个独立的 <see cref="DataRegistry"/> 实例，与 <c>econ.vendor</c>
    /// 不在同一份加载结果里——若登记为 <c>Reference</c>，<c>reference_integrity</c> 会因"目标表在本
    /// registry 里从未加载"而对现有全部测试数据误报（<c>ReferenceExists</c> 对未加载目标表恒
    /// 返回不存在），因此退回 <c>Id</c>，存在性核对保留为 <see cref="EconomyContentValidationRule"/>
    /// 的业务判断，仿照 <c>core/gameplay/spawn.SpawnContentRefRule</c>"目标表已加载才检查"的宽松惯例
    /// （详见该类型注释）。<c>price_amount&gt;=0</c>、<c>stock_limit&gt;=0</c>、
    /// <c>restock_policy=timer</c> 时 <c>restock_timer</c> 必填且 &gt;0，均为登记表达不了的数值
    /// 范围/条件必填约束，保留为业务判断。
    /// </para>
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

        private static readonly FieldSchema SellItem = new FieldSchema(
            "<sell_item>", FieldKind.Object, required: true,
            description: "VendorSellItem",
            fields: new[]
            {
                new FieldSchema("item_id", FieldKind.Id, required: true,
                    description: "item.<template>；存在性核对见 EconomyContentValidationRule（跨 registry 装配判断记录见类型注释）"),
                new FieldSchema("price_currency_id", FieldKind.Reference, required: true,
                    referenceTable: "econ.currency"),
                new FieldSchema("price_amount", FieldKind.Int, required: true,
                    description: ">=0 是登记表达不了的数值范围约束，见 EconomyContentValidationRule"),
                new FieldSchema("stock_limit", FieldKind.Int, required: false,
                    description: "限量；缺省不限量；>=0 是登记表达不了的数值范围约束，见 EconomyContentValidationRule"),
                new FieldSchema("restock_policy", FieldKind.Enum, required: false,
                    enumValues: new[] { "on_map_enter", "timer" }),
                new FieldSchema("restock_timer", FieldKind.Number, required: false,
                    description: "restock_policy=timer 时必填且必须 >0，是登记表达不了的条件必填/范围约束，见 EconomyContentValidationRule"),
            });

        public static readonly TableSchema Vendor = new TableSchema(
            name: "econ.vendor",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "econ.vendor.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("sell_items", FieldKind.Array, required: true, item: SellItem,
                    description: "List<VendorSellItem>，子结构登记见本类型注释"),
                new FieldSchema("buy_price_rule", FieldKind.Expr, required: false,
                    description: "玩家出售物品给商人时的收购价规则；本版只支持返回数值的简单 Expr"),
                new FieldSchema("map_id", FieldKind.Id, required: false, description: "on_map_enter 补货用"),
            });
    }
}
