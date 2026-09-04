using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <c>econ.currency</c>/<c>econ.vendor</c> 记录 → 强类型的唯一解析入口（惯例同
    /// <c>core/gameplay/loot.LootTableParser</c>"运行期与校验期共用同一份解析逻辑"）。<c>buy_price_rule</c>
    /// 默认用 <c>core/rules/expr_host.RulesExprSchema.Base</c> 解析（见
    /// <c>EconomyHost</c> 判断记录"<c>self.item_level</c>/<c>self.quality</c> 的契约缺口"）；调用方可传入
    /// <paramref name="exprSchema"/>覆盖，惯例同 <c>LootTableParser.Parse</c> 判断记录。
    /// </summary>
    public static class EconomyDataParser
    {
        public static CurrencyDef ParseCurrency(DataRecord record)
        {
            var id = record.GetId("id");
            var nameKey = record.GetId("name_key");
            long? cap = record.TryGetInt("cap", out var capValue) ? capValue : (long?)null;
            var displayRef = record.GetId("display_ref");
            return new CurrencyDef(id, nameKey, cap, displayRef);
        }

        public static VendorDef ParseVendor(DataRecord record, IExprSchema? exprSchema = null)
        {
            var id = record.GetId("id");
            var nameKey = record.GetId("name_key");
            var sellItemsArray = record.GetArray("sell_items");

            var sellItems = new List<VendorSellItem>(sellItemsArray.Count);
            for (var i = 0; i < sellItemsArray.Count; i++)
            {
                if (!(sellItemsArray[i] is JsonObject itemObj))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "sell_items", $"第 {i} 项不是对象");
                }

                sellItems.Add(ParseSellItem(record, i, itemObj));
            }

            ExprNode? buyPriceRule = null;
            if (record.TryGetString("buy_price_rule", out var ruleText) && !string.IsNullOrEmpty(ruleText))
            {
                try
                {
                    buyPriceRule = ExprParser.Parse(ruleText, exprSchema ?? Core.Rules.ExprHost.RulesExprSchema.Base);
                }
                catch (ExprParseException ex)
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "buy_price_rule", $"解析失败：{ex.Message}");
                }
            }

            Id? mapId = record.TryGetId("map_id", out var mid) ? (Id?)mid : null;

            return new VendorDef(id, nameKey, sellItems, buyPriceRule, mapId);
        }

        private static VendorSellItem ParseSellItem(DataRecord record, int index, JsonObject itemObj)
        {
            if (!itemObj.TryGetValue("item_id", out var itemIdVal) || !(itemIdVal is JsonString itemIdStr) || !Id.TryParse(itemIdStr.Value, out var itemId))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "sell_items", $"第 {index} 项缺少合法的 item_id");
            }

            if (!itemObj.TryGetValue("price_currency_id", out var curVal) || !(curVal is JsonString curStr) || !Id.TryParse(curStr.Value, out var currencyId))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "sell_items", $"第 {index} 项缺少合法的 price_currency_id");
            }

            if (!itemObj.TryGetValue("price_amount", out var amtVal) || !(amtVal is JsonNumber amtNum) || !amtNum.TryGetInt64(out var priceAmount) || priceAmount < 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "sell_items", $"第 {index} 项缺少合法的非负整数 price_amount");
            }

            int? stockLimit = null;
            if (itemObj.TryGetValue("stock_limit", out var stockVal) && stockVal is JsonNumber stockNum && stockNum.TryGetInt64(out var stockLong))
            {
                if (stockLong < 0)
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "sell_items", $"第 {index} 项 stock_limit 不能为负数");
                }

                stockLimit = (int)stockLong;
            }

            var restockPolicy = VendorRestockPolicy.None;
            if (itemObj.TryGetValue("restock_policy", out var policyVal) && policyVal is JsonString policyStr)
            {
                switch (policyStr.Value)
                {
                    case "on_map_enter": restockPolicy = VendorRestockPolicy.OnMapEnter; break;
                    case "timer": restockPolicy = VendorRestockPolicy.Timer; break;
                    default:
                        throw new DataFieldException(record.Table.Name, record.Key, "sell_items",
                            $"第 {index} 项 restock_policy 值 \"{policyStr.Value}\" 不合法（只能是 on_map_enter 或 timer）");
                }
            }

            double? restockTimer = null;
            if (itemObj.TryGetValue("restock_timer", out var timerVal) && timerVal is JsonNumber timerNum)
            {
                restockTimer = timerNum.Value;
            }

            if (restockPolicy == VendorRestockPolicy.Timer && (!restockTimer.HasValue || restockTimer.Value <= 0))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "sell_items",
                    $"第 {index} 项 restock_policy=timer 时必须提供正数 restock_timer");
            }

            return new VendorSellItem(itemId, currencyId, priceAmount, stockLimit, restockPolicy, restockTimer);
        }
    }
}
