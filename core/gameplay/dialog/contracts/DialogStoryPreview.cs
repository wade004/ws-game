using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Dialog
{
    /// <summary>
    /// 消费方反馈第 55 条（2026-09-18，见
    /// architecture/落地计划/消费方反馈-2026-09-18-编辑器-第55条.md）：单条剧情分支在预演结构里的
    /// 只读快照——<see cref="TargetNodeId"/> 为 <c>null</c> 表示该分支是终止分支（同
    /// <see cref="StoryBranchDef.NextNodeId"/> 语义，见该类型判断记录"分支粒度的终止语义"）。
    /// <see cref="ConditionText"/> 为该分支 <see cref="StoryBranchDef.Condition"/> 的规范化文本
    /// （<see cref="Core.Foundation.Expr.ExprNode.ToString"/> 还原，不是源文件里原始逐字符文本——
    /// 语法树本身不保留原始字符串，规范化文本与原文语义等价，见 <c>ExprNode.ToString</c> 判断记录），
    /// <c>null</c> 表示该分支无条件（恒可见）。
    /// </summary>
    public sealed class DialogStoryPreviewBranch
    {
        /// <summary>该分支在所属节点 <see cref="StoryNodeDefinition.Branches"/> 里的原始下标（供调用方
        /// 与 <see cref="IDialogHost.AdvanceStory"/> 的 <c>branchIndex</c> 参数对齐）。</summary>
        public int BranchIndex { get; }

        public Id TextKey { get; }

        /// <summary>无条件（恒可见）时为 <c>null</c>。</summary>
        public string? ConditionText { get; }

        /// <summary>为 <c>null</c> 表示该分支终止对话。</summary>
        public Id? TargetNodeId { get; }

        public DialogStoryPreviewBranch(
            int branchIndex,
            Id textKey,
            string? conditionText,
            Id? targetNodeId)
        {
            BranchIndex = branchIndex;
            TextKey = textKey;
            ConditionText = conditionText;
            TargetNodeId = targetNodeId;
        }
    }

    /// <summary>单个剧情节点在预演结构里的只读快照，字段与 <see cref="StoryNodeDefinition"/> 一一对应。</summary>
    public sealed class DialogStoryPreviewNode
    {
        public Id NodeId { get; }

        public Id TextKey { get; }

        public Id? SpeakerRef { get; }

        public IReadOnlyList<DialogStoryPreviewBranch> Branches { get; }

        public DialogStoryPreviewNode(
            Id nodeId,
            Id textKey,
            Id? speakerRef,
            IReadOnlyList<DialogStoryPreviewBranch> branches)
        {
            NodeId = nodeId;
            TextKey = textKey;
            SpeakerRef = speakerRef;
            Branches = branches;
        }
    }

    /// <summary>
    /// <see cref="DialogStoryPreview.Preview"/> 的返回值。
    /// <para>
    /// <see cref="ReachableNodeIds"/>/<see cref="Paths"/> 仅在调用方传入
    /// <c>evaluateCondition</c> 回调时才非 <c>null</c>（未传入时为 <c>null</c>，不是空集合——区分
    /// "未做该项分析" 与 "分析结果为空"，见 <see cref="DialogStoryPreview"/> 判断记录）。
    /// <see cref="PathsTruncated"/> 恒可读，未做路径分析时恒为 <c>false</c>。
    /// </para>
    /// </summary>
    public sealed class DialogStoryPreviewResult
    {
        public Id TreeId { get; }

        /// <summary>起点，恒等于 <see cref="StoryTreeDefinition.FirstNode"/>.Id（与
        /// <see cref="DialogHost.StartStory"/> 共用同一段"起点 = nodes[0]"取值逻辑，本类型不复制一份，
        /// 直接读取 <see cref="StoryTreeDefinition.FirstNode"/>）。</summary>
        public Id StartNodeId { get; }

        /// <summary>树上全部节点（原始顺序，即 <see cref="StoryTreeDefinition.Nodes"/> 顺序），不限定
        /// 是否从起点可达——"从起点是否可达"是 <see cref="ReachableNodeIds"/>（需要条件回调）或调用方
        /// 自行按 <see cref="DialogStoryPreviewBranch.TargetNodeId"/> 遍历的职责，本字段只给结构全貌。</summary>
        public IReadOnlyList<DialogStoryPreviewNode> Nodes { get; }

        /// <summary>终止节点：该节点全部分支的 <see cref="DialogStoryPreviewBranch.TargetNodeId"/> 均为
        /// <c>null</c>，或该节点没有任何分支（无法继续）。节点存在部分终止分支、部分非终止分支时不计入
        /// 本列表（该节点仍可继续对话），但对应分支本身仍在 <see cref="DialogStoryPreviewNode.Branches"/>
        /// 里标记为终止边。</summary>
        public IReadOnlyList<Id> TerminalNodeIds { get; }

        /// <summary>仅在传入条件回调时非 <c>null</c>：从起点出发，按回调判定沿可见分支能到达的全部节点
        /// id（含起点自身），按发现顺序排列。</summary>
        public IReadOnlyList<Id>? ReachableNodeIds { get; }

        /// <summary>仅在传入条件回调时非 <c>null</c>：从起点出发的全部（或截断前的部分）路径枚举，
        /// 每条路径是访问过的节点 id 序列（含起点，不含"终止"这一虚拟步骤）。</summary>
        public IReadOnlyList<IReadOnlyList<Id>>? Paths { get; }

        /// <summary>路径枚举是否因达到 <c>maxPaths</c>/<c>maxDepth</c>、遇到环、或遇到悬空
        /// <c>next_node_id</c> 引用而未能穷举完整——真为"部分/近似结果"，见
        /// <see cref="DialogStoryPreview"/> 判断记录"降级显式标记"。</summary>
        public bool PathsTruncated { get; }

        public DialogStoryPreviewResult(
            Id treeId,
            Id startNodeId,
            IReadOnlyList<DialogStoryPreviewNode> nodes,
            IReadOnlyList<Id> terminalNodeIds,
            IReadOnlyList<Id>? reachableNodeIds,
            IReadOnlyList<IReadOnlyList<Id>>? paths,
            bool pathsTruncated)
        {
            TreeId = treeId;
            StartNodeId = startNodeId;
            Nodes = nodes;
            TerminalNodeIds = terminalNodeIds;
            ReachableNodeIds = reachableNodeIds;
            Paths = paths;
            PathsTruncated = pathsTruncated;
        }
    }

    /// <summary>
    /// 消费方反馈第 55 条：只读、无状态的剧情树"预演"入口——不改动 <see cref="IDialogHost"/> 既有成员
    /// （运行期会话契约），单独新增本静态类，供编辑器"试走"面板等场景在不持有任何单位/会话的前提下
    /// 分析一棵 <see cref="StoryTreeDefinition"/>。
    /// <para>
    /// <b>语义边界（判断记录）</b>：本类型只做语法树/图结构遍历与（调用方提供回调时的）分支可见性判定，
    /// 不执行任何节点动作（<see cref="StoryNodeDefinition.PerformanceHookRef"/> 不被调用）、不推进任何
    /// 任务/世界状态、不发布任何事件、不要求/不创建任何对话会话，也不自己求值 <c>condition</c> 表达式——
    /// 是否可见完全由调用方通过 <c>evaluateCondition</c> 回调决定（未提供回调时只给出不考虑条件的纯结构
    /// 信息）。这与 <see cref="IDialogHost.GetStoryView"/> 有本质区别：后者要求单位已处于一个运行期会话，
    /// 按会话当前的 <c>Expr</c> 上下文对唯一一个"当前节点"求值；本类型面向"给定任意剧情树，脱离任何单位/
    /// 会话，模拟走一遍"这一预演场景（如编辑器在保存前检查作者写的分支条件文本是否能覆盖到某个节点）。
    /// </para>
    /// <para>
    /// <b>为何不改 <see cref="IDialogHost"/></b>：预演是一个新的、与运行期会话正交的概念（无状态查询
    /// vs. 有状态推进），加进 <see cref="IDialogHost"/>（哪怕用默认接口成员）会把两种概念混进同一份运行期
    /// 会话契约，且未来预演能力的演进（如接入更丰富的条件求值上下文）不应该受 <see cref="IDialogHost"/>
    /// 兼容性约束牵连；拆成独立静态类是本仓库既有惯例（同 <c>ExprReferenceCollector</c>"只读遍历入口"
    /// 判断记录）。
    /// </para>
    /// <para>
    /// <b>路径枚举语义</b>：提供 <c>evaluateCondition</c> 时，从起点沿"可能可见"的分支做深度优先枚举——
    /// 分支无条件、或回调返回 <c>true</c>/<c>null</c>（未知按"可能"处理，见参数文档）时视为可走；回调
    /// 明确返回 <c>false</c> 时才排除。枚举以下列任一条件安全终止一条路径：命中终止分支
    /// （<c>next_node_id</c> 为空）、该节点没有任何"可能可见"的出边、达到 <paramref name="maxDepth"/>、
    /// 检测到环（当前路径已经访问过目标节点——即便真实剧情树校验会拦下 <c>story_tree_cycle</c>，预演入口
    /// 面向任意（含未经校验/编辑中）数据，必须自行防御，不能假设输入已验证）、或目标 <c>next_node_id</c>
    /// 悬空（引用了不存在的节点，另见反馈第 56 条校验缺口）。命中 <paramref name="maxPaths"/>、或以上任一
    /// "非正常抵达终点"的安全终止发生时，<see cref="DialogStoryPreviewResult.PathsTruncated"/> 置真——
    /// 结果是"部分/近似"的显式标记，不静默呈现一个看似完整实则被截断的路径集合。
    /// </para>
    /// </summary>
    public static class DialogStoryPreview
    {
        public const int DefaultMaxPaths = 200;
        public const int DefaultMaxDepth = 64;

        /// <param name="tree">要预演的剧情树定义（直接传对象；从 <c>IDataRegistryView</c> 按 id 取出
        /// 记录并 <see cref="StoryTreeDefinition.FromRecord"/> 解析是调用方职责，本方法不依赖
        /// <c>data_registry</c>）。</param>
        /// <param name="evaluateCondition">可选的条件判定回调：入参是某分支 <c>condition</c> 的规范化
        /// 文本（<see cref="DialogStoryPreviewBranch.ConditionText"/> 同款），返回 <c>true</c>=可见、
        /// <c>false</c>=不可见、<c>null</c>=未知（按"可能可见"处理，本方法据此把该分支计入可达性/路径
        /// 枚举，但不代表调用方应当把 <c>null</c> 呈现为"确定可见"——具体呈现由调用方决定）。省略时只返回
        /// 不含 <see cref="DialogStoryPreviewResult.ReachableNodeIds"/>/<see cref="DialogStoryPreviewResult.Paths"/>
        /// 的纯结构信息。</param>
        /// <param name="maxPaths">路径枚举的最大条数上限（防御用：分支密集的树路径数可能指数级增长）。</param>
        /// <param name="maxDepth">单条路径的最大节点数上限（防御用，同时兜底"数据本身有环但未被
        /// <see cref="StoryTreeDefinition.HasCycle"/> 校验拦截"的情形——本方法自己也做环检测，见类型
        /// 判断记录，<paramref name="maxDepth"/> 是双重保险）。</param>
        public static DialogStoryPreviewResult Preview(
            StoryTreeDefinition tree,
            Func<string, bool?>? evaluateCondition = null,
            int maxPaths = DefaultMaxPaths,
            int maxDepth = DefaultMaxDepth)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (maxPaths <= 0) throw new ArgumentOutOfRangeException(nameof(maxPaths), "maxPaths 必须为正数");
            if (maxDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maxDepth), "maxDepth 必须为正数");

            var nodes = new List<DialogStoryPreviewNode>(tree.Nodes.Count);
            var terminalNodeIds = new List<Id>();

            foreach (var node in tree.Nodes)
            {
                var branches = new List<DialogStoryPreviewBranch>(node.Branches.Count);
                var allTerminal = true;
                for (var i = 0; i < node.Branches.Count; i++)
                {
                    var b = node.Branches[i];
                    var conditionText = b.Condition?.ToString();
                    branches.Add(new DialogStoryPreviewBranch(i, b.TextKey, conditionText, b.NextNodeId));
                    if (b.NextNodeId.HasValue)
                    {
                        allTerminal = false;
                    }
                }
                nodes.Add(new DialogStoryPreviewNode(node.Id, node.TextKey, node.SpeakerRef, branches));
                if (allTerminal)
                {
                    terminalNodeIds.Add(node.Id);
                }
            }

            IReadOnlyList<Id>? reachable = null;
            IReadOnlyList<IReadOnlyList<Id>>? paths = null;
            var truncated = false;

            if (evaluateCondition != null)
            {
                var reachableOrder = new List<Id>();
                var reachableSeen = new HashSet<Id> { tree.FirstNode.Id };
                reachableOrder.Add(tree.FirstNode.Id);

                var allPaths = new List<IReadOnlyList<Id>>();
                var currentPath = new List<Id> { tree.FirstNode.Id };
                var onPath = new HashSet<Id> { tree.FirstNode.Id };

                truncated = Walk(
                    tree, tree.FirstNode, evaluateCondition, currentPath, onPath,
                    reachableOrder, reachableSeen, allPaths, maxPaths, maxDepth);

                reachable = reachableOrder;
                paths = allPaths;
            }

            return new DialogStoryPreviewResult(
                tree.Id, tree.FirstNode.Id, nodes, terminalNodeIds, reachable, paths, truncated);
        }

        /// <summary>深度优先枚举：返回值表示本次调用（含递归子调用）是否发生过任一"截断"事件。</summary>
        private static bool Walk(
            StoryTreeDefinition tree,
            StoryNodeDefinition node,
            Func<string, bool?> evaluateCondition,
            List<Id> currentPath,
            HashSet<Id> onPath,
            List<Id> reachableOrder,
            HashSet<Id> reachableSeen,
            List<IReadOnlyList<Id>> allPaths,
            int maxPaths,
            int maxDepth)
        {
            var truncated = false;
            var tookAnyBranch = false;

            for (var i = 0; i < node.Branches.Count; i++)
            {
                var branch = node.Branches[i];
                var visible = branch.Condition == null || (evaluateCondition(branch.Condition.ToString()) ?? true);
                if (!visible)
                {
                    continue;
                }

                tookAnyBranch = true;

                if (!branch.NextNodeId.HasValue)
                {
                    if (!TryAddPath(allPaths, currentPath, maxPaths))
                    {
                        return true;
                    }
                    continue;
                }

                var nextId = branch.NextNodeId.Value;

                if (onPath.Contains(nextId))
                {
                    // 遇环：安全终止本路径，不再深入（见类型判断记录，预演入口不假设输入已通过成环校验）。
                    truncated = true;
                    if (!TryAddPath(allPaths, currentPath, maxPaths))
                    {
                        return true;
                    }
                    continue;
                }

                if (!tree.TryGetNode(nextId, out var nextNode))
                {
                    // 悬空 next_node_id：无法继续，显式标记截断而不是抛异常/静默吞掉。
                    truncated = true;
                    if (!TryAddPath(allPaths, currentPath, maxPaths))
                    {
                        return true;
                    }
                    continue;
                }

                if (currentPath.Count >= maxDepth)
                {
                    truncated = true;
                    if (!TryAddPath(allPaths, currentPath, maxPaths))
                    {
                        return true;
                    }
                    continue;
                }

                if (reachableSeen.Add(nextId))
                {
                    reachableOrder.Add(nextId);
                }

                currentPath.Add(nextId);
                onPath.Add(nextId);
                if (Walk(tree, nextNode, evaluateCondition, currentPath, onPath, reachableOrder, reachableSeen, allPaths, maxPaths, maxDepth))
                {
                    truncated = true;
                }
                onPath.Remove(nextId);
                currentPath.RemoveAt(currentPath.Count - 1);

                if (allPaths.Count >= maxPaths)
                {
                    return true;
                }
            }

            if (!tookAnyBranch)
            {
                if (!TryAddPath(allPaths, currentPath, maxPaths))
                {
                    return true;
                }
            }

            return truncated;
        }

        private static bool TryAddPath(
            List<IReadOnlyList<Id>> allPaths,
            List<Id> currentPath,
            int maxPaths)
        {
            if (allPaths.Count >= maxPaths)
            {
                return false;
            }
            allPaths.Add(new List<Id>(currentPath));
            return true;
        }
    }
}
