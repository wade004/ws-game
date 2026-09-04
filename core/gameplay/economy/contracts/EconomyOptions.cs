namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <see cref="EconomyHost"/> 的构造期策略配置（见 08 第 9 节 Economy 行"策略配置项：收购价规则、
    /// 限量刷新周期"）。
    /// </summary>
    public sealed class EconomyOptions
    {
        /// <summary>没有 <see cref="VendorDef.BuyPriceRule"/> 时，收购价的默认公式：
        /// <c>该物品在任一商人的售价 × DefaultBuyPricePct</c>（任务书拍板，找不到售价时收购价为 0），
        /// 默认 0.25。</summary>
        public double DefaultBuyPricePct { get; set; } = 0.25;
    }
}
