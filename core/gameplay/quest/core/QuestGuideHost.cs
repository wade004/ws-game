using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.WorldState;
using Core.Rules.Common;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 任务目标指引宿主（ADR-0173）：只读查询——"此刻玩家下一步该做什么、箭头该指哪里"。
    /// <para>
    /// 判断记录（只读、无状态、缺省关闭）：本类型不持有任何运行期状态，也不订阅事件；每次 <see cref="Evaluate"/> 现算。
    /// 没有任何任务声明 <c>guide</c> 时恒返回空，行为与不存在本能力时逐位一致——"缺省关闭"靠数据决定，不另设全局开关。
    /// 不修改 <see cref="IQuestHost"/>（接口加成员会破坏既有假实现），任务定义经 <see cref="QuestHost.Definitions"/> 读取。
    /// </para>
    /// <para>
    /// 判断记录（求值口径）：任务按 <see cref="QuestDefinition.GuidePriority"/> 降序（同值保持定义顺序）逐个看，每个任务的
    /// 步骤按数组顺序看；取第一条"<see cref="QuestGuideStep.When"/> 求值为真，且（没有目标，或至少有一个目标可见且可达）"的
    /// 步骤。目标全部不可达（例如通往目标地图的唯一通道还关着）的步骤被跳过——因此作者可以把"清理房间"写在"前往首领"之前，
    /// 也可以只写后者让关着的通道自然落到下一条。同一步骤多个目标时取"跳数最少、再取离玩家最近"的一个。
    /// </para>
    /// <para>
    /// 判断记录（跨地图寻路）：目标不在玩家当前地图时，沿 <see cref="IQuestGuideLocator.Transitions"/> 里当前开着（<c>Condition</c>
    /// 求值为真）的通道做广度优先搜索，箭头指向路径上的第一条通道的中心；搜不到路径视为不可达。这不是导航网格寻路，
    /// 同地图内只给直线方向，由玩家自己走。
    /// </para>
    /// </summary>
    public sealed class QuestGuideHost
    {
        private readonly Func<IEnumerable<QuestDefinition>> _definitions;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly IWorldState _worldState;
        private readonly IQuestGuideLocator _locator;
        private readonly IExprDiagnostics _diagnostics;

        public QuestGuideHost(
            Func<IEnumerable<QuestDefinition>> definitions,
            IExprHostFactory exprHostFactory,
            IWorldState worldState,
            IQuestGuideLocator locator,
            IExprDiagnostics? diagnostics = null)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _locator = locator ?? throw new ArgumentNullException(nameof(locator));
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();
        }

        /// <summary>是否有任何任务声明了引导步骤（HUD 据此决定要不要建指引控件）。</summary>
        public bool HasAnyGuide => _definitions().Any(d => d.GuideSteps.Count > 0);

        /// <summary>求出玩家 <paramref name="unitId"/> 在 <paramref name="mapId"/> 的 <paramref name="position"/> 处此刻的目标指引；
        /// 没有命中任何步骤时返回 <c>null</c>。</summary>
        public QuestGuideInfo? Evaluate(Id unitId, Id mapId, Vec2 position)
        {
            var host = _exprHostFactory.CreateFor(unitId, null, null);
            var ordered = _definitions().Where(d => d.GuideSteps.Count > 0)
                .Select((d, i) => (Def: d, Index: i))
                .OrderByDescending(t => t.Def.GuidePriority).ThenBy(t => t.Index)
                .Select(t => t.Def);
            foreach (var def in ordered)
            {
                for (var s = 0; s < def.GuideSteps.Count; s++)
                {
                    var step = def.GuideSteps[s];
                    if (step.When != null && !ExprEvaluator.EvaluateBool(step.When, host, _diagnostics))
                    {
                        continue;
                    }

                    QuestGuideTarget? target = null;
                    if (step.Targets.Count > 0)
                    {
                        target = PickTarget(step, host, mapId, position);
                        if (target == null)
                        {
                            continue;
                        }
                    }

                    int? done = null;
                    int? total = null;
                    if (step.ProgressFlags.Count > 0)
                    {
                        total = step.ProgressFlags.Count;
                        done = step.ProgressFlags.Count(f => _worldState.Has(f));
                    }

                    return new QuestGuideInfo(def.Id, s, step.TextKey, done, total, target);
                }
            }

            return null;
        }

        private QuestGuideTarget? PickTarget(QuestGuideStep step, IExprHost host, Id mapId, Vec2 position)
        {
            QuestGuideTarget? best = null;
            var bestHops = int.MaxValue;
            var bestDistance = double.MaxValue;
            foreach (var def in step.Targets)
            {
                if (def.VisibleIf != null && !ExprEvaluator.EvaluateBool(def.VisibleIf, host, _diagnostics))
                {
                    continue;
                }

                if (!TryResolve(def, out var anchor))
                {
                    continue;
                }

                if (!TryRoute(host, mapId, anchor.MapId, out var hops, out var via))
                {
                    continue;
                }

                var arrow = via != null ? via.Center : anchor.Position;
                var distance = Vec2.Distance(position, arrow);
                if (hops < bestHops || (hops == bestHops && distance < bestDistance))
                {
                    bestHops = hops;
                    bestDistance = distance;
                    best = new QuestGuideTarget(anchor.MapId, anchor.Position, anchor.SourceId, arrow, via?.AreaId);
                }
            }

            return best;
        }

        private bool TryResolve(QuestGuideTargetDef def, out QuestGuideAnchor anchor)
        {
            if (def.AreaRef.HasValue)
            {
                return _locator.TryResolveArea(def.AreaRef.Value, out anchor);
            }

            if (def.SpawnRef.HasValue)
            {
                return _locator.TryResolveSpawn(def.SpawnRef.Value, out anchor);
            }

            anchor = new QuestGuideAnchor(def.MapId!.Value, def.Position!.Value, null);
            return true;
        }

        /// <summary>从 <paramref name="from"/> 到 <paramref name="to"/> 的最少通道跳数及第一条通道；同图为 0 跳、无通道。</summary>
        private bool TryRoute(IExprHost host, Id from, Id to, out int hops, out QuestGuideTransition? first)
        {
            first = null;
            hops = 0;
            if (from.Equals(to))
            {
                return true;
            }

            var open = _locator.Transitions
                .Where(t => t.Condition == null || ExprEvaluator.EvaluateBool(t.Condition, host, _diagnostics))
                .ToList();
            var visited = new HashSet<Id> { from };
            var queue = new Queue<(Id Map, int Hops, QuestGuideTransition First)>();
            foreach (var t in open.Where(t => t.FromMapId.Equals(from)))
            {
                if (visited.Add(t.ToMapId))
                {
                    queue.Enqueue((t.ToMapId, 1, t));
                }
            }

            while (queue.Count > 0)
            {
                var (map, h, firstHop) = queue.Dequeue();
                if (map.Equals(to))
                {
                    hops = h;
                    first = firstHop;
                    return true;
                }

                foreach (var t in open.Where(t => t.FromMapId.Equals(map)))
                {
                    if (visited.Add(t.ToMapId))
                    {
                        queue.Enqueue((t.ToMapId, h + 1, firstHop));
                    }
                }
            }

            return false;
        }
    }
}
