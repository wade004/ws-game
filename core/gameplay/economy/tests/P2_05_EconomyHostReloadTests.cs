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

        // -----------------------------------------------------------------
        // 2026-09-16 深度复审 D-S2：FindAnyVendorSellPrice 改按索引查询（原线性扫描）——本节验证
        // 索引与 _vendors/_vendorOrder 同步刷新，不存在"reload 之后价格已变、但 Sell 仍读到旧索引"
        // 这种缓存落后于数据的情形。
        // -----------------------------------------------------------------

        /// <summary>手填售价 vendor 缺省场景下的 <c>Sell</c>（走"该物品在任一商人的手填售价 ×
        /// DefaultBuyPricePct"旧口径回退分支，见 <see cref="EconomyHost.ComputeSellPrice"/> 判断
        /// 记录）：reload 后 vendor 的 <c>price_amount</c> 改变，<c>Sell</c> 算出的售价必须立即反映
        /// 新值——如果索引没有跟着 <c>ReloadFromRegistry</c> 同步重建，这里会读到 reload 之前的旧
        /// 索引条目。</summary>
        [Fact]
        public void Reload_ChangedPriceAmount_SellPriceIndexPicksUpNewValueImmediately()
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

            var unitId = new Id("player.p2_05_sell");
            var vendorId = new Id("econ.vendor.p2_05_shop");
            var itemId = new Id("item.p2_05_sword");
            inventory.AddItem(unitId, itemId, 2);
            var instance1 = inventory.ListItems(unitId)[0].InstanceId;

            var firstSell = host.Sell(unitId, vendorId, instance1, 1);
            Assert.True(firstSell.Success);
            // 售价 10 × 0.25(默认 DefaultBuyPricePct) = 2.5 -> (long) 截断为 2。
            Assert.Equal(2, firstSell.Price);

            // reload：price_amount 改成 100。
            source.Replace(EconomySchemas.Vendor.Name,
                Envelope(EconomySchemas.Vendor.Name, VendorRow(priceAmount: 100, stockLimit: 3)));
            var reload = registry.Reload(EconomySchemas.Vendor.Name);
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 2, reload.ErrorCount, reload.WarningCount));

            var instance2 = inventory.ListItems(unitId)[0].InstanceId;
            var secondSell = host.Sell(unitId, vendorId, instance2, 1);
            Assert.True(secondSell.Success);
            // 100 × 0.25 = 25——不是仍停留在 reload 之前算出的 2，证明索引确实随 reload 同步刷新。
            Assert.Equal(25, secondSell.Price);
        }

        /// <summary>多商人优先级：同一 templateId 在按 Id 序数排在前面的商人处未填
        /// <c>price_amount</c>、排在后面的商人处填了——<see cref="EconomyHost.ComputeSellPrice"/>
        /// 的旧口径回退分支应命中后面这个商人的价格（"任一商人"语义），索引化实现必须逐位保留这一
        /// 优先级，不能因为改用 <c>Dictionary</c> 查询而错取到别的商人的价格或找不到。</summary>
        [Fact]
        public void MultipleVendors_OnlySecondVendorHasPriceAmount_SellUsesSecondVendorsPrice()
        {
            var bus = EconomyTestSupport.NewEventBus();
            // 按 Id 序数，econ.vendor.p2_05_shop_a 排在 econ.vendor.p2_05_shop_b 之前。
            var vendorRows =
                "[{\"id\": \"econ.vendor.p2_05_shop_a\", \"name_key\": \"l10n.vendor.p2_05_a.name\", \"sell_items\": [" +
                "{\"item_id\": \"item.p2_05_sword\", \"price_currency_id\": \"econ.currency.p2_05_coin\"}" + // 未填 price_amount。
                "]}," +
                "{\"id\": \"econ.vendor.p2_05_shop_b\", \"name_key\": \"l10n.vendor.p2_05_b.name\", \"sell_items\": [" +
                "{\"item_id\": \"item.p2_05_sword\", \"price_currency_id\": \"econ.currency.p2_05_coin\", \"price_amount\": 40}" +
                "]}]";
            var source = new MutableSource()
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, CurrencyRow))
                .Add(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, vendorRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterValidationRule(new EconomyContentValidationRule());
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var inventory = new FakeInventoryHost();
            var host = new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());

            var unitId = new Id("player.p2_05_multi_vendor");
            var itemId = new Id("item.p2_05_sword");
            inventory.AddItem(unitId, itemId, 1);
            var instanceId = inventory.ListItems(unitId)[0].InstanceId;

            // 在没有手填价格的商人 A 处出售——ComputeSellPrice 的"该物品在任一商人的手填售价"分支
            // 应跨过 A（未填）找到 B 的 40。
            var result = host.Sell(unitId, new Id("econ.vendor.p2_05_shop_a"), instanceId, 1);

            Assert.True(result.Success);
            // 40 × 0.25 = 10。
            Assert.Equal(10, result.Price);
        }
    }
}
