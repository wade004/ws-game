using System;
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
    /// "策略配置项：是否启用抗性维度"、00_架构总则.md 第 3 节"抗性系统……可选属性维度"；换算层自
    /// T-N1-3 起始终启用，不再是策略配置项，见 <see cref="EnableRatingConversion"/> 判断记录）。
    /// </summary>
    public sealed class StatHostOptions
    {
        /// <summary>
        /// （已废弃，无任何效果）曾经的"评级换算是否启用"开关（00 第 3 节旧文"作为 StatBlock 的
        /// 可选策略，默认关闭"）。
        /// <para>
        /// 判断记录（T-N1-3，ADR-0030 决策 3；分阶段落地计划 T-N1-3 设计层裁定）：计划原文要求
        /// "删除 <c>EnableRatingConversion</c>"，但落地计划第 1 节"每阶段对外契约变化必须控制在
        /// MINOR"——删除既有公开属性是 ABI 破坏（G3 门禁禁止）。设计层裁定改为<b>保留但无效化</b>：
        /// 本属性仍可读写（保留二进制兼容），但 <see cref="StatHost"/> 自 T-N1-3 起不再读取它——
        /// 换算层改为始终启用（<c>category=="percent"</c> 即触发，见 <see
        /// cref="StatHost.ComputeFinal"/> 判断记录），<c>conversion_ref</c> 缺省时按恒等曲线处理，
        /// 不存在任何"关闭换算层"的路径（见 <c>StatHostTests</c> 的
        /// <c>EnableRatingConversion_HasNoEffect_PercentStatsStillConvert</c> 与
        /// <c>StatHostOptions_NoOtherUnobsoleteConversionSwitch_Exists</c> 两条反射/行为测试）。
        /// </para>
        /// </summary>
        [Obsolete("换算层自 1.31.0 起始终启用（ADR-0030 决策 3），本属性无任何作用，保留仅为二进制兼容")]
        public bool EnableRatingConversion { get; set; } = false;

        /// <summary>抗性维度是否启用（00 第 3 节"作为可选属性维度，游戏层决定是否启用"）：
        /// 关闭时，<c>category: defense</c> 的属性仍可正常定义、设置基础值、挂修正，但
        /// <see cref="IStatHost.GetStat"/> 恒返回 0 并记一条警告（见
        /// <see cref="StatHost.Warnings"/>）。默认开启。
        /// <para>
        /// 判断记录（T-N1-2，ADR-0030 拍板 1"抗性维度开关改独立策略项"；汇报里写明）：本属性在
        /// T-N1-1 之前就已经是 <see cref="StatHostOptions"/> 上一个独立于内容数据的 <see cref="bool"/>
        /// 策略项——真正随本任务变化的只是它所"门控"的判定条件：<see cref="StatHost.ComputeFinal"/>
        /// 从 <c>stat.definition.group == "resistance"</c> 改为 <c>category == "defense"</c>
        /// （<c>group</c> 已废弃，<see cref="StatHost"/> 不再读取，见 <see cref="StatHost.LoadDefinitions"/>
        /// 判断记录）。属性名维持 <c>EnableResistanceGroup</c> 不改名——G3 ABI 门禁禁止删除既有公开
        /// 成员，改名等价于"删除旧成员 + 新增同义成员"，会破坏调用方已编译的代码；新增一个同义的
        /// <c>EnableResistanceDimension</c> 属性并让两者保持同步又会引入"两个旗标谁为准"的新歧义，
        /// 没有实际收益。默认值维持 <c>true</c>，与 T-N1-1 之前（<c>group == resistance</c> 驱动）的
        /// 默认行为等价——迁移链保证 v1 <c>group: resistance</c> 的属性经 1→2 迁移后 <c>category</c>
        /// 恒为 <c>defense</c>（拍板 1 明文"resistance→defense"），因此"默认开启、判定条件从 group
        /// 改到 category"这次切换对任何已迁移数据都是行为不变的重构，不是新契约。
        /// </para></summary>
        public bool EnableResistanceGroup { get; set; } = true;

        /// <summary>
        /// 换算曲线（<c>stat.rating_conversion</c>，断点表或饱和两种形态均以本委托取单位等级，
        /// 见 <c>CurveSchema</c> 判断记录）的自变量来源（设计层 2026-09-05 拍板：断点表形态的
        /// <c>x</c> 就是单位等级，取代此前"level 是评级原始值自己的插值断点"的判断，见
        /// <c>StatHost.ConvertRating</c> 判断记录）。为 null 时等级一律按 1 处理。
        /// <para>
        /// 判断记录（T-N1-3）：换算层始终启用（见 <see cref="EnableRatingConversion"/> 判断记录），
        /// 本委托对任何 <c>category=="percent"</c> 且带 <c>conversion_ref</c> 的属性都会被调用——
        /// 此前"<c>EnableRatingConversion=false</c> 时本委托完全不会被调用"的描述随换算层开关
        /// 一并失效，已删除。
        /// </para>
        /// </summary>
        public LevelLookup? LevelLookup { get; set; }
    }
}
