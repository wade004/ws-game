using System;
using Core.Foundation.Common;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// 评级换算曲线的形态标记（分阶段落地计划 T-N2-3 从 <see cref="StatHost"/> 内部私有的同名嵌套枚举
    /// 提升为命名空间级公开类型，供 <see cref="RatingConversionEvaluator"/> 对外暴露；<see
    /// cref="StatHost"/> 自身改用本类型，不再维护独立的一份，见 <see cref="StatHost"/> 判断记录）。
    /// </summary>
    public enum RatingConversionShape
    {
        /// <summary>"标准版：等级索引除数"——按等级在断点曲线上插值取得"每 1% 效果所需点数"，
        /// <c>percent = rawValue / pointsPerPercent(level)</c>。</summary>
        Breakpoints,

        /// <summary>"变态版：饱和曲线，除数随等级增长"——<c>percent = rawValue / (rawValue + k ×
        /// level)</c>，以 <c>cap</c> 封顶。</summary>
        Saturation,
    }

    /// <summary>
    /// 评级换算的纯函数求值器（分阶段落地计划 T-N2-3；ADR-0032 决策 3"百分比属性先经换算曲线折回
    /// 点数再乘权重"；数值总纲第 4.3/4.4 节）：把 <see cref="StatHost.ConvertRating"/> 原本手写在
    /// 该私有方法内部的"点数→百分比"求值式子（<see cref="ToPercent"/>）与装备预算消耗侧需要的反方向
    /// "百分比→点数"（<see cref="ToPoints"/>）抽成 <c>Core.Numbers</c> 命名空间级公开静态工具，<see
    /// cref="StatHost"/> 与 <c>Core.Carriers.Item.ItemBudgetCurve</c> 共用同一份实现——硬性规则"禁止
    /// 复制插值实现"：装备模块（L3）不得为百分比属性折算另写一份断点插值/饱和公式，必须复用本类型。
    /// <para>
    /// 判断记录（正向式子逐运算不变）：<see cref="ToPercent"/> 的两个分支与 <see cref="StatHost"/>
    /// 迁移前手写的 <c>ConvertRating</c>/<c>EvaluateSaturation</c> 逐运算相同（<see
    /// cref="PiecewiseCurve.Evaluate"/> 取 <c>pointsPerPercent</c> 后 <c>rawValue / pointsPerPercent</c>；
    /// 饱和 <c>rawValue / (rawValue + k × level)</c> 以 <c>cap</c> 封顶、非正分母与负结果降级为 0），
    /// <see cref="StatHost"/> 侧既有测试（<c>RatingConversionMigrationTests</c> 等）验证结果逐位一致
    /// 未变化，因此重构安全。
    /// </para>
    /// <para>
    /// 判断记录（反函数 <see cref="ToPoints"/> 是否总是存在，T-N2-3 新增）：<see
    /// cref="RatingConversionShape.Breakpoints"/> 形态下 <c>pointsPerPercent(level)</c> 只依赖等级、不
    /// 依赖 <c>rawValue</c> 本身，正向式子对 <c>rawValue</c> 是线性映射，反函数恒存在且无条件——
    /// <c>points = percent × pointsPerPercent(level)</c>（<c>pointsPerPercent == 0</c> 时按 <see
    /// cref="ToPercent"/> 既有"内容错误静默降级"同一口径返回 0，不抛异常）。<see
    /// cref="RatingConversionShape.Saturation"/> 形态 <c>percent = rawValue / (rawValue + k × level)</c>
    /// 对 <c>rawValue &gt;= 0</c> 严格单调递增、值域 <c>[0, 1)</c>，反函数 <c>points = percent × k ×
    /// level / (1 − percent)</c> 仅当 <c>percent &lt; 1</c> 时有限；调用方（词缀/物品模板作者）填了一个
    /// 用当前饱和曲线永远达不到的百分比（<c>percent &gt;= 1</c>，含数据错误与"故意填极端值试探预算"
    /// 两种来源，装备预算消耗侧无法区分）时，返回 <see cref="double.PositiveInfinity"/>——语义上"需要
    /// 无穷多点数才能达到"，调用方（预算消耗求和）据此自然判定超预算，不需要本类型抛异常中断整条
    /// 校验流水线，同 <see cref="ToPercent"/> 既有"内容错误被静默降级、不在此处二次判定"口径（04 第 5
    /// 节"运行时不做静默降级"针对的是查询期未过校验的数据，本类型在已加载数据上求值，属另一层次）。
    /// </para>
    /// </summary>
    public static class RatingConversionEvaluator
    {
        /// <summary>点数 → 百分比（<see cref="StatHost.ConvertRating"/> 原式，正向求值）。<paramref
        /// name="curve"/> 仅 <paramref name="shape"/> 为 <see cref="RatingConversionShape.Breakpoints"/>
        /// 时读取，可为 <c>null</c>/空（此时退化为直通 <paramref name="rawValue"/>，同既有"空曲线退化为
        /// 直通"口径）。</summary>
        public static double ToPercent(RatingConversionShape shape, PiecewiseCurve? curve, double k, double cap,
            double rawValue, int level)
        {
            if (shape == RatingConversionShape.Saturation)
            {
                return EvaluateSaturation(rawValue, level, k, cap);
            }

            if (curve == null || curve.Count == 0)
            {
                return rawValue;
            }

            var pointsPerPercent = curve.Evaluate(level);
            return pointsPerPercent == 0 ? 0.0 : rawValue / pointsPerPercent;
        }

        /// <summary>百分比 → 点数（<see cref="ToPercent"/> 的反函数，T-N2-3 新增，装备预算消耗侧
        /// "百分比属性先经换算曲线折回点数再乘权重"消费）。<paramref name="curve"/> 同 <see
        /// cref="ToPercent"/> 判断记录。</summary>
        public static double ToPoints(RatingConversionShape shape, PiecewiseCurve? curve, double k, double cap,
            double percent, int level)
        {
            if (shape == RatingConversionShape.Saturation)
            {
                return InverseSaturation(percent, level, k);
            }

            if (curve == null || curve.Count == 0)
            {
                return percent;
            }

            var pointsPerPercent = curve.Evaluate(level);
            return percent * pointsPerPercent;
        }

        /// <summary>饱和形态正向求值：<c>输出 = rawValue / (rawValue + k × level)</c>，以 <paramref
        /// name="cap"/> 封顶；非正分母与负结果一律降级为 0（<see cref="StatHost"/> 迁移前
        /// <c>EvaluateSaturation</c> 原式逐运算复刻）。</summary>
        public static double EvaluateSaturation(double rawValue, int level, double k, double cap)
        {
            var denom = rawValue + k * level;
            var result = denom <= 0.0 ? 0.0 : rawValue / denom;
            if (result < 0.0) result = 0.0;
            return Math.Min(result, cap);
        }

        /// <summary>饱和形态反函数：解 <c>percent = rawValue / (rawValue + k × level)</c> 得 <c>rawValue
        /// = percent × k × level / (1 − percent)</c>；<paramref name="percent"/> <c>&lt;= 0</c> 时按
        /// 定义域端点返回 0；<c>&gt;= 1</c>（曲线永远达不到的百分比）返回 <see
        /// cref="double.PositiveInfinity"/>，见类型判断记录。</summary>
        private static double InverseSaturation(double percent, int level, double k)
        {
            if (percent <= 0.0) return 0.0;

            var denom = 1.0 - percent;
            if (denom <= 0.0) return double.PositiveInfinity;

            return percent * k * level / denom;
        }
    }
}
