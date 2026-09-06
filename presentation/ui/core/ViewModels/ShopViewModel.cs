using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Gameplay.Economy;

namespace Presentation.Ui
{
    /// <summary>商人出售清单一条的快照（见 08_玩法层_掉落任务对话关卡.md 第 7.2 节
    /// <c>sell_items</c> 元素结构）。</summary>
    public readonly struct VendorSellItemSnapshot
    {
        public Id ItemId { get; }

        public Id PriceCurrencyId { get; }

        public long PriceAmount { get; }

        /// <summary>剩余库存；null 表示不限量（见 <see cref="VendorSellItem.StockLimit"/>）。</summary>
        public int? Stock { get; }

        public VendorSellItemSnapshot(Id itemId, Id priceCurrencyId, long priceAmount, int? stock)
        {
            ItemId = itemId;
            PriceCurrencyId = priceCurrencyId;
            PriceAmount = priceAmount;
            Stock = stock;
        }
    }

    /// <summary>
    /// 商店视图模型（拍板 7，见 09_表现层.md 第 7.1 节 UI 组成清单"商店"；补齐审计发现的"商店 UI
    /// 单元无视图模型/面板，只有 UiIntents.Buy/Sell"缺口）：展示当前打开的商人的出售清单/库存/价格与
    /// 玩家各货币余额；买卖操作本身走既有 <see cref="UiIntents.Buy"/>/<see cref="UiIntents.Sell"/>
    /// 意图（铁律 P3），本视图模型只读不提交任何写操作。
    /// <para>
    /// 判断记录（依赖 <see cref="EconomyHost"/> 具体类型而非 <see cref="IEconomyHost"/> 接口）：
    /// <see cref="IEconomyHost"/> 只提供 <see cref="IEconomyHost.GetStock"/>/<see cref="IEconomyHost.GetBalance"/>
    /// 等运行期读写，没有暴露"某商人登记了哪些出售条目/单条的货币与单价"（<see cref="VendorDef"/>/
    /// <see cref="VendorSellItem"/>）——这两者是 <see cref="EconomyHost"/> 的具体公开成员
    /// （<c>GetVendorDef</c>/<c>VendorIds</c>），不在接口契约里。本视图模型需要这份静态清单才能渲染
    /// 商店货架，因此收紧持有具体类型，惯例同 <c>core/gameplay/assembly.TimeModelSwitch</c> 判断记录
    /// "字段类型从 ITurnScheduler 收紧为具体类型 TurnScheduler"——接口契约暂缺对应能力时，按同一惯例
    /// 直接用已有的具体只读查询方法，不为此新造一个只多这两个方法的接口。
    /// </para>
    /// </summary>
    public sealed class ShopViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly EconomyHost _economy;
        private readonly Id _playerId;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly List<VendorSellItemSnapshot> _sellItems = new List<VendorSellItemSnapshot>();

        /// <summary>当前打开的商人；未打开任何商店时为 null（见 <see cref="OpenVendor"/>/
        /// <see cref="CloseVendor"/>）。</summary>
        public Id? CurrentVendorId { get; private set; }

        /// <summary><see cref="CurrentVendorId"/> 的出售清单；未打开商店时为空列表。</summary>
        public IReadOnlyList<VendorSellItemSnapshot> SellItems => _sellItems;

        public ShopViewModel(IUiDataSource dataSource, EconomyHost economy, Id playerId)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _playerId = playerId;

            _subscriptions.Add(_dataSource.Subscribe(EconomyEventKeys.CurrencyChanged, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(EconomyEventKeys.VendorRestocked, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(EconomyEventKeys.ItemPurchased, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(EconomyEventKeys.ItemSold, OnRelevantEvent));
        }

        /// <summary>打开一个商人的货架（见 09 第 7.1 节"商店"面板，具体交互——点击 NPC 触发对话/进店
        /// 的判定——不属于本视图模型职责，由具体游戏的对话/交互层在决定"打开商店"后调用本方法）。</summary>
        public void OpenVendor(Id vendorId)
        {
            CurrentVendorId = vendorId;
            Refresh();
        }

        /// <summary>关闭当前货架；未打开时幂等。</summary>
        public void CloseVendor()
        {
            CurrentVendorId = null;
            _sellItems.Clear();
        }

        /// <summary>玩家某货币的当前余额（经 <see cref="IEconomyHost.GetBalance"/> 只读查询，供货架
        /// UI 判断"买不买得起"）。</summary>
        public long GetPlayerBalance(Id currencyId) => _economy.GetBalance(_playerId, currencyId);

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            _sellItems.Clear();
            if (CurrentVendorId == null)
            {
                return;
            }

            // EconomyHost.GetVendorDef 对未知商人 id 抛 ArgumentException（该方法注释"未知商人 id
            // 抛 ArgumentException"，服务于 VendorStockPersistable 这类"调用方已知 id 一定存在"的场景）；
            // 本视图模型的调用方（游戏层交互代码）可能传入一个尚未登记/已被数据变更移除的 id，按既有
            // UI 视图模型惯例（InventoryViewModel/QuestLogViewModel 对"查不到"的一贯处理）静默退化为
            // 空货架，不向上抛异常中断 UI 刷新。
            if (!_economy.VendorIds.Contains(CurrentVendorId.Value))
            {
                return;
            }

            var def = _economy.GetVendorDef(CurrentVendorId.Value);
            foreach (var sellItem in def.SellItems)
            {
                var stock = _economy.GetStock(CurrentVendorId.Value, sellItem.ItemId);
                _sellItems.Add(new VendorSellItemSnapshot(sellItem.ItemId, sellItem.PriceCurrencyId, sellItem.PriceAmount, stock));
            }
        }

        public void Dispose()
        {
            foreach (var handle in _subscriptions)
            {
                handle.Dispose();
            }
            _subscriptions.Clear();
        }
    }
}
