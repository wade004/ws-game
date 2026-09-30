using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Gameplay.Economy;
using Tests.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// T-H10（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：<see cref="EconomyHost.Buy"/> /
    /// <see cref="EconomyHost.Sell"/> 的失败路径——未知商人、商品不在售、不拥有物品、<c>count&lt;=0</c>
    /// 抛 <see cref="ArgumentOutOfRangeException"/>。每条失败路径都同时断言"货币余额、背包内容、商人库存
    /// 均与调用前逐项相等，且没有任何事件入队"（<see cref="IEventBus.DispatchPending"/> 返回 0）。
    /// </summary>
    public sealed class T_H10_EconomyHostFailurePathTests
    {
        private static readonly Id Coin = new Id("econ.currency.sample_coin");
        private static readonly Id Shop = new Id("econ.vendor.sample_shop");
        private static readonly Id Sword = new Id("item.sample_sword");
        private static readonly Id Shield = new Id("item.sample_shield");
        private static readonly Id Unit = new Id("player.sample_1");

        private const int StockLimit = 3;
        private const int InitialBalance = 500;

        private const string CurrencyRow =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.currency.sample.name\", " +
            "\"cap\": 100000, \"display_ref\": \"display.sample_coin\"}]";

        // 商人只卖 sword（限量 3）；shield 不在售，用来构造 ItemNotSold。
        private const string VendorRow =
            "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.vendor.sample.name\", \"sell_items\": [" +
            "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
            "\"price_amount\": 10, \"stock_limit\": 3}]}]";

        private sealed class Fixture
        {
            public EconomyHost Host = null!;
            public FakeInventoryHost Inventory = null!;
            public IEventBus Bus = null!;
            public Id OwnedShieldInstance;
            public int OwnedShieldCount;
        }

        private static Fixture NewFixture()
        {
            var bus = EconomyTestSupport.NewEventBus();
            var registry = EconomyTestSupport.MakeRegistry(bus, CurrencyRow, VendorRow);
            var inventory = new FakeInventoryHost();
            var host = new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());

            host.Add(Unit, Coin, InitialBalance, sourceId: Unit);
            inventory.AddItem(Unit, Shield, 2);
            var shieldInstance = inventory.ListItems(Unit).Single(i => i.TemplateId.Equals(Shield));

            // 先把 setup 阶段排队的事件全部派发掉，使后面"失败调用之后 DispatchPending()==0"只反映失败调用本身。
            bus.DispatchPending();
            return new Fixture
            {
                Host = host,
                Inventory = inventory,
                Bus = bus,
                OwnedShieldInstance = shieldInstance.InstanceId,
                OwnedShieldCount = shieldInstance.Count,
            };
        }

        /// <summary>可观测状态快照：货币余额、背包（实例 id/模板/数量，按列表顺序）、商人库存。</summary>
        private sealed class Snapshot
        {
            public long Balance;
            public List<string> Bag = new List<string>();
            public int? SwordStock;
            public int? ShieldStock;
            public int? GhostVendorStock;
        }

        private static Snapshot Capture(Fixture f)
        {
            var s = new Snapshot
            {
                Balance = f.Host.GetBalance(Unit, Coin),
                SwordStock = f.Host.GetStock(Shop, Sword),
                ShieldStock = f.Host.GetStock(Shop, Shield),
                GhostVendorStock = f.Host.GetStock(new Id("econ.vendor.sample_ghost"), Sword),
            };
            foreach (var item in f.Inventory.ListItems(Unit))
            {
                s.Bag.Add($"{item.InstanceId}|{item.TemplateId}|{item.Count}");
            }

            return s;
        }

        private static void AssertUnchanged(Fixture f, Snapshot before)
        {
            var after = Capture(f);
            Assert.Equal(before.Balance, after.Balance);
            Assert.Equal(before.Bag, after.Bag);
            Assert.Equal(before.SwordStock, after.SwordStock);
            Assert.Equal(before.ShieldStock, after.ShieldStock);
            Assert.Equal(before.GhostVendorStock, after.GhostVendorStock);

            // 无事件入队：失败路径既不发 ItemPurchased/ItemSold，也不发 CurrencyChanged/EconomyCharged。
            Assert.Equal(0, f.Bus.DispatchPending());
        }

        private static List<string> SubscribeAllEconomyEvents(Fixture f)
        {
            var seen = new List<string>();
            f.Bus.Subscribe<ItemPurchasedEvent>(EconomyEventKeys.ItemPurchased, e => seen.Add("purchased"));
            f.Bus.Subscribe<ItemSoldEvent>(EconomyEventKeys.ItemSold, e => seen.Add("sold"));
            f.Bus.Subscribe<CurrencyChangedEvent>(EconomyEventKeys.CurrencyChanged, e => seen.Add("currency_changed"));
            return seen;
        }

        [Fact]
        public void Buy_UnknownVendor_FailsWithUnknownVendor_AndNothingChanges()
        {
            var f = NewFixture();
            var seen = SubscribeAllEconomyEvents(f);
            var before = Capture(f);

            var result = f.Host.Buy(Unit, new Id("econ.vendor.sample_ghost"), Sword, 1);

            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.UnknownVendor, result.Reason);
            Assert.Equal(0L, result.Price);
            AssertUnchanged(f, before);
            Assert.Empty(seen);
        }

        [Fact]
        public void Buy_ItemNotSoldByVendor_FailsWithItemNotSold_AndNothingChanges()
        {
            var f = NewFixture();
            var seen = SubscribeAllEconomyEvents(f);
            var before = Capture(f);

            // shield 不在 sample_shop 的 sell_items 里；用户余额充足，排除"钱不够"的干扰。
            var result = f.Host.Buy(Unit, Shop, Shield, 1);

            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.ItemNotSold, result.Reason);
            Assert.Equal(0L, result.Price);
            AssertUnchanged(f, before);
            Assert.Empty(seen);
            Assert.Null(f.Host.GetStock(Shop, Shield));
        }

        [Fact]
        public void Sell_UnknownVendor_FailsWithUnknownVendor_AndNothingChanges()
        {
            var f = NewFixture();
            var seen = SubscribeAllEconomyEvents(f);
            var before = Capture(f);

            // 物品确实拥有，失败只能归因于商人不存在。
            var result = f.Host.Sell(Unit, new Id("econ.vendor.sample_ghost"), f.OwnedShieldInstance, 1);

            Assert.False(result.Success);
            Assert.Equal(SellFailureReason.UnknownVendor, result.Reason);
            Assert.Equal(0L, result.Price);
            AssertUnchanged(f, before);
            Assert.Empty(seen);
        }

        [Fact]
        public void Sell_UnknownItemInstance_FailsWithNotOwned_AndNothingChanges()
        {
            var f = NewFixture();
            var seen = SubscribeAllEconomyEvents(f);
            var before = Capture(f);

            var result = f.Host.Sell(Unit, Shop, new Id("item.inst_does_not_exist"), 1);

            Assert.False(result.Success);
            Assert.Equal(SellFailureReason.NotOwned, result.Reason);
            Assert.Equal(0L, result.Price);
            AssertUnchanged(f, before);
            Assert.Empty(seen);
        }

        [Fact]
        public void Sell_InstanceOwnedByAnotherUnit_FailsWithNotOwned_AndNothingChanges()
        {
            var f = NewFixture();
            var other = new Id("player.sample_2");
            f.Inventory.AddItem(other, Shield, 1);
            var otherInstance = f.Inventory.ListItems(other).Single().InstanceId;
            f.Bus.DispatchPending();
            var seen = SubscribeAllEconomyEvents(f);
            var before = Capture(f);
            var otherBagBefore = f.Inventory.ListItems(other).Select(i => $"{i.InstanceId}|{i.TemplateId}|{i.Count}").ToList();

            var result = f.Host.Sell(Unit, Shop, otherInstance, 1);

            Assert.False(result.Success);
            Assert.Equal(SellFailureReason.NotOwned, result.Reason);
            AssertUnchanged(f, before);
            Assert.Empty(seen);
            Assert.Equal(otherBagBefore,
                f.Inventory.ListItems(other).Select(i => $"{i.InstanceId}|{i.TemplateId}|{i.Count}").ToList());
            Assert.Equal(0L, f.Host.GetBalance(other, Coin));
        }

        [Fact]
        public void Sell_CountExceedsOwnedCount_FailsWithNotOwned_AndNothingChanges()
        {
            var f = NewFixture();
            var seen = SubscribeAllEconomyEvents(f);
            var before = Capture(f);

            // 期望值由规则算出：拥有 N 个，卖 N+1 个必须失败。
            var result = f.Host.Sell(Unit, Shop, f.OwnedShieldInstance, f.OwnedShieldCount + 1);

            Assert.False(result.Success);
            Assert.Equal(SellFailureReason.NotOwned, result.Reason);
            AssertUnchanged(f, before);
            Assert.Empty(seen);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void Buy_NonPositiveCount_ThrowsArgumentOutOfRange_AndNothingChanges(int count)
        {
            var f = NewFixture();
            var seen = SubscribeAllEconomyEvents(f);
            var before = Capture(f);

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => f.Host.Buy(Unit, Shop, Sword, count));

            Assert.Equal("count", ex.ParamName);
            AssertUnchanged(f, before);
            Assert.Empty(seen);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void Sell_NonPositiveCount_ThrowsArgumentOutOfRange_AndNothingChanges(int count)
        {
            var f = NewFixture();
            var seen = SubscribeAllEconomyEvents(f);
            var before = Capture(f);

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => f.Host.Sell(Unit, Shop, f.OwnedShieldInstance, count));

            Assert.Equal("count", ex.ParamName);
            AssertUnchanged(f, before);
            Assert.Empty(seen);
        }

        [Fact]
        public void Buy_StockLimitUnchanged_AfterEveryFailurePath_ThenSucceedsNormally()
        {
            // 不变量：一连串失败调用之后，商人库存仍是初始限量，随后的正常购买行为与没发生过失败完全一致。
            var f = NewFixture();
            f.Host.Buy(Unit, new Id("econ.vendor.sample_ghost"), Sword, 1);
            f.Host.Buy(Unit, Shop, Shield, 1);
            f.Host.Sell(Unit, new Id("econ.vendor.sample_ghost"), f.OwnedShieldInstance, 1);
            f.Host.Sell(Unit, Shop, new Id("item.inst_does_not_exist"), 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => f.Host.Buy(Unit, Shop, Sword, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.Host.Sell(Unit, Shop, f.OwnedShieldInstance, -3));
            Assert.Equal(StockLimit, f.Host.GetStock(Shop, Sword));

            var ok = f.Host.Buy(Unit, Shop, Sword, 2);

            Assert.True(ok.Success);
            Assert.Equal(InitialBalance - ok.Price, f.Host.GetBalance(Unit, Coin));
            Assert.Equal(StockLimit - 2, f.Host.GetStock(Shop, Sword));
        }
    }
}
