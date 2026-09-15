using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="EquipmentScoreAnalyzer.Score"/> 的不可变返回结果（分阶段落地计划 T-N2-4；ADR-0032
    /// 决策 9"实际消耗公式换成职业权重即评分"）。
    /// </summary>
    public sealed class EquipmentScoreResult
    {
        /// <summary>被评分的物品模板 id。</summary>
        public Id TemplateId { get; }

        /// <summary>评分所用的职业 id；<c>null</c> 表示未按职业覆盖，使用 <c>stat.weight</c> 顶层
        /// 基础权重（同 <see cref="ItemBudgetCurve.BuildStatBudgetInfo(Core.Foundation.DataRegistry
        /// .IDataRegistryView)"/> 语义）。</summary>
        public Id? ClassId { get; }

        /// <summary>该模板的物品等级（取自 <c>item.template.item_level</c>）。</summary>
        public int ItemLevel { get; }

        /// <summary>评分值：<c>(Σ(属性值×职业权重)^k)^(1/k)</c>，与 <see
        /// cref="ItemBudgetCurve.ComputeConsumed"/> 同一公式，只是权重来源换成职业覆盖。</summary>
        public double Score { get; }

        /// <summary>评分公式使用的指数 k。</summary>
        public double Exponent { get; }

        /// <summary>评分时实际使用的各属性权重（按 <see cref="ClassId"/> 覆盖后的最终取值），供
        /// 调用方核对/展示（例如编辑器"为什么这件装备分高"明细面板）。</summary>
        public IReadOnlyDictionary<Id, double> Weights { get; }

        public EquipmentScoreResult(
            Id templateId,
            Id? classId,
            int itemLevel,
            double score,
            double exponent,
            IReadOnlyDictionary<Id, double> weights)
        {
            TemplateId = templateId;
            ClassId = classId;
            ItemLevel = itemLevel;
            Score = score;
            Exponent = exponent;
            Weights = weights;
        }
    }
}
