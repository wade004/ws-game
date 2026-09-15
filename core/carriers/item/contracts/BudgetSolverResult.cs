using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <c>IBudgetSolver.Solve</c> 的不可变返回结果（分阶段落地计划 T-N2-4；任务书"返回不可变
    /// 结果（各属性值、目标预算、实际消耗、使用的 k/权重）"）。全部成员只读，构造后不可修改；
    /// <see cref="Values"/>/<see cref="Weights"/> 两个字典由 <see cref="Core.Carriers.Item
    /// .BudgetSolver"/> 在构造期一次性建好后原样传入，不对外暴露任何可写入口。
    /// </summary>
    public sealed class BudgetSolverResult
    {
        /// <summary>反解所用的物品等级。</summary>
        public int ItemLevel { get; }

        /// <summary>反解所用的品质 id。</summary>
        public Id QualityId { get; }

        /// <summary>反解所用的槽位 id。</summary>
        public Id SlotId { get; }

        /// <summary>该物品的预算上限（<c>曲线(ItemLevel) × 品质预算倍率 × 槽位系数</c>，
        /// <c>shareOfBudget</c> 缩份额之前的全额）。</summary>
        public double ItemBudgetLimit { get; }

        /// <summary>本次反解使用的预算份额（<c>(0,1]</c>，缺省 1.0）。</summary>
        public double ShareOfBudget { get; }

        /// <summary>本次反解的目标预算 = <see cref="ItemBudgetLimit"/> × <see cref="ShareOfBudget"/>。</summary>
        public double TargetBudget { get; }

        /// <summary>消耗公式指数 k（取自 <c>item.budget_curve</c> 记录的可选字段 <c>exponent</c>，
        /// 缺省 <see cref="ItemBudgetCurve.DefaultExponent"/>）。</summary>
        public double Exponent { get; }

        /// <summary>用 <see cref="Values"/> 正向复算（<see cref="ItemBudgetCurve.ComputeConsumed"/>，
        /// 全部按 <c>op=flat</c> 语义）得到的实际消耗——供调用方验证可逆性；与 <see cref="TargetBudget"/>
        /// 的差异应在浮点误差范围内（&lt; 1e-9）。</summary>
        public double ActualConsumed { get; }

        /// <summary>反解出的各属性值，按 <c>op=flat</c> 语义（即"点数"，<c>category=percent</c> 的
        /// 属性同样是点数，调用方需要换算成 <c>op=pct</c>/<c>mult</c> 的填写值时自行调用 <see
        /// cref="Core.Numbers.StatBlock.RatingConversionEvaluator.ToPercent"/>）。</summary>
        public IReadOnlyDictionary<Id, double> Values { get; }

        /// <summary>反解时实际使用的各属性权重（含"未登记 <c>stat.weight</c> 记录按缺省权重 1 回退"
        /// 的最终取值），供调用方核对/展示。</summary>
        public IReadOnlyDictionary<Id, double> Weights { get; }

        public BudgetSolverResult(
            int itemLevel,
            Id qualityId,
            Id slotId,
            double itemBudgetLimit,
            double shareOfBudget,
            double targetBudget,
            double exponent,
            double actualConsumed,
            IReadOnlyDictionary<Id, double> values,
            IReadOnlyDictionary<Id, double> weights)
        {
            ItemLevel = itemLevel;
            QualityId = qualityId;
            SlotId = slotId;
            ItemBudgetLimit = itemBudgetLimit;
            ShareOfBudget = shareOfBudget;
            TargetBudget = targetBudget;
            Exponent = exponent;
            ActualConsumed = actualConsumed;
            Values = values;
            Weights = weights;
        }
    }
}
