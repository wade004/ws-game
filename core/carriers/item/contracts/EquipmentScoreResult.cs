using System;
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

        /// <summary>
        /// 消费方反馈第 45 条（2026-09-17）新增：本次评分是否曾因 registry 阻断态读不到某些支持表/
        /// 记录（见 <see cref="EquipmentScoreAnalyzer"/> 判断记录、
        /// <c>Core.Foundation.DataRegistry.TolerantRegistryView</c>）——<c>false</c> 时本结果与
        /// 改动前完全一致（既有测试逐位不变）；<c>true</c> 时供内容工具提示"这份评分基于不完整数据"，
        /// 具体缺失表见 <see cref="MissingTables"/>。使用旧构造函数（不含这两个参数）时恒为
        /// <c>false</c>，保持与改动前调用方完全一致的默认行为。</summary>
        public bool IsDegraded { get; }

        /// <summary>消费方反馈第 45 条：<see cref="IsDegraded"/> 为 <c>true</c> 时具体缺失的表名；
        /// 否则空列表。</summary>
        public IReadOnlyList<string> MissingTables { get; }

        public EquipmentScoreResult(
            Id templateId,
            Id? classId,
            int itemLevel,
            double score,
            double exponent,
            IReadOnlyDictionary<Id, double> weights)
            : this(templateId, classId, itemLevel, score, exponent, weights, isDegraded: false, missingTables: Array.Empty<string>())
        {
        }

        /// <summary>消费方反馈第 45 条新增重载（纯新增，不改既有 6 参构造函数——ABI 只允许新增）：
        /// 额外接受 <paramref name="isDegraded"/>/<paramref name="missingTables"/>，供
        /// <see cref="EquipmentScoreAnalyzer.Score"/> 内部构造带降级标记的结果。</summary>
        public EquipmentScoreResult(
            Id templateId,
            Id? classId,
            int itemLevel,
            double score,
            double exponent,
            IReadOnlyDictionary<Id, double> weights,
            bool isDegraded,
            IReadOnlyList<string> missingTables)
        {
            TemplateId = templateId;
            ClassId = classId;
            ItemLevel = itemLevel;
            Score = score;
            Exponent = exponent;
            Weights = weights;
            IsDegraded = isDegraded;
            MissingTables = missingTables ?? Array.Empty<string>();
        }
    }
}
