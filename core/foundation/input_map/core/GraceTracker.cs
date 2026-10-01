using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Feel;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 宽限窗口追踪（手感设计/01 第 2.4 节）：每个模拟 tick 对登记的（行动者, 条件）求值并记录"最近一次为真的 tick"；
    /// 查询时条件当前为真，或虽为假但 <c>now - lastTrueTick &lt;= grace_ticks</c>（手感档案输入组 <c>grace_ms</c> 按步长换算）即视为满足。
    /// 机制由框架提供，不预置任何条件；条件求值由上层经 <see cref="IGraceConditionEvaluator"/> 实现（宿主为行动者上下文的 Expr 求值）。
    /// <para>
    /// 判断记录（时间基准）：宽限按<b>模拟 tick</b> 计（设计文本"规则层每 tick 记录每个条件最近一次为真的 tick"），不随顿帧暂停——
    /// 与缓冲过期（行动者动作时钟）有意不同：宽限描述的是世界状态（目标是否在射程）的新鲜度，世界不因某个行动者顿帧而停。
    /// </para>
    /// <para>
    /// 判断记录（没有手感档案）：没有 <see cref="IFeelJudgingSource"/> 时 <c>grace_ms</c> 视为 0，即只有"当前为真"才满足，等价于没有宽限。
    /// </para>
    /// <para>
    /// 判断记录（宽限只放宽接受）：本类型只回答"条件算不算满足"。随后结算的几何（例如目标已离开范围，时间线模式的空间命中仍可能打空）
    /// 不受影响——这是消费方（施法管线步骤 7）的职责，不在此处改写。
    /// </para>
    /// </summary>
    public sealed class GraceTracker : IGraceQuery
    {
        private readonly IGraceConditionEvaluator _evaluator;
        private readonly IFeelJudgingSource? _feel;
        private readonly List<Id> _actorOrder = new List<Id>();
        private readonly Dictionary<Id, List<Id>> _conditionsByActor = new Dictionary<Id, List<Id>>();
        private readonly Dictionary<(Id Actor, Id Condition), long> _lastTrue = new Dictionary<(Id, Id), long>();
        private long _now = -1;

        public GraceTracker(IGraceConditionEvaluator evaluator, IFeelJudgingSource? feel = null)
        {
            _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            _feel = feel;
        }

        /// <summary>最近一次 <see cref="Sample"/> 的模拟 tick（采样之前为 -1）。</summary>
        public long CurrentTick => _now;

        /// <summary>让行动者的这些条件进入每 tick 采样（重复登记同一条件无效果）。</summary>
        public void Register(Id actorId, IEnumerable<Id> conditionIds)
        {
            if (conditionIds == null) throw new ArgumentNullException(nameof(conditionIds));
            if (!_conditionsByActor.TryGetValue(actorId, out var list))
            {
                list = new List<Id>();
                _conditionsByActor.Add(actorId, list);
                _actorOrder.Add(actorId);
            }

            foreach (var id in conditionIds)
            {
                if (!list.Contains(id)) list.Add(id);
            }
        }

        /// <summary>登记某动作定义的全部 <see cref="ActionDefinition.GraceConditions"/>。</summary>
        public void RegisterAction(Id actorId, ActionDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.GraceConditions.Count > 0) Register(actorId, definition.GraceConditions);
        }

        /// <summary>行动者销毁/死亡：停止采样并忘掉它的全部记录。</summary>
        public void Unregister(Id actorId)
        {
            if (!_conditionsByActor.TryGetValue(actorId, out var list)) return;
            for (var i = 0; i < list.Count; i++) _lastTrue.Remove((actorId, list[i]));
            _conditionsByActor.Remove(actorId);
            _actorOrder.Remove(actorId);
        }

        /// <summary>
        /// 采样一个模拟 tick：对每个登记的（行动者, 条件）求值，为真则记录 <paramref name="tick"/> 为最近一次为真。
        /// 由 <see cref="InputBufferTickHandler"/> 在步骤 1 调用；先于施法管线，所以管线里读到的是本 tick 的值。
        /// </summary>
        public void Sample(long tick)
        {
            _now = tick;
            for (var a = 0; a < _actorOrder.Count; a++)
            {
                var actorId = _actorOrder[a];
                var list = _conditionsByActor[actorId];
                for (var i = 0; i < list.Count; i++)
                {
                    if (_evaluator.Evaluate(actorId, list[i]))
                    {
                        _lastTrue[(actorId, list[i])] = tick;
                    }
                }
            }
        }

        public long LastTrueTick(Id actorId, Id conditionId) =>
            _lastTrue.TryGetValue((actorId, conditionId), out var tick) ? tick : -1;

        public bool IsSatisfied(Id actorId, Id conditionId)
        {
            var last = LastTrueTick(actorId, conditionId);
            return last >= 0 && _now - last <= GraceTicks(actorId);
        }

        public bool IsInGrace(Id actorId, Id conditionId)
        {
            var last = LastTrueTick(actorId, conditionId);
            return last >= 0 && last < _now && _now - last <= GraceTicks(actorId);
        }

        public bool AreAllSatisfied(Id actorId, IReadOnlyList<Id> conditionIds)
        {
            if (conditionIds == null) throw new ArgumentNullException(nameof(conditionIds));
            for (var i = 0; i < conditionIds.Count; i++)
            {
                if (!IsSatisfied(actorId, conditionIds[i])) return false;
            }
            return true;
        }

        private int GraceTicks(Id actorId) =>
            _feel == null ? 0 : _feel.ResolveJudging(actorId).GetTicks(FeelFieldNames.GraceMs);
    }
}
