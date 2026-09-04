using Core.Foundation.Common;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// 货币与商人契约（见 08 第 9 节汇总表 Economy 行 <c>EconomyHost.buy/sell(...)</c>）。<see
    /// cref="EconomyHost"/> 是唯一实现。
    /// </summary>
    public interface IEconomyHost
    {
        /// <summary>为 <paramref name="unitId"/> 建立一份货币余额记录（默认全部为 0）；重复调用无副
        /// 作用。</summary>
        void RegisterUnit(Id unitId);

        long GetBalance(Id unitId, Id currencyId);

        /// <summary>增减 <paramref name="unitId"/> 的 <paramref name="currencyId"/> 余额（<paramref
        /// name="amount"/> 可正可负），按 <see cref="CurrencyDef.Cap"/> 夹取到 <c>[0, cap]</c>；实际
        /// 发生变化时发 <c>economy.currency_changed</c>。<paramref name="sourceId"/> 供事件追溯来源，
        /// 不做任何权限校验（惯例同 <c>core/gameplay/world_state.IWorldState.Set</c>"谁能写"）。</summary>
        bool Add(Id unitId, Id currencyId, long amount, Id sourceId);

        /// <summary>尝试扣除 <paramref name="amount"/>（要求非负），余额不足则不生效、返回 false。</summary>
        bool TryPay(Id unitId, Id currencyId, long amount);

        PurchaseResult Buy(Id unitId, Id vendorId, Id itemId, int count);

        SellResult Sell(Id unitId, Id vendorId, Id itemInstanceId, int count);

        /// <summary><paramref name="mapId"/> 上全部 <c>restock_policy=on_map_enter</c> 的商人条目补满，
        /// 每个实际发生补货的商人发一次 <c>economy.vendor_restocked</c>。</summary>
        void OnMapEnter(Id mapId);

        /// <summary>推进 <c>restock_policy=timer</c> 的补货倒计时（见 <see
        /// cref="VendorRestockPolicy.Timer"/>）。</summary>
        void Update(double dt);

        /// <summary>当前剩余库存；无限量或未知商人/物品返回 null。</summary>
        int? GetStock(Id vendorId, Id itemId);
    }
}
