using Core.Foundation.Common;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// 查询单位当前等级的具名委托（见 <see cref="StatHostOptions.LevelLookup"/>、
    /// <c>StatHost.ConvertRating</c> 判断记录 2026-09-05）。<c>stat_block</c> 不引用
    /// <c>core/numbers/progression</c> 的任何具体类型（同层并行开发、避免编译期耦合，见本模块
    /// README"依赖"一节），评级换算需要的"单位等级"改由调用方把真正的等级来源（如
    /// <c>IProgressionHost.GetLevel</c>）适配成这个委托签名后注入。
    /// </summary>
    /// <param name="unitId">要查询等级的单位。</param>
    /// <returns>该单位当前等级。</returns>
    public delegate int LevelLookup(Id unitId);

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

        /// <summary>
        /// 评级换算曲线（<c>stat.rating_conversion.entries[].level</c>）的自变量来源（设计层
        /// 2026-09-05 拍板：<c>level</c> 就是单位等级，取代此前"level 是评级原始值自己的插值
        /// 断点"的判断，见 <c>StatHost.ConvertRating</c> 判断记录）。为 null 时等级一律按 1
        /// 处理（<c>EnableRatingConversion=false</c> 时本委托完全不会被调用）。
        /// </summary>
        public LevelLookup? LevelLookup { get; set; }
    }
}
