using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <see cref="IEconomyHost"/> 唯一实现（见 08 第 9 节 Economy 行）。
    /// <para>
    /// 判断记录 1——收购价（<see cref="Sell"/>）的默认公式与 <c>buy_price_rule</c> 覆盖：08 第 7.2 节
    /// <c>buy_price_rule</c> 描述"<c>self</c> 分组给出 <c>self.item_level</c>/<c>self.quality</c>"，
    /// 但 <c>core/rules/expr_host.RulesExprSchema</c> 的 <c>self</c> 分组是"当前单位"视角（<c>self</c>
    /// = <see cref="IExprHostFactory.CreateFor"/> 的 <c>selfId</c>，见该接口注释），没有
    /// <c>item_level</c>/<c>quality</c> 这两个键——那是"正在出售的物品"的属性，不是单位属性，本任务
    /// 没有获得任何 <c>ItemExprGroupProvider</c> 一类把物品属性接入 <c>self</c> 分组的契约（这类
    /// provider 若存在也会与"当前单位"语义的 <c>self</c> 冲突，需要新的分组，超出任务书拍板范围）。
    /// 本模块因此按任务书"本版只支持 Expr 返回数值的简单形式"原样落地：<c>buy_price_rule</c> 存在时，
    /// 经 <c>IExprHostFactory.CreateFor(unitId, null, null)</c> 求值，取其数值结果作为"每件收购价"
    /// （内容作者目前只能引用 <c>self.*</c> 里"出售者"自身的既有字段，如
    /// <c>self.level</c>，不能引用物品属性——这是一处记录在案、未解决的契约缺口，不在本任务修复范围）；
    /// 不存在时按拍板默认公式 <c>DefaultBuyPricePct × 该物品在任一商人的售价</c>（找不到售价则收购价
    /// 为 0，货币种类退化为"全局已加载的第一种货币"，见 <see cref="ResolveSellCurrency"/>）。
    /// </para>
    /// <para>
    /// 判断记录 2——<see cref="Buy"/> 的"满包回滚不扣款"顺序：先检查限量库存与余额（只读，不改任何
    /// 状态），确认余额足够后才调用 <c>IInventoryHost.AddItem</c>；若加入数量（用前后
    /// <c>CountOf</c> 差值判定，同 <c>core/gameplay/loot.LootHost</c> 判断记录 7）不足
    /// <paramref name="count"/>，回滚已加入的部分并直接返回失败，**不调用** <see cref="TryPay"/>——
    /// 保证"背包放不下"这一失败分支绝不会产生任何货币/库存副作用，不需要"先扣款、货物给不了再退款"
    /// 这种更复杂、更容易出错的补偿事务。
    /// </para>
    /// </summary>
    public sealed class EconomyHost : IEconomyHost
    {
        private sealed class StockState
        {
            public int? Remaining;
            public double? TimerRemaining;
        }

        private readonly IDataRegistryView _registry;
        private readonly IEventBus _bus;
        private readonly IInventoryHost _inventory;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly EconomyOptions _options;
        private readonly IExprDiagnostics _diagnostics;
        private readonly IExprSchema? _conditionSchema;

        private readonly Dictionary<Id, CurrencyDef> _currencies = new Dictionary<Id, CurrencyDef>();
        private readonly Dictionary<Id, VendorDef> _vendors = new Dictionary<Id, VendorDef>();
        private readonly List<Id> _currencyOrder = new List<Id>();
        private readonly List<Id> _vendorOrder = new List<Id>();
        private readonly Dictionary<Id, Dictionary<Id, long>> _balances = new Dictionary<Id, Dictionary<Id, long>>();
        private readonly Dictionary<Id, Dictionary<Id, StockState>> _stock = new Dictionary<Id, Dictionary<Id, StockState>>();

        public EconomyHost(
            IDataRegistryView registry,
            IEventBus bus,
            IInventoryHost inventory,
            IExprHostFactory exprHostFactory,
            EconomyOptions? options = null,
            IExprDiagnostics? diagnostics = null,
            IExprSchema? conditionSchema = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _options = options ?? new EconomyOptions();
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();
            _conditionSchema = conditionSchema;

            ReloadFromRegistry();

            // P2-05 同类缓存收口（外部审计 audit-c9ff301-20260909 followup-2026-09-10）：
            // _currencies/_vendors/_currencyOrder/_vendorOrder 此前只在构造期从 registry 读取
            // 一次、永久常驻，与 SkillDefCache/ArchetypeRegistry/StatHost/LootHost 同一类模式。
            // _balances（单位持有的货币余额）是纯运行期状态，与 _currencies 定义表无耦合，reload
            // 不触碰。_stock（限量商品剩余/补货计时）派生自 _vendors 的 sell_items 配置，是本类型
            // 与 Quest/Dialog/Loot 场景不同的地方——见 <see cref="ReloadFromRegistry"/> 判断记录。
            _bus.Subscribe<Core.Foundation.DataRegistry.DataLoadCompletedEvent>(
                Core.Foundation.DataRegistry.DataRegistryEventKeys.LoadCompleted, _ => ReloadFromRegistry());
        }

        /// <summary>
        /// 判断记录（<see cref="_stock"/> 的选择性保留）：_currencies/_vendors/两个 order 列表整体
        /// 清空重建（同 ArchetypeRegistry.ReloadFromRegistry），但 _stock 是"已存在商人的限量商品
        /// 剩余库存/补货倒计时"这一运行期状态，若整体清空重建会把玩家已经买剩的库存/补货进度全部
        /// 重置回满库存——同 QuestHost.Reload 判断记录"不清空运行期状态"的同一条原则，只是这里的
        /// 运行期状态（_stock）在构造期是从 def 派生初始化的，不能像 Quest 进度那样完全独立于
        /// reload 逻辑之外。处理方式：已存在的 (vendorId, itemId) 组合保留原 StockState 不动；
        /// 新出现的 vendorId 或已有商人新增的 sell_item（此前不存在于 _stock[vendorId]）按 def
        /// 初始化为满库存——与"首次构造/首次遇到这个商品"语义一致。已下架但仍留在 _stock 里的旧
        /// itemId 条目不主动清理（不影响任何查询路径：<see cref="GetStock"/>/<see
        /// cref="TryConsumeStock"/>/<see cref="Update"/> 均按当前 <c>_vendors[vendorId].SellItems</c>
        /// 反向驱动访问 _stock，不会遍历到这些孤儿 key）。
        /// </summary>
        private void ReloadFromRegistry()
        {
            _currencies.Clear();
            _currencyOrder.Clear();
            foreach (var record in _registry.GetAll(EconomySchemas.Currency.Name))
            {
                var def = EconomyDataParser.ParseCurrency(record);
                _currencies[def.Id] = def;
                _currencyOrder.Add(def.Id);
            }

            _currencyOrder.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));

            _vendors.Clear();
            _vendorOrder.Clear();
            foreach (var record in _registry.GetAll(EconomySchemas.Vendor.Name))
            {
                var def = EconomyDataParser.ParseVendor(record, _conditionSchema);
                _vendors[def.Id] = def;
                _vendorOrder.Add(def.Id);

                if (!_stock.TryGetValue(def.Id, out var stockByItem))
                {
                    stockByItem = new Dictionary<Id, StockState>();
                    _stock[def.Id] = stockByItem;
                }

                foreach (var sellItem in def.SellItems)
                {
                    if (stockByItem.ContainsKey(sellItem.ItemId))
                    {
                        continue;
                    }

                    stockByItem[sellItem.ItemId] = new StockState
                    {
                        Remaining = sellItem.StockLimit,
                        TimerRemaining = sellItem.RestockPolicy == VendorRestockPolicy.Timer ? sellItem.RestockTimer : null,
                    };
                }
            }

            _vendorOrder.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
        }

        /// <summary>全部已加载货币 id，按 <see cref="Id"/> 序数排列（供 <see
        /// cref="CurrencyPersistable.Save"/> 遍历一个单位可能持有的全部货币种类）。</summary>
        public IReadOnlyList<Id> CurrencyIds => _currencyOrder;

        // -----------------------------------------------------------------
        // 余额
        // -----------------------------------------------------------------

        public void RegisterUnit(Id unitId)
        {
            if (!_balances.ContainsKey(unitId))
            {
                _balances[unitId] = new Dictionary<Id, long>();
            }
        }

        public long GetBalance(Id unitId, Id currencyId) =>
            _balances.TryGetValue(unitId, out var wallet) && wallet.TryGetValue(currencyId, out var v) ? v : 0;

        public bool Add(Id unitId, Id currencyId, long amount, Id sourceId)
        {
            if (!_currencies.TryGetValue(currencyId, out var currency))
            {
                throw new ArgumentException($"未知的货币 \"{currencyId}\"", nameof(currencyId));
            }

            var wallet = EnsureWallet(unitId);
            var old = wallet.TryGetValue(currencyId, out var current) ? current : 0;
            var raw = old + amount;
            var capped = currency.Cap.HasValue ? Math.Min(raw, currency.Cap.Value) : raw;
            var newValue = Math.Max(0, capped);

            if (newValue != old)
            {
                wallet[currencyId] = newValue;
                _bus.Enqueue(new CurrencyChangedEvent(unitId, currencyId, old, newValue));
            }

            return true;
        }

        /// <summary>把 <paramref name="unitId"/> 的 <paramref name="currencyId"/> 余额直接替换为
        /// <paramref name="amount"/>（按 <see cref="CurrencyDef.Cap"/> 夹取到 <c>[0, cap]</c>），不与
        /// 当前余额相加；供 <see cref="CurrencyPersistable.Load"/> 读档时使用，保证读档语义是"替换"而
        /// 非"叠加"，连续多次 Load 同一快照结果幂等。实际发生变化时发 <c>economy.currency_changed</c>，
        /// 与 <see cref="Add"/> 共用同一事件形状便于下游统一消费。</summary>
        public bool SetBalance(Id unitId, Id currencyId, long amount)
        {
            if (!_currencies.TryGetValue(currencyId, out var currency))
            {
                throw new ArgumentException($"未知的货币 \"{currencyId}\"", nameof(currencyId));
            }

            var wallet = EnsureWallet(unitId);
            var old = wallet.TryGetValue(currencyId, out var current) ? current : 0;
            var capped = currency.Cap.HasValue ? Math.Min(amount, currency.Cap.Value) : amount;
            var newValue = Math.Max(0, capped);

            if (newValue != old)
            {
                wallet[currencyId] = newValue;
                _bus.Enqueue(new CurrencyChangedEvent(unitId, currencyId, old, newValue));
            }

            return true;
        }

        public bool TryPay(Id unitId, Id currencyId, long amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "TryPay 的 amount 不能为负数");
            }

            if (!_currencies.ContainsKey(currencyId))
            {
                throw new ArgumentException($"未知的货币 \"{currencyId}\"", nameof(currencyId));
            }

            if (GetBalance(unitId, currencyId) < amount)
            {
                return false;
            }

            Add(unitId, currencyId, -amount, sourceId: unitId);
            return true;
        }

        private Dictionary<Id, long> EnsureWallet(Id unitId)
        {
            if (!_balances.TryGetValue(unitId, out var wallet))
            {
                wallet = new Dictionary<Id, long>();
                _balances[unitId] = wallet;
            }

            return wallet;
        }

        // -----------------------------------------------------------------
        // Buy / Sell
        // -----------------------------------------------------------------

        public PurchaseResult Buy(Id unitId, Id vendorId, Id itemId, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "count 必须为正数");
            }

            if (!_vendors.TryGetValue(vendorId, out var vendor))
            {
                return PurchaseResult.Fail(PurchaseFailureReason.UnknownVendor);
            }

            var sellItem = FindSellItem(vendor, itemId);
            if (sellItem == null)
            {
                return PurchaseResult.Fail(PurchaseFailureReason.ItemNotSold);
            }

            var remaining = GetStock(vendorId, itemId);
            if (remaining.HasValue && remaining.Value < count)
            {
                return PurchaseResult.Fail(PurchaseFailureReason.InsufficientStock);
            }

            var price = sellItem.PriceAmount * count;
            if (GetBalance(unitId, sellItem.PriceCurrencyId) < price)
            {
                return PurchaseResult.Fail(PurchaseFailureReason.InsufficientFunds);
            }

            // CR130-01 根治（外部审计 audit-5c444f1-20260908）：AddItem 成功那一刻已经把 item.added
            // 排入事件总线待发队列（IEventBus.Enqueue 只入队、不立即派发），下面"数量不足则
            // RollbackAdd"补的是独立一条 item.removed；两条事件在同一次 DispatchPending 里先后派发
            // 时，下游订阅者（如 QuestHost.HandleItemAdded 的 consumeOnProgress）会把先到的
            // item.added 当真、立即消费玩家已有的同模板物品推进任务进度，后到的 item.removed 抵消不
            // 了这个副作用——本次购买"数量"确实回滚了，但任务系统已经误判、多扣了一份玩家原有的物品
            // （外部审计复现：容量为 1 的 Partial 背包已有 A5，购买失败回滚后变 A4，任务却记了一次
            // 消费进度）。惯例同 RewardDispatcher.GrantItems/QuestHost.TurnIn：_inventory 实现
            // IBatchableInventoryHost 时把 AddItem 与其失败补偿一并包进一次事务，失败时 using 块结束
            // 触发 Dispose（未 Commit 即回滚）把背包状态与缓存事件一并撤销，下游完全观察不到这次失败
            // 的购买发生过，不需要再调用 RollbackAdd；不支持事务的宿主（多数测试用的 Fake）退回历史
            // 行为，逐项 RollbackAdd。
            var transaction = _inventory is IBatchableInventoryHost batchable ? batchable.BeginBatch() : null;
            using (transaction)
            {
                var before = _inventory.CountOf(unitId, itemId);
                _inventory.AddItem(unitId, itemId, count);
                var added = Math.Max(0, _inventory.CountOf(unitId, itemId) - before);

                if (added < count)
                {
                    if (transaction == null && added > 0)
                    {
                        RollbackAdd(unitId, itemId, added);
                    }

                    return PurchaseResult.Fail(PurchaseFailureReason.InventoryFull);
                }

                transaction?.Commit();
            }

            TryPay(unitId, sellItem.PriceCurrencyId, price);
            DecrementStock(vendorId, itemId, count);
            _bus.Enqueue(new ItemPurchasedEvent(unitId, vendorId, itemId, count, price));
            return PurchaseResult.Ok(price);
        }

        public SellResult Sell(Id unitId, Id vendorId, Id itemInstanceId, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "count 必须为正数");
            }

            if (!_vendors.ContainsKey(vendorId))
            {
                return SellResult.Fail(SellFailureReason.UnknownVendor);
            }

            var instance = _inventory.FindInstance(unitId, itemInstanceId);
            if (!instance.HasValue || instance.Value.Count < count)
            {
                return SellResult.Fail(SellFailureReason.NotOwned);
            }

            var templateId = instance.Value.TemplateId;
            var (perItemPrice, currencyId) = ComputeSellPrice(vendorId, unitId, templateId);
            var total = Math.Max(0L, (long)(perItemPrice * count));

            // CR130-01 根治：Sell 目前只有一次 RemoveItem（成功即整份移除、失败不落地任何变化，见
            // InventoryHost.RemoveItem——不是"部分成功再补偿"的多步操作），本身不存在
            // Buy/RollbackAdd 那种"先落地再补偿"的事件错位窗口；仍然按 Buy 同款惯例包进事务，是为了
            // 与"购买/出售/补偿全部走批量事务"这一统一口径保持一致，也让日后若在此处新增任何补偿步骤
            // 天然获得同样的原子性，不需要再补一次事务改造。
            var transaction = _inventory is IBatchableInventoryHost batchable ? batchable.BeginBatch() : null;
            using (transaction)
            {
                if (!_inventory.RemoveItem(unitId, itemInstanceId, count))
                {
                    return SellResult.Fail(SellFailureReason.NotOwned);
                }

                transaction?.Commit();
            }

            Add(unitId, currencyId, total, sourceId: vendorId);
            _bus.Enqueue(new ItemSoldEvent(unitId, vendorId, itemInstanceId, total));
            return SellResult.Ok(total);
        }

        private (double PerItemPrice, Id CurrencyId) ComputeSellPrice(Id vendorId, Id unitId, Id templateId)
        {
            var (foundPrice, foundCurrency) = FindAnyVendorSellPrice(templateId);
            var vendor = _vendors[vendorId];

            if (vendor.BuyPriceRule != null)
            {
                var host = _exprHostFactory.CreateFor(unitId, null, null);
                var value = ExprEvaluator.Evaluate(vendor.BuyPriceRule, host, _diagnostics);
                var perItem = value.IsNumeric ? Math.Max(0.0, value.ToDouble()) : 0.0;
                return (perItem, foundCurrency ?? ResolveSellCurrency());
            }

            return foundCurrency.HasValue
                ? (foundPrice * _options.DefaultBuyPricePct, foundCurrency.Value)
                : (0.0, ResolveSellCurrency());
        }

        /// <summary>按 <c>Id</c> 序数遍历全部已加载商人（判断记录 1"任一商人"），返回第一条命中
        /// <paramref name="templateId"/> 的 <c>sell_items</c> 项的价格与货币；找不到返回 <c>(0,
        /// null)</c>。</summary>
        private (long Price, Id? CurrencyId) FindAnyVendorSellPrice(Id templateId)
        {
            foreach (var vendorId in _vendorOrder)
            {
                foreach (var sellItem in _vendors[vendorId].SellItems)
                {
                    if (sellItem.ItemId.Equals(templateId))
                    {
                        return (sellItem.PriceAmount, sellItem.PriceCurrencyId);
                    }
                }
            }

            return (0, null);
        }

        /// <summary>全局找不到任何该物品的售价数据时的货币兜底（判断记录 1）：取全部已加载货币里
        /// 按 <c>Id</c> 序数最靠前的一种；一种货币都没有登记属于内容配置错误，抛异常而不是静默捏造
        /// 一个 <c>Id</c>。</summary>
        private Id ResolveSellCurrency()
        {
            if (_currencyOrder.Count == 0)
            {
                throw new InvalidOperationException("没有任何已加载的 econ.currency，无法确定收购价的货币种类");
            }

            return _currencyOrder[0];
        }

        private static VendorSellItem? FindSellItem(VendorDef vendor, Id itemId)
        {
            foreach (var item in vendor.SellItems)
            {
                if (item.ItemId.Equals(itemId))
                {
                    return item;
                }
            }

            return null;
        }

        private void RollbackAdd(Id unitId, Id templateId, int amount)
        {
            var remaining = amount;
            foreach (var instance in _inventory.ListItems(unitId))
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (!instance.TemplateId.Equals(templateId))
                {
                    continue;
                }

                var take = Math.Min(remaining, instance.Count);
                _inventory.RemoveItem(unitId, instance.InstanceId, take);
                remaining -= take;
            }
        }

        // -----------------------------------------------------------------
        // 库存 / 补货
        // -----------------------------------------------------------------

        public int? GetStock(Id vendorId, Id itemId) =>
            _stock.TryGetValue(vendorId, out var byItem) && byItem.TryGetValue(itemId, out var state) ? state.Remaining : null;

        /// <summary>AUD-03 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：<c>timer</c>
        /// 补货策略当前剩余倒计时秒数，供 <see cref="VendorStockPersistable.Save"/> 一并持久化（见
        /// 该类型判断记录修订——不再是"可选、不存"，见 <see cref="SetStock"/> 判断记录同款修订）。
        /// 未知商人/物品，或该物品不是 <c>timer</c> 策略（<see cref="StockState.TimerRemaining"/>
        /// 本就为 null），返回 null。</summary>
        public double? GetStockTimerRemaining(Id vendorId, Id itemId) =>
            _stock.TryGetValue(vendorId, out var byItem) && byItem.TryGetValue(itemId, out var state) ? state.TimerRemaining : null;

        /// <summary>全部已加载商人 id，按 <see cref="Id"/> 序数排列（供 <see
        /// cref="VendorStockPersistable"/> 遍历）。</summary>
        public IReadOnlyList<Id> VendorIds => _vendorOrder;

        /// <summary>按 id 取回商人强类型定义（供 <see cref="VendorStockPersistable"/> 遍历
        /// <see cref="VendorDef.SellItems"/>）；未知商人 id 抛 <see cref="ArgumentException"/>。</summary>
        public VendorDef GetVendorDef(Id vendorId) =>
            _vendors.TryGetValue(vendorId, out var def) ? def : throw new ArgumentException($"未知的商人 \"{vendorId}\"", nameof(vendorId));

        /// <summary>
        /// 供 <see cref="VendorStockPersistable.Load"/> 直接写入某商人某物品的剩余库存（不经
        /// <see cref="DecrementStock"/> 的"只减不加"语义，也不发任何事件——读档恢复状态不是一次
        /// "补货"，见 <c>core/gameplay/loot.LootHost.RestoreDropped</c> 同款判断记录"这不是发生了
        /// 一次新的……只是恢复既有状态"）。未知商人/物品静默忽略（存档可能来自内容已变更的旧版本）。
        /// <para>
        /// AUD-03 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：新增可选参数 <paramref
        /// name="timerRemaining"/>——此前本方法只写 <see cref="StockState.Remaining"/>，从不触碰
        /// <see cref="StockState.TimerRemaining"/>，导致同一宿主原地读档（不重新构造 <see
        /// cref="EconomyHost"/>）时，倒计时仍是读档前那个正在跑的旧值，读档后继续沿用会让补货比
        /// 快照时刻应有的时间提前触发（真实探针复现：t=2 存档时倒计时剩 8 秒，读档前又跑了 7 秒
        /// 只剩 1 秒，读档后再过 1 秒立即补货，而不是应有的"剩 8 秒"）。现在的语义——<paramref
        /// name="timerRemaining"/> 有值时按其显式写入 <see cref="StockState.TimerRemaining"/>
        /// （对应存档里存了 <c>timer_remaining</c> 字段的条目）；为 <c>null</c>（旧格式存档没有这个
        /// 可选字段，或该物品并非 <c>timer</c> 补货策略）时，若该物品确实是 <c>timer</c> 策略，
        /// 重置为该策略定义的完整周期 <see cref="VendorSellItem.RestockTimer"/>（旧档缺失时的兜底
        /// 语义，呼应 10 第 2.3 节勘误），不是"保持不动"——这正是本次修复前的缺陷根源。
        /// </para>
        /// </summary>
        public void SetStock(Id vendorId, Id itemId, int? remaining, double? timerRemaining = null)
        {
            if (!_stock.TryGetValue(vendorId, out var byItem) || !byItem.TryGetValue(itemId, out var state))
            {
                return;
            }

            state.Remaining = remaining;

            if (timerRemaining.HasValue)
            {
                state.TimerRemaining = timerRemaining;
                return;
            }

            if (_vendors.TryGetValue(vendorId, out var vendorDef))
            {
                foreach (var sellItem in vendorDef.SellItems)
                {
                    if (sellItem.ItemId.Equals(itemId) && sellItem.RestockPolicy == VendorRestockPolicy.Timer
                        && sellItem.RestockTimer.HasValue)
                    {
                        state.TimerRemaining = sellItem.RestockTimer;
                        break;
                    }
                }
            }
        }

        private void DecrementStock(Id vendorId, Id itemId, int count)
        {
            if (_stock.TryGetValue(vendorId, out var byItem) && byItem.TryGetValue(itemId, out var state) && state.Remaining.HasValue)
            {
                state.Remaining = Math.Max(0, state.Remaining.Value - count);
            }
        }

        public void OnMapEnter(Id mapId)
        {
            foreach (var vendorId in _vendorOrder)
            {
                var vendor = _vendors[vendorId];
                if (!vendor.MapId.HasValue || !vendor.MapId.Value.Equals(mapId))
                {
                    continue;
                }

                var restocked = false;
                foreach (var sellItem in vendor.SellItems)
                {
                    if (sellItem.RestockPolicy != VendorRestockPolicy.OnMapEnter || !sellItem.StockLimit.HasValue)
                    {
                        continue;
                    }

                    _stock[vendorId][sellItem.ItemId].Remaining = sellItem.StockLimit;
                    restocked = true;
                }

                if (restocked)
                {
                    _bus.Enqueue(new VendorRestockedEvent(vendorId));
                }
            }
        }

        public void Update(double dt)
        {
            foreach (var vendorId in _vendorOrder)
            {
                var vendor = _vendors[vendorId];
                var restocked = false;

                foreach (var sellItem in vendor.SellItems)
                {
                    if (sellItem.RestockPolicy != VendorRestockPolicy.Timer)
                    {
                        continue;
                    }

                    var state = _stock[vendorId][sellItem.ItemId];
                    if (!state.TimerRemaining.HasValue || !sellItem.RestockTimer.HasValue)
                    {
                        continue;
                    }

                    state.TimerRemaining -= dt;
                    while (state.TimerRemaining.HasValue && state.TimerRemaining.Value <= 0)
                    {
                        state.Remaining = sellItem.StockLimit;
                        state.TimerRemaining += sellItem.RestockTimer.Value;
                        restocked = true;
                    }
                }

                if (restocked)
                {
                    _bus.Enqueue(new VendorRestockedEvent(vendorId));
                }
            }
        }
    }
}
