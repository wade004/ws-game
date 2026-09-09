using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.Economy;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// ADR-0019 / F1b：<see cref="EconomySchemas.Vendor"/> 的 <c>sell_items</c> 子结构登记（<c>Item</c>）+
    /// <see cref="EconomyContentValidationRule"/> 收窄后仍保留的业务判断，覆盖范围：
    /// <c>restock_policy</c> 枚举登记值与 <see cref="VendorRestockPolicy"/> 运行时枚举（去掉缺省态
    /// <c>None</c>，它不是判别字段的合法字面量）一致、子结构命中/坏形状各一例、<c>price_currency_id</c>
    /// 改用 <see cref="FieldKind.Reference"/> 后 <c>reference_integrity</c> 检查项自动覆盖此前的手写
    /// 货币存在性核对、<c>EconomyContentValidationRule</c> 收窄后仍报告的三类业务判断
    /// （<c>price_amount</c>、<c>stock_limit</c>、<c>restock_policy=timer</c> 时 <c>restock_timer</c>
    /// 条件必填）各一例。
    /// </summary>
    public sealed class EconomySchemaCoverageTests
    {
        private static IEventBus NewBus() => EconomyTestSupport.NewEventBus();

        private static string Envelope(string table, string rowsJson) => EconomyTestSupport.Envelope(table, rowsJson);

        [Fact]
        public void RestockPolicyEnumValues_MatchRuntimeEnum()
        {
            var sellItemField = EconomySchemas.Vendor.GetField("sell_items")!;
            var restockPolicyField = sellItemField.Item!.Fields!.Single(f => f.Name == "restock_policy");

            var registered = restockPolicyField.EnumValues!.ToHashSet();
            var runtime = new[] { "on_map_enter", "timer" }.ToHashSet();
            Assert.Equal(runtime, registered);

            // VendorRestockPolicy 运行时多一个 None（缺省态，不是字符串字面量，见 EconomyDataParser.ParseSellItem）。
            Assert.Equal(3, System.Enum.GetValues(typeof(VendorRestockPolicy)).Length);
        }

        private static DataRegistry MakeRegistryRaw(string currencyRows, string vendorRows, bool registerRule = true)
        {
            var bus = NewBus();
            var source = new InMemoryDataSource()
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, currencyRows))
                .Add(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, vendorRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            if (registerRule)
            {
                registry.RegisterValidationRule(new EconomyContentValidationRule());
            }
            return registry;
        }

        [Fact]
        public void WellFormedSellItems_LoadsWithoutErrors()
        {
            var currency = "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 10, \"stock_limit\": 3, \"restock_policy\": \"timer\", \"restock_timer\": 30}]}]";

            var registry = MakeRegistryRaw(currency, vendor);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void MissingPriceAmount_ReportsRequiredFieldWithNestedPath()
        {
            var currency = "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\"}]}]";

            var registry = MakeRegistryRaw(currency, vendor);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues,
                i => i.Check == "required_field" && i.Field == "sell_items[0].price_amount");
            Assert.DoesNotContain(report.Issues, i => i.Check == "economy_content");
        }

        [Fact]
        public void InvalidRestockPolicyValue_ReportsFieldTypeError()
        {
            var currency = "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 10, \"restock_policy\": \"not_a_policy\"}]}]";

            var registry = MakeRegistryRaw(currency, vendor);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues,
                i => i.Check == "field_type" && i.Field == "sell_items[0].restock_policy");
        }

        [Fact]
        public void PriceCurrencyId_UnregisteredCurrency_ReportsReferenceIntegrityError()
        {
            var currency = "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.does_not_exist\", " +
                "\"price_amount\": 10}]}]";

            var registry = MakeRegistryRaw(currency, vendor);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues,
                i => i.Check == "reference_integrity" && i.Field == "sell_items[0].price_currency_id");

            // 手写的货币存在性核对已整条退役（改由 reference_integrity 覆盖），不应双报。
            Assert.DoesNotContain(report.Issues, i => i.Check == "economy_content");
        }

        [Fact]
        public void NegativePriceAmount_ReportsEconomyContentBusinessError()
        {
            var currency = "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": -1}]}]";

            var registry = MakeRegistryRaw(currency, vendor);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues,
                i => i.Check == "economy_content" && i.Message.Contains("price_amount"));
        }

        [Fact]
        public void NegativeStockLimit_ReportsEconomyContentBusinessError()
        {
            var currency = "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 10, \"stock_limit\": -1}]}]";

            var registry = MakeRegistryRaw(currency, vendor);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues,
                i => i.Check == "economy_content" && i.Message.Contains("stock_limit"));
        }

        [Fact]
        public void RestockPolicyTimer_MissingRestockTimer_ReportsEconomyContentBusinessError()
        {
            var currency = "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 10, \"restock_policy\": \"timer\"}]}]";

            var registry = MakeRegistryRaw(currency, vendor);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues,
                i => i.Check == "economy_content" && i.Message.Contains("restock_timer"));
        }
    }
}
