using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.Economy
{
    /// <summary>补货策略（见 08 第 7.2 节 <c>restockPolicy: Optional&lt;on_map_enter|timer&gt;</c>）。</summary>
    public enum VendorRestockPolicy
    {
        /// <summary>不自动补货（<c>stock_limit</c> 用尽后恒不可购买，除非另有脚本手工补货）。</summary>
        None,

        /// <summary>玩家进入 <see cref="VendorDef.MapId"/> 时补满（见 <c>IEconomyHost.OnMapEnter</c>）。</summary>
        OnMapEnter,

        /// <summary>按 <see cref="VendorSellItem.RestockTimer"/> 周期性补满（见
        /// <c>IEconomyHost.Update</c>）。</summary>
        Timer,
    }

    /// <summary>一条出售清单项（见 08 第 7.2 节 <c>sell_items</c> 元素结构）。</summary>
    public sealed class VendorSellItem
    {
        public Id ItemId { get; }

        public Id PriceCurrencyId { get; }

        /// <summary>手填买价；<see cref="HasPriceAmount"/> 为 <c>false</c>（<c>price_amount</c> 未在
        /// 数据里填写，分阶段落地计划 T-N4-6；ADR-0034 决策 2）时本属性恒为 0，不代表实际买价——
        /// 实际买价这时由 <c>EconomyHost.Buy</c> 按价格公式（<c>econ.value_curve</c> × 品质价格倍率 ×
        /// 槽位价格系数，<c>item.template.value_override</c> 优先）动态算出。旧构造函数（见下）恒把
        /// <see cref="HasPriceAmount"/> 置真，语义与 T-N4-6 之前完全一致，不受本次改动影响。</summary>
        public long PriceAmount { get; }

        /// <summary>T-N4-6（ADR-0034 决策 2；08 第 7.2 节修订段"<c>priceAmount</c> 改为可选"）：
        /// <c>price_amount</c> 是否在数据里显式填写。硬性规则 5（ABI 只允许新增）：本属性与下方新增
        /// 构造重载是本任务新增的唯一变更，旧构造函数与 <see cref="PriceAmount"/> 属性本身的类型/语义
        /// 不变。</summary>
        public bool HasPriceAmount { get; }

        /// <summary>限量；null 表示不限量（见 08 第 7.2 节 <c>stockLimit: Optional&lt;Int&gt;</c>）。</summary>
        public int? StockLimit { get; }

        public VendorRestockPolicy RestockPolicy { get; }

        /// <summary><see cref="VendorRestockPolicy.Timer"/> 下的补货周期（游戏内秒数）；其余策略下为
        /// null。</summary>
        public double? RestockTimer { get; }

        /// <summary>既有构造函数（T-N4-6 之前）：原样保留，<see cref="HasPriceAmount"/> 恒为
        /// <c>true</c>（旧调用方总是显式提供了 <paramref name="priceAmount"/>，语义与改动前逐位
        /// 相同）。</summary>
        public VendorSellItem(Id itemId, Id priceCurrencyId, long priceAmount, int? stockLimit, VendorRestockPolicy restockPolicy, double? restockTimer)
            : this(itemId, priceCurrencyId, priceAmount, hasPriceAmount: true, stockLimit, restockPolicy, restockTimer)
        {
        }

        /// <summary>T-N4-6 新增构造重载：显式指定 <paramref name="hasPriceAmount"/>，供
        /// <c>price_amount</c> 未填时构造（<paramref name="priceAmount"/> 传 0 占位，不参与任何
        /// 计算，见 <see cref="PriceAmount"/> 判断记录）。</summary>
        public VendorSellItem(Id itemId, Id priceCurrencyId, long priceAmount, bool hasPriceAmount, int? stockLimit, VendorRestockPolicy restockPolicy, double? restockTimer)
        {
            ItemId = itemId;
            PriceCurrencyId = priceCurrencyId;
            PriceAmount = priceAmount;
            HasPriceAmount = hasPriceAmount;
            StockLimit = stockLimit;
            RestockPolicy = restockPolicy;
            RestockTimer = restockTimer;
        }
    }

    /// <summary>一条 <c>econ.vendor</c> 记录的强类型视图（见 08 第 7.2 节字段表 + 任务书拍板补录
    /// <c>name_key</c>/<c>map_id</c>）。</summary>
    public sealed class VendorDef
    {
        public Id Id { get; }

        public Id NameKey { get; }

        public IReadOnlyList<VendorSellItem> SellItems { get; }

        /// <summary>收购价规则（见 08 第 7.2 节 <c>buy_price_rule: Optional&lt;Expr 或曲线引用&gt;</c>，
        /// 任务书拍板"本版只支持 Expr 返回数值的简单形式"）；为 null 时收购价按
        /// <see cref="EconomyOptions.DefaultBuyPricePct"/> 的默认公式计算，见
        /// <c>EconomyHost</c> 判断记录。</summary>
        public ExprNode? BuyPriceRule { get; }

        /// <summary><see cref="VendorRestockPolicy.OnMapEnter"/> 用（见 08 第 7.2 节字段表）；可空。</summary>
        public Id? MapId { get; }

        public VendorDef(Id id, Id nameKey, IReadOnlyList<VendorSellItem> sellItems, ExprNode? buyPriceRule, Id? mapId)
        {
            Id = id;
            NameKey = nameKey;
            SellItems = sellItems;
            BuyPriceRule = buyPriceRule;
            MapId = mapId;
        }
    }
}
