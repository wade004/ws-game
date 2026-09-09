using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Dialog
{
    /// <summary>
    /// <c>dialog.gossip_menu</c>/<c>dialog.story_tree</c> 专属内容校验规则（见
    /// <see cref="IValidationRule"/>"模块专属校验规则的扩展点"）。
    /// <para>
    /// ADR-0019 / F1b 收窄判断记录：本规则此前整条委托给 <see cref="GossipMenuDefinition.FromRecord"/>/
    /// <see cref="StoryTreeDefinition.FromRecord"/> 解析、把任意解析异常（含纯结构性问题：
    /// <c>text_key</c> 缺失、<c>actions[].kind</c> 缺失/非法、<c>ref</c> 按 <c>kind</c> 语义缺失、
    /// <c>visible_if</c>/<c>condition</c> Expr 语法错等）打包成一条泛化的"解析失败"消息报告。
    /// <c>DialogSchemas.GossipOptionItemSchema</c>/<c>StoryNodeItemSchema</c>（F1b 首批登记）已经把
    /// 这些纯结构检查登记为机器可读的 <c>Fields</c>/<c>Variants</c>/<c>Reference</c>/<c>Expr</c>，
    /// <c>DataRegistry</c> 加载期已经独立、逐字段地报出
    /// <c>required_field</c>/<c>field_type</c>/<c>variant_discriminator</c>/<c>reference_integrity</c>/
    /// <c>expr_parsable</c>——若本规则继续整条委托 <c>FromRecord</c>，会对同一处缺陷重复报告一次
    /// （任务书"同一缺陷不得双报"）。本规则因此收窄为只做登记表达不了的纯业务判断（<c>story_tree</c>
    /// 侧保留全部四项登记表达不了的图结构判断）：
    /// </para>
    /// <list type="bullet">
    /// <item><c>nodes</c> 数组至少一个元素（<c>StoryTreeDefinition</c> 构造期硬约束，
    /// <see cref="FieldKind.Array"/> 不表达最小长度）——<c>story_tree_min_nodes</c>。</item>
    /// <item><c>nodes[].id</c> 同一棵树内必须唯一（<c>StoryTreeDefinition</c> 构造期硬约束，跨元素
    /// 一致性检查，<see cref="FieldSchema"/> 不表达）——<c>story_tree_duplicate_node_id</c>。</item>
    /// <item><c>nodes[].branches[].next_node_id</c> 必须命中同一棵树内某个已知节点（<c>Id</c> 本身
    /// 不携带"是否命中已知节点"的信息）——<c>story_tree_dangling_next_node</c>。</item>
    /// <item>树不得成环（<c>StoryTreeDefinition.HasCycle</c> 三色标记 DFS 同款算法，改在原始 JSON
    /// 上直接跑，不依赖 <c>FromRecord</c> 成功解析）——<c>story_tree_cycle</c>。</item>
    /// </list>
    /// <para>
    /// P2-06 关联根治（同一份联合类型缺口，见 <c>QuestContentValidationRule</c>
    /// <c>reward_world_flag_value_shape</c> 判断记录）：<c>gossip_menu</c> 侧新增一项——
    /// <c>options[].actions[].kind == "set_flag"</c> 时 <c>params.value</c>（若提供）经
    /// <see cref="Core.Gameplay.Common.ExprValueJson.Parse"/> 解析（<c>DialogHost.ExecuteAction</c>），
    /// <see cref="DialogSchemas.GossipActionItemSchema"/> 的 <c>set_flag</c> 变体未登记
    /// <c>params.value</c> 子结构（联合类型，<see cref="FieldKind"/> 无法表达，见该处判断记录），
    /// 此前缺失校验，非法形状会在 <c>DialogHost.ExecuteAction</c> 才抛 <see cref="System.FormatException"/>
    /// ——<c>gossip_action_set_flag_value_shape</c>。<c>value</c> 缺省时按 <c>DialogHost</c> 语义
    /// 取 <c>Bool(true)</c>，不要求必填，本项只在 <c>value</c> 存在时校验其形状。
    /// </para>
    /// <para>
    /// 判断记录：出现节点 id 重复时图结构不可靠，<c>next_node_id</c> 悬空引用/成环两项检查对该记录
    /// 跳过（避免在错误的图上继续误报，延续 <c>StoryTreeDefinition</c> 构造函数"重复 id 直接抛异常，
    /// 后续图算法不会跑在损坏的图上"这一既有语义）。
    /// </para>
    /// <para>
    /// <paramref name="exprSchema"/> 构造参数保留只是为了不改动
    /// <c>GameplaySchemaCatalog.RegisterDialogSchemas</c> 现有调用点的签名
    /// （<c>new DialogContentValidationRule(exprSchema)</c>）——本规则收窄后不再需要用它做任何 Expr
    /// 解析：<c>visible_if</c>/<c>condition</c> 的 <c>expr_parsable</c> 检查已经由 <c>DataRegistry</c>
    /// 的子结构字段登记覆盖，且用的是同一份 <c>exprSchema</c> 实例（<c>GameplaySchemaCatalog.
    /// RegisterDialogSchemas</c> 把 <c>FullExprSchema</c> 同时传给 <c>DataRegistryOptions.ExprSchema</c>
    /// 与本规则构造函数），不会产生解析行为分歧（惯例同 <c>QuestContentValidationRule</c> 判断记录）。
    /// </para>
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方需要显式
    /// <c>registry.RegisterValidationRule(new DialogContentValidationRule())</c>（惯例同
    /// <c>QuestContentValidationRule</c>）。
    /// </para>
    /// </summary>
    public sealed class DialogContentValidationRule : IValidationRule
    {
        public DialogContentValidationRule(IExprSchema? exprSchema = null)
        {
            // 见类型顶部判断记录：参数只保留用于构造签名兼容，本规则收窄后不再使用它。
            _ = exprSchema;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (view.Tables.Contains(DialogSchemas.GossipMenu.Name))
            {
                foreach (var record in view.GetAll(DialogSchemas.GossipMenu.Name))
                {
                    foreach (var issue in ValidateGossipMenuSetFlagValues(record))
                    {
                        yield return issue;
                    }
                }
            }

            if (!view.Tables.Contains(DialogSchemas.StoryTree.Name))
            {
                yield break;
            }

            foreach (var record in view.GetAll(DialogSchemas.StoryTree.Name))
            {
                foreach (var issue in ValidateStoryTree(record))
                {
                    yield return issue;
                }
            }
        }

        /// <summary>见类型顶部判断记录（P2-06 关联根治）：<c>options[].actions[].kind == "set_flag"</c>
        /// 时若提供了 <c>params.value</c>，其形状必须落在
        /// <see cref="Core.Gameplay.Common.ExprValueJson.IsValid"/> 接受的集合内。</summary>
        private static IEnumerable<ValidationIssue> ValidateGossipMenuSetFlagValues(DataRecord record)
        {
            if (!record.TryGetArray("options", out var optionsRaw))
            {
                yield break; // 缺失/类型不符已由 required_field/field_type 报告。
            }

            for (var i = 0; i < optionsRaw.Count; i++)
            {
                if (!(optionsRaw[i] is JsonObject option))
                {
                    continue;
                }

                if (!option.TryGetValue("actions", out var actionsRaw) || !(actionsRaw is JsonArray actions))
                {
                    continue;
                }

                for (var j = 0; j < actions.Count; j++)
                {
                    if (!(actions[j] is JsonObject action))
                    {
                        continue;
                    }

                    if (!action.TryGetValue("kind", out var kindVal) || !(kindVal is JsonString kindStr) ||
                        kindStr.Value != DialogActionKinds.ToWireString(DialogActionKind.SetFlag))
                    {
                        continue;
                    }

                    if (!action.TryGetValue("params", out var paramsRaw) || !(paramsRaw is JsonObject paramsObj))
                    {
                        continue; // params 本身可选，缺失时 DialogHost.ExecuteAction 取 Bool(true)。
                    }

                    if (!paramsObj.TryGetValue("value", out var valueRaw))
                    {
                        continue; // value 可选，同上缺省语义。
                    }

                    if (!Core.Gameplay.Common.ExprValueJson.IsValid(valueRaw))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, DialogSchemas.GossipMenu.Name,
                            "gossip_action_set_flag_value_shape",
                            $"options[{i}].actions[{j}].params.value 不是合法的 ExprValue 形状" +
                                $"（Bool|Number|String|{{\"$id\":...}}），实际类型：{valueRaw.Kind}",
                            recordKey: record.Key, field: $"options[{i}].actions[{j}].params.value");
                    }
                }
            }
        }

        private static IEnumerable<ValidationIssue> ValidateStoryTree(DataRecord record)
        {
            // 数组本身缺失/类型不对已由 required_field/field_type 结构校验报告，这里只处理"数组
            // 已经确认存在"之后登记表达不了的图结构判断。
            if (!record.TryGetArray("nodes", out var nodesRaw))
            {
                yield break;
            }

            if (nodesRaw.Count == 0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, DialogSchemas.StoryTree.Name, "story_tree_min_nodes",
                    "dialog.story_tree.nodes 至少需要一个节点", recordKey: record.Key, field: "nodes");
                yield break; // 没有节点，后续悬空引用/成环检查无意义。
            }

            // nodeId -> 该节点各分支的 next_node_id 列表（null 表示该分支是终止分支）。
            var branchesByNode = new Dictionary<string, List<string?>>(System.StringComparer.Ordinal);
            var firstIndexOf = new Dictionary<string, int>(System.StringComparer.Ordinal);
            var nodeOrder = new List<string>();
            var duplicateIds = new SortedSet<string>(System.StringComparer.Ordinal);

            for (var i = 0; i < nodesRaw.Count; i++)
            {
                if (!(nodesRaw[i] is JsonObject nodeObj))
                {
                    continue; // 元素不是对象已由 field_type 报告。
                }

                if (!nodeObj.TryGetValue("id", out var idRaw) || !(idRaw is JsonString idStr) || !Id.TryParse(idStr.Value, out _))
                {
                    continue; // id 缺失/格式非法已由 required_field/field_type 报告。
                }

                var nodeId = idStr.Value;
                if (firstIndexOf.ContainsKey(nodeId))
                {
                    duplicateIds.Add(nodeId);
                }
                else
                {
                    firstIndexOf[nodeId] = i;
                    nodeOrder.Add(nodeId);
                }

                var nexts = new List<string?>();
                if (nodeObj.TryGetValue("branches", out var branchesRaw) && branchesRaw is JsonArray branchesArr)
                {
                    foreach (var b in branchesArr)
                    {
                        if (!(b is JsonObject bo))
                        {
                            continue; // 元素不是对象已由 field_type 报告。
                        }

                        string? nextId = null;
                        if (bo.TryGetValue("next_node_id", out var nn) && nn is JsonString nnStr && Id.TryParse(nnStr.Value, out _))
                        {
                            nextId = nnStr.Value;
                        }
                        nexts.Add(nextId);
                    }
                }

                // 重复 id 时以最后一次出现的 branches 为准；重复本身已在下方单独报错，不影响正确性判断。
                branchesByNode[nodeId] = nexts;
            }

            foreach (var dup in duplicateIds)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, DialogSchemas.StoryTree.Name, "story_tree_duplicate_node_id",
                    $"节点 id \"{dup}\" 重复", recordKey: record.Key, field: "nodes");
            }

            if (duplicateIds.Count > 0)
            {
                yield break; // 图结构不可靠，见类型顶部判断记录，不再继续跑悬空引用/成环检查。
            }

            var nodeIdSet = new HashSet<string>(firstIndexOf.Keys, System.StringComparer.Ordinal);
            foreach (var nodeId in nodeOrder)
            {
                foreach (var next in branchesByNode[nodeId])
                {
                    if (next != null && !nodeIdSet.Contains(next))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, DialogSchemas.StoryTree.Name, "story_tree_dangling_next_node",
                            $"节点 \"{nodeId}\" 的分支 next_node_id \"{next}\" 在树内不存在",
                            recordKey: record.Key, field: "nodes");
                    }
                }
            }

            if (TryFindCycle(nodeOrder, branchesByNode, nodeIdSet, out var cyclePath))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, DialogSchemas.StoryTree.Name, "story_tree_cycle",
                    $"剧情树成环：{string.Join(" -> ", cyclePath)}", recordKey: record.Key);
            }
        }

        /// <summary>三色标记的迭代式 DFS 成环检测，算法与 <see cref="StoryTreeDefinition.HasCycle"/>
        /// 一致，只是直接跑在原始 JSON 派生出的邻接表上（不要求 <c>FromRecord</c> 成功解析出强类型
        /// 的 <see cref="StoryTreeDefinition"/>），避免与登记表达不了的图结构检查重复依赖同一次强
        /// 类型解析。</summary>
        private static bool TryFindCycle(
            List<string> nodeOrder,
            Dictionary<string, List<string?>> branchesByNode,
            HashSet<string> nodeIdSet,
            out IReadOnlyList<string> cyclePath)
        {
            var state = new Dictionary<string, int>(System.StringComparer.Ordinal); // 0=未访问 1=访问中 2=已完成
            var path = new List<string>();

            foreach (var start in nodeOrder)
            {
                if (state.TryGetValue(start, out var s0) && s0 != 0)
                {
                    continue;
                }
                if (Visit(start, branchesByNode, nodeIdSet, state, path))
                {
                    cyclePath = path;
                    return true;
                }
            }

            cyclePath = System.Array.Empty<string>();
            return false;
        }

        private static bool Visit(
            string nodeId,
            Dictionary<string, List<string?>> branchesByNode,
            HashSet<string> nodeIdSet,
            Dictionary<string, int> state,
            List<string> path)
        {
            state[nodeId] = 1;
            path.Add(nodeId);

            if (branchesByNode.TryGetValue(nodeId, out var nexts))
            {
                foreach (var next in nexts)
                {
                    if (next == null || !nodeIdSet.Contains(next))
                    {
                        continue; // 悬空引用属于另一条校验项（story_tree_dangling_next_node），本方法只管成环检测。
                    }

                    if (state.TryGetValue(next, out var nextState))
                    {
                        if (nextState == 1)
                        {
                            path.Add(next);
                            return true;
                        }
                        if (nextState == 2)
                        {
                            continue;
                        }
                    }

                    if (Visit(next, branchesByNode, nodeIdSet, state, path))
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
