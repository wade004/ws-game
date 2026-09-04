using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Dialog
{
    /// <summary>一条剧情分支（见 08 第 3.2 节 <c>{textKey, condition?, nextNodeId?}</c>）。
    /// <see cref="NextNodeId"/> 为 null 表示该分支是终止分支（见 08 第 3.2 节"分支为空时该节点是
    /// 终止节点"——任务书拍板扩展为"某条分支的 <c>nextNodeId</c> 为空即该分支终止对话"，比"整个节点
    /// 没有任何分支才终止"更细粒度，允许同一节点部分分支继续、部分分支结束对话）。</summary>
    public sealed class StoryBranchDef
    {
        public Id TextKey { get; }

        public ExprNode? Condition { get; }

        public Id? NextNodeId { get; }

        public StoryBranchDef(Id textKey, ExprNode? condition, Id? nextNodeId)
        {
            TextKey = textKey;
            Condition = condition;
            NextNodeId = nextNodeId;
        }
    }

    /// <summary>一个剧情节点（见 08 第 3.2 节 <c>StoryNode</c> 结构）。</summary>
    public sealed class StoryNodeDefinition
    {
        public Id Id { get; }

        public Id TextKey { get; }

        public Id? SpeakerRef { get; }

        public IReadOnlyList<StoryBranchDef> Branches { get; }

        public Id? PerformanceHookRef { get; }

        public StoryNodeDefinition(Id id, Id textKey, Id? speakerRef, IReadOnlyList<StoryBranchDef> branches, Id? performanceHookRef)
        {
            Id = id;
            TextKey = textKey;
            SpeakerRef = speakerRef;
            Branches = branches ?? throw new ArgumentNullException(nameof(branches));
            PerformanceHookRef = performanceHookRef;
        }
    }

    /// <summary><c>dialog.story_tree</c> 一条记录的内存态表示（见 08 第 3.2 节）。起始节点固定为
    /// <see cref="Nodes"/> 的第一个元素（见任务书"起始节点 = nodes[0]（README）"）。</summary>
    public sealed class StoryTreeDefinition
    {
        public Id Id { get; }

        public IReadOnlyList<StoryNodeDefinition> Nodes { get; }

        private readonly Dictionary<Id, StoryNodeDefinition> _byId;

        public StoryTreeDefinition(Id id, IReadOnlyList<StoryNodeDefinition> nodes)
        {
            Id = id;
            if (nodes == null || nodes.Count == 0)
            {
                throw new ArgumentException("dialog.story_tree.nodes 至少需要一个节点", nameof(nodes));
            }
            Nodes = nodes;

            _byId = new Dictionary<Id, StoryNodeDefinition>();
            foreach (var node in nodes)
            {
                if (_byId.ContainsKey(node.Id))
                {
                    throw new ArgumentException($"dialog.story_tree[{id}] 节点 id \"{node.Id}\" 重复", nameof(nodes));
                }
                _byId[node.Id] = node;
            }
        }

        public StoryNodeDefinition FirstNode => Nodes[0];

        public bool TryGetNode(Id nodeId, out StoryNodeDefinition node) => _byId.TryGetValue(nodeId, out node!);

        public StoryNodeDefinition RequireNode(Id nodeId)
        {
            if (!_byId.TryGetValue(nodeId, out var node))
            {
                throw new ArgumentException($"dialog.story_tree[{Id}] 不存在节点 \"{nodeId}\"", nameof(nodeId));
            }
            return node;
        }

        /// <summary>
        /// 树是否成环（见 08 第 3.2/9 节校验要求"树无环（DFS）"）。用三色标记（未访问/访问中/已完成）
        /// 的迭代式 DFS，检测到"访问中"节点被再次到达即为一条环；<paramref name="cyclePath"/> 在
        /// 检测到环时给出环上的节点 id 序列（供校验消息展示），未成环时为空列表。
        /// </summary>
        public bool HasCycle(out IReadOnlyList<Id> cyclePath)
        {
            var state = new Dictionary<Id, int>(); // 0=未访问 1=访问中 2=已完成
            var path = new List<Id>();

            foreach (var node in Nodes)
            {
                if (state.TryGetValue(node.Id, out var s) && s != 0)
                {
                    continue;
                }
                if (VisitForCycle(node.Id, state, path))
                {
                    cyclePath = path;
                    return true;
                }
            }

            cyclePath = Array.Empty<Id>();
            return false;
        }

        private bool VisitForCycle(Id nodeId, Dictionary<Id, int> state, List<Id> path)
        {
            state[nodeId] = 1;
            path.Add(nodeId);

            if (_byId.TryGetValue(nodeId, out var node))
            {
                foreach (var branch in node.Branches)
                {
                    if (!branch.NextNodeId.HasValue)
                    {
                        continue;
                    }
                    var next = branch.NextNodeId.Value;
                    if (!_byId.ContainsKey(next))
                    {
                        continue; // 悬空引用属于另一条校验项（引用完整性），本方法只管环检测
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

                    if (VisitForCycle(next, state, path))
                    {
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[nodeId] = 2;
            return false;
        }

        public static StoryTreeDefinition FromRecord(DataRecord record, IExprSchema exprSchema)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (exprSchema == null) throw new ArgumentNullException(nameof(exprSchema));

            var id = record.GetId("id");
            var nodesArray = record.GetArray("nodes");
            var nodes = new List<StoryNodeDefinition>(nodesArray.Count);
            foreach (var item in nodesArray)
            {
                if (!(item is JsonObject o))
                {
                    throw new FormatException($"dialog.story_tree[{id}].nodes 的元素必须是对象");
                }
                nodes.Add(ParseNode(id, o, exprSchema));
            }
            return new StoryTreeDefinition(id, nodes);
        }

        private static StoryNodeDefinition ParseNode(Id treeId, JsonObject o, IExprSchema exprSchema)
        {
            if (!o.TryGetValue("id", out var idVal) || !(idVal is JsonString idStr) || !Id.TryParse(idStr.Value, out var nodeId))
            {
                throw new FormatException($"dialog.story_tree[{treeId}].nodes[].id 缺失或不是合法 Id");
            }
            if (!o.TryGetValue("text_key", out var tkVal) || !(tkVal is JsonString tkStr) || !Id.TryParse(tkStr.Value, out var textKey))
            {
                throw new FormatException($"dialog.story_tree[{treeId}].nodes[{nodeId}].text_key 缺失或不是合法 Id");
            }

            Id? speakerRef = null;
            if (o.TryGetValue("speaker_ref", out var sr) && sr is JsonString srStr && Id.TryParse(srStr.Value, out var srId))
            {
                speakerRef = srId;
            }

            Id? hookRef = null;
            if (o.TryGetValue("performance_hook_ref", out var hr) && hr is JsonString hrStr && Id.TryParse(hrStr.Value, out var hrId))
            {
                hookRef = hrId;
            }

            var branches = new List<StoryBranchDef>();
            if (o.TryGetValue("branches", out var branchesVal) && branchesVal is JsonArray branchesArr)
            {
                foreach (var b in branchesArr)
                {
                    if (!(b is JsonObject bo))
                    {
                        throw new FormatException($"dialog.story_tree[{treeId}].nodes[{nodeId}].branches 的元素必须是对象");
                    }
                    branches.Add(ParseBranch(treeId, nodeId, bo, exprSchema));
                }
            }

            return new StoryNodeDefinition(nodeId, textKey, speakerRef, branches, hookRef);
        }

        private static StoryBranchDef ParseBranch(Id treeId, Id nodeId, JsonObject o, IExprSchema exprSchema)
        {
            if (!o.TryGetValue("text_key", out var tkVal) || !(tkVal is JsonString tkStr) || !Id.TryParse(tkStr.Value, out var textKey))
            {
                throw new FormatException($"dialog.story_tree[{treeId}].nodes[{nodeId}].branches[].text_key 缺失或不是合法 Id");
            }

            ExprNode? condition = null;
            if (o.TryGetValue("condition", out var c) && c is JsonString cStr && !string.IsNullOrEmpty(cStr.Value))
            {
                condition = ExprParser.Parse(cStr.Value, exprSchema);
            }

            Id? nextNodeId = null;
            if (o.TryGetValue("next_node_id", out var n) && n is JsonString nStr && Id.TryParse(nStr.Value, out var nId))
            {
                nextNodeId = nId;
            }

            return new StoryBranchDef(textKey, condition, nextNodeId);
        }
    }
}
