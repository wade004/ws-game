using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.Archetype
{
    /// <summary>
    /// 消费方反馈第 56 条追问（2026-09-18，对 1.41.0 处置结果的回应）：<c>talent_tree</c> 里
    /// "既无前置、也未被任何其它节点引用为前置"的孤立节点在 <see cref="ArchTalentTreeCycleValidationRule"/>
    /// 判断记录里被认定为合法内容形态（天赋树允许多个并列根节点），不落地为强制诊断；设计层拍板同
    /// <see cref="Core.Gameplay.Quest.QuestPrerequisiteIsolationRule"/>：提供一条**默认关闭**的可选
    /// 规则，由宿主经 <see cref="ContentValidationOptions.EnableGraphIsolationDiagnostics"/> 显式开启。
    /// <para>
    /// 图与算法：对每一棵 <c>arch.talent_tree</c>（每条记录各自一棵独立的树，与
    /// <see cref="ArchTalentTreeCycleValidationRule"/> 同一构图口径）分别以 <c>nodes[].id</c> 为节点、
    /// <c>nodes[].prerequisites[]</c> 为边（"节点 → 前置节点"），交给
    /// <see cref="ContentGraphAnalyzer.Analyze"/> 统一图分析（不传 <c>roots</c>，本规则只关心
    /// <see cref="ContentGraphAnalysis.IsolatedNodeIds"/>——多个并列根节点是合法设计，没有唯一入口）。
    /// 节点 id 缺失/非法已由 <c>talent_node_id</c> 报告（<see cref="ArchTalentTreeCycleValidationRule"/>），
    /// 本规则直接跳过这类元素，不重复报告；前置指向不存在的节点已由 <c>talent_prerequisite_missing</c>
    /// 报告，本规则统计入度出度时按"目标在图内才算一条边"处理（<see cref="ContentGraphAnalyzer"/> 自身
    /// 的既有判断记录），不会因悬空前置而误判/重复报告。
    /// </para>
    /// <para>
    /// 判断记录（"树内节点数 ≥2 时才报"）：单节点的树必然无前置、也不可能被引用——恒为孤立节点，报告
    /// 没有信息量，本规则对这样的树整体不产出任何问题（逐树判断，不是整表判断——同一批数据里其它树
    /// 节点数 ≥2 时仍正常报告）。
    /// </para>
    /// </summary>
    public sealed class TalentTreeIsolationRule : IValidationRule
    {
        /// <summary>见 <c>Presentation.Assembly.OptionalRuleDescriptor.CheckName</c> 判断记录：消费方
        /// 应引用本常量按检查名过滤诊断，不要复制字面量。</summary>
        public const string CheckName = "talent_node_isolated";

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

        /// <summary>同 <see cref="Core.Gameplay.Quest.QuestPrerequisiteIsolationRule.NonEscalatable"/>
        /// 判断记录：展示性提示，不应被 <see cref="DataRegistryStrictness.WarningsBlock"/> 提升为阻断。</summary>
        public bool NonEscalatable => true;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll("arch.talent_tree"))
            {
                if (!record.TryGetArray("nodes", out var nodesJson))
                {
                    continue;
                }

                var prerequisites = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                var nodeIndexOf = new Dictionary<string, int>(StringComparer.Ordinal);
                var nodeOrder = new List<string>();

                for (var i = 0; i < nodesJson.Count; i++)
                {
                    if (!(nodesJson[i] is JsonObject nodeObj)
                        || !nodeObj.TryGetValue("id", out var idValue)
                        || !(idValue is JsonString idStr))
                    {
                        continue; // id 缺失/非法已由 talent_node_id 报告，见类型顶部判断记录。
                    }

                    var nodeId = idStr.Value;
                    if (nodeIndexOf.ContainsKey(nodeId))
                    {
                        continue; // 重复 id：以首次出现的下标为准，重复本身不是本规则职责。
                    }

                    var targets = new List<string>();
                    if (nodeObj.TryGetValue("prerequisites", out var preValue) && preValue is JsonArray preArray)
                    {
                        foreach (var p in preArray)
                        {
                            if (p is JsonString preStr)
                            {
                                targets.Add(preStr.Value);
                            }
                        }
                    }

                    nodeIndexOf[nodeId] = i;
                    nodeOrder.Add(nodeId);
                    prerequisites[nodeId] = targets;
                }

                if (nodeOrder.Count < 2)
                {
                    continue; // 见类型顶部判断记录。
                }

                var analysis = ContentGraphAnalyzer.Analyze(nodeOrder, prerequisites);
                foreach (var isolatedId in analysis.IsolatedNodeIds)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Warning, "arch.talent_tree", CheckName,
                        $"节点 \"{isolatedId}\" 既无前置天赋，也未被任何其它节点的 prerequisites 引用——" +
                            "是这棵天赋树内的孤立节点（可能是并列根节点，也可能是遗漏的前置关系，" +
                            "本诊断只作提示，不代表内容缺陷）",
                        record.Key, field: $"nodes[{nodeIndexOf[isolatedId]}]", group: null, note: null, ruleId: null,
                        affectedNodeIds: new[] { isolatedId });
                }
            }
        }
    }
}
