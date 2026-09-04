using System;

namespace Core.Gameplay.Economy
{
    public enum SellFailureReason
    {
        None,

        /// <summary>该商人不存在。</summary>
        UnknownVendor,

        /// <summary>该物品实例不属于该单位背包，或数量不足。</summary>
        NotOwned,
    }

    /// <summary><see cref="IEconomyHost.Sell"/> 的返回值（惯例同 <see cref="PurchaseResult"/>）。</summary>
    public readonly struct SellResult
    {
        public bool Success { get; }

        public SellFailureReason Reason { get; }

        public long Price { get; }

        private SellResult(bool success, SellFailureReason reason, long price)
        {
            Success = success;
            Reason = reason;
            Price = price;
        }

        public static SellResult Ok(long price) => new SellResult(true, SellFailureReason.None, price);

        public static SellResult Fail(SellFailureReason reason)
        {
            if (reason == SellFailureReason.None)
            {
                throw new ArgumentException("失败结果必须携带非 None 的失败原因", nameof(reason));
            }

            return new SellResult(false, reason, 0);
        }
    }
}
