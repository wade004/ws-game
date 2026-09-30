using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.SaveSystem;
using Core.Gameplay.Economy;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// T-M31（ADR-0125）：<see cref="ShopViewModel"/> 在 ui/tests 内的直接用例——真实 <see cref="EconomyHost"/>
    ///（此前只有 <c>PresentationAssembly</c> 级间接覆盖）。覆盖货架快照、购买成功/各失败路径后的视图状态、
    /// 事件驱动刷新以及 <see cref="ShopViewModel.Dispose"/> 退订。
    /// </summary>
    public sealed class ShopViewModelTests
    {
        private const long InitialBalance = 500;

        private static readonly Id Coin = new Id("econ.currency.sample_coin");
        private static readonly Id Shop = new Id("econ.vendor.sample_shop");
        private static readonly Id Sword = new Id("item.sample_sword");
        private static readonly Id Shield = new Id("item.sample_shield");

        private const string CurrencyRow =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.currency.sample.name\", " +
            "\"cap\": 100000, \"display_ref\": \"display.sample_coin\"}]";

        // 商人只卖 sword（限量 3，单价 10）；shield 不在售，用来构造 ItemNotSold。
        private const string VendorRow =
            "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.vendor.sample.name\", \"sell_items\": [" +
            "{\"item_id\": \"item.sample_sword\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
            "\"price_amount\": 10, \"stock_limit\": 3}]}]";

        /// <summary>真实 <see cref="EconomyHost"/> 装配：与 <see cref="UiWorldFixture"/> 共用事件总线与背包假宿主。</summary>
        internal static EconomyHost BuildEconomy(UiWorldFixture world)
        {
            string Envelope(string table, string rows) =>
                "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rows + "}";

            var source = new InMemoryDataSource()
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, CurrencyRow))
                .Add(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, VendorRow));
            var registry = new DataRegistry(source, world.EventBus, new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterValidationRule(new EconomyContentValidationRule());
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var host = new EconomyHost(registry, world.EventBus, world.Inventory, new NullExprHostFactory());
            host.Add(world.PlayerId, Coin, InitialBalance, world.PlayerId);
            world.EventBus.DispatchPending();
            return host;
        }

        private static ShopViewModel NewShop(UiWorldFixture world, EconomyHost economy) =>
            new ShopViewModel(world.DataSource, economy, world.PlayerId);

        [Fact]
        public void OpenVendor_PopulatesSellItems_WithPriceCurrencyAndRemainingStock()
        {
            var world = new UiWorldFixture();
            var economy = BuildEconomy(world);
            using var vm = NewShop(world, economy);

            vm.OpenVendor(Shop);

            Assert.Equal(Shop, vm.CurrentVendorId);
            var item = Assert.Single(vm.SellItems);
            Assert.Equal(Sword, item.ItemId);
            Assert.Equal(Coin, item.PriceCurrencyId);
            Assert.Equal(economy.TryGetSellItemPrice(Shop, Sword), item.PriceAmount);
            Assert.Equal(economy.GetStock(Shop, Sword), item.Stock);
            Assert.Equal(InitialBalance, vm.GetPlayerBalance(Coin));
        }

        [Fact]
        public void OpenVendor_UnknownVendor_DegradesToEmptyShelf_WithoutThrowing()
        {
            var world = new UiWorldFixture();
            using var vm = NewShop(world, BuildEconomy(world));

            vm.OpenVendor(new Id("econ.vendor.sample_ghost"));

            Assert.Equal(new Id("econ.vendor.sample_ghost"), vm.CurrentVendorId);
            Assert.Empty(vm.SellItems);
        }

        [Fact]
        public void CloseVendor_ClearsShelf_AndLaterEventsDoNotReopenIt()
        {
            var world = new UiWorldFixture();
            var economy = BuildEconomy(world);
            using var vm = NewShop(world, economy);
            vm.OpenVendor(Shop);
            Assert.NotEmpty(vm.SellItems);

            vm.CloseVendor();
            economy.Add(world.PlayerId, Coin, 1, world.PlayerId);
            world.EventBus.DispatchPending();

            Assert.Null(vm.CurrentVendorId);
            Assert.Empty(vm.SellItems);
        }

        [Fact]
        public void Refresh_WithoutOpenVendor_IsIdempotentNoOp()
        {
            var world = new UiWorldFixture();
            using var vm = NewShop(world, BuildEconomy(world));

            vm.Refresh();
            vm.Refresh();

            Assert.Null(vm.CurrentVendorId);
            Assert.Empty(vm.SellItems);
        }

        [Fact]
        public void Purchase_Success_RefreshesStockAndBalanceViaEvents()
        {
            var world = new UiWorldFixture();
            var economy = BuildEconomy(world);
            using var vm = NewShop(world, economy);
            vm.OpenVendor(Shop);
            var stockBefore = vm.SellItems.Single().Stock!.Value;
            var balanceBefore = vm.GetPlayerBalance(Coin);

            var result = economy.Buy(world.PlayerId, Shop, Sword, 2);
            world.EventBus.DispatchPending();

            Assert.True(result.Success);
            Assert.Equal(stockBefore - 2, vm.SellItems.Single().Stock);
            Assert.Equal(balanceBefore - result.Price, vm.GetPlayerBalance(Coin));
            Assert.Equal(2L * vm.SellItems.Single().PriceAmount, result.Price);
        }

        [Fact]
        public void Purchase_InsufficientFunds_LeavesShelfAndBalanceUnchanged()
        {
            var world = new UiWorldFixture();
            var economy = BuildEconomy(world);
            using var vm = NewShop(world, economy);
            vm.OpenVendor(Shop);
            // 余额降到买不起一件（单价 - 1）。
            economy.SetBalance(world.PlayerId, Coin, vm.SellItems.Single().PriceAmount - 1);
            world.EventBus.DispatchPending();
            var shelfBefore = vm.SellItems.ToArray();
            var balanceBefore = vm.GetPlayerBalance(Coin);

            var result = economy.Buy(world.PlayerId, Shop, Sword, 1);

            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.InsufficientFunds, result.Reason);
            Assert.Equal(0, world.EventBus.DispatchPending()); // 失败不产生任何事件
            Assert.Equal(shelfBefore, vm.SellItems.ToArray());
            Assert.Equal(balanceBefore, vm.GetPlayerBalance(Coin));
        }

        [Fact]
        public void Purchase_MoreThanRemainingStock_FailsWithInsufficientStock_ShelfUnchanged()
        {
            var world = new UiWorldFixture();
            var economy = BuildEconomy(world);
            using var vm = NewShop(world, economy);
            vm.OpenVendor(Shop);
            var remaining = vm.SellItems.Single().Stock!.Value;
            var shelfBefore = vm.SellItems.ToArray();
            var balanceBefore = vm.GetPlayerBalance(Coin);

            var result = economy.Buy(world.PlayerId, Shop, Sword, remaining + 1);

            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.InsufficientStock, result.Reason);
            Assert.Equal(0, world.EventBus.DispatchPending());
            Assert.Equal(shelfBefore, vm.SellItems.ToArray());
            Assert.Equal(balanceBefore, vm.GetPlayerBalance(Coin));
        }

        [Fact]
        public void Purchase_ItemNotSold_AndUnknownVendor_FailWithDistinctReasons_ShelfUnchanged()
        {
            var world = new UiWorldFixture();
            var economy = BuildEconomy(world);
            using var vm = NewShop(world, economy);
            vm.OpenVendor(Shop);
            var shelfBefore = vm.SellItems.ToArray();

            var notSold = economy.Buy(world.PlayerId, Shop, Shield, 1);
            var ghost = economy.Buy(world.PlayerId, new Id("econ.vendor.sample_ghost"), Sword, 1);

            Assert.Equal(PurchaseFailureReason.ItemNotSold, notSold.Reason);
            Assert.Equal(PurchaseFailureReason.UnknownVendor, ghost.Reason);
            Assert.Equal(0, world.EventBus.DispatchPending());
            Assert.Equal(shelfBefore, vm.SellItems.ToArray());
        }

        [Fact]
        public void Purchase_SoldOutItem_StaysOnShelfWithZeroStock()
        {
            var world = new UiWorldFixture();
            var economy = BuildEconomy(world);
            using var vm = NewShop(world, economy);
            vm.OpenVendor(Shop);

            economy.Buy(world.PlayerId, Shop, Sword, vm.SellItems.Single().Stock!.Value);
            world.EventBus.DispatchPending();

            var item = Assert.Single(vm.SellItems);
            Assert.Equal(0, item.Stock);
        }

        /// <summary>同图读档的抑制作用域会压住购买事件本身（见 <see cref="ShopViewModel"/> 构造函数注释），
        /// 只有作用域外正常派发的 <c>save.loaded</c> 负责让已打开的货架整体重建。</summary>
        [Fact]
        public void SaveLoaded_RebuildsOpenShelf_EvenWhenPurchaseEventsWereSuppressed()
        {
            var world = new UiWorldFixture();
            var economy = BuildEconomy(world);
            using var vm = NewShop(world, economy);
            vm.OpenVendor(Shop);
            var stockBefore = vm.SellItems.Single().Stock!.Value;

            using (world.EventBus.SuppressDispatch())
            {
                economy.Buy(world.PlayerId, Shop, Sword, 1);
            }
            world.EventBus.DispatchPending();
            var staleStock = vm.SellItems.Single().Stock;

            world.EventBus.Enqueue(new SaveLoadedEvent(new Id("save.slot_1")));
            world.EventBus.DispatchPending();

            Assert.Equal(stockBefore, staleStock);
            Assert.Equal(economy.GetStock(Shop, Sword), vm.SellItems.Single().Stock);
            Assert.Equal(stockBefore - 1, vm.SellItems.Single().Stock);
        }

        [Fact]
        public void Dispose_StopsRefresh_AndIsIdempotent()
        {
            var world = new UiWorldFixture();
            var economy = BuildEconomy(world);
            var vm = NewShop(world, economy);
            vm.OpenVendor(Shop);
            var stockBefore = vm.SellItems.Single().Stock;

            vm.Dispose();
            vm.Dispose();
            economy.Buy(world.PlayerId, Shop, Sword, 1);
            world.EventBus.DispatchPending();

            Assert.Equal(stockBefore, vm.SellItems.Single().Stock); // 退订后不再刷新
        }
    }
}
