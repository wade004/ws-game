using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Common;
using Core.Gameplay.Dialog;
using Core.Rules.Common;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <see cref="IQuestHost"/> 的默认实现（见 08 第 2.2 节状态机、第 9 节 Quest 行契约、任务书
    /// 展开的完整方法签名）。
    /// <para>
    /// 事件驱动进度（见任务书"事件驱动进度（订阅 IEventBus，只对 Active 任务）"）：本类型在构造期
    /// 一次性订阅 <c>unit.died</c>/<c>item.added</c>/<c>item.removed</c>/<c>gobj.interacted</c>/
    /// <c>skill.cast_success</c>/<c>area.trigger_entered</c>/<c>dialog.gossip_opened</c>/
    /// <c>dialog.story_node_entered</c>，以及全部 <c>quest.def</c> 中声明的 <c>event</c> 类目标的
    /// 各个不同 <c>target_ref</c> 事件 key；每次收到事件时遍历当前全部 <see cref="QuestState.Active"/>
    /// 记录做匹配，而不是按"接取时动态订阅、交付/失败时取消订阅"的思路逐条管理订阅句柄——
    /// 判断记录：后者需要为"daily 可重复任务多次接取/交付"这类场景反复订阅/取消订阅同一组事件、
    /// 且要正确处理"同一 unitId 同时持有多条引用同一 target_ref 的不同任务"的句柄归属，复杂度明显
    /// 高于"固定订阅一次、每次事件到达时按当前 Active 快照过滤"，且两者性能特征在单机场景下差异
    /// 可忽略（活跃任务数量级很小）。
    /// </para>
    /// <para>
    /// 契约缺口判断记录：<c>area.trigger_entered</c> 事件由 <c>core/gameplay/area_trigger</c>
    /// 模块发布（与本模块同批次、由另一 agent 并行建设，本任务不允许改动该目录），本模块不对其
    /// 具体事件类做编译期依赖，改用 <see cref="IEventBus.Subscribe(Id, EventHandler)"/> 非泛型订阅
    /// + <see cref="IExprReadableEvent"/>（本仓库事件类型的通用惯例，见该接口注释）按字段名读取
    /// <c>triggerId</c>/<c>unitId</c>，不要求编译期已知具体事件类型。
    /// </para>
    /// </summary>
    public sealed class QuestHost : IQuestHost
    {
        /// <summary>见类型顶部判断记录：<c>area.trigger_entered</c> 不是本模块定义的强类型事件。</summary>
        private static readonly Id AreaTriggerEnteredKey = new Id("area.trigger_entered");

        private readonly Dictionary<Id, QuestDefinition> _definitions = new Dictionary<Id, QuestDefinition>();
        private readonly Dictionary<(Id UnitId, Id QuestId), QuestRuntimeState> _progress =
            new Dictionary<(Id UnitId, Id QuestId), QuestRuntimeState>();

        private readonly IEventBus _eventBus;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly IRewardDispatcher _rewardDispatcher;
        private readonly IInventoryHost _inventoryHost;
        private readonly IUnitAccess _unitAccess;
        private readonly QuestOptions _options;
        private readonly Func<Id, Id?>? _ownerResolver;
        private readonly Func<Id, Id?>? _gobjTemplateResolver;
        private readonly Func<long> _dayProvider;
        private readonly IExprDiagnostics _exprDiagnostics;

        public QuestHost(
            IEnumerable<QuestDefinition> definitions,
            IEventBus eventBus,
            IExprHostFactory exprHostFactory,
            IRewardDispatcher rewardDispatcher,
            IInventoryHost inventoryHost,
            IUnitAccess unitAccess,
            QuestOptions? options = null,
            Func<Id, Id?>? ownerResolver = null,
            Func<Id, Id?>? gobjTemplateResolver = null,
            Func<long>? dayProvider = null,
            IExprDiagnostics? exprDiagnostics = null)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (var def in definitions)
            {
                _definitions[def.Id] = def;
            }

            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _rewardDispatcher = rewardDispatcher ?? throw new ArgumentNullException(nameof(rewardDispatcher));
            _inventoryHost = inventoryHost ?? throw new ArgumentNullException(nameof(inventoryHost));
            _unitAccess = unitAccess ?? throw new ArgumentNullException(nameof(unitAccess));
            _options = options ?? new QuestOptions();
            _ownerResolver = ownerResolver;
            _gobjTemplateResolver = gobjTemplateResolver;
            _dayProvider = dayProvider ?? (() => 0L);
            _exprDiagnostics = exprDiagnostics ?? new ExprDiagnosticsRecorder();

            _eventBus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, HandleUnitDied);
            _eventBus.Subscribe<ItemAddedEvent>(CarriersEventKeys.ItemAdded, HandleItemAdded);
            _eventBus.Subscribe<ItemRemovedEvent>(CarriersEventKeys.ItemRemoved, HandleItemRemoved);
            _eventBus.Subscribe<GobjInteractedEvent>(CarriersEventKeys.GobjInteracted, HandleGobjInteracted);
            _eventBus.Subscribe<SkillCastSuccessEvent>(RulesEventKeys.SkillCastSuccess, HandleSkillCastSuccess);
            _eventBus.Subscribe(AreaTriggerEnteredKey, HandleAreaTriggerEntered);
            _eventBus.Subscribe<GossipOpenedEvent>(DialogEventKeys.GossipOpened, HandleGossipOpened);
            _eventBus.Subscribe<StoryNodeEnteredEvent>(DialogEventKeys.StoryNodeEntered, HandleStoryNodeEntered);

            var eventObjectiveKeys = new HashSet<Id>();
            foreach (var def in _definitions.Values)
            {
                foreach (var objective in def.Objectives)
                {
                    if (objective.Type == QuestObjectiveType.Event)
                    {
                        eventObjectiveKeys.Add(objective.TargetRef);
                    }
                }
            }
            foreach (var key in eventObjectiveKeys)
            {
                _eventBus.Subscribe(key, HandleGenericQuestEvent);
            }
        }

        // -------------------------------------------------------------
        // IQuestHost
        // -------------------------------------------------------------

        public QuestState GetState(Id unitId, Id questId)
        {
            var def = RequireDef(questId);

            if (_progress.TryGetValue((unitId, questId), out var rt))
            {
                switch (rt.State)
                {
                    case QuestState.Active:
                    case QuestState.ObjectivesComplete:
                    case QuestState.Failed:
                        return rt.State;

                    case QuestState.TurnedIn:
                        if (def.Repeatable == QuestRepeatable.None)
                        {
                            return QuestState.TurnedIn;
                        }
                        if (def.Repeatable == QuestRepeatable.Daily && rt.LastCompletedDay.HasValue &&
                            rt.LastCompletedDay.Value == _dayProvider())
                        {
                            return QuestState.Unavailable;
                        }
                        break; // daily(次日)/unlimited：落到下方按 prerequisite 实时求值
                }
            }

            return EvaluatePrerequisite(unitId, def) ? QuestState.Available : QuestState.Unavailable;
        }

        public bool Accept(Id unitId, Id questId)
        {
            var def = RequireDef(questId);
            if (GetState(unitId, questId) != QuestState.Available)
            {
                return false;
            }

            if (def.ExclusiveGroup.HasValue && HasActiveExclusiveConflict(unitId, def))
            {
                return false;
            }

            var existing = _progress.TryGetValue((unitId, questId), out var prev) ? prev : null;
            _progress[(unitId, questId)] = new QuestRuntimeState
            {
                State = QuestState.Active,
                ObjectiveCounts = new int[def.Objectives.Count],
                CompletionCount = existing?.CompletionCount ?? 0,
                LastCompletedDay = existing?.LastCompletedDay,
            };

            Publish(new QuestAcceptedEvent(unitId, questId));

            // GP-08 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：非消耗型
            // （consumeOnProgress == false）collect 目标此前只能靠后续 item.added/item.removed 事件
            // 联动推进（见 HandleItemAdded/HandleItemRemoved 的 SetObjectiveAbsolute 分支）——刚接取
            // 时无条件从 0 起算，若玩家在接取之前就已经持有足量目标物品（先攒够材料、再去接任务这一
            // 常见玩法顺序），任务会错误地显示"尚未收集"，必须再触发一次物品增减事件才能被动纠正。
            // 改法：接取的一瞬间就按当前库存把非消耗目标的进度设为绝对值（可能因此直接
            // ObjectivesComplete，见 SetObjectiveAbsolute -> ApplyObjectiveCount 的双向同步）。消耗型
            // （consumeOnProgress == true）目标不在此列——它的语义是"接取后主动上交/消耗"，不是
            // "统计当前持有量"，接取时不应该倒扣已有库存（GP-08 验收明确只覆盖 nonconsume）。
            for (var i = 0; i < def.Objectives.Count; i++)
            {
                var objective = def.Objectives[i];
                if (objective.Type == QuestObjectiveType.Collect && !objective.ConsumeOnProgress)
                {
                    SetObjectiveAbsolute(unitId, questId, i, _inventoryHost.CountOf(unitId, objective.TargetRef));
                }
            }

            return true;
        }

        public bool UpdateProgress(Id unitId, Id questId, int objectiveIndex, int delta)
        {
            if (!TryGetActive(unitId, questId, out var def, out var rt))
            {
                return false;
            }
            if (objectiveIndex < 0 || objectiveIndex >= def.Objectives.Count)
            {
                return false;
            }

            ApplyObjectiveCount(unitId, questId, def, rt, objectiveIndex, rt.ObjectiveCounts[objectiveIndex] + delta);
            return true;
        }

        public bool TurnIn(Id unitId, Id questId) => TurnIn(unitId, questId, out _);

        /// <summary>
        /// 判断记录（N02/N11 根治）：交付分三步，任一步失败都不改变任何状态（不消耗物品、不发放
        /// 奖励、不置 TurnedIn），保证整个交付是原子操作：
        /// <list type="number">
        /// <item>预检——对每个 collect 且 <c>!ConsumeOnProgress</c> 目标，按当前实际库存核验能否扣除
        /// 完整数量（N11：不依赖 <c>ObjectiveCounts</c> 缓存进度；同一批物品若已被另一次并发交付
        /// 抢先消耗，这里会如实核出不足）；不足则整体失败于 <see cref="QuestTurnInFailure.InsufficientItems"/>。</item>
        /// <item>实际移除——预检通过后才真正调用 <see cref="RemoveCollectedItems"/>；仍检查其返回值
        /// 防御极端并发下的移除失败，一旦失败立即把本次已移除的部分全部放回背包再返回失败（不应该
        /// 发生，预检已核过量，留作防御性回滚）。</item>
        /// <item>发放奖励——经 <see cref="IRewardDispatcher.Grant"/>（N02：内部已对物品奖励做原子
        /// 发放与失败回滚，见其判断记录）；返回 false 时（背包已满装不下奖励物品）把第 2 步移除的
        /// collect 物品全部放回背包，整体失败于 <see cref="QuestTurnInFailure.InventoryFull"/>，任务
        /// 保持 <see cref="QuestState.ObjectivesComplete"/>，玩家清出空间后可重新交付。</item>
        /// </list>
        /// </summary>
        public bool TurnIn(Id unitId, Id questId, out QuestTurnInFailure failure)
        {
            var def = RequireDef(questId);
            if (!_progress.TryGetValue((unitId, questId), out var rt) || rt.State != QuestState.ObjectivesComplete)
            {
                failure = QuestTurnInFailure.NotReady;
                return false;
            }

            // 步骤 1：预检——不依赖缓存进度，按实际库存核验足量（N11）。
            for (var i = 0; i < def.Objectives.Count; i++)
            {
                var objective = def.Objectives[i];
                if (objective.Type == QuestObjectiveType.Collect && !objective.ConsumeOnProgress &&
                    _inventoryHost.CountOf(unitId, objective.TargetRef) < objective.Count)
                {
                    failure = QuestTurnInFailure.InsufficientItems;
                    return false;
                }
            }

            // 步骤 2：实际移除，逐个目标核验返回值；任一失败回滚本次已移除的部分。
            var removed = new List<(Id TemplateId, int Count)>();
            for (var i = 0; i < def.Objectives.Count; i++)
            {
                var objective = def.Objectives[i];
                if (objective.Type != QuestObjectiveType.Collect || objective.ConsumeOnProgress)
                {
                    continue;
                }

                if (RemoveCollectedItems(unitId, objective.TargetRef, objective.Count))
                {
                    removed.Add((objective.TargetRef, objective.Count));
                    continue;
                }

                foreach (var prior in removed)
                {
                    _inventoryHost.AddItem(unitId, prior.TemplateId, prior.Count);
                }

                failure = QuestTurnInFailure.InsufficientItems;
                return false;
            }

            // 步骤 3：发放奖励；失败则把步骤 2 移除的物品全部放回背包（N02）。
            if (!_rewardDispatcher.Grant(unitId, def.Rewards, questId))
            {
                foreach (var prior in removed)
                {
                    _inventoryHost.AddItem(unitId, prior.TemplateId, prior.Count);
                }

                failure = QuestTurnInFailure.InventoryFull;
                return false;
            }

            rt.CompletionCount++;
            rt.State = QuestState.TurnedIn;
            if (def.Repeatable == QuestRepeatable.Daily)
            {
                rt.LastCompletedDay = _dayProvider();
            }
            // Repeatable.Unlimited：不记录 LastCompletedDay，GetState 因此在下一次调用即直接落到
            // prerequisite 实时求值分支，等价于"立即回落 Available"（见 GetState 判断记录）。

            failure = QuestTurnInFailure.None;
            Publish(new QuestTurnedInEvent(unitId, questId));
            return true;
        }

        public bool Fail(Id unitId, Id questId, string reason)
        {
            if (!_options.AllowFail)
            {
                return false;
            }

            RequireDef(questId);
            if (!_progress.TryGetValue((unitId, questId), out var rt))
            {
                return false;
            }
            if (rt.State != QuestState.Active && rt.State != QuestState.ObjectivesComplete)
            {
                return false;
            }

            rt.State = QuestState.Failed;
            Publish(new QuestFailedEvent(unitId, questId, reason));
            return true;
        }

        public IReadOnlyList<QuestProgress> GetLog(Id unitId)
        {
            var result = new List<QuestProgress>();
            foreach (var kv in _progress)
            {
                if (!kv.Key.UnitId.Equals(unitId))
                {
                    continue;
                }
                result.Add(new QuestProgress(
                    kv.Key.QuestId, kv.Value.State, (int[])kv.Value.ObjectiveCounts.Clone(),
                    kv.Value.CompletionCount, kv.Value.LastCompletedDay));
            }
            return result;
        }

        public IReadOnlyList<(Id QuestId, int ObjectiveIndex, Id TargetRef)> GetActiveObjectives(Id unitId)
        {
            var result = new List<(Id, int, Id)>();
            foreach (var kv in _progress)
            {
                if (!kv.Key.UnitId.Equals(unitId) || kv.Value.State != QuestState.Active)
                {
                    continue;
                }
                var def = _definitions[kv.Key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    if (kv.Value.ObjectiveCounts[i] < def.Objectives[i].Count)
                    {
                        result.Add((kv.Key.QuestId, i, def.Objectives[i].TargetRef));
                    }
                }
            }
            return result;
        }

        public void Update(Id unitId)
        {
            foreach (var def in _definitions.Values)
            {
                if (def.StartMethod == QuestStartMethod.Auto && GetState(unitId, def.Id) == QuestState.Available)
                {
                    Accept(unitId, def.Id);
                }
            }

            foreach (var def in _definitions.Values)
            {
                if (def.TurnInMethod == QuestTurnInMethod.Auto && GetState(unitId, def.Id) == QuestState.ObjectivesComplete)
                {
                    TurnIn(unitId, def.Id);
                }
            }
        }

        /// <summary>供 <see cref="QuestPersistable"/> 读档恢复用：把某个单位的任务状态整体替换为
        /// 存档快照（GP-01 判断记录，architecture/落地计划/audit-b3b91ee-20260907/code-review.md）。
        /// 判断记录：本方法取代了旧的逐条 <c>RestoreProgress(unitId, progress)</c> 注入方式——
        /// 那种"只覆盖快照里出现的 questId"的做法正是 GP-01 的根因，已删除，不再保留。
        /// <para>
        /// 旧实现（<see cref="RestoreProgress"/> 逐条调用）只会覆盖快照里出现的 questId，运行期
        /// 已有、但快照里不存在的任务（例如存档时还未接取、读档后已经接取/完成的任务）会原样残留，
        /// 导致"读空档"无法回到"什么任务都没有"的保存点，完成计数/每日记录同理只能被覆盖不能被
        /// 清除。本方法按"快照即权威真相"语义：读档后该单位的任务状态必须恰好等于快照内容，
        /// 快照未覆盖到的 questId 一律移除。
        /// </para>
        /// <para>
        /// 先用 <paramref name="snapshot"/> 构建完整的新记录集合（未知 questId 会在
        /// <see cref="RequireDef"/> 处抛异常）校验全部通过后，再一次性删除该单位旧记录、写入新
        /// 记录——保证不会出现"校验到一半失败，部分任务已被清空、部分还是旧值"的半提交状态。
        /// </para>
        /// <para>
        /// 只按 <paramref name="unitId"/> 过滤 <c>_progress</c> 的 key，不触碰其他单位的记录——
        /// 天然满足"跨槽隔离"要求（不同存档槽通过各自独立的 <see cref="QuestHost"/> 实例隔离，
        /// 同一实例内不同单位通过 <c>(UnitId, QuestId)</c> 复合键隔离）。
        /// </para>
        /// </summary>
        internal void ReplaceAllProgress(Id unitId, IReadOnlyList<QuestProgress> snapshot)
        {
            var newEntries = new List<((Id UnitId, Id QuestId) Key, QuestRuntimeState State)>(snapshot.Count);
            foreach (var progress in snapshot)
            {
                var def = RequireDef(progress.QuestId);
                var counts = new int[def.Objectives.Count];
                for (var i = 0; i < counts.Length && i < progress.ObjectiveCounts.Count; i++)
                {
                    counts[i] = progress.ObjectiveCounts[i];
                }

                newEntries.Add(((unitId, progress.QuestId), new QuestRuntimeState
                {
                    State = progress.State,
                    ObjectiveCounts = counts,
                    CompletionCount = progress.CompletionCount,
                    LastCompletedDay = progress.LastCompletedDay,
                }));
            }

            foreach (var key in new List<(Id UnitId, Id QuestId)>(_progress.Keys))
            {
                if (key.UnitId.Equals(unitId))
                {
                    _progress.Remove(key);
                }
            }

            foreach (var (key, state) in newEntries)
            {
                _progress[key] = state;
            }
        }

        /// <summary>供 <see cref="QuestPersistable"/> 存档写入用：全部单位的运行期记录快照。</summary>
        internal IEnumerable<(Id UnitId, QuestProgress Progress)> SnapshotAll()
        {
            foreach (var kv in _progress)
            {
                yield return (kv.Key.UnitId, new QuestProgress(
                    kv.Key.QuestId, kv.Value.State, (int[])kv.Value.ObjectiveCounts.Clone(),
                    kv.Value.CompletionCount, kv.Value.LastCompletedDay));
            }
        }

        // -------------------------------------------------------------
        // 内部帮助方法
        // -------------------------------------------------------------

        private QuestDefinition RequireDef(Id questId)
        {
            if (!_definitions.TryGetValue(questId, out var def))
            {
                throw new ArgumentException($"未登记的任务 id：\"{questId}\"", nameof(questId));
            }
            return def;
        }

        private bool EvaluatePrerequisite(Id unitId, QuestDefinition def)
        {
            if (def.Prerequisite == null)
            {
                return true;
            }
            var host = _exprHostFactory.CreateFor(unitId, null, null);
            return ExprEvaluator.EvaluateBool(def.Prerequisite, host, _exprDiagnostics);
        }

        private bool HasActiveExclusiveConflict(Id unitId, QuestDefinition def)
        {
            var group = def.ExclusiveGroup!.Value;
            foreach (var other in _definitions.Values)
            {
                if (other.Id.Equals(def.Id) || !other.ExclusiveGroup.HasValue || !other.ExclusiveGroup.Value.Equals(group))
                {
                    continue;
                }
                if (_progress.TryGetValue((unitId, other.Id), out var otherRt) &&
                    (otherRt.State == QuestState.Active || otherRt.State == QuestState.ObjectivesComplete))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryGetActive(Id unitId, Id questId, out QuestDefinition def, out QuestRuntimeState rt)
        {
            def = RequireDef(questId);
            if (_progress.TryGetValue((unitId, questId), out var found) && found.State == QuestState.Active)
            {
                rt = found;
                return true;
            }
            rt = null!;
            return false;
        }

        /// <summary>同 <see cref="TryGetActive"/>，但额外放行 <see cref="QuestState.ObjectivesComplete"/>
        /// ——供 <c>collect</c> 目标的物品增减联动使用（见 <see cref="ApplyObjectiveCount"/> 判断记录：
        /// 已达标但尚未交付时，若玩家把已收集物品卖掉/丢弃，任务应退回 Active，需要能在
        /// ObjectivesComplete 状态下继续接收目标计数的下调）。公开的 <see cref="UpdateProgress"/>
        /// 仍然只放行 Active（见该方法注释"要求任务当前为 Active"），本方法只服务事件驱动的
        /// 内部联动，不对外暴露。</summary>
        private bool TryGetActiveOrObjectivesComplete(Id unitId, Id questId, out QuestDefinition def, out QuestRuntimeState rt)
        {
            def = RequireDef(questId);
            if (_progress.TryGetValue((unitId, questId), out var found) &&
                (found.State == QuestState.Active || found.State == QuestState.ObjectivesComplete))
            {
                rt = found;
                return true;
            }
            rt = null!;
            return false;
        }

        /// <summary>
        /// 判断记录：目标计数变化后按当前实际是否全部达标双向同步任务状态——不仅
        /// Active→ObjectivesComplete（达标）需要处理，ObjectivesComplete→Active（此前已达标的
        /// <c>collect</c> 目标因物品被移除/卖出而回落）同样需要处理，否则"已达标"会与"背包里其实
        /// 已经没有足够物品"这一实际情况脱节（08 文档未明确规定这一反向场景，任务书验收标准 3
        /// 只要求"非法转移被拒"，本双向同步不违反该要求——ObjectivesComplete→Active 不是一次
        /// "转移请求被接受"，而是运行期对状态与实际计数保持一致性的被动同步）。回落时不发送专门的
        /// "取消完成"事件——08 第 9 节事件词汇表没有定义这一事件，<c>quest.objective_progress</c>
        /// 已经足够让订阅方感知数值变化。
        /// </summary>
        private void ApplyObjectiveCount(Id unitId, Id questId, QuestDefinition def, QuestRuntimeState rt, int objectiveIndex, int rawNewValue)
        {
            var objective = def.Objectives[objectiveIndex];
            var clamped = Clamp(rawNewValue, 0, objective.Count);
            if (clamped == rt.ObjectiveCounts[objectiveIndex])
            {
                return;
            }

            rt.ObjectiveCounts[objectiveIndex] = clamped;
            Publish(new QuestObjectiveProgressEvent(unitId, questId, objectiveIndex, clamped));

            if (rt.State == QuestState.Active && AllObjectivesComplete(def, rt))
            {
                rt.State = QuestState.ObjectivesComplete;
                Publish(new QuestCompletedEvent(unitId, questId));
            }
            else if (rt.State == QuestState.ObjectivesComplete && !AllObjectivesComplete(def, rt))
            {
                rt.State = QuestState.Active;
            }
        }

        private void SetObjectiveAbsolute(Id unitId, Id questId, int objectiveIndex, int absoluteValue)
        {
            if (!TryGetActiveOrObjectivesComplete(unitId, questId, out var def, out var rt))
            {
                return;
            }
            if (objectiveIndex < 0 || objectiveIndex >= def.Objectives.Count)
            {
                return;
            }
            ApplyObjectiveCount(unitId, questId, def, rt, objectiveIndex, absoluteValue);
        }

        private static bool AllObjectivesComplete(QuestDefinition def, QuestRuntimeState rt)
        {
            for (var i = 0; i < def.Objectives.Count; i++)
            {
                if (rt.ObjectiveCounts[i] < def.Objectives[i].Count)
                {
                    return false;
                }
            }
            return true;
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        /// <summary>从 <paramref name="unitId"/> 背包移除总计 <paramref name="count"/> 个
        /// <paramref name="templateId"/> 物品（跨堆叠），返回是否实际移除了完整数量（N11 根治：调用方
        /// 必须以此返回值为准，不能假定"进度缓存显示已达标"就等于"物品还在背包里"）。
        /// <para>
        /// C06 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：此前边遍历边改——
        /// 数量不足以凑满 <paramref name="count"/> 时，已经扫到的那部分仍然会被真正移除，只是最终
        /// 返回值是 false；调用方（<see cref="HandleItemAdded"/> 的 <c>ConsumeOnProgress</c> 分支、
        /// <see cref="TurnIn"/> 步骤 2）都把 false 当作"这次没有发生任何变化"处理——本方法在多个
        /// 目标共享同一模板、逐个调用时会出现"实际扣了物品但没有记入任何进度、也没有被回滚"的净
        /// 丢失（见该问题两条复现路径）。改为"先规划再执行"：先用 <see cref="IInventoryHost.CountOf"/>
        /// 核验总量是否足够，不够直接返回 false、不触碰背包任何状态；核验通过后再真正移除——此时
        /// 单线程调用下背包状态与刚才核验时一致，移除必然能凑满整数量，不会再出现"部分移除后失败"
        /// 的中间态。</para>
        /// </summary>
        private bool RemoveCollectedItems(Id unitId, Id templateId, int count)
        {
            if (_inventoryHost.CountOf(unitId, templateId) < count)
            {
                return false;
            }

            var remaining = count;
            foreach (var item in _inventoryHost.ListItems(unitId))
            {
                if (remaining <= 0)
                {
                    break;
                }
                if (!item.TemplateId.Equals(templateId))
                {
                    continue;
                }
                var take = Math.Min(remaining, item.Count);
                if (_inventoryHost.RemoveItem(unitId, item.InstanceId, take))
                {
                    remaining -= take;
                }
            }

            return remaining <= 0;
        }

        private IEnumerable<(Id UnitId, Id QuestId)> ActiveKeysForUnit(Id unitId)
        {
            foreach (var key in new List<(Id UnitId, Id QuestId)>(_progress.Keys))
            {
                if (key.UnitId.Equals(unitId) && _progress.TryGetValue(key, out var rt) && rt.State == QuestState.Active)
                {
                    yield return key;
                }
            }
        }

        /// <summary>同 <see cref="ActiveKeysForUnit"/>，但额外包含 <see cref="QuestState.ObjectivesComplete"/>
        /// ——供 <c>collect</c> 目标的物品增减联动使用（见 <see cref="TryGetActiveOrObjectivesComplete"/>
        /// 判断记录）；<c>kill</c>/<c>interact</c>/<c>cast</c>/<c>explore</c>/<c>talk</c>/<c>event</c>
        /// 六种目标只增不减、已达标后继续匹配也没有实际效果，因此仍用范围更窄的
        /// <see cref="ActiveKeysForUnit"/>，不必都改成本方法。</summary>
        private IEnumerable<(Id UnitId, Id QuestId)> ActiveOrObjectivesCompleteKeysForUnit(Id unitId)
        {
            foreach (var key in new List<(Id UnitId, Id QuestId)>(_progress.Keys))
            {
                if (key.UnitId.Equals(unitId) && _progress.TryGetValue(key, out var rt) &&
                    (rt.State == QuestState.Active || rt.State == QuestState.ObjectivesComplete))
                {
                    yield return key;
                }
            }
        }

        private void Publish(IEvent evt) => _eventBus.PublishImmediate(evt);

        // -------------------------------------------------------------
        // 事件驱动进度：见类型顶部判断记录
        // -------------------------------------------------------------

        private void HandleUnitDied(UnitDiedEvent evt)
        {
            if (!evt.KillerId.HasValue)
            {
                return;
            }
            var killerId = evt.KillerId.Value;
            var deadTemplate = _unitAccess.GetTemplateId(evt.UnitId);
            if (!deadTemplate.HasValue)
            {
                return;
            }

            foreach (var key in new List<(Id UnitId, Id QuestId)>(_progress.Keys))
            {
                if (!_progress.TryGetValue(key, out var rt) || rt.State != QuestState.Active)
                {
                    continue;
                }
                if (!IsCredited(key.UnitId, killerId))
                {
                    continue;
                }
                var def = _definitions[key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type == QuestObjectiveType.Kill && objective.TargetRef.Equals(deadTemplate.Value))
                    {
                        UpdateProgress(key.UnitId, key.QuestId, i, 1);
                    }
                }
            }
        }

        private bool IsCredited(Id questHolder, Id killerId)
        {
            if (questHolder.Equals(killerId))
            {
                return true;
            }
            if (_ownerResolver == null)
            {
                return false;
            }
            var owner = _ownerResolver(killerId);
            return owner.HasValue && owner.Value.Equals(questHolder);
        }

        private void HandleItemAdded(ItemAddedEvent evt)
        {
            foreach (var key in ActiveOrObjectivesCompleteKeysForUnit(evt.UnitId))
            {
                var rt = _progress[key];
                var def = _definitions[key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type != QuestObjectiveType.Collect || !objective.TargetRef.Equals(evt.ItemTemplateId))
                    {
                        continue;
                    }

                    if (objective.ConsumeOnProgress)
                    {
                        var remaining = objective.Count - rt.ObjectiveCounts[i];
                        if (remaining <= 0)
                        {
                            continue;
                        }
                        var take = Math.Min(remaining, evt.Count);
                        if (take <= 0)
                        {
                            continue;
                        }
                        // GP-07 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
                        // 旧实现无条件按事件携带的数量 take 推进进度，即使扣除失败也一样——失败时
                        // 背包里其实一件都没扣，却仍然把 take 记进任务进度，会让同一件物品同时喂饱
                        // 多个 consume 型目标（例如两条任务都要"消耗 1 个同款材料"，背包只有 1 个也
                        // 都各自记满进度）。改法：只有扣除真正成功时才推进，失败时本目标本次不计入
                        // 任何进度——按"实际成功扣除量"推进，成功即整个 take 都算数，失败即 0。
                        //
                        // N12 根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
                        // 旧实现按 evt.ItemInstanceId 单个实例调用 IInventoryHost.RemoveItem——但
                        // ItemAddedEvent 在跨堆叠合并新增（同一次 AddItem 把 toAdd 件分散填进多个
                        // 既有堆叠/新建堆叠）时只携带 touchedInstanceId（最后一个被触碰的实例，见
                        // InventoryHost.AddItem 判断记录），该实例未必持有完整 take 件——例如两个
                        // consume 2 的任务各需要同款材料、库存原本各差 1 件，一次 AddItem(2) 把这 2
                        // 件分别填进两个不同的既有堆叠，touchedInstanceId 只是其中一个、只有 1 件，
                        // RemoveItem(touchedInstanceId, take=2) 因为该实例数量不足而整体失败（全有
                        // 全无语义），consume 进度因此永远推进不了（stack1 一次加 2 时 consume2
                        // 进度为 0 的原始复现）。改用与 RemoveCollectedItems（TurnIn 交付时的既有
                        // 逻辑，见该方法）同款"按模板 id 跨堆叠扣除"，不依赖事件携带的单一实例 id，
                        // 也不需要等待 core/carriers 一侧扩展 ItemRemovedEvent 携带按实例分解的
                        // 移除清单——两条独立路径按各自可用的信息分别根治同一类"跨堆叠丢粒度"问题。
                        if (RemoveCollectedItems(evt.UnitId, objective.TargetRef, take))
                        {
                            UpdateProgress(evt.UnitId, key.QuestId, i, take);
                        }
                    }
                    else
                    {
                        SetObjectiveAbsolute(evt.UnitId, key.QuestId, i, _inventoryHost.CountOf(evt.UnitId, objective.TargetRef));
                    }
                }
            }
        }

        private void HandleItemRemoved(ItemRemovedEvent evt)
        {
            foreach (var key in ActiveOrObjectivesCompleteKeysForUnit(evt.UnitId))
            {
                var def = _definitions[key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type != QuestObjectiveType.Collect || objective.ConsumeOnProgress)
                    {
                        continue;
                    }
                    SetObjectiveAbsolute(evt.UnitId, key.QuestId, i, _inventoryHost.CountOf(evt.UnitId, objective.TargetRef));
                }
            }
        }

        private void HandleGobjInteracted(GobjInteractedEvent evt)
        {
            if (_gobjTemplateResolver == null)
            {
                return;
            }
            var templateId = _gobjTemplateResolver(evt.GobjInstanceId);
            if (!templateId.HasValue)
            {
                return;
            }

            foreach (var key in ActiveKeysForUnit(evt.UnitId))
            {
                var def = _definitions[key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type == QuestObjectiveType.Interact && objective.TargetRef.Equals(templateId.Value))
                    {
                        UpdateProgress(evt.UnitId, key.QuestId, i, 1);
                    }
                }
            }
        }

        private void HandleSkillCastSuccess(SkillCastSuccessEvent evt)
        {
            foreach (var key in ActiveKeysForUnit(evt.CasterId))
            {
                var def = _definitions[key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type == QuestObjectiveType.Cast && objective.TargetRef.Equals(evt.SkillId))
                    {
                        UpdateProgress(evt.CasterId, key.QuestId, i, 1);
                    }
                }
            }
        }

        private void HandleGossipOpened(GossipOpenedEvent evt)
        {
            foreach (var key in ActiveKeysForUnit(evt.UnitId))
            {
                var def = _definitions[key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type == QuestObjectiveType.Talk && objective.TargetRef.Equals(evt.MenuId))
                    {
                        UpdateProgress(evt.UnitId, key.QuestId, i, 1);
                    }
                }
            }
        }

        private void HandleStoryNodeEntered(StoryNodeEnteredEvent evt)
        {
            foreach (var key in ActiveKeysForUnit(evt.UnitId))
            {
                var def = _definitions[key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type == QuestObjectiveType.Talk && objective.TargetRef.Equals(evt.NodeId))
                    {
                        UpdateProgress(evt.UnitId, key.QuestId, i, 1);
                    }
                }
            }
        }

        private void HandleAreaTriggerEntered(IEvent evt)
        {
            if (!(evt is IExprReadableEvent readable)) return;
            if (!readable.TryGetField("triggerId", out var triggerVal) || triggerVal.Kind != ExprValueKind.Id) return;
            if (!readable.TryGetField("unitId", out var unitVal) || unitVal.Kind != ExprValueKind.Id) return;

            var triggerId = triggerVal.AsId;
            var unitId = unitVal.AsId;

            foreach (var key in ActiveKeysForUnit(unitId))
            {
                var def = _definitions[key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type == QuestObjectiveType.Explore && objective.TargetRef.Equals(triggerId))
                    {
                        UpdateProgress(unitId, key.QuestId, i, 1);
                    }
                }
            }
        }

        private void HandleGenericQuestEvent(IEvent evt)
        {
            foreach (var key in new List<(Id UnitId, Id QuestId)>(_progress.Keys))
            {
                if (!_progress.TryGetValue(key, out var rt) || rt.State != QuestState.Active)
                {
                    continue;
                }
                var def = _definitions[key.QuestId];
                for (var i = 0; i < def.Objectives.Count; i++)
                {
                    var objective = def.Objectives[i];
                    if (objective.Type != QuestObjectiveType.Event || !objective.TargetRef.Equals(evt.Key))
                    {
                        continue;
                    }

                    var matches = true;
                    if (objective.EventFilter != null)
                    {
                        var host = _exprHostFactory.CreateFor(key.UnitId, null, evt);
                        matches = ExprEvaluator.EvaluateBool(objective.EventFilter, host, _exprDiagnostics);
                    }

                    if (matches)
                    {
                        UpdateProgress(key.UnitId, key.QuestId, i, 1);
                    }
                }
            }
        }

        private sealed class QuestRuntimeState
        {
            public QuestState State;
            public int[] ObjectiveCounts = Array.Empty<int>();
            public int CompletionCount;
            public long? LastCompletedDay;
        }
    }
}
