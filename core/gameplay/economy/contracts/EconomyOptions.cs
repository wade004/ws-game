using Core.Foundation.Common;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <see cref="EconomyHost"/> 的构造期策略配置（见 08 第 9 节 Economy 行"策略配置项：收购价规则、
    /// 限量刷新周期"）。
    /// </summary>
    public sealed class EconomyOptions
    {
        /// <summary>
        /// 售价比例策略项（分阶段落地计划 T-N4-6；ADR-0034 决策 2"售价 = 基准价值 × 售价比例
        /// （策略配置项，建议四分之一）"；08 第 7.4 节修订段）：没有 <see cref="VendorDef.BuyPriceRule"/>
        /// 时，售价（玩家出售给商人的收购价）= 本比例 × 基准价值。默认 0.25，与 ADR"建议四分之一"
        /// 一致。
        /// <para>
        /// 判断记录（重定位，未改名/未新增字段——设计层裁定（2026-09-15）：采纳，落地改动点清单
        /// M2"<c>DefaultBuyPricePct</c> 重定位为'售价比例'策略项"）：本字段 T-N4-6 之前的语义是
        /// "<c>该物品在任一商人的售价 × DefaultBuyPricePct</c>"（"该物品在任一商人的售价"取
        /// <c>sell_items[].price_amount</c> 手填值，找不到则为 0）；T-N4-6 起改为"基准价值 ×
        /// 本比例"（基准价值来自 <see cref="EconomyPriceFormula"/>，<c>item.template</c>/
        /// <c>econ.value_curve</c> 不可解析——即本注册表未加载物品数据——时回退旧的"任一商人手填
        /// 价格"，保证既有隔离测试场景不受影响，见 <c>EconomyHost.ComputeSellPrice</c> 判断记录）。
        /// 字段名与默认值（0.25）均不变——硬性规则 5（ABI 只允许新增），且新旧默认值刚好一致，不
        /// 新增一个重复含义的字段。
        /// </para>
        /// </summary>
        public double DefaultBuyPricePct { get; set; } = 0.25;

        /// <summary>
        /// 价格公式使用的 <c>econ.value_curve</c> id（分阶段落地计划 T-N4-6；ADR-0034 决策 2）：
        /// 缺省 <c>econ.value.default</c>，同 <c>Core.Carriers.Item.ItemOptions.BudgetCurveId</c> 一贯
        /// 惯例——曲线 id 是调用方配置项，不预设/硬编码唯一默认曲线。该曲线在已加载数据里找不到对应
        /// 记录、或物品模板缺少 <c>item_level</c> 时，价格公式按"无法算出基准价值"处理（见
        /// <see cref="EconomyPriceFormula.TryComputeFormulaValue"/>），<c>EconomyHost.Buy</c>/
        /// <c>Sell</c> 各自的回退口径见调用处判断记录，不抛异常。
        /// </summary>
        public Id ValueCurveId { get; set; } = new Id("econ.value.default");

        /// <summary>
        /// "手填价格偏离公式"警告（分阶段落地计划 T-N4-6；04 第 5 节数值类校验项警告级；检查名
        /// <see cref="EconomyPriceDeviatesFormulaRule.Check"/>）的偏离阈值，缺省 0.2（±20%），同
        /// <c>Core.Carriers.Item.ItemWeaponDamageDeviatesDpsCurveRule.DefaultDeviationThreshold</c>
        /// 一贯口径——04 第 5 节该行未给出具体阈值，本任务按同组既有警告规则的既定阈值类推（设计层
        /// 裁定（2026-09-15）：采纳）。
        /// </summary>
        public double PriceDeviationWarningThreshold { get; set; } = EconomyPriceDeviatesFormulaRule.DefaultDeviationThreshold;
    }
}
