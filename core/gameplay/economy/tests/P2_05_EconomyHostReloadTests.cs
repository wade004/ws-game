using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Economy;
using Tests.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// P2-05 同类缓存收口回归测试（外部审计 audit-c9ff301-20260909 followup-2026-09-10）：<see
    /// cref="EconomyHost"/> 的 <c>econ.currency</c>/<c>econ.vendor</c> 定义缓存与
    /// <c>Core.Rules.Skill.SkillDefCache</c>/<c>Core.Numbers.Archetype.ArchetypeRegistry</c> 同一类
    /// "构造期一次性读 registry 建索引、之后只读"模式，此前没有订阅 <see
    /// cref="DataLoadCompletedEvent"/>。与 Loot/Stat/Archetype 三处修复不同的是，<see
    /// cref="EconomyHost"/> 的限量库存 <c>_stock</c> 是从 <c>_vendors</c> 派生的运行期状态（见
    /// <see cref="EconomyHost.ReloadFromRegistry"/> 判断记录），本测试同时覆盖"新定价生效"与"已购
    /// 买剩的库存不因 reload 被重置回满库存"两点。改造自
    /// <c>P2_05_ArchetypeRegistryReloadTests</c> 同一套 <c>MutableSource</c> 写法
    /// （<c>InMemoryDataSource.Add</c> 只能追加、不能就地覆写）。
    /// </summary>
    public sealed class P2_05_EconomyHostReloadTests
    {
        private sealed class MutableSource : IDataSource
        {
            private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
            public MutableSource Add(string table, string text) { _texts[table] = text; return this; }
            public void Replace(string table, string text) => _texts[table] = text;
            public IReadOnlyList<DataTableSource> ListTables()
            {
                var result = new List<DataTableSource>();
                foreach (var pair in _texts)
                {
                    var table = pair.Key;
                    result.Add(new DataTableSource(table, "memory://" + table, () => _texts[table]));
                }
                return result;
            }
        }

        private const string CurrencyRow =
            "[{\"id\": \"econ.currency.p2_05_coin\", \"name_key\": \"l10n.currency.p2_05.name\", " +
            "\"cap\": 100000, \"display_ref\": \"display.p2_05_coin\"}]";

        private static string VendorRow(int priceAmount, int stockLimit) =>
            "[{\"id\": \"econ.vendor.p2_05_shop\", \"name_key\": \"l10n.vendor.p2_05.name\", \"sell_items\": [" +
            "{\"item_id\": \"item.p2_05_sword\", \"price_currency_id\": \"econ.currency.p2_05_coin\", " +
            "\"price_amount\": " + priceAmount + ", \"stock_limit\": " + stockLimit + "}]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        [Fact]
        public void Reload_PicksUpNewPrice_ButPreservesAlreadyConsumedStock()
        {
            var bus = EconomyTestSupport.NewEventBus();
            var source = new MutableSource()
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, CurrencyRow))
                .Add(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, VendorRow(priceAmount: 10, stockLimit: 3)));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterValidationRule(new EconomyContentValidationRule());
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var inventory = new FakeInventoryHost();
            var host = new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());

            var unitId = new Id("player.p2_05");
            var vendorId = new Id("econ.vendor.p2_05_shop");
            var currencyId = new Id("econ.currency.p2_05_coin");
            var itemId = new Id("item.p2_05_sword");

            host.Add(unitId, currencyId, 1000, sourceId: unitId);

            var firstBuy = host.Buy(unitId, vendorId, itemId, 1);
            Assert.True(firstBuy.Success);
            Assert.Equal(10, firstBuy.Price);
            Assert.Equal(2, host.GetStock(vendorId, itemId));

            // 修复前：EconomyHost 只在构造期读过一次 econ.vendor，reload 后 resident host 继续按旧
            // 定价（10）结算。新定价 999 之外，stock_limit 仍写 3——用来验证 reload 不会把已经消耗过
            // 的库存（GetStock 应保持 2）重置回满库存 3（见 EconomyHost.ReloadFromRegistry 判断记
            // 录"已存在的 (vendorId, itemId) 组合保留原 StockState 不动"）。
            source.Replace(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, VendorRow(priceAmount: 999, stockLimit: 3)));
            var reload = registry.Reload(EconomySchemas.Vendor.Name);
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            var recordCount = registry.GetAll(EconomySchemas.Currency.Name).Count + registry.GetAll(EconomySchemas.Vendor.Name).Count;
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, recordCount, reload.ErrorCount, reload.WarningCount));

            // 库存未被重置回满库存。
            Assert.Equal(2, host.GetStock(vendorId, itemId));

            // 新定价确实生效：旧余额 990 买不起新价 999。
            var secondBuy = host.Buy(unitId, vendorId, itemId, 1);
            Assert.False(secondBuy.Success);
            Assert.Equal(PurchaseFailureReason.InsufficientFunds, secondBuy.Reason);

            // 补足余额后能以新价成交，且库存从"保留下来的 2"继续递减到 1（不是从重置后的 3 减到 2）。
            host.Add(unitId, currencyId, 1000, sourceId: unitId);
            var thirdBuy = host.Buy(unitId, vendorId, itemId, 1);
            Assert.True(thirdBuy.Success);
            Assert.Equal(999, thirdBuy.Price);
            Assert.Equal(1, host.GetStock(vendorId, itemId));
        }

        [Fact]
        public void Reload_NewSellItemOnExistingVendor_GetsFreshFullStock()
        {
            var bus = EconomyTestSupport.NewEventBus();
            var source = new MutableSource()
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, CurrencyRow))
                .Add(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, VendorRow(priceAmount: 10, stockLimit: 3)));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterValidationRule(new EconomyContentValidationRule());
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var inventory = new FakeInventoryHost();
            var host = new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());

            var vendorId = new Id("econ.vendor.p2_05_shop");
            var newItemId = new Id("item.p2_05_shield");

            var vendorWithNewItem =
                "[{\"id\": \"econ.vendor.p2_05_shop\", \"name_key\": \"l10n.vendor.p2_05.name\", \"sell_items\": [" +
                "{\"item_id\": \"item.p2_05_sword\", \"price_currency_id\": \"econ.currency.p2_05_coin\", \"price_amount\": 10, \"stock_limit\": 3}," +
                "{\"item_id\": \"item.p2_05_shield\", \"price_currency_id\": \"econ.currency.p2_05_coin\", \"price_amount\": 20, \"stock_limit\": 5}" +
                "]}]";
            source.Replace(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, vendorWithNewItem));
            var reload = registry.Reload(EconomySchemas.Vendor.Name);
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 2, reload.ErrorCount, reload.WarningCount));

            Assert.Equal(5, host.GetStock(vendorId, newItemId));
        }
    }
}
