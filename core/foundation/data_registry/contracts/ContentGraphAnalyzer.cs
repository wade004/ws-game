using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 消费方反馈第 56 条（2026-09-18）：<c>story_tree</c>/<c>quest_prerequisite</c>/<c>talent_tree</c>
    /// 三类"节点 + 有向边"图形数据里，孤立节点在 <c>quest_prerequisite</c>（一条独立支线任务本身就是
    /// 合法的"无前置、未被引用"）与 <c>talent_tree</c>（按行解锁天赋，允许多个并列根节点）里都是合法
    /// 内容形态，不应该报告校验警告（会在示例数据上产生大量误报，见
    /// <c>core/gameplay/quest/README.md</c>/<c>core/numbers/archetype/README.md</c>"判断记录"）；
    /// 但内容工具（编辑器）仍然需要"不可达/孤立/入度出度/环"这类图结构信息来做展示（如高亮孤立节点、
    /// 提示编辑者"这条支线任务链断开了"），只是不应该被当作阻断/警告级问题。
    /// <para>
    /// 本类型是提供给内容工具的公开只读图分析入口，对三类图给出统一的分析结果（<see
    /// cref="ContentGraphAnalysis"/>），不产出任何 <see cref="ValidationIssue"/>——是否、以及如何把
    /// 分析结果转成校验问题由调用方自行决定（<c>story_tree</c> 新增的
    /// <c>story_tree_node_unreachable</c> 警告级检查在 <c>DialogContentValidationRule</c> 内部复用
    /// 本类型的 <see cref="Analyze"/>；<c>quest_prerequisite</c>/<c>talent_tree</c> 按上面的判断记录
    /// 不产出新校验问题，只把本类型作为公开分析能力暴露给内容工具）。
    /// </para>
    /// <para>
    /// 判断记录：只读纯函数、不接触 <see cref="IDataRegistryView"/>，输入是调用方已经从各自模块的
    /// 原始 JSON/强类型定义里提取好的"节点 id 全集 + 邻接表"（如
    /// <c>DialogContentValidationRule.ValidateStoryTree</c> 内部的 <c>nodeOrder</c>/
    /// <c>branchesByNode</c>），不感知任何具体表结构——保证同一份实现能被 <c>Core.Gameplay</c>
    /// （<c>dialog</c>/<c>quest</c>）与 <c>Core.Numbers</c>（<c>archetype</c>）两侧共用（两者共同的
    /// 依赖只有 <c>Core.Foundation</c>，见 <c>core/*/Core.*.csproj</c> 的 <c>ProjectReference</c> 链）。
    /// 边引用了 <paramref name="nodeIds"/> 之外的目标（悬空引用）时本类型直接忽略该条边（既不计入
    /// 度数统计、也不参与可达性/成环分析）——悬空引用属于各自模块单独校验的问题（如
    /// <c>story_tree_dangling_next_node</c>/<c>talent_prerequisite_missing</c>），本类型只分析"图内
    /// 真实存在的边"，不重复报告。
    /// </para>
    /// </summary>
    public static class ContentGraphAnalyzer
    {
        /// <summary>对给定的节点集合 + 有向邻接表跑一次统一的图结构分析。</summary>
        /// <param name="nodeIds">图内全部节点 id（决定 <see cref="ContentGraphAnalysis.NodeIds"/>/
        /// 入度出度/孤立节点统计的遍历顺序，调用方传入的顺序即输出顺序，不做任何重排序——保证结果
        /// 确定、可用于快照测试，见 AGENTS.md"保持确定性"）。</param>
        /// <param name="edges">节点 id -> 该节点指向的目标节点 id 列表（有向边，允许为空列表；未出现在
        /// 本字典里的节点视为出度 0）。目标 id 若不在 <paramref name="nodeIds"/> 内按类型顶部判断记录
        /// 直接忽略。</param>
        /// <param name="roots">可达性分析的起点集合；<c>null</c> 或空集合时不做可达性分析——
        /// <see cref="ContentGraphAnalysis.ReachableNodeIds"/>/<see cref="ContentGraphAnalysis.UnreachableNodeIds"/>
        /// 均为空列表（<c>story_tree</c> 传入运行时实际入口 <c>nodes[0]</c>；
        /// <c>quest_prerequisite</c>/<c>talent_tree</c> 允许多根/无单一起点，不适用可达性分析，
        /// 调用方省略本参数即可，只取 <see cref="ContentGraphAnalysis.IsolatedNodeIds"/> 等其它结果）。</param>
        public static ContentGraphAnalysis Analyze(
            IReadOnlyList<string> nodeIds,
            IReadOnlyDictionary<string, IReadOnlyList<string>> edges,
            IReadOnlyList<string>? roots = null)
        {
            if (nodeIds == null) throw new ArgumentNullException(nameof(nodeIds));
            if (edges == null) throw new ArgumentNullException(nameof(edges));

            var nodeIdSet = new HashSet<string>(nodeIds, StringComparer.Ordinal);

            var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
            var outDegree = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in nodeIds)
            {
                inDegree[id] = 0;
                outDegree[id] = 0;
            }

            foreach (var id in nodeIds)
            {
                if (!edges.TryGetValue(id, out var targets))
                {
                    continue;
                }

                foreach (var target in targets)
                {
                    if (!nodeIdSet.Contains(target))
                    {
                        continue; // 悬空引用，见类型顶部判断记录。
                    }

                    outDegree[id]++;
                    inDegree[target]++;
                }
            }

            var isolated = new List<string>();
            foreach (var id in nodeIds)
            {
                if (inDegree[id] == 0 && outDegree[id] == 0)
                {
                    isolated.Add(id);
                }
            }

            IReadOnlyList<string> reachable = Array.Empty<string>();
            IReadOnlyList<string> unreachable = Array.Empty<string>();
            if (roots != null && roots.Count > 0)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var queue = new Queue<string>();
                var reachableOrder = new List<string>();

                foreach (var root in roots)
                {
                    if (!nodeIdSet.Contains(root))
                    {
                        continue; // 起点本身不在图内（如上游数据缺陷），忽略，不抛异常——只读分析入口。
                    }

                    if (visited.Add(root))
                    {
                        queue.Enqueue(root);
                        reachableOrder.Add(root);
                    }
                }

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!edges.TryGetValue(current, out var targets))
                    {
                        continue;
                    }

                    foreach (var target in targets)
                    {
                        if (!nodeIdSet.Contains(target))
                        {
                            continue;
                        }

                        if (visited.Add(target))
                        {
                            queue.Enqueue(target);
                            reachableOrder.Add(target);
                        }
                    }
                }

                reachable = reachableOrder;

                var unreachableOrder = new List<string>();
                foreach (var id in nodeIds)
                {
                    if (!visited.Contains(id))
                    {
                        unreachableOrder.Add(id);
                    }
                }
                unreachable = unreachableOrder;
            }

            var cycles = FindCycles(nodeIds, edges, nodeIdSet);

            return new ContentGraphAnalysis(nodeIds, inDegree, outDegree, isolated, reachable, unreachable, cycles);
        }

        /// <summary>三色标记 DFS，算法与 <c>Core.Gameplay.Dialog.DialogContentValidationRule</c>/
        /// <c>Core.Gameplay.Quest.QuestContentValidationRule</c>/
        /// <c>Core.Numbers.Archetype.ArchTalentTreeCycleValidationRule</c>
        /// 三处既有的单一成环检测同款三色标记思路，区别是：既有三处发现第一个环即整体返回（校验规则只
        /// 需要"有没有环"这一阻断判断，见各自类型判断记录）；本方法作为通用分析入口，继续遍历完整个
        /// 图，收集全部通过"当前 DFS 栈回边"发现的环（同一节点参与的多条环路可能被分别记录，不做去重/
        /// 合并——这是"有多少条独立回边"的忠实记录，不是"最小环基"一类图论最优结果，供内容工具自行按
        /// 需要展示）。</summary>
        private static List<IReadOnlyList<string>> FindCycles(
            IReadOnlyList<string> nodeIds,
            IReadOnlyDictionary<string, IReadOnlyList<string>> edges,
            HashSet<string> nodeIdSet)
        {
            var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=未访问 1=访问中 2=已完成
            var path = new List<string>();
            var cycles = new List<IReadOnlyList<string>>();

            foreach (var start in nodeIds)
            {
                if (state.TryGetValue(start, out var s0) && s0 != 0)
                {
                    continue;
                }

                Visit(start, edges, nodeIdSet, state, path, cycles);
            }

            return cycles;
        }

        private static void Visit(
            string nodeId,
            IReadOnlyDictionary<string, IReadOnlyList<string>> edges,
            HashSet<string> nodeIdSet,
            Dictionary<string, int> state,
            List<string> path,
            List<IReadOnlyList<string>> cycles)
        {
            state[nodeId] = 1;
            path.Add(nodeId);

            if (edges.TryGetValue(nodeId, out var targets))
            {
                foreach (var target in targets)
                {
                    if (!nodeIdSet.Contains(target))
                    {
                        continue;
                    }

                    if (state.TryGetValue(target, out var targetState))
                    {
                        if (targetState == 1)
                        {
                            // 找到一条回边：从 path 中 target 首次出现的位置到当前栈顶，闭合成一条环
                            // （首尾都含 target，与既有三处 story_tree_cycle 等消息格式一致）。复制成
                            // 新列表，不修改共享的 path 本身（path 之后还要继续 DFS 回溯）。
                            var idx = path.IndexOf(target);
                            var cycle = path.GetRange(idx, path.Count - idx);
                            cycle.Add(target);
                            cycles.Add(cycle);
                            continue;
                        }
                        if (targetState == 2)
                        {
                            continue;
                        }
                    }

                    Visit(target, edges, nodeIdSet, state, path, cycles);
                }
            }

            path.RemoveAt(path.Count - 1);
            state[nodeId] = 2;
        }
    }

    /// <summary>一次 <see cref="ContentGraphAnalyzer.Analyze"/> 的只读结果。</summary>
    public sealed class ContentGraphAnalysis
    {
        /// <summary>输入的节点 id 全集，顺序与调用方传入的 <c>nodeIds</c> 一致。</summary>
        public IReadOnlyList<string> NodeIds { get; }

        /// <summary>每个节点的入度（指向该节点、且目标在图内的边数）。</summary>
        public IReadOnlyDictionary<string, int> InDegree { get; }

        /// <summary>每个节点的出度（该节点指向图内其它节点的边数）。</summary>
        public IReadOnlyDictionary<string, int> OutDegree { get; }

        /// <summary>入度与出度均为 0 的节点 id 列表，顺序同 <see cref="NodeIds"/>。</summary>
        public IReadOnlyList<string> IsolatedNodeIds { get; }

        /// <summary>从调用方传入的起点集合可达的节点 id 列表（含起点自身，按 BFS 发现顺序）；
        /// 未传入起点时为空列表。</summary>
        public IReadOnlyList<string> ReachableNodeIds { get; }

        /// <summary><see cref="NodeIds"/> 中不在 <see cref="ReachableNodeIds"/> 里的节点，顺序同
        /// <see cref="NodeIds"/>；未传入起点时为空列表。</summary>
        public IReadOnlyList<string> UnreachableNodeIds { get; }

        /// <summary>DFS 过程中发现的全部回边对应的环（见
        /// <see cref="ContentGraphAnalyzer"/>"三色标记 DFS"判断记录，不是穷举的最小环基）；无环时为
        /// 空列表。</summary>
        public IReadOnlyList<IReadOnlyList<string>> Cycles { get; }

        public ContentGraphAnalysis(
            IReadOnlyList<string> nodeIds,
            IReadOnlyDictionary<string, int> inDegree,
            IReadOnlyDictionary<string, int> outDegree,
            IReadOnlyList<string> isolatedNodeIds,
            IReadOnlyList<string> reachableNodeIds,
            IReadOnlyList<string> unreachableNodeIds,
            IReadOnlyList<IReadOnlyList<string>> cycles)
        {
            NodeIds = nodeIds ?? throw new ArgumentNullException(nameof(nodeIds));
            InDegree = inDegree ?? throw new ArgumentNullException(nameof(inDegree));
            OutDegree = outDegree ?? throw new ArgumentNullException(nameof(outDegree));
            IsolatedNodeIds = isolatedNodeIds ?? throw new ArgumentNullException(nameof(isolatedNodeIds));
            ReachableNodeIds = reachableNodeIds ?? throw new ArgumentNullException(nameof(reachableNodeIds));
            UnreachableNodeIds = unreachableNodeIds ?? throw new ArgumentNullException(nameof(unreachableNodeIds));
            Cycles = cycles ?? throw new ArgumentNullException(nameof(cycles));
        }
    }
}
