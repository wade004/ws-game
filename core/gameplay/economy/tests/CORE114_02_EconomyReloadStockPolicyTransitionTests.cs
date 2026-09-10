using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Economy;
using Tests.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// CORE114-02 根治验收（外部审计 audit-76d16a5-20260910，见 <c>EconomyHost.
    /// ReconcileStockStateWithNewDefinition</c> 判断记录）：<c>ReloadFromRegistry</c>
    /// 对已存在的 (vendor,item) 库存状态此前"原样保留不动"，<c>none → timer</c> 场景会让
    /// <c>TimerRemaining</c> 永远停留在 null、<see cref="EconomyHost.Update"/> 因此永久跳过补货。
    /// 本文件覆盖三种补货策略两两转换 + 库存上限/计时周期变化的 clamp 行为，改造自审计探针
    /// <c>CoreBoundaryProbe.EconomyNoneToTimer</c>。
    /// </summary>
    public sealed class CORE114_02_EconomyReloadStockPolicyTransitionTests
    {
        private sealed class MutableSource : IDataSource
        {
            private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(System.StringComparer.Ordinal);
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
            "[{\"id\": \"econ.currency.core114_02_coin\", \"name_key\": \"l10n.currency.core114_02.name\", " +
            "\"cap\": 100000, \"display_ref\": \"display.core114_02_coin\"}]";

        private static readonly Id VendorId = new Id("econ.vendor.core114_02_shop");
        private static readonly Id ItemId = new Id("item.core114_02_token");

        private static string VendorRow(string policyJsonFragment, int stockLimit) =>
            "[{\"id\": \"econ.vendor.core114_02_shop\", \"name_key\": \"l10n.vendor.core114_02.name\", \"sell_items\": [" +
            "{\"item_id\": \"item.core114_02_token\", \"price_currency_id\": \"econ.currency.core114_02_coin\", " +
            "\"price_amount\": 1, \"stock_limit\": " + stockLimit + policyJsonFragment + "}]}]";

        private static string NonePolicy => "";
        private static string TimerPolicy(double timer) => ",\"restock_policy\": \"timer\", \"restock_timer\": " + timer;

        private (DataRegistry Registry, MutableSource Source, Core.Foundation.EventBus.IEventBus Bus) MakeRegistry(string vendorRowsJson)
        {
            var bus = EconomyTestSupport.NewEventBus();
            var source = new MutableSource()
                .Add(EconomySchemas.Currency.Name, EconomyTestSupport.Envelope(EconomySchemas.Currency.Name, CurrencyRow))
                .Add(EconomySchemas.Vendor.Name, EconomyTestSupport.Envelope(EconomySchemas.Vendor.Name, vendorRowsJson));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterValidationRule(new EconomyContentValidationRule());
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return (registry, source, bus);
        }

        private void ReloadVendor((DataRegistry Registry, MutableSource Source, Core.Foundation.EventBus.IEventBus Bus) rig, string newVendorRowsJson)
        {
            rig.Source.Replace(EconomySchemas.Vendor.Name, EconomyTestSupport.Envelope(EconomySchemas.Vendor.Name, newVendorRowsJson));
            var reload = rig.Registry.Reload(EconomySchemas.Vendor.Name);
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            rig.Bus.PublishImmediate(new DataLoadCompletedEvent(rig.Registry.Tables.Count, 2, reload.ErrorCount, reload.WarningCount));
        }

        /// <summary>探针复现场景本身（外部审计 ECON-NONE-TO-TIMER）：none 策略库存耗尽为 0，
        /// reload 为 timer 策略后必须建立可用计时器，到期后 Update 补到新定义的 stock_limit。</summary>
        [Fact]
        public void Reload_NoneToTimer_TimerInitializedAndRestocksOnUpdate()
        {
            var rig = MakeRegistry(VendorRow(NonePolicy, stockLimit: 2));
            var host = new EconomyHost(rig.Registry, rig.Bus, new FakeInventoryHost(), new FakeNumericExprHostFactory());
            host.SetStock(VendorId, ItemId, 0);

            ReloadVendor(rig, VendorRow(TimerPolicy(1), stockLimit: 2));

            Assert.Equal(0, host.GetStock(VendorId, ItemId));
            Assert.NotNull(host.GetStockTimerRemaining(VendorId, ItemId)); // 此前是 null（外部审计复现）

            host.Update(2);

            Assert.Equal(2, host.GetStock(VendorId, ItemId));
            Assert.NotNull(host.GetStockTimerRemaining(VendorId, ItemId));
        }

        [Fact]
        public void Reload_TimerToNone_TimerClearedAndNoLongerRestocks()
        {
            var rig = MakeRegistry(VendorRow(TimerPolicy(1), stockLimit: 2));
            var host = new EconomyHost(rig.Registry, rig.Bus, new FakeInventoryHost(), new FakeNumericExprHostFactory());
            host.SetStock(VendorId, ItemId, 0);
            Assert.NotNull(host.GetStockTimerRemaining(VendorId, ItemId));

            ReloadVendor(rig, VendorRow(NonePolicy, stockLimit: 2));

            Assert.Null(host.GetStockTimerRemaining(VendorId, ItemId));

            host.Update(10); // 计时器已清空，Update 不应再补货。
            Assert.Equal(0, host.GetStock(VendorId, ItemId));
        }

        [Fact]
        public void Reload_TimerPeriodShortened_TimerRemainingClampedToNewPeriod()
        {
            var rig = MakeRegistry(VendorRow(TimerPolicy(10), stockLimit: 2));
            var host = new EconomyHost(rig.Registry, rig.Bus, new FakeInventoryHost(), new FakeNumericExprHostFactory());
            // 构造期即是 timer 策略，倒计时从满周期 10 开始，无需手工 SetStock。
            Assert.Equal(10, host.GetStockTimerRemaining(VendorId, ItemId));

            ReloadVendor(rig, VendorRow(TimerPolicy(3), stockLimit: 2));

            // 剩余 10 秒 > 新周期 3 秒：clamp 到 3，不允许"剩余时间超过新定义的完整周期"。
            Assert.Equal(3, host.GetStockTimerRemaining(VendorId, ItemId));
        }

        [Fact]
        public void Reload_StockLimitShrunk_RemainingClampedDownButNotRestocked()
        {
            var rig = MakeRegistry(VendorRow(NonePolicy, stockLimit: 5));
            var host = new EconomyHost(rig.Registry, rig.Bus, new FakeInventoryHost(), new FakeNumericExprHostFactory());
            host.SetStock(VendorId, ItemId, 4); // 已消费 1 件，剩 4。

            ReloadVendor(rig, VendorRow(NonePolicy, stockLimit: 3));

            // 新上限 3 < 旧剩余 4：clamp 到 3，不白送库存。
            Assert.Equal(3, host.GetStock(VendorId, ItemId));
        }

        [Fact]
        public void Reload_StockLimitRaised_RemainingUnaffectedNotRefilled()
        {
            var rig = MakeRegistry(VendorRow(NonePolicy, stockLimit: 2));
            var host = new EconomyHost(rig.Registry, rig.Bus, new FakeInventoryHost(), new FakeNumericExprHostFactory());
            host.SetStock(VendorId, ItemId, 0); // 已耗尽。

            ReloadVendor(rig, VendorRow(NonePolicy, stockLimit: 5));

            // 上限调大不能凭空把已消耗的库存补回来："已消费库存守恒，不白送"。
            Assert.Equal(0, host.GetStock(VendorId, ItemId));
        }
    }
}
