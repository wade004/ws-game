using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.Archetype
{
    /// <summary>
    /// <c>arch.talent_tree.nodes</c> 专属的内容校验规则（见 04_数据与内容管线.md 第 5 节
    /// "循环引用检测：声明为树/链结构的字段（如天赋前置……）不得成环"）：每个节点的
    /// <c>prerequisites</c> 必须指向同一棵树内存在的节点 id，且前置关系不得成环（含节点引用
    /// 自身）。调用方需要 <c>registry.RegisterValidationRule(new
    /// ArchTalentTreeCycleValidationRule())</c> 才会生效，本模块不自动注册。
    /// <para>
    /// 消费方反馈第 57 条（2026-09-18）：<c>talent_node_id</c>/<c>talent_prerequisite_missing</c> 两项
    /// 单节点类诊断的 <see cref="ValidationIssue.Field"/> 补上具体下标路径（<c>nodes[i].id</c>/
    /// <c>nodes[i].prerequisites[j]</c>），后者同时把持有该缺失前置的节点自身 id 填进
    /// <see cref="ValidationIssue.AffectedNodeIds"/>（<c>talent_node_id</c> 恰好是"id 本身缺失/非法"，
    /// 没有合法节点 id 可填，留空——见该属性默认空集合语义）；<c>talent_prerequisite_cycle</c> 是跨
    /// 节点路径类诊断，按环上出现顺序填入环上全部节点 id。
    /// </para>
    /// <para>
    /// 消费方反馈第 56 条：天赋树允许多个并列根节点（<c>prerequisites=[]</c> 的节点可以有多个，属
    /// 合法的并列分支设计），"孤立节点"（无前置也未被任何其它节点引用）在这里不是内容缺陷——不新增
    /// 等价 <c>story_tree_node_unreachable</c> 的警告级检查，避免对合法内容形态误报。需要"孤立/入度
    /// 出度"一类图结构信息的内容工具改用公开的 <see cref="ContentGraphAnalyzer"/>，本规则不重复提供。
    /// </para>
    /// <para>
    /// 判断记录（成环检测算法，消费方反馈第 57 条一并勘误）：原实现每个候选起点各自用一套全新的
    /// <c>visiting</c>/<c>visited</c> 集合跑递归，等价于"白/灰/黑"三色标记但没有记录路径，无法产出
    /// <see cref="ValidationIssue.AffectedNodeIds"/> 要求的环上节点序列；且按 <c>Dictionary.Keys</c>
    /// 顺序选取候选起点（AGENTS.md"保持确定性：不依赖字典枚举顺序"明确禁止）。改为与
    /// <c>Core.Gameplay.Dialog.DialogContentValidationRule</c>/
    /// <c>Core.Gameplay.Quest.QuestContentValidationRule</c> 同款的单次共享状态三色标记 DFS + 显式
    /// <c>path</c> 列表，起点顺序改用节点在原始 JSON 数组里的出现顺序（<c>nodeOrder</c>），不再依赖
    /// 字典枚举顺序；"是否存在环""报告首个发现的环"两条既有语义不变，只是新增了路径记录能力。
    /// </para>
    /// </summary>
    public sealed class ArchTalentTreeCycleValidationRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll("arch.talent_tree"))
            {
                if (!record.TryGetArray("nodes", out var nodesJson))
                {
                    continue;
                }

                var prerequisites = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                var prerequisiteJsonIndex = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                var nodeIndexOf = new Dictionary<string, int>(StringComparer.Ordinal);
                var nodeOrder = new List<string>();
                var nodeIds = new HashSet<string>(StringComparer.Ordinal);

                for (var i = 0; i < nodesJson.Count; i++)
                {
                    if (!(nodesJson[i] is JsonObject nodeObj)
                        || !nodeObj.TryGetValue("id", out var idValue)
                        || !(idValue is JsonString idStr))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "arch.talent_tree", "talent_node_id",
                            "节点缺少合法的 id 字段", record.Key, field: $"nodes[{i}].id");
                        continue;
                    }

                    var nodeId = idStr.Value;
                    var list = new List<string>();
                    var jsonIndexList = new List<int>();
                    if (nodeObj.TryGetValue("prerequisites", out var preValue) && preValue is JsonArray preArray)
                    {
                        for (var j = 0; j < preArray.Count; j++)
                        {
                            if (preArray[j] is JsonString preStr)
                            {
                                list.Add(preStr.Value);
                                jsonIndexList.Add(j);
                            }
                        }
                    }

                    nodeIds.Add(nodeId);
                    nodeIndexOf[nodeId] = i;
                    nodeOrder.Add(nodeId);
                    prerequisites[nodeId] = list;
                    prerequisiteJsonIndex[nodeId] = jsonIndexList;
                }

                foreach (var kv in prerequisites)
                {
                    var jsonIndexList = prerequisiteJsonIndex[kv.Key];
                    for (var entryIdx = 0; entryIdx < kv.Value.Count; entryIdx++)
                    {
                        var pre = kv.Value[entryIdx];
                        if (!nodeIds.Contains(pre))
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, "arch.talent_tree", "talent_prerequisite_missing",
                                $"节点 \"{kv.Key}\" 的前置 \"{pre}\" 不存在", record.Key,
                                field: $"nodes[{nodeIndexOf[kv.Key]}].prerequisites[{jsonIndexList[entryIdx]}]",
                                group: null, note: null, ruleId: null, affectedNodeIds: new[] { kv.Key });
                        }
                    }
                }

                if (TryFindCycle(nodeOrder, prerequisites, out var cyclePath))
                {
                    // 消费方反馈第 57 条：跨节点路径类诊断按环上出现顺序填入环上全部节点 id；消息文本
                    // 顺带补上完整链路，与 story_tree_cycle/quest_prerequisite_cycle 同款格式一致
                    // （原消息只报告 DFS 起点，未展示完整链路，本次一并勘误）。
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "arch.talent_tree", "talent_prerequisite_cycle",
                        $"节点 \"{cyclePath[0]}\" 的前置链存在环：{string.Join(" -> ", cyclePath)}", record.Key,
                        field: null, group: null, note: null, ruleId: null, affectedNodeIds: cyclePath);
                }
            }
        }

        /// <summary>三色标记的迭代式 DFS 成环检测，算法与
        /// <c>Core.Gameplay.Dialog.DialogContentValidationRule</c>/
        /// <c>Core.Gameplay.Quest.QuestContentValidationRule</c> 的同名检查同款（见类型顶部判断记录），
        /// 候选起点按 <paramref name="nodeOrder"/>（原始 JSON 数组出现顺序）而非字典枚举顺序遍历，只
        /// 报告首个发现的环，不穷举全部环。</summary>
        private static bool TryFindCycle(
            List<string> nodeOrder,
            Dictionary<string, List<string>> prerequisites,
            out IReadOnlyList<string> cyclePath)
        {
            var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=未访问 1=访问中 2=已完成
            var path = new List<string>();

            foreach (var start in nodeOrder)
            {
                if (state.TryGetValue(start, out var s0) && s0 != 0)
                {
                    continue;
                }

                if (Visit(start, prerequisites, state, path))
                {
                    cyclePath = path;
                    return true;
                }
            }

            cyclePath = Array.Empty<string>();
            return false;
        }

        private static bool Visit(
            string nodeId,
            Dictionary<string, List<string>> prerequisites,
            Dictionary<string, int> state,
            List<string> path)
        {
            state[nodeId] = 1;
            path.Add(nodeId);

            if (prerequisites.TryGetValue(nodeId, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (!prerequisites.ContainsKey(dep))
                    {
                        continue; // 前置指向不存在的节点属于另一条校验项（talent_prerequisite_missing），本方法只管成环检测。
                    }

                    if (state.TryGetValue(dep, out var depState))
                    {
                        if (depState == 1)
                        {
                            path.Add(dep);
                            return true;
                        }
                        if (depState == 2)
                        {
                            continue;
                        }
                    }

                    if (Visit(dep, prerequisites, state, path))
                    {
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[nodeId] = 2;
            return false;
        }
    }
}
