using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// T-N3-9（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 2；06 第
    /// 3.10 节"锚点秒伤与期望属性来自 <c>sim.anchor</c>"；数值总纲第 4.5 节技能预算公式）：技能预算
    /// 比值分子（实际价值）与分母（预算上限）都需要"某个技能等级下的锚点秒伤 <c>DPS(L)</c>"与"该等级
    /// 期望缩放属性最终值"两个量——查询钩子，供 <see cref="Core.Rules.Skill.SkillBudgetAnalyzer"/>
    /// 在求值时注入，不在本接口内部读取任何具体数据表。
    /// <para>
    /// 判断记录（同 <see cref="IGearLevelOffsetProvider"/> 先例——本接口是它在技能预算场景下的同类
    /// 应用，理由完全一致）：<c>sim.anchor</c>（"期望装备等级曲线 E(L)"、"锚点秒伤 DPS(L)"、"期望
    /// 缩放属性"）归阶段 N6（数值仿真）交付物，晚于本阶段（N3）；<c>SkillBudgetAnalyzer</c>（本模块，
    /// L2 <c>core/rules/skill</c>）不允许依赖 <c>sim.anchor</c> 表本身是否已注册、更不能硬编码某种
    /// "求值这一步"的实现——求值需要同时看到锚点表与期望缩放属性表的调用方（游戏层/L6 数值仿真装配
    /// 代码）才具备。本任务因此只落地这个接口钩子，真实实现（把 <c>sim.anchor</c> 断点表接到本接口）
    /// 留给 N6 接入，是本任务向设计层的一项如实上报（任务书"契约不清如实上报，标待设计层确认并给出
    /// 临时判断"）。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="NullSkillBudgetAnchorProvider"/> 的两个默认值 1.0/0.0，以及"为何注册期
    /// 默认不接入"）：<see cref="Core.Rules.Assembly.RulesSchemaCatalog"/> 默认注册的技能预算校验
    /// 规则（<c>SkillBudgetValidationRule</c>）在未显式注入真实实现时使用
    /// <see cref="NullSkillBudgetAnchorProvider.Instance"/>——该哨兵值不是"合理的中性锚点"（技能
    /// 基础值的量级通常远大于 1.0，若真的按此计算比值，几乎任何技能都会报"远超硬上限"，产生大量误
    /// 报），而是一个可被规则层按引用相等识别的"未接入"标记（同 04 第 5.1 节
    /// <c>SpawnSummonOnlyCreatureRule</c>"<c>creatureTemplateQuery</c> 为 null 时该规则不注册，这项
    /// 跨表检查完全跳过"先例）——<c>SkillBudgetValidationRule</c> 检测到调用方未注入真实
    /// <see cref="ISkillBudgetAnchorProvider"/> 时，整条规则不产生任何 <see
    /// cref="Core.Foundation.DataRegistry.ValidationIssue"/>（不是"用哨兵值算出一堆误报"），保证
    /// 示例数据与内容管线在 <c>sim.anchor</c> 真正接入前保持零告警；<see
    /// cref="Core.Rules.Skill.SkillBudgetAnalyzer.Analyze"/> 本身不做这层"未接入即跳过"的特判——
    /// 调用方（含测试）显式传入 <see cref="NullSkillBudgetAnchorProvider.Instance"/> 时，分析器仍会
    /// 老老实实按 1.0/0.0 算出一个（多数情况下没有实际意义的）比值，"是否要把这个比值当真"完全是
    /// 规则注册层的判断，不藏在分析器内部。
    /// </para>
    /// </summary>
    public interface ISkillBudgetAnchorProvider
    {
        /// <summary>返回技能等级 <paramref name="level"/> 对应的锚点秒伤 <c>DPS(L)</c>（数值总纲
        /// 第 4.1 节"每级一行……期望秒伤 <c>DPS(L)</c>"，第 4.5 节"技能预算 = DPS(L) × T × ……"）。</summary>
        double GetAnchorDps(int level);

        /// <summary>返回技能等级 <paramref name="level"/> 下 <paramref name="stat"/> 指向的缩放属性
        /// 的期望最终值（数值总纲第 4.5 节"实际价值 = 基础值 + Σ(系数 × 该等级期望缩放属性)……期望
        /// 属性来自锚点表与期望装备等级反解，见 07 第 1.2 节"）。</summary>
        double GetExpectedScalingStatValue(Id stat, int level);
    }

    /// <summary>
    /// 缺省实现：<see cref="GetAnchorDps"/> 恒返回 1.0，<see cref="GetExpectedScalingStatValue"/> 恒
    /// 返回 0.0（保持 <see cref="Core.Rules.Skill.SkillBudgetAnalyzer.Analyze"/> 在未接入真实
    /// <see cref="ISkillBudgetAnchorProvider"/> 时仍可求值、不抛异常；是否把求值结果当真由调用方决定，
    /// 见本文件类型判断记录）。
    /// </summary>
    public sealed class NullSkillBudgetAnchorProvider : ISkillBudgetAnchorProvider
    {
        public static readonly NullSkillBudgetAnchorProvider Instance = new NullSkillBudgetAnchorProvider();

        private NullSkillBudgetAnchorProvider()
        {
        }

        public double GetAnchorDps(int level) => 1.0;

        public double GetExpectedScalingStatValue(Id stat, int level) => 0.0;
    }
}
