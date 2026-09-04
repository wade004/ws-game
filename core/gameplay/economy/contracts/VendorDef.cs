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

        public long PriceAmount { get; }

        /// <summary>限量；null 表示不限量（见 08 第 7.2 节 <c>stockLimit: Optional&lt;Int&gt;</c>）。</summary>
        public int? StockLimit { get; }

        public VendorRestockPolicy RestockPolicy { get; }

        /// <summary><see cref="VendorRestockPolicy.Timer"/> 下的补货周期（游戏内秒数）；其余策略下为
        /// null。</summary>
        public double? RestockTimer { get; }

        public VendorSellItem(Id itemId, Id priceCurrencyId, long priceAmount, int? stockLimit, VendorRestockPolicy restockPolicy, double? restockTimer)
        {
            ItemId = itemId;
            PriceCurrencyId = priceCurrencyId;
            PriceAmount = priceAmount;
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
