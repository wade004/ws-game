namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// <see cref="StatHost"/> 的构造期策略配置（见 01_分层与依赖.md L1 模块表 <c>stat_block</c> 行
    /// "策略配置项：是否启用评级换算、是否启用抗性维度"、00_架构总则.md 第 3 节"属性评级换算
    /// ……可选策略默认关闭""抗性系统……可选属性维度"）。
    /// </summary>
    public sealed class StatHostOptions
    {
        /// <summary>评级换算是否启用（06 第 1.1 节）：启用且属性定义
        /// <c>stat.definition.is_rating=true</c> 时，基础值+固定修正段先经
        /// <c>stat.rating_conversion</c> 曲线换算，再进入百分比/独立乘区两段；默认关闭
        /// （00 第 3 节"作为 StatBlock 的可选策略，默认关闭"）。</summary>
        public bool EnableRatingConversion { get; set; } = false;

        /// <summary>抗性维度是否启用（00 第 3 节"作为可选属性维度，游戏层决定是否启用"）：
        /// 关闭时，<c>group: resistance</c> 的属性仍可正常定义、设置基础值、挂修正，但
        /// <see cref="IStatHost.GetStat"/> 恒返回 0 并记一条警告（见
        /// <see cref="StatHost.Warnings"/>）。默认开启。</summary>
        public bool EnableResistanceGroup { get; set; } = true;
    }
}
