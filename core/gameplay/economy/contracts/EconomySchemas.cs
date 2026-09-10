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
    /// （详见该类型注释）。<c>restock_policy=timer</c> 时 <c>restock_timer</c> 必填且 &gt;0——"必填"
    /// 与"&gt;0"两者都以 <c>restock_policy</c> 取值为条件，登记表达不了条件必填/条件范围，保留为
    /// 业务判断。
    /// </para>
    /// <para>
    /// ADR-0021 补登（04 第 4 节勘误"范围约束"）：<c>price_amount&gt;=0</c>、<c>stock_limit&gt;=0</c>、
    /// <c>cap&gt;=0</c> 三处此前因 <see cref="FieldSchema"/> 尚无 Range 能力只能退回业务判断，现已用
    /// <see cref="FieldSchema.WithRange"/> 补登为加载期 <c>field_range</c> 阻断；与
    /// <see cref="EconomyContentValidationRule"/> 里同名判断并存的理由同 <c>LootSchemas</c> 类型注释
    /// 的判断记录（不是互斥选一个，字段级校验更早拦截，不影响既有 <c>Assert.Contains</c> 断言）。
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
                new FieldSchema("name_key", FieldKind.TextKey, required: true, description: "货币显示名的本地化文本键"),
                new FieldSchema("cap", FieldKind.Int, required: false, description: "上限，空表示无上限")
                    // 依据（ADR-0021）：EconomyHost.SetBalance/Add 文档明确按 "[0, cap]" 夹取余额
                    // （core/EconomyHost.cs :190 "夹取到 [0, cap]"），cap 为负会使该区间本身非法。
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("display_ref", FieldKind.Id, required: true, description: "货币图标/展示资源引用 id"),
            });

        private static readonly FieldSchema SellItem = new FieldSchema(
            "<sell_item>", FieldKind.Object, required: true,
            description: "VendorSellItem",
            fields: new[]
            {
                new FieldSchema("item_id", FieldKind.Id, required: true,
                    description: "item.<template>；存在性核对见 EconomyContentValidationRule（跨 registry 装配判断记录见类型注释）"),
                new FieldSchema("price_currency_id", FieldKind.Reference, required: true,
                    referenceTable: "econ.currency", description: "计价所用的货币"),
                new FieldSchema("price_amount", FieldKind.Int, required: true,
                    description: ">=0，见 EconomyContentValidationRule 同名判断")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("stock_limit", FieldKind.Int, required: false,
                    description: "限量；缺省不限量；>=0，见 EconomyContentValidationRule 同名判断")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("restock_policy", FieldKind.Enum, required: false,
                    enumValues: new[] { "on_map_enter", "timer" },
                    description: "补货时机：on_map_enter 进图时补货；timer 按 restock_timer 定时补货；缺省不补货"),
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
                new FieldSchema("name_key", FieldKind.TextKey, required: true, description: "商人显示名的本地化文本键"),
                new FieldSchema("sell_items", FieldKind.Array, required: true, item: SellItem,
                    description: "List<VendorSellItem>，子结构登记见本类型注释"),
                new FieldSchema("buy_price_rule", FieldKind.Expr, required: false,
                    description: "玩家出售物品给商人时的收购价规则；本版只支持返回数值的简单 Expr"),
                new FieldSchema("map_id", FieldKind.Id, required: false, description: "on_map_enter 补货用"),
            });
    }
}
