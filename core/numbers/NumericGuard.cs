using System;

namespace Core.Numbers
{
    /// <summary>
    /// ADR-0125 D17：numbers 各宿主写入口共用的数值入参校验。非有限数（NaN、±Infinity）进入
    /// 聚合/夹取/比较后会产生"每次都不等于自己"（<c>NaN != NaN</c> 使变化检测恒真，重复发布事件）、
    /// 夹取对 NaN 失效（当前值被写成 NaN）等静默损坏，因此在写入口一律前置拒绝，不改任何状态、不发事件。
    /// </summary>
    internal static class NumericGuard
    {
        /// <summary><paramref name="value"/> 为 NaN 或 ±Infinity 时抛 <see cref="ArgumentOutOfRangeException"/>。
        /// 必须在任何状态变更之前调用。</summary>
        internal static void RequireFinite(double value, string paramName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "数值入参必须是有限数（不接受 NaN 与 ±Infinity）");
            }
        }
    }
}
