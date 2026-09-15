using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 预算反解契约（分阶段落地计划 T-N2-4；ADR-0032 决策 3"新增'预算反解'契约：给定物品等级、
    /// 品质、槽位与属性组合比例，反解各属性值（标准玩家生成器与词缀落值共用）"；07 第 1.2 节修订段
    /// "<c>BudgetSolver.solve(itemLevel, qualityId, slotId, statMix): Map&lt;StatKey, Number&gt;</c>"；
    /// 落地改动点清单 E5）：<see cref="ItemBudgetCurve.ComputeConsumed"/> 消耗公式
    /// <c>(Σ(值×权重)^k)^(1/k)</c> 的反函数——给定预算目标与各属性"加权贡献"的组合比例，解出使正向
    /// 复算（<see cref="ItemBudgetCurve.ComputeConsumed"/>）等于该目标的属性值集合。消费者：词缀
    /// 落值（07 第 1.6 节，T-N2-8 落地）、数值仿真标准玩家生成器（ADR-0035，落地改动点清单 N3）。
    /// <para>
    /// 判断记录（<c>statMix</c> 比例语义——**上报待设计层确认**）：07 第 1.6 节 <c>item.affix.
    /// stat_mix</c> 原文"属性组合与内部分配比例，之和不超过一"、07 第 1.2 节 <c>solve</c> 签名本身
    /// 均未展开"分配比例"是分配"属性原始值 <c>v_i</c>"的份额，还是分配"加权贡献 <c>v_i × weight_i</c>"
    /// 的份额。本接口按"各属性的加权贡献占比"实现（线性、作者填写时最直观：两条 0.5/0.5 的属性无论
    /// 权重是否相同，各自占半份预算消耗；权重差异只体现在"同样的加权贡献份额下，权重低的属性需要
    /// 更大的原始值"），完整推导见 <see cref="Core.Carriers.Item.BudgetSolver"/> 类型判断记录。
    /// </para>
    /// <para>
    /// 判断记录（新增 <c>budgetCurveId</c>/<c>shareOfBudget</c> 两个参数，07 原文签名未列出——本身
    /// 是"中立接口记法，不是代码"的落地展开，不算矛盾，仍记录判断依据）：<c>budgetCurveId</c>——
    /// 预算曲线 id 在本模块一贯是调用方配置项（同 <see cref="ItemOptions.BudgetCurveId"/>/<see
    /// cref="ItemBudgetValidationRule"/> 构造参数惯例），本接口不预设/硬编码唯一默认曲线 id。
    /// <c>shareOfBudget</c>——ADR-0032 决策 7 词缀落值需要"该件预算 × <c>budget_share</c>"这一缩小
    /// 目标；若不显式建模该参数，调用方唯一的替代做法是把 <c>statMix</c> 的比例之和从 1 改写成
    /// <c>budget_share</c>，但这与本接口"比例之和须为 1"的输入校验（见 <see
    /// cref="Core.Carriers.Item.BudgetSolver"/> 判断记录）冲突，且会让 <c>statMix</c> 同时承载
    /// "内部分配"与"总量占比"两种语义，故改为独立参数，缺省 1.0（不缩份额，即模板自身属性场景）。
    /// </para>
    /// </summary>
    public interface IBudgetSolver
    {
        /// <summary>
        /// 反解各属性值。<paramref name="statMix"/> 各元素 <c>(Stat, Ratio)</c> 的 <c>Ratio</c> 之和
        /// 须为 1（±1e-9，否则 <see cref="System.ArgumentException"/>），单项 <c>Ratio</c> 须 &gt; 0；
        /// 目标预算 = <c>item.budget_curve(itemLevel) × quality.budget_multiplier ×
        /// slot.budget_coefficient × shareOfBudget</c>。<paramref name="statMix"/> 引用的属性若在
        /// <c>stat.weight</c> 里显式登记权重为 0，无法反解出有限值，抛 <see
        /// cref="System.ArgumentException"/>。返回不可变的 <see cref="BudgetSolverResult"/>。
        /// </summary>
        BudgetSolverResult Solve(
            int itemLevel,
            Id qualityId,
            Id slotId,
            IReadOnlyList<(Id Stat, double Ratio)> statMix,
            Id budgetCurveId,
            double shareOfBudget,
            IDataRegistryView view);

        /// <summary>便捷重载：<c>shareOfBudget</c> 取默认值 1.0（模板自身属性场景，消耗目标 = 该件
        /// 预算全额）。带默认实现的接口成员（同 <see cref="IDataRegistryView.RecordCount"/> 惯例，
        /// C# 8 默认接口方法），不要求实现类另行实现，不构成 ABI 破坏。</summary>
        BudgetSolverResult Solve(
            int itemLevel,
            Id qualityId,
            Id slotId,
            IReadOnlyList<(Id Stat, double Ratio)> statMix,
            Id budgetCurveId,
            IDataRegistryView view)
            => Solve(itemLevel, qualityId, slotId, statMix, budgetCurveId, 1.0, view);
    }
}
