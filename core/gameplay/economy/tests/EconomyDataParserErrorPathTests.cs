using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Economy;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// T-M14（core 半，测试覆盖剩余项 2026-10-01）：<see cref="EconomyDataParser"/> 的错误路径——货币缺必填字段、
    /// 商人 <c>sell_items</c> 元素形状/字段不合法、<c>buy_price_rule</c> 解析失败、<c>restock_policy</c> 取值，
    /// 以及本批发现的 <c>stock_limit</c> 超 32 位范围静默回绕（README 判断记录 17）。
    /// </summary>
    public class EconomyDataParserErrorPathTests
    {
        private static DataRecord Vendor(string sellItems, string extra = "")
        {
            var json = "{\"id\":\"econ.vendor.ep_sample\",\"name_key\":\"l10n.econ.vendor.ep_sample.name\",\"sell_items\":" + sellItems + extra + "}";
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(EconomySchemas.Vendor, "econ.vendor.ep_sample", new Id("econ.vendor.ep_sample"), raw);
        }

        private static DataRecord Currency(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(EconomySchemas.Currency, "econ.currency.ep_gold", new Id("econ.currency.ep_gold"), raw);
        }

        private static string Item(string extra = "", string itemId = "\"item.ep_ore\"", string currency = "\"econ.currency.ep_gold\"") =>
            "{\"item_id\":" + itemId + ",\"price_currency_id\":" + currency + extra + "}";

        private static DataFieldException FailVendor(string sellItems, string extra = "") =>
            Assert.Throws<DataFieldException>(() => EconomyDataParser.ParseVendor(Vendor(sellItems, extra)));

        // ------------------------------ ParseCurrency ------------------------------

        [Fact]
        public void Currency_ValidBaseline_ParsesWithOptionalCap()
        {
            var uncapped = EconomyDataParser.ParseCurrency(Currency(
                "{\"id\":\"econ.currency.ep_gold\",\"name_key\":\"l10n.gold\",\"display_ref\":\"display.gold\"}"));
            var capped = EconomyDataParser.ParseCurrency(Currency(
                "{\"id\":\"econ.currency.ep_gold\",\"name_key\":\"l10n.gold\",\"display_ref\":\"display.gold\",\"cap\":500}"));

            Assert.Null(uncapped.Cap);
            Assert.Equal(500L, capped.Cap);
        }

        [Theory]
        [InlineData("{\"id\":\"econ.currency.ep_gold\",\"display_ref\":\"display.gold\"}", "name_key")]
        [InlineData("{\"id\":\"econ.currency.ep_gold\",\"name_key\":\"l10n.gold\"}", "display_ref")]
        [InlineData("{\"id\":\"econ.currency.ep_gold\",\"name_key\":\"bad id\",\"display_ref\":\"display.gold\"}", "name_key")]
        public void Currency_MissingOrMalformedRequiredId_ThrowsNamingTheField(string json, string field)
        {
            var ex = Assert.Throws<DataFieldException>(() => EconomyDataParser.ParseCurrency(Currency(json)));

            Assert.Equal(field, ex.Field);
        }

        // ------------------------------ ParseVendor ------------------------------

        [Fact]
        public void Vendor_ValidBaseline_ParsesPriceStockAndRestock()
        {
            var vendor = EconomyDataParser.ParseVendor(Vendor(
                "[" + Item(",\"price_amount\":12,\"stock_limit\":4,\"restock_policy\":\"timer\",\"restock_timer\":30") + "]"));

            var item = Assert.Single(vendor.SellItems);
            Assert.True(item.HasPriceAmount);
            Assert.Equal(12L, item.PriceAmount);
            Assert.Equal(4, item.StockLimit);
            Assert.Equal(VendorRestockPolicy.Timer, item.RestockPolicy);
            Assert.Equal(30.0, item.RestockTimer);
        }

        [Fact]
        public void Vendor_MissingRequiredTopLevelFields_ThrowNamingTheField()
        {
            var noSell = new DataRecord(EconomySchemas.Vendor, "econ.vendor.ep_sample", new Id("econ.vendor.ep_sample"),
                (JsonObject)JsonReader.Parse("{\"id\":\"econ.vendor.ep_sample\",\"name_key\":\"l10n.v\"}"));
            var noName = new DataRecord(EconomySchemas.Vendor, "econ.vendor.ep_sample", new Id("econ.vendor.ep_sample"),
                (JsonObject)JsonReader.Parse("{\"id\":\"econ.vendor.ep_sample\",\"sell_items\":[]}"));

            Assert.Equal("sell_items", Assert.Throws<DataFieldException>(() => EconomyDataParser.ParseVendor(noSell)).Field);
            Assert.Equal("name_key", Assert.Throws<DataFieldException>(() => EconomyDataParser.ParseVendor(noName)).Field);
        }

        [Theory]
        [InlineData("[\"not an object\"]", "第 0 项不是对象")]
        [InlineData("[{\"price_currency_id\":\"econ.currency.ep_gold\"}]", "item_id")]
        [InlineData("[{\"item_id\":\"bad id\",\"price_currency_id\":\"econ.currency.ep_gold\"}]", "item_id")]
        [InlineData("[{\"item_id\":\"item.ep_ore\"}]", "price_currency_id")]
        [InlineData("[{\"item_id\":\"item.ep_ore\",\"price_currency_id\":3}]", "price_currency_id")]
        public void Vendor_BadSellItemShape_ThrowsOnSellItems_NamingTheProblem(string sellItems, string expectedInMessage)
        {
            var ex = FailVendor(sellItems);

            Assert.Equal("sell_items", ex.Field);
            Assert.Contains(expectedInMessage, ex.Message);
        }

        [Fact]
        public void Vendor_ErrorInLaterSellItem_NamesThatIndex()
        {
            var ex = FailVendor("[" + Item() + "," + Item() + "," + Item(itemId: "\"bad id\"") + "]");

            Assert.Contains("第 2 项", ex.Message);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("2.5")]
        [InlineData("\"ten\"")]
        [InlineData("null")]
        public void Vendor_PriceAmountPresentButNotNonNegativeInteger_Throws(string amount)
        {
            var ex = FailVendor("[" + Item(",\"price_amount\":" + amount) + "]");

            Assert.Contains("price_amount", ex.Message);
        }

        [Fact]
        public void Vendor_PriceAmountAbsent_IsAllowed_AndMarkedAsNotProvided()
        {
            var vendor = EconomyDataParser.ParseVendor(Vendor("[" + Item() + "]"));

            Assert.False(vendor.SellItems[0].HasPriceAmount);
            Assert.Equal(0L, vendor.SellItems[0].PriceAmount);
        }

        [Fact]
        public void Vendor_NegativeStockLimit_Throws()
        {
            Assert.Contains("stock_limit", FailVendor("[" + Item(",\"stock_limit\":-1") + "]").Message);
        }

        /// <summary>T-M14 发现的缺陷复现：旧实现 <c>(int)stockLong</c> 静默回绕，4294967297 变成 1。</summary>
        [Theory]
        [InlineData(4294967297L)]
        [InlineData(2147483648L)]
        public void Vendor_StockLimitOutsideInt32_Throws_InsteadOfWrappingAround(long stock)
        {
            var ex = FailVendor("[" + Item(",\"stock_limit\":" + stock.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "]");

            Assert.Equal("sell_items", ex.Field);
            Assert.Contains("stock_limit", ex.Message);
        }

        [Fact]
        public void Vendor_StockLimitAtInt32Max_IsAccepted_AndNonNumericIsTreatedAsUnlimited()
        {
            var atMax = EconomyDataParser.ParseVendor(Vendor(
                "[" + Item(",\"stock_limit\":" + int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "]"));
            var text = EconomyDataParser.ParseVendor(Vendor("[" + Item(",\"stock_limit\":\"lots\"") + "]"));

            Assert.Equal(int.MaxValue, atMax.SellItems[0].StockLimit);
            Assert.Null(text.SellItems[0].StockLimit);
        }

        [Theory]
        [InlineData("\"daily\"")]
        [InlineData("\"ON_MAP_ENTER\"")]
        [InlineData("\"\"")]
        public void Vendor_UnknownRestockPolicy_Throws_NamingTheValue(string policy)
        {
            var ex = FailVendor("[" + Item(",\"restock_policy\":" + policy) + "]");

            Assert.Contains("restock_policy", ex.Message);
            Assert.Contains("on_map_enter", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData(",\"restock_timer\":0")]
        [InlineData(",\"restock_timer\":-5")]
        [InlineData(",\"restock_timer\":\"soon\"")]
        public void Vendor_TimerPolicyWithoutPositiveTimer_Throws(string timer)
        {
            var ex = FailVendor("[" + Item(",\"restock_policy\":\"timer\"" + timer) + "]");

            Assert.Contains("restock_timer", ex.Message);
        }

        [Fact]
        public void Vendor_OnMapEnterPolicy_NeedsNoTimer()
        {
            var vendor = EconomyDataParser.ParseVendor(Vendor("[" + Item(",\"restock_policy\":\"on_map_enter\"") + "]"));

            Assert.Equal(VendorRestockPolicy.OnMapEnter, vendor.SellItems[0].RestockPolicy);
            Assert.Null(vendor.SellItems[0].RestockTimer);
        }

        [Fact]
        public void Vendor_UnparsableBuyPriceRule_ThrowsOnBuyPriceRule()
        {
            var ex = FailVendor("[" + Item() + "]", ",\"buy_price_rule\":\"((( not an expression\"");

            Assert.Equal("buy_price_rule", ex.Field);
            Assert.Contains("解析失败", ex.Message);
        }

        [Fact]
        public void Vendor_EmptyOrAbsentBuyPriceRule_MeansNoRule()
        {
            var absent = EconomyDataParser.ParseVendor(Vendor("[" + Item() + "]"));
            var empty = EconomyDataParser.ParseVendor(Vendor("[" + Item() + "]", ",\"buy_price_rule\":\"\""));

            Assert.Null(absent.BuyPriceRule);
            Assert.Null(empty.BuyPriceRule);
        }

        [Fact]
        public void Vendor_ErrorCarriesTableAndRecordKey()
        {
            var ex = FailVendor("[3]");

            Assert.Equal("econ.vendor", ex.Table);
            Assert.Equal("econ.vendor.ep_sample", ex.RecordKey);
        }
    }
}
