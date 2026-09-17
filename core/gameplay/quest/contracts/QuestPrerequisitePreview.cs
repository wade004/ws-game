using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <see cref="QuestPrerequisitePreview.Preview"/> 的返回值。
    /// <para>
    /// <see cref="IsAvailableByReferencedQuests"/>/<see cref="BlockingQuestIds"/> 仅在调用方传入
    /// <c>completedQuestIds</c> 时才非 <c>null</c>（区分"未做该项判定"与"判定结果为空"，同
    /// <see cref="Core.Gameplay.Dialog.DialogStoryPreviewResult"/> 判断记录）。
    /// </para>
    /// </summary>
    public sealed class QuestPrerequisitePreviewResult
    {
        public Id QuestId { get; }

        /// <summary><see cref="QuestId"/> 的 <c>prerequisite</c> 表达式里直接引用（<c>quest.*</c> 分组
        /// 引用的 Id 字面量参数，见 <see cref="QuestPrerequisitePreview"/> 判断记录）的任务 id，按表达式
        /// 中出现顺序去重排列——不区分表达式内 <c>and</c>/<c>or</c>/<c>not</c> 语义（与既有
        /// <c>quest_prerequisite_cycle</c>/<c>quest_prerequisite_unknown</c> 校验构图同一套"只看引用了
        /// 谁，不看布尔组合方式"的口径，见判断记录）。可能包含在给定定义集合里找不到的 id——那些同时会出现在
        /// <see cref="UnknownReferencedQuestIds"/> 里。</summary>
        public IReadOnlyList<Id> DirectPrerequisiteQuestIds { get; }

        /// <summary>从 <see cref="QuestId"/> 出发，沿"直接引用"边可达的全部已知任务 id（不含
        /// <see cref="QuestId"/> 自身，不含未知引用），按广度优先发现顺序排列。</summary>
        public IReadOnlyList<Id> TransitiveClosureQuestIds { get; }

        /// <summary>遍历过程中遇到的、不在给定定义集合内的引用任务 id（去重，按发现顺序）——数据缺陷
        /// （悬空 <c>prerequisite</c> 引用）的显式标记，不静默丢弃（见 AGENTS.md"只读分析类入口遇阻断态
        /// 显式标记降级"）。</summary>
        public IReadOnlyList<Id> UnknownReferencedQuestIds { get; }

        /// <summary>以 <see cref="QuestId"/> 为根的前置子图（<see cref="QuestId"/> ∪
        /// <see cref="TransitiveClosureQuestIds"/>）内是否存在环。</summary>
        public bool HasCycle { get; }

        /// <summary><see cref="HasCycle"/> 为真时，给出发现的第一条环（首尾重复同一 id，标记环的闭合，
        /// 与 <see cref="Core.Gameplay.Dialog.StoryTreeDefinition.HasCycle"/>/既有
        /// <c>quest_prerequisite_cycle</c> 校验消息同一套格式）；否则为空列表。</summary>
        public IReadOnlyList<Id> CyclePath { get; }

        /// <summary>子图的拓扑序：前置任务排在被依赖任务之前（即"按此顺序逐个完成，能满足下游任务的
        /// <c>quest.*</c> 引用类前置"的一个合法顺序）。<see cref="HasCycle"/> 为真时恒为空列表——不是
        /// "恰好没有前置"，是"该子图不存在合法拓扑序"，调用方需要先看 <see cref="HasCycle"/> 再解读本
        /// 字段（见类型判断记录，不能把"空列表"直接误读为"无前置"）。</summary>
        public IReadOnlyList<Id> TopologicalOrder { get; }

        /// <summary>遍历因达到 <c>maxNodes</c> 上限而未能扩展完整子图时置真——结果（尤其
        /// <see cref="TransitiveClosureQuestIds"/>/<see cref="TopologicalOrder"/>）可能不完整，显式标记，
        /// 不静默截断。真实内容规模下预计不会触发（见 <see cref="QuestPrerequisitePreview.DefaultMaxNodes"/>
        /// 默认值判断记录）。</summary>
        public bool ClosureTruncated { get; }

        /// <summary>仅在调用方传入 <c>completedQuestIds</c> 时非 <c>null</c>：<see cref="QuestId"/> 的
        /// 全部 <see cref="DirectPrerequisiteQuestIds"/> 是否都在给定的已完成任务集合内——注意这只覆盖
        /// <c>quest.*</c> 引用类前置，不覆盖 <c>prerequisite</c> 里可能同时存在的其它条件（如世界标志/
        /// 玩家属性），也不做布尔组合语义求值（同 <see cref="DirectPrerequisiteQuestIds"/> 判断记录）——
        /// 是"任务链"维度的结构性预演，不是完整前置表达式的真值判定，见
        /// <see cref="QuestPrerequisitePreview"/> 类型判断记录"语义边界"。</summary>
        public bool? IsAvailableByReferencedQuests { get; }

        /// <summary>仅在调用方传入 <c>completedQuestIds</c> 时非 <c>null</c>：
        /// <see cref="DirectPrerequisiteQuestIds"/> 中不在已完成集合内的子集（含未知引用——未知引用
        /// 恒视为"未满足"，不假设缺失数据等价于已完成）。</summary>
        public IReadOnlyList<Id>? BlockingQuestIds { get; }

        public QuestPrerequisitePreviewResult(
            Id questId,
            IReadOnlyList<Id> directPrerequisiteQuestIds,
            IReadOnlyList<Id> transitiveClosureQuestIds,
            IReadOnlyList<Id> unknownReferencedQuestIds,
            bool hasCycle,
            IReadOnlyList<Id> cyclePath,
            IReadOnlyList<Id> topologicalOrder,
            bool closureTruncated,
            bool? isAvailableByReferencedQuests,
            IReadOnlyList<Id>? blockingQuestIds)
        {
            QuestId = questId;
            DirectPrerequisiteQuestIds = directPrerequisiteQuestIds;
            TransitiveClosureQuestIds = transitiveClosureQuestIds;
            UnknownReferencedQuestIds = unknownReferencedQuestIds;
            HasCycle = hasCycle;
            CyclePath = cyclePath;
            TopologicalOrder = topologicalOrder;
            ClosureTruncated = closureTruncated;
            IsAvailableByReferencedQuests = isAvailableByReferencedQuests;
            BlockingQuestIds = blockingQuestIds;
        }
    }

    /// <summary>
    /// 消费方反馈第 55 条（2026-09-18，见
    /// architecture/落地计划/消费方反馈-2026-09-18-编辑器-第55条.md）：只读、无状态的任务前置链
    /// "预演"入口——不改动 <see cref="IQuestHost"/> 既有成员（运行期状态机契约），单独新增本静态类。
    /// <para>
    /// <b>语义边界（判断记录）</b>：本类型只分析 <see cref="QuestDefinition.Prerequisite"/> 表达式里
    /// <c>quest.*</c> 分组引用（即"这个任务的前置里点名了哪些其它任务"）构成的有向图结构（直接前置/
    /// 传递闭包/成环/拓扑序），不涉及 <see cref="QuestObjective"/> 语义（目标类型/计数/是否达标一律不
    /// 关心），也不对 <c>prerequisite</c> 整条表达式做真正的布尔求值——<c>and</c>/<c>or</c>/<c>not</c>
    /// 组合方式与既有 <c>quest_prerequisite_cycle</c>/<c>quest_prerequisite_unknown</c> 校验构图同一套
    /// 口径，均"只看引用了谁，不看怎么组合"（见 <c>QuestContentValidationRule.CollectQuestIdLiteralArgs</c>
    /// 判断记录）。<see cref="QuestPrerequisitePreviewResult.IsAvailableByReferencedQuests"/> 因此是
    /// "假设 prerequisite 只由这些 quest.* 引用以合取方式组成"这一简化视角下的可接性，不是
    /// <see cref="IQuestHost.GetState"/> 的等价物——真正的可接性仍须调用 <see cref="IQuestHost.GetState"/>
    /// 按运行期世界状态完整求值。
    /// </para>
    /// <para>
    /// <b>前置引用的提取</b>（整合验收，1.41.0 勘误）：直接复用第 54 条新增的
    /// <see cref="QuestReferenceExtractor"/> 的 <c>internal</c> 重载（接受已解析的
    /// <see cref="ExprNode"/>），不需要 <see cref="IExprSchema"/>：直接读取
    /// <see cref="QuestDefinition.Prerequisite"/> 已解析好的语法树，不重新解析文本。本方法原本直接调用
    /// <see cref="ExprReferenceCollector.Collect"/> 自行过滤 <c>Group == quest</c> + 取 Id 字面量参数，
    /// 与 <c>QuestReferenceExtractor.CollectQuestIdLiteralArgs</c>（第 54 条，同样最终基于
    /// <see cref="ExprReferenceCollector.Collect"/>）重复了这一步过滤逻辑——整合时改为直接调用前者，
    /// 消除两处重复实现，行为不变（回归测试见 <c>E55_QuestPrerequisitePreviewTests</c>）。
    /// </para>
    /// <para>
    /// <b>为何不改 <see cref="IQuestHost"/></b>：同 <c>DialogStoryPreview</c> 判断记录——预演是与运行期
    /// 状态机正交的无状态查询，不应该混进 <see cref="IQuestHost"/> 这份有状态契约。
    /// </para>
    /// </summary>
    public static class QuestPrerequisitePreview
    {
        /// <summary>子图节点数防御上限——真实内容的任务前置链规模远小于此值，命中说明数据本身有异常
        /// （如极端稠密的引用图），属于防御性兜底，不期望在正常内容上触发。</summary>
        public const int DefaultMaxNodes = 10000;

        /// <param name="questId">要预演的任务 id。</param>
        /// <param name="definitions">已知任务定义集合（直接传对象；从 <c>IDataRegistryView</c> 按 id 取出
        /// 记录并 <see cref="QuestDefinition.FromRecord"/> 解析是调用方职责）。<paramref name="questId"/>
        /// 必须能在其中找到，否则抛 <see cref="ArgumentException"/>（同仓库既有"未登记 id 直接抛出"
        /// 惯例，见 <c>DialogHost.RequireTree</c> 同款判断记录）。</param>
        /// <param name="completedQuestIds">可选的已完成任务集合；提供时额外给出
        /// <see cref="QuestPrerequisitePreviewResult.IsAvailableByReferencedQuests"/>/
        /// <see cref="QuestPrerequisitePreviewResult.BlockingQuestIds"/>。</param>
        /// <param name="maxNodes">子图节点数防御上限，见 <see cref="DefaultMaxNodes"/> 判断记录。</param>
        public static QuestPrerequisitePreviewResult Preview(
            Id questId,
            IEnumerable<QuestDefinition> definitions,
            IReadOnlyCollection<Id>? completedQuestIds = null,
            int maxNodes = DefaultMaxNodes)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (maxNodes <= 0) throw new ArgumentOutOfRangeException(nameof(maxNodes), "maxNodes 必须为正数");

            var byId = new Dictionary<Id, QuestDefinition>();
            foreach (var def in definitions)
            {
                byId[def.Id] = def;
            }

            if (!byId.TryGetValue(questId, out var rootDef))
            {
                throw new ArgumentException($"未在给定定义集合中找到 quest.def id：\"{questId}\"", nameof(questId));
            }

            var dependsOn = new Dictionary<Id, IReadOnlyList<Id>>();
            var closureOrder = new List<Id>();
            var closureSeen = new HashSet<Id> { questId };
            var unknownOrder = new List<Id>();
            var unknownSeen = new HashSet<Id>();
            var truncated = false;

            var direct = ExtractDirectReferences(rootDef);
            dependsOn[questId] = direct;
            RecordUnknown(direct, byId, unknownSeen, unknownOrder);

            var queue = new Queue<Id>();
            EnqueueKnownNew(direct, byId, closureSeen, closureOrder, queue, maxNodes, ref truncated);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentDef = byId[current];
                var refs = ExtractDirectReferences(currentDef);
                dependsOn[current] = refs;
                RecordUnknown(refs, byId, unknownSeen, unknownOrder);
                EnqueueKnownNew(refs, byId, closureSeen, closureOrder, queue, maxNodes, ref truncated);
            }

            // 子图 = questId ∪ transitive closure（已知节点）；拓扑序按"前置在前"排列，构造反向邻接表
            // （prerequisite -> dependent）后跑 Kahn 算法，全程只用列表/队列迭代顺序，不依赖字典枚举顺序
            // 或哈希顺序（AGENTS.md 第 3 节"保持确定性"）。
            var nodesInOrder = new List<Id> { questId };
            nodesInOrder.AddRange(closureOrder);
            var nodeSet = new HashSet<Id>(nodesInOrder);

            var unlocks = new Dictionary<Id, List<Id>>();
            var inDegree = new Dictionary<Id, int>();
            foreach (var x in nodesInOrder)
            {
                var refs = dependsOn[x];
                var degree = 0;
                foreach (var y in refs)
                {
                    if (!nodeSet.Contains(y))
                    {
                        continue; // 未知引用，或因 maxNodes 截断而未纳入子图——不计入拓扑序的边。
                    }
                    degree++;
                    if (!unlocks.TryGetValue(y, out var list))
                    {
                        list = new List<Id>();
                        unlocks[y] = list;
                    }
                    list.Add(x);
                }
                inDegree[x] = degree;
            }

            var frontier = new Queue<Id>();
            foreach (var x in nodesInOrder)
            {
                if (inDegree[x] == 0)
                {
                    frontier.Enqueue(x);
                }
            }

            var topoOrder = new List<Id>();
            var remaining = new Dictionary<Id, int>(inDegree);
            while (frontier.Count > 0)
            {
                var y = frontier.Dequeue();
                topoOrder.Add(y);
                if (unlocks.TryGetValue(y, out var dependents))
                {
                    foreach (var x in dependents)
                    {
                        remaining[x]--;
                        if (remaining[x] == 0)
                        {
                            frontier.Enqueue(x);
                        }
                    }
                }
            }

            var hasCycle = topoOrder.Count != nodesInOrder.Count;
            IReadOnlyList<Id> cyclePath = Array.Empty<Id>();
            IReadOnlyList<Id> finalTopoOrder = Array.Empty<Id>();
            if (hasCycle)
            {
                cyclePath = FindCycle(questId, nodesInOrder, dependsOn, nodeSet);
            }
            else
            {
                finalTopoOrder = topoOrder;
            }

            bool? isAvailable = null;
            IReadOnlyList<Id>? blocking = null;
            if (completedQuestIds != null)
            {
                var completedSet = new HashSet<Id>(completedQuestIds);
                var blockingList = new List<Id>();
                foreach (var r in direct)
                {
                    if (!completedSet.Contains(r))
                    {
                        blockingList.Add(r);
                    }
                }
                isAvailable = blockingList.Count == 0;
                blocking = blockingList;
            }

            return new QuestPrerequisitePreviewResult(
                questId, direct, closureOrder, unknownOrder,
                hasCycle, cyclePath, finalTopoOrder, truncated, isAvailable, blocking);
        }

        /// <summary>从一条 <see cref="QuestDefinition.Prerequisite"/>（可能为 <c>null</c>）里提取
        /// 全部 <c>quest.*</c> 引用的 Id 字面量参数，按出现顺序去重——直接复用
        /// <see cref="QuestReferenceExtractor"/> 的 <c>internal</c> 重载（见类型判断记录"前置引用的
        /// 提取"，整合验收时消除本方法与其重复实现的同一段过滤逻辑）。</summary>
        private static IReadOnlyList<Id> ExtractDirectReferences(QuestDefinition def) =>
            QuestReferenceExtractor.ExtractReferencedQuestIds(def.Prerequisite);

        private static void RecordUnknown(
            IReadOnlyList<Id> refs,
            Dictionary<Id, QuestDefinition> byId,
            HashSet<Id> unknownSeen,
            List<Id> unknownOrder)
        {
            foreach (var r in refs)
            {
                if (!byId.ContainsKey(r) && unknownSeen.Add(r))
                {
                    unknownOrder.Add(r);
                }
            }
        }

        private static void EnqueueKnownNew(
            IReadOnlyList<Id> refs,
            Dictionary<Id, QuestDefinition> byId,
            HashSet<Id> closureSeen,
            List<Id> closureOrder,
            Queue<Id> queue,
            int maxNodes,
            ref bool truncated)
        {
            foreach (var r in refs)
            {
                if (!byId.ContainsKey(r) || closureSeen.Contains(r))
                {
                    continue;
                }
                if (closureOrder.Count >= maxNodes)
                {
                    truncated = true;
                    continue;
                }
                closureSeen.Add(r);
                closureOrder.Add(r);
                queue.Enqueue(r);
            }
        }

        /// <summary>三色标记迭代式 DFS 找一条环（算法与
        /// <see cref="Core.Gameplay.Dialog.StoryTreeDefinition.HasCycle"/>/既有
        /// <c>quest_prerequisite_cycle</c> 校验同款，见类型判断记录"前置引用的提取"——图算法本身是通用
        /// 逻辑，不属于需要复用的"提取"环节，本方法独立实现）：只在 <paramref name="nodeSet"/>（本次
        /// 预演的子图）内的边上寻找，自环（引用自身）按同一套算法自然判定为环。</summary>
        private static IReadOnlyList<Id> FindCycle(
            Id start,
            List<Id> nodesInOrder,
            Dictionary<Id, IReadOnlyList<Id>> dependsOn,
            HashSet<Id> nodeSet)
        {
            var state = new Dictionary<Id, int>(); // 0=未访问 1=访问中 2=已完成
            var path = new List<Id>();

            foreach (var s in nodesInOrder)
            {
                if (state.TryGetValue(s, out var s0) && s0 != 0)
                {
                    continue;
                }
                if (Visit(s, dependsOn, nodeSet, state, path))
                {
                    return path;
                }
            }

            return Array.Empty<Id>();
        }

        private static bool Visit(
            Id id,
            Dictionary<Id, IReadOnlyList<Id>> dependsOn,
            HashSet<Id> nodeSet,
            Dictionary<Id, int> state,
            List<Id> path)
        {
            state[id] = 1;
            path.Add(id);

            if (dependsOn.TryGetValue(id, out var refs))
            {
                foreach (var target in refs)
                {
                    if (!nodeSet.Contains(target))
                    {
                        continue;
                    }

                    if (state.TryGetValue(target, out var targetState))
                    {
                        if (targetState == 1)
                        {
                            path.Add(target);
                            return true;
                        }
                        if (targetState == 2)
                        {
                            continue;
                        }
                    }

                    if (Visit(target, dependsOn, nodeSet, state, path))
                    {
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[id] = 2;
            return false;
        }
    }
}
