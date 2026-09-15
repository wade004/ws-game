using Core.Foundation.Common;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// <see cref="IProgressionHost.GrantXp"/> 的当量上下文（分阶段落地计划 T-N4-2；
    /// [ADR-0033](../../../../architecture/adr/0033-等级经验模块正文与当量来源.md) 决策 1/3；
    /// [06 第 2.5 节](../../../../architecture/06_规则层_属性技能战斗AI.md) 契约原文：
    /// <c>struct XpContext { sourceLevel: Int, tierId: Optional&lt;Id&gt;, equivalent: Optional&lt;Number&gt; }</c>
    /// ——三种经验来源（<c>kill|quest|discovery</c>）共用同一个上下文结构，按
    /// <c>prog.xp_source.kind</c> 决定折算方式：
    /// <list type="bullet">
    /// <item><description><b>kill</b>：<see cref="SourceLevel"/> 是被击杀单位的等级，折算
    /// <c>base_curve_ref</c> 曲线(<see cref="SourceLevel"/>)；<see cref="Equivalent"/> 不使用
    /// （击杀基数曲线本身已经是"一只同级怪的经验"，ADR-0033 决策 3）。</description></item>
    /// <item><description><b>quest</b>：<see cref="SourceLevel"/> 是任务等级，
    /// <see cref="Equivalent"/> 是任务定义写的当量 N（缺省 1）。</description></item>
    /// <item><description><b>discovery</b>：<see cref="SourceLevel"/> 是区域等级，
    /// <see cref="Equivalent"/> 缺省按"一只怪当量"处理（缺省 1）——ADR-0033 决策 3 原文的探索公式
    /// 没有给出可变当量语法，本模块仍保留该字段可显式覆盖，以防未来内容需要，但默认路径与 ADR
    /// 描述完全一致（等于 1）。</description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判断记录（<see cref="TierId"/> 本任务只登记、不消费）：06 第 2.5 节 struct 定义带
    /// <c>tierId</c>（"分档"），但按 ADR-0033 决策 3/4，分档倍率
    /// （<c>creature.tier_definition.xp_multiplier</c>）与难度倍率
    /// （<c>diff.tier.xp_multiplier</c>）的实际计算方在 T-N4-3（击杀经验监听器）/T-N4-4
    /// （分档与难度倍率注入）——那两个任务才知道去哪张表按 <see cref="TierId"/> 查倍率；本模块
    /// 并行开发期不引用 <c>creature</c>/<c>diff</c> 模块的具体类型（见 README"并行开发期不引用
    /// 具体类型"惯例），也不应该现在就去猜它们的字段形状。本任务只登记该字段供调用方传入/读取，
    /// <see cref="ProgressionHost.GrantXp"/> 内部不读取它——分档/难度倍率钩子改用已在 T-N4-1
    /// 落地的 <see cref="ProgressionOptions.ExtraXpMultiplierProvider"/>（签名
    /// <c>(unitId, sourceId) → 倍率</c>，缺省 1，只用于 <c>kind=kill</c> 分支，见
    /// <see cref="ProgressionHost"/> 判断记录）。**契约疑点上报**：该委托签名不含
    /// <see cref="TierId"/>，若 T-N4-4 落地时发现真的需要按 <see cref="TierId"/> 查倍率，
    /// 委托签名或注入方式需要那时再按需扩展/调整，以彼时任务书与设计层裁定为准（本类是
    /// 阶段 N4 内多个任务共同完善、随 1.34.0 一次性发布的全新类型，本阶段内的字段/委托调整不是
    /// "已发布 ABI"的破坏性变更，同 <c>ProgressionOptions</c> 类型注释"契约疑点上报"同一惯例）。
    /// </para>
    /// <para>
    /// 不可变类型：公开构造 + 只读属性（设计层裁定：C# 里没有真正意义上"只读值类型"的轻量写法能
    /// 同时满足"可选字段有默认值"且不引入额外的 <c>readonly struct</c> 装箱/相等性顾虑，用
    /// <c>sealed class</c> + 只读自动属性是本仓库既有惯例——参见 <c>TargetResolution</c> 一类新增
    /// 结果类型的写法）。
    /// </para>
    /// </remarks>
    public sealed class XpContext
    {
        /// <summary>来源等级：<c>kill</c>=被击杀单位的等级，<c>quest</c>=任务等级，
        /// <c>discovery</c>=区域等级（ADR-0033 决策 3"三种来源全部以同级怪当量计"）。用于在
        /// <c>prog.xp_source.base_curve_ref</c> 指向的 <c>prog.xp_base_curve</c> 曲线上取值，也是
        /// 计算等级差 Δ 的"目标/来源侧"输入（<c>combat.level_diff_table.xp_factor</c> 的横轴
        /// Δ = <see cref="SourceLevel"/> − 领取者当前有效等级）。</summary>
        public int SourceLevel { get; }

        /// <summary>分档 id（如 <c>creature.tier_definition</c>/<c>diff.tier</c> 的记录 id），本任务
        /// 只登记、不消费，见类型判断记录。</summary>
        public Id? TierId { get; }

        /// <summary>当量：<c>quest</c> 场景填任务当量 N；<c>discovery</c> 场景省略即按 1（一只怪
        /// 当量）处理；<c>kill</c> 场景不使用。</summary>
        public double? Equivalent { get; }

        public XpContext(int sourceLevel, Id? tierId = null, double? equivalent = null)
        {
            SourceLevel = sourceLevel;
            TierId = tierId;
            Equivalent = equivalent;
        }
    }
}
