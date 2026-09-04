using System;

namespace Core.Gameplay.Economy
{
    public enum PurchaseFailureReason
    {
        None,

        /// <summary>该商人不存在。</summary>
        UnknownVendor,

        /// <summary>该商人不出售此物品。</summary>
        ItemNotSold,

        /// <summary>限量库存不足。</summary>
        InsufficientStock,

        /// <summary>货币余额不足。</summary>
        InsufficientFunds,

        /// <summary>背包放不下（见 <c>EconomyHost.Buy</c> 判断记录"满包回滚不扣款"）。</summary>
        InventoryFull,
    }

    /// <summary><see cref="IEconomyHost.Buy"/> 的返回值（08 第 9 节 Economy 行 <c>EconomyHost.buy</c>
    /// 契约签名未给出返回类型，本模块按现有 <c>EquipResult</c> 惯例补上）。</summary>
    public readonly struct PurchaseResult
    {
        public bool Success { get; }

        public PurchaseFailureReason Reason { get; }

        public long Price { get; }

        private PurchaseResult(bool success, PurchaseFailureReason reason, long price)
        {
            Success = success;
            Reason = reason;
            Price = price;
        }

        public static PurchaseResult Ok(long price) => new PurchaseResult(true, PurchaseFailureReason.None, price);

        public static PurchaseResult Fail(PurchaseFailureReason reason)
        {
            if (reason == PurchaseFailureReason.None)
            {
                throw new ArgumentException("失败结果必须携带非 None 的失败原因", nameof(reason));
            }

            return new PurchaseResult(false, reason, 0);
        }
    }
}
