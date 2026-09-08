using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Gameplay.Economy;
using System;
using Tests.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// CORE-170-03 根治（architecture/落地计划/audit-8160178-20260908，P2）：<see
    /// cref="CurrencyPersistable.Load"/>/<see cref="VendorStockPersistable.Load"/> 修复前都是边解析
    /// 边直接调用 <see cref="EconomyHost.SetBalance"/>/<see cref="EconomyHost.SetStock"/> 修改运行期
    /// 状态——同一次 <c>Load</c> 调用里，排在后面的条目格式不合法时，排在前面的条目已经把新值写进了
    /// 真实 <see cref="EconomyHost"/>，抛异常后这些已提交的写入不会回滚，形成"部分是新存档值、部分
    /// 还是读档前旧值"的半新半旧中间态，与 <c>EquipmentPersistable.Load</c> 曾经的同一类缺陷成因
    /// 相同。根治后两者都先完整解析校验成临时恢复计划，只有整份数据校验通过才一次性提交。
    /// </summary>
    public sealed class CORE_170_03_CurrencyAndVendorStockPersistableLoadFailureTests
    {
        private const string TwoCurrencyRows =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.currency.sample_coin.name\", " +
            "\"cap\": 1000, \"display_ref\": \"display.sample_coin\"}," +
            "{\"id\": \"econ.currency.sample_gem\", \"name_key\": \"l10n.currency.sample_gem.name\", " +
            "\"cap\": 1000, \"display_ref\": \"display.sample_gem\"}]";

        private const string ShopVendorRow =
            "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.vendor.sample.name\", \"sell_items\": [" +
            "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", \"price_amount\": 10, \"stock_limit\": 3}," +
            "{\"item_id\": \"item.sample_shield\", \"price_currency_id\": \"econ.currency.sample_coin\", \"price_amount\": 10, \"stock_limit\": 5}" +
            "]}]";

        private static EconomyHost NewHost(
            string currencyRowsJson,
            string vendorRowsJson,
            out Core.Foundation.EventBus.IEventBus bus)
        {
            bus = EconomyTestSupport.NewEventBus();
            var registry = EconomyTestSupport.MakeRegistry(bus, currencyRowsJson, vendorRowsJson);
            var inventory = new FakeInventoryHost();
            return new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());
        }

        [Fact]
        public void CurrencyPersistable_Load_BadShape_ThrowsFormatException_AndLeavesBalancesUntouched()
        {
            var host = NewHost(TwoCurrencyRows, "[]", out _);
            var unitId = new Id("player.core_170_03_hero");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 50, sourceId: unitId);
            host.Add(unitId, new Id("econ.currency.sample_gem"), 7, sourceId: unitId);
            var persistable = new CurrencyPersistable(unitId, host);

            var badShape = new JsonString("wrong-shape");
            var ex = Record.Exception(() => persistable.Load(badShape));

            Assert.IsType<FormatException>(ex);
            Assert.Equal(50, host.GetBalance(unitId, new Id("econ.currency.sample_coin")));
            Assert.Equal(7, host.GetBalance(unitId, new Id("econ.currency.sample_gem")));
        }

        /// <summary>核心复现：排在后面的货币条目格式非法时，排在前面已经校验通过的条目不应该被
        /// 提前写入——否则修复前会出现"sample_coin 已经被改写成存档里的新值，sample_gem 因为后面
        /// 抛异常从未被处理"这种半新半旧状态。断言两个货币都完全保持读档前的值，不是"第一个改了、
        /// 第二个没改"。</summary>
        [Fact]
        public void CurrencyPersistable_Load_LaterEntryBadShape_DoesNotPartiallyCommitEarlierEntries()
        {
            var host = NewHost(TwoCurrencyRows, "[]", out _);
            var unitId = new Id("player.core_170_03_hero");
            host.Add(unitId, new Id("econ.currency.sample_coin"), 50, sourceId: unitId);
            host.Add(unitId, new Id("econ.currency.sample_gem"), 7, sourceId: unitId);
            var persistable = new CurrencyPersistable(unitId, host);

            // sample_coin 条目格式合法（应当被解析成计划的一部分），sample_gem 条目值不是合法整数
            // ——修复前 sample_coin 会先被真实提交为 999，随后处理 sample_gem 时才抛异常。
            var badData = new JsonObjectBuilder()
                .Add("econ.currency.sample_coin", new JsonNumber(999))
                .Add("econ.currency.sample_gem", new JsonString("not-a-number"))
                .Build();

            var ex = Record.Exception(() => persistable.Load(badData));

            Assert.IsType<FormatException>(ex);
            Assert.Equal(50, host.GetBalance(unitId, new Id("econ.currency.sample_coin")));
            Assert.Equal(7, host.GetBalance(unitId, new Id("econ.currency.sample_gem")));
        }

        [Fact]
        public void VendorStockPersistable_Load_BadVendorEntry_ThrowsFormatException_AndLeavesStockUntouched()
        {
            var host = NewHost(TwoCurrencyRows, ShopVendorRow, out _);
            host.SetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 2);
            host.SetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_shield"), 4);
            var persistable = new VendorStockPersistable(host);

            var badData = new JsonObjectBuilder().Add("not-a-legal-vendor-id!!", new JsonNumber(1)).Build();
            var ex = Record.Exception(() => persistable.Load(badData));

            Assert.IsType<FormatException>(ex);
            Assert.Equal(2, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));
            Assert.Equal(4, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_shield")));
        }

        /// <summary>核心复现：同一商人下排在后面的物品条目格式非法时，排在前面已经校验通过的条目
        /// 不应该被提前写入。</summary>
        [Fact]
        public void VendorStockPersistable_Load_LaterItemEntryBadShape_DoesNotPartiallyCommitEarlierEntries()
        {
            var host = NewHost(TwoCurrencyRows, ShopVendorRow, out _);
            host.SetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword"), 2);
            host.SetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_shield"), 4);
            var persistable = new VendorStockPersistable(host);

            var badData = new JsonObjectBuilder()
                .Add("econ.vendor.sample_shop", new JsonObjectBuilder()
                    .Add("item.sample_sword", new JsonNumber(0)) // 合法：应当被解析进计划
                    .Add("item.sample_shield", JsonBool.True) // 非法：既不是数字也不是合法对象形状
                    .Build())
                .Build();

            var ex = Record.Exception(() => persistable.Load(badData));

            Assert.IsType<FormatException>(ex);
            Assert.Equal(2, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_sword")));
            Assert.Equal(4, host.GetStock(new Id("econ.vendor.sample_shop"), new Id("item.sample_shield")));
        }
    }
}
