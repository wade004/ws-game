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

                var prerequisites = new Dictionary<string, List<string>>();
                var nodeIds = new HashSet<string>();

                foreach (var nodeValue in nodesJson)
                {
                    if (!(nodeValue is JsonObject nodeObj)
                        || !nodeObj.TryGetValue("id", out var idValue)
                        || !(idValue is JsonString idStr))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "arch.talent_tree", "talent_node_id",
                            "节点缺少合法的 id 字段", record.Key);
                        continue;
                    }

                    var nodeId = idStr.Value;
                    var list = new List<string>();
                    if (nodeObj.TryGetValue("prerequisites", out var preValue) && preValue is JsonArray preArray)
                    {
                        foreach (var p in preArray)
                        {
                            if (p is JsonString preStr)
                            {
                                list.Add(preStr.Value);
                            }
                        }
                    }

                    nodeIds.Add(nodeId);
                    prerequisites[nodeId] = list;
                }

                foreach (var kv in prerequisites)
                {
                    foreach (var pre in kv.Value)
                    {
                        if (!nodeIds.Contains(pre))
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, "arch.talent_tree", "talent_prerequisite_missing",
                                $"节点 \"{kv.Key}\" 的前置 \"{pre}\" 不存在", record.Key);
                        }
                    }
                }

                var reportedCycle = false;
                foreach (var nodeId in prerequisites.Keys)
                {
                    if (reportedCycle)
                    {
                        break;
                    }

                    if (HasCycle(nodeId, prerequisites, new HashSet<string>(), new HashSet<string>()))
                    {
                        reportedCycle = true;
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "arch.talent_tree", "talent_prerequisite_cycle",
                            $"节点 \"{nodeId}\" 的前置链存在环", record.Key);
                    }
                }
            }
        }

        private static bool HasCycle(
            string nodeId, Dictionary<string, List<string>> prerequisites, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(nodeId))
            {
                return false;
            }

            if (visiting.Contains(nodeId))
            {
                return true;
            }

            if (!prerequisites.TryGetValue(nodeId, out var deps))
            {
                return false;
            }

            visiting.Add(nodeId);
            foreach (var dep in deps)
            {
                if (HasCycle(dep, prerequisites, visiting, visited))
                {
                    return true;
                }
            }
            visiting.Remove(nodeId);
            visited.Add(nodeId);
            return false;
        }
    }
}
