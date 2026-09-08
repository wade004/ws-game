using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
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
        public void CurrencyPersistable_Load_ReplacesRatherThanAdds_AndIsIdempotent()
        {
            // N01：存 100 → 运行中变 80 → Load 应得 100（替换，不是 180）；再 Load 一次仍为 100（幂等）。
            var host = NewHost(OneCurrencyRow, "[]", out _, out _);
            var currencyId = new Id("econ.currency.sample_coin");
            var unitId = new Id("player.sample_1");

            host.Add(unitId, currencyId, 100, sourceId: unitId);
            var persistable = new CurrencyPersistable(unitId, host);
            var saved = persistable.Save();

            host.Add(unitId, currencyId, -20, sourceId: unitId);
            Assert.Equal(80, host.GetBalance(unitId, currencyId));

            persistable.Load(saved);
            Assert.Equal(100, host.GetBalance(unitId, currencyId));

            persistable.Load(saved);
            Assert.Equal(100, host.GetBalance(unitId, currencyId));
        }

        [Fact]
        public void CurrencyPersistable_Load_ZeroesCurrenciesNotPresentInSnapshot()
        {
            // Save 只写非零余额；Load 应把快照未出现（存档时为零）的货币也显式置零，而不是保留运行期残留。
            var twoCurrencyRow =
                "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.currency.sample.name\", " +
                "\"display_ref\": \"display.sample_coin\"}," +
                "{\"id\": \"econ.currency.sample_gem\", \"name_key\": \"l10n.currency.gem.name\", " +
                "\"display_ref\": \"display.sample_gem\"}]";
            var host = NewHost(twoCurrencyRow, "[]", out _, out _);
            var unitId = new Id("player.sample_1");
            var coinId = new Id("econ.currency.sample_coin");
            var gemId = new Id("econ.currency.sample_gem");

            host.Add(unitId, coinId, 50, sourceId: unitId);
            var saved = new CurrencyPersistable(unitId, host).Save(); // gem 余额为 0，不写入快照

            host.Add(unitId, gemId, 5, sourceId: unitId); // 运行中拿到 5 个 gem
            Assert.Equal(5, host.GetBalance(unitId, gemId));

            new CurrencyPersistable(unitId, host).Load(saved);

            Assert.Equal(50, host.GetBalance(unitId, coinId));
            Assert.Equal(0, host.GetBalance(unitId, gemId));
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

        /// <summary>
        /// AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：修复前
        /// <c>VendorStockPersistable.Load</c> 对 <c>JsonNull</c>（本段整体缺失，如只含 meta 的旧格式
        /// 存档）直接 no-op 返回，运行期已扣减的库存原样保留——真实探针复现：先把库存从 5 改到 2，
        /// 再加载一份只含 meta 的存档，返回 <c>Loaded</c> 但库存仍是 2，而不是按内容定义的满库存
        /// （5/此处 3）重置。
        /// </summary>
        [Fact]
        public void VendorStockPersistable_Load_NullData_ResetsStockToContentDefinedFull()
        {
            var host = NewHost(OneCurrencyRow, ShopVendorRow, out _, out _);
            var unitId = new Id("player.sample_1");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 100, sourceId: unitId);
            host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 2);
            Assert.Equal(1, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));

            new VendorStockPersistable(host).Load(JsonNull.Instance);

            Assert.Equal(3, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));
        }

        private const string TimerVendorRow =
            "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.vendor.sample.name\", \"sell_items\": [" +
            "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", \"price_amount\": 10, " +
            "\"stock_limit\": 1, \"restock_policy\": \"timer\", \"restock_timer\": 10}" +
            "]}]";

        /// <summary>
        /// AUD-03 根治（architecture/落地计划/audit-85f1f4f-20260908，P2，10 第 2.3 节勘误）：真实
        /// 探针复现——10 秒补货周期，t=2 存档（倒计时剩 8 秒），原地（同一 <see cref="EconomyHost"/>
        /// 实例，不重新构造）继续推进 7 秒（倒计时只剩 1 秒），此时读档；修复前 <c>SetStock</c> 只
        /// 写 <c>Remaining</c> 不碰 <c>TimerRemaining</c>，读档后倒计时仍是"剩 1 秒"，再过 1 秒立即
        /// 补货（<c>ACTUAL=after_one_second_remaining=5</c>）——现在必须恢复到快照时刻的"剩 8 秒"，
        /// 再过 1 秒应为"剩 7 秒"，不触发补货，直到累计推进满 8 秒才补货。
        /// </summary>
        [Fact]
        public void VendorStockPersistable_RoundTrip_RestoresTimerRemaining_OnSameHostReload_NotStaleValue()
        {
            var host = NewHost(OneCurrencyRow, TimerVendorRow, out _, out var bus);
            var unitId = new Id("player.sample_1");
            var vendorId = new Id("econ.vendor.sample_shop");
            var itemId = new Id("item.sample_sword");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 100, sourceId: unitId);
            host.Buy(unitId, vendorId, itemId, 1);
            Assert.Equal(0, host.GetStock(vendorId, itemId));

            host.Update(2); // 倒计时剩 8 秒时存档快照。
            var persistable = new VendorStockPersistable(host);
            var saved = persistable.Save();

            host.Update(7); // 原地（不读档）继续推进：倒计时只剩 1 秒。
            persistable.Load(saved); // 读档应恢复到快照时刻的"剩 8 秒"，不是保留刚跑到的"剩 1 秒"。

            Assert.Equal(0, host.GetStock(vendorId, itemId));

            host.Update(1); // 若正确恢复至剩 8 秒，此时应还剩 7 秒，不应补货。
            VendorRestockedEvent? received = null;
            bus.Subscribe<VendorRestockedEvent>(EconomyEventKeys.VendorRestocked, e => received = e);
            bus.DispatchPending();
            Assert.Equal(0, host.GetStock(vendorId, itemId));

            host.Update(7); // 再推进 7 秒（累计剩余 8-1-7=0），应恰好补货。
            bus.DispatchPending();
            Assert.Equal(1, host.GetStock(vendorId, itemId));
        }

        /// <summary>
        /// AUD-03 补充：旧格式存档（<c>timer_remaining</c> 字段缺失，如 1.6 早期只写纯数字条目）
        /// 读档时兜底重置为完整周期，而不是保留读档前的旧倒计时——呼应 <see
        /// cref="EconomyHost.SetStock"/> 的可选参数语义。构造一份手写的旧格式快照（纯数字，无
        /// <c>timer_remaining</c>），验证读档后立即从满周期起算。
        /// </summary>
        [Fact]
        public void VendorStockPersistable_Load_LegacyNumberShape_ResetsTimerToFullPeriod()
        {
            var host = NewHost(OneCurrencyRow, TimerVendorRow, out _, out _);
            var unitId = new Id("player.sample_1");
            var vendorId = new Id("econ.vendor.sample_shop");
            var itemId = new Id("item.sample_sword");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 100, sourceId: unitId);
            host.Buy(unitId, vendorId, itemId, 1);
            host.Update(9); // 让运行期倒计时跑到只剩 1 秒（不触发补货）。
            Assert.Equal(0, host.GetStock(vendorId, itemId));

            var legacyShape = new JsonObjectBuilder()
                .Add(vendorId.Value, new JsonObjectBuilder().Add(itemId.Value, new JsonNumber(0)).Build())
                .Build();

            new VendorStockPersistable(host).Load(legacyShape);

            // 读档后立即推进 1 秒：若正确重置为完整周期（10 秒），不应补货；若仍沿用读档前跑到只剩
            // 1 秒的旧倒计时（未修复前的缺陷），会立即补货——用这一步区分两种行为。
            host.Update(1);
            Assert.Equal(0, host.GetStock(vendorId, itemId));
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
