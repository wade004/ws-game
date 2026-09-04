using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.Economy;
using Core.Rules.ExprHost;
using Tests.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Economy
{
    public class EconomyHostTests
    {
        private const string OneCurrencyRow =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.currency.sample.name\", " +
            "\"cap\": 1000, \"display_ref\": \"display.sample_coin\"}]";

        private static EconomyHost NewHost(
            string currencyRowsJson,
            string vendorRowsJson,
            out FakeInventoryHost inventory,
            out Core.Foundation.EventBus.IEventBus bus,
            Core.Rules.Common.IExprHostFactory? exprHostFactory = null)
        {
            bus = EconomyTestSupport.NewEventBus();
            var registry = EconomyTestSupport.MakeRegistry(bus, currencyRowsJson, vendorRowsJson);
            inventory = new FakeInventoryHost();
            return new EconomyHost(registry, bus, inventory, exprHostFactory ?? new FakeNumericExprHostFactory());
        }

        [Fact]
        public void Add_ClampsToCapAndPublishesEvent()
        {
            var host = NewHost(OneCurrencyRow, "[]", out _, out var bus);
            CurrencyChangedEvent? received = null;
            bus.Subscribe<CurrencyChangedEvent>(EconomyEventKeys.CurrencyChanged, e => received = e);

            var unitId = new Id("player.sample_1");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 5000, sourceId: unitId);
            bus.DispatchPending();

            Assert.Equal(1000, host.GetBalance(unitId, new Id("econ.currency.sample_coin")));
            Assert.NotNull(received);
            Assert.Equal(0, received!.OldValue);
            Assert.Equal(1000, received.NewValue);
        }

        [Fact]
        public void Add_ClampsToZero_WhenDeductionExceedsBalance()
        {
            var host = NewHost(OneCurrencyRow, "[]", out _, out _);
            var unitId = new Id("player.sample_1");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 10, sourceId: unitId);

            host.Add(unitId, new Id("econ.currency.sample_coin"), -999, sourceId: unitId);

            Assert.Equal(0, host.GetBalance(unitId, new Id("econ.currency.sample_coin")));
        }

        [Fact]
        public void TryPay_InsufficientBalance_Fails()
        {
            var host = NewHost(OneCurrencyRow, "[]", out _, out _);
            var unitId = new Id("player.sample_1");

            var ok = host.TryPay(unitId, new Id("econ.currency.sample_coin"), 10);

            Assert.False(ok);
            Assert.Equal(0, host.GetBalance(unitId, new Id("econ.currency.sample_coin")));
        }

        [Fact]
        public void TryPay_SufficientBalance_DeductsAndSucceeds()
        {
            var host = NewHost(OneCurrencyRow, "[]", out _, out _);
            var unitId = new Id("player.sample_1");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 50, sourceId: unitId);

            var ok = host.TryPay(unitId, new Id("econ.currency.sample_coin"), 30);

            Assert.True(ok);
            Assert.Equal(20, host.GetBalance(unitId, new Id("econ.currency.sample_coin")));
        }

        private const string ShopVendorRow =
            "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.vendor.sample.name\", \"sell_items\": [" +
            "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", \"price_amount\": 10, \"stock_limit\": 3}" +
            "]}]";

        [Fact]
        public void Buy_Success_DeductsBalanceAddsItemAndPublishesEvent()
        {
            var host = NewHost(OneCurrencyRow, ShopVendorRow, out var inventory, out var bus);
            ItemPurchasedEvent? received = null;
            bus.Subscribe<ItemPurchasedEvent>(EconomyEventKeys.ItemPurchased, e => received = e);

            var unitId = new Id("player.sample_1");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 100, sourceId: unitId);

            var result = host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 2);
            bus.DispatchPending();

            Assert.True(result.Success);
            Assert.Equal(20, result.Price);
            Assert.Equal(80, host.GetBalance(unitId, new Id("econ.currency.sample_coin")));
            Assert.Equal(2, inventory.CountOf(unitId, new Id("item.sample_sword")));
            Assert.Equal(1, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));
            Assert.NotNull(received);
            Assert.Equal(2, received!.Count);
            Assert.Equal(20, received.Price);
        }

        [Fact]
        public void Buy_ExceedsLimitedStock_Fails()
        {
            var host = NewHost(OneCurrencyRow, ShopVendorRow, out _, out _);
            var unitId = new Id("player.sample_1");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 1000, sourceId: unitId);

            var result = host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 4);

            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.InsufficientStock, result.Reason);
        }

        [Fact]
        public void Buy_InsufficientFunds_Fails()
        {
            var host = NewHost(OneCurrencyRow, ShopVendorRow, out _, out _);
            var unitId = new Id("player.sample_1");

            var result = host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 1);

            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.InsufficientFunds, result.Reason);
        }

        [Fact]
        public void Buy_InventoryFull_RollsBackWithoutChargingOrConsumingStock()
        {
            var host = NewHost(OneCurrencyRow, ShopVendorRow, out var inventory, out _);
            inventory.MaxTotalItems = 0;
            var unitId = new Id("player.sample_1");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 100, sourceId: unitId);

            var result = host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 1);

            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.InventoryFull, result.Reason);
            Assert.Equal(100, host.GetBalance(unitId, new Id("econ.currency.sample_coin")));
            Assert.Equal(3, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));
            Assert.Equal(0, inventory.CountOf(unitId, new Id("item.sample_sword")));
        }

        [Fact]
        public void Sell_UsesDefaultBuyPricePercentOfVendorSellPrice_AndPublishesEvent()
        {
            var host = NewHost(OneCurrencyRow, ShopVendorRow, out var inventory, out var bus);
            ItemSoldEvent? received = null;
            bus.Subscribe<ItemSoldEvent>(EconomyEventKeys.ItemSold, e => received = e);

            var unitId = new Id("player.sample_1");
            inventory.AddItem(unitId, new Id("item.sample_sword"), 1);
            var instanceId = inventory.ListItems(unitId)[0].InstanceId;

            var result = host.Sell(unitId, new Id("econ.vendor.sample_shop"), instanceId, 1);
            bus.DispatchPending();

            // 售价 10 * 0.25(默认 DefaultBuyPricePct) = 2.5 -> (long) 截断为 2。
            Assert.True(result.Success);
            Assert.Equal(2, result.Price);
            Assert.Equal(2, host.GetBalance(unitId, new Id("econ.currency.sample_coin")));
            Assert.NotNull(received);
            Assert.Equal(2, received!.Price);
        }

        [Fact]
        public void Sell_UsesBuyPriceRule_WhenVendorDeclaresOne()
        {
            var ruleVendorRow =
                "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.vendor.sample.name\", " +
                "\"buy_price_rule\": \"self.level\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", \"price_amount\": 10}" +
                "]}]";

            var exprFactory = new FakeNumericExprHostFactory();
            var host = NewHost(OneCurrencyRow, ruleVendorRow, out var inventory, out _, exprFactory);

            var unitId = new Id("player.sample_1");
            exprFactory.Levels[unitId] = 7;
            inventory.AddItem(unitId, new Id("item.sample_sword"), 1);
            var instanceId = inventory.ListItems(unitId)[0].InstanceId;

            var result = host.Sell(unitId, new Id("econ.vendor.sample_shop"), instanceId, 1);

            Assert.True(result.Success);
            Assert.Equal(7, result.Price);
        }

        [Fact]
        public void OnMapEnter_RestocksLimitedItemsAndPublishesEvent()
        {
            var vendorRow =
                "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.vendor.sample.name\", \"map_id\": \"map.sample_1\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", \"price_amount\": 10, " +
                "\"stock_limit\": 3, \"restock_policy\": \"on_map_enter\"}" +
                "]}]";
            var host = NewHost(OneCurrencyRow, vendorRow, out var inventory, out var bus);
            VendorRestockedEvent? received = null;
            bus.Subscribe<VendorRestockedEvent>(EconomyEventKeys.VendorRestocked, e => received = e);

            var unitId = new Id("player.sample_1");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 100, sourceId: unitId);
            host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 3);
            Assert.Equal(0, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));

            host.OnMapEnter(new Id("map.sample_1"));
            bus.DispatchPending();

            Assert.Equal(3, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));
            Assert.NotNull(received);
            Assert.Equal(new Id("econ.vendor.sample_shop"), received!.VendorId);
        }

        [Fact]
        public void Update_RestocksTimerItemsAfterElapsedTimeAndPublishesEvent()
        {
            var vendorRow =
                "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.vendor.sample.name\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", \"price_amount\": 10, " +
                "\"stock_limit\": 1, \"restock_policy\": \"timer\", \"restock_timer\": 10}" +
                "]}]";
            var host = NewHost(OneCurrencyRow, vendorRow, out var inventory, out var bus);
            VendorRestockedEvent? received = null;
            bus.Subscribe<VendorRestockedEvent>(EconomyEventKeys.VendorRestocked, e => received = e);

            var unitId = new Id("player.sample_1");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 100, sourceId: unitId);
            host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 1);
            Assert.Equal(0, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));

            host.Update(5); // 未到周期
            Assert.Equal(0, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));

            host.Update(6); // 累计 11 >= 10，触发补货
            bus.DispatchPending();

            Assert.Equal(1, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));
            Assert.NotNull(received);
        }

        [Fact]
        public void CurrencyPersistable_RoundTripsBalances()
        {
            var host1 = NewHost(OneCurrencyRow, "[]", out _, out _);
            var unitId = new Id("player.sample_1");
            host1.Add(unitId, new Id("econ.currency.sample_coin"), 42, sourceId: unitId);
            var saved = new CurrencyPersistable(unitId, host1).Save();

            var host2 = NewHost(OneCurrencyRow, "[]", out _, out _);
            new CurrencyPersistable(unitId, host2).Load(saved);

            Assert.Equal(42, host2.GetBalance(unitId, new Id("econ.currency.sample_coin")));
        }

        [Fact]
        public void VendorStockPersistable_RoundTripsRemainingStock()
        {
            var host1 = NewHost(OneCurrencyRow, ShopVendorRow, out _, out _);
            var unitId = new Id("player.sample_1");
            host1.Add(unitId, new Id("econ.currency.sample_coin"), 100, sourceId: unitId);
            host1.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 2);
            Assert.Equal(1, host1.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));

            var saved = new VendorStockPersistable(host1).Save();

            var host2 = NewHost(OneCurrencyRow, ShopVendorRow, out _, out _);
            new VendorStockPersistable(host2).Load(saved);

            Assert.Equal(1, host2.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));
        }

        [Fact]
        public void PlayerCurrencyExprGroupProvider_ResolvesBalance_ThroughRealExprParserAndEvaluator()
        {
            var host = NewHost(OneCurrencyRow, "[]", out _, out _);
            var playerId = new Id("player.sample_1");
            host.Add(playerId, new Id("econ.currency.sample_coin"), 77, sourceId: playerId);

            var provider = new PlayerCurrencyExprGroupProvider(host, () => playerId);
            var exprHost = new PlayerOnlyExprHost(provider);

            var schema = EconomyExprSchemaEntries.RegisterInto(new ExprSchema());
            var node = ExprParser.Parse("player.currency(econ.currency.sample_coin)", schema);
            var diagnostics = new ExprDiagnosticsRecorder();
            var value = ExprEvaluator.Evaluate(node, exprHost, diagnostics);

            Assert.Equal(ExprValueKind.Int, value.Kind);
            Assert.Equal(77, value.AsInt);
        }

        [Fact]
        public void ChainedExprGroupProvider_FallsThroughToNextProviderForUnknownKeys()
        {
            var host = NewHost(OneCurrencyRow, "[]", out _, out _);
            var playerId = new Id("player.sample_1");
            host.Add(playerId, new Id("econ.currency.sample_coin"), 5, sourceId: playerId);

            var currencyProvider = new PlayerCurrencyExprGroupProvider(host, () => playerId);
            var chained = new ChainedExprGroupProvider(new List<Core.Rules.ExprHost.IExprGroupProvider> { currencyProvider });

            var result = chained.Query("currency", new[] { ExprValue.OfId(new Id("econ.currency.sample_coin")) });
            Assert.Equal(5, result.AsInt);

            var fallback = chained.Query("totally_unknown_key", System.Array.Empty<ExprValue>());
            Assert.Equal(ExprValueKind.Bool, fallback.Kind);
            Assert.False(fallback.AsBool);
        }
    }
}
