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
    /// 判断记录（<see cref="TierId"/> 消费方，T-N4-4 补记）：06 第 2.5 节 struct 定义带
    /// <c>tierId</c>（"分档"）——T-N4-1/T-N4-2 落地时本字段只登记、不消费（<see
    /// cref="ProgressionHost.GrantXp"/> 内部不读取它），T-N4-3 起 <c>CreatureDeathXpListener</c>
    /// 把死亡单位所属生物模板的分档 id 传进来，T-N4-4 起 <see cref="ProgressionHost.GrantXp"/>
    /// 的 <c>kind=kill</c> 分支把本字段原样传给
    /// <see cref="ProgressionOptions.ExtraXpMultiplierProvider"/>（委托签名同一次任务扩展为
    /// <c>(unitId, sourceId, tierId) → 倍率</c>，见该委托判断记录"T-N4-4 变更记录"）——此前
    /// 这里登记的"契约疑点上报：该委托签名不含 TierId"已按此裁定并落地，不再是待确认事项。
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
