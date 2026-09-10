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

        /// <summary>已订阅过的 <c>event</c> 类目标 <c>target_ref</c> 集合，供 <see cref="Reload"/>
        /// 判断哪些是新出现的 key、避免重复订阅同一个 key（见该方法判断记录）。</summary>
        private readonly HashSet<Id> _subscribedEventObjectiveKeys = new HashSet<Id>();

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

            SubscribeEventObjectiveKeys(_definitions.Values);
        }

        /// <summary>见 <see cref="_subscribedEventObjectiveKeys"/> 判断记录：只对本次调用新出现的
        /// <c>event</c> 类目标 <c>target_ref</c> 订阅一次，已订阅过的 key 跳过——<see cref="Reload"/>
        /// 可能被反复调用，同一个 key 重复 <see cref="IEventBus.Subscribe(Id, EventHandler)"/> 会
        /// 让 <see cref="HandleGenericQuestEvent"/> 对同一次事件触发多次。</summary>
        private void SubscribeEventObjectiveKeys(IEnumerable<QuestDefinition> definitions)
        {
            foreach (var def in definitions)
            {
                foreach (var objective in def.Objectives)
                {
                    if (objective.Type == QuestObjectiveType.Event && _subscribedEventObjectiveKeys.Add(objective.TargetRef))
                    {
                        _eventBus.Subscribe(objective.TargetRef, HandleGenericQuestEvent);
                    }
                }
            }
        }

        /// <summary>
        /// P2-05 关联根治（外部审计 audit-c9ff301-20260909，见 <see cref="SkillDefCache.InvalidateAll"/>
        /// 同一类判断记录）：<see cref="_definitions"/> 构造期从注入的 <see cref="IEnumerable{T}"/>
        /// 一次性建索引、此前没有任何刷新入口——开发期 DataHotReload 对 <c>quest.def</c> 表 reload
        /// 后，resident <see cref="QuestHost"/> 会继续用旧任务定义（目标数量、奖励、
        /// prerequisite/eventFilter 等），直到进程重建全新 host 才会看到新定义。装配根（见
        /// <c>GameplayAssembly</c> 判断记录）订阅
        /// <see cref="Core.Foundation.DataRegistry.DataLoadCompletedEvent"/> 后用最新
        /// <c>registry.GetAll("quest.def")</c> 重新 <see cref="QuestDefinition.FromRecord"/> 一遍，
        /// 调用本方法整体替换 <see cref="_definitions"/>；新出现的 <c>event</c> 类目标 target_ref
        /// 会补订阅，已存在的不重复订阅（见 <see cref="SubscribeEventObjectiveKeys"/>）。不清空
        /// <see cref="_progress"/>——玩家当前的任务进度/状态不因为定义表 reload 而重置，这是运行期
        /// 状态而不是定义缓存。
        /// </summary>
        public void Reload(IEnumerable<QuestDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));

            // CORE-118-QUEST 根治（外部审计 audit-d6fda65-20260911）：此前迁移前在这里现取一份
            // "当前 _definitions" 快照传给 MigrateProgressAfterReload，按 (Type, TargetRef) 把新
            // 目标数组与"上一次 Reload 调用时刻"的旧目标数组配对——这个快照只在"相邻两次 Reload"
            // 之间有效：若中间插入过一次把某个 questId 整体删除的 Reload（判断记录 5，删除定义时
            // 保留进度、不清 _progress），下一次恢复同一 questId 时，这里现取的快照已经是"删除后"
            // 的空快照，压根找不到该 questId 的旧定义，MigrateProgressAfterReload 因此把这条进度
            // 的 ObjectiveCounts 原样跳过（内部原以为"理论上不会发生"的分支）——真实探针复现：
            // Reload(空) 删除定义、Reload([2 目标版]) 恢复同一 id 后，ObjectiveCounts 仍是接取时的
            // 1 长度数组，UpdateProgress(index=1) 直接 IndexOutOfRangeException。
            // 根治改法：不再由 Reload 每次临时抓一份快照传下去，迁移锚点改为每条
            // QuestRuntimeState 自己持有的 LastKnownObjectives（见该字段判断记录）——它记录的是
            // "这条进度当前 ObjectiveCounts 实际对应的目标数组"，由 Accept/本方法迁移完成后共同
            // 维护，天然不受中间隔了多少次删除/恢复 Reload 影响。
            _definitions.Clear();
            foreach (var def in definitions)
            {
                _definitions[def.Id] = def;
            }

            SubscribeEventObjectiveKeys(_definitions.Values);

            // CORE114-01 根治（外部审计 audit-76d16a5-20260910）：此前本方法只替换 _definitions，
            // 完全不触碰 _progress——真实探针复现：旧定义 1 条 kill 目标、玩家已接取并取得进度，
            // reload 同一 quest id 为 2 条目标后，再次 UpdateProgress(index=1) 直接
            // IndexOutOfRangeException（rt.ObjectiveCounts 仍是旧的 1 长度数组，见
            // UpdateProgress/ApplyObjectiveCount 判断记录）。改为 reload 后立即对每条运行中
            // （Active/ObjectivesComplete）的进度按新定义迁移 ObjectiveCounts 形状，见
            // MigrateProgressAfterReload 判断记录。
            MigrateProgressAfterReload();
        }

        /// <summary>
        /// CORE114-01 根治（外部审计 audit-76d16a5-20260910）：<see cref="Reload"/> 替换定义后，把
        /// 每条运行中（<see cref="QuestState.Active"/>/<see cref="QuestState.ObjectivesComplete"/>，
        /// 见判断记录 1）的 <see cref="QuestRuntimeState.ObjectiveCounts"/> 从旧定义的目标数组形状
        /// 迁移到新定义的目标数组形状，不再让后续任何按新定义索引的读写（<see
        /// cref="UpdateProgress"/>/<see cref="GetActiveObjectives"/> 等）撞上一个大小仍是旧值的数组。
        /// <para>
        /// 判断记录 1（迁移范围只覆盖 Active/ObjectivesComplete）：<see cref="QuestState.Failed"/>/
        /// <see cref="QuestState.TurnedIn"/> 两个终态下，<c>ObjectiveCounts</c> 不会再被任何按当前
        /// 定义索引的路径触碰（<see cref="TryGetActive"/>/<see
        /// cref="TryGetActiveOrObjectivesComplete"/> 都已把范围限定为这两个非终态），保留旧数组不会
        /// 造成越界，也不需要为不会被再读取的历史进度花代价重算。
        /// </para>
        /// <para>
        /// 判断记录 2（逐新目标按 (Type, TargetRef) 匹配旧目标，先到先得）：新目标 i 依次在旧定义里
        /// 找"Type 与 TargetRef 相同、且尚未被本次迁移匹配过"的第一条旧目标——用
        /// <c>matched</c> 数组防止同一条旧目标被两个新目标重复认领（例如旧定义有两条不同
        /// <c>target_ref</c> 的 kill 目标，新定义把其中一条拆成两条相同 <c>target_ref</c> 的目标，
        /// 只有排在前面的新目标能认领到那条旧进度，另一条按"无匹配"处理，不会凭空把同一份旧计数
        /// 复制两遍）。命中的旧计数按新目标的 <c>Count</c> clamp（新上限可能比旧的小）。目标增加、
        /// 减少、重排三种变化都落在这同一套"逐新目标找旧目标"的循环里，不需要分情况处理。
        /// </para>
        /// <para>
        /// 判断记录 3（无匹配的新目标：Collect 非消耗型按持有量重算，其余置 0）：新出现的目标没有
        /// 历史进度可继承，语义上等价于"任务刚刚才第一次拥有这个目标"——<see cref="Accept"/> 对
        /// <c>Collect</c> 且 <c>!ConsumeOnProgress</c> 的目标就是"接取瞬间按当前库存持有量重算"
        /// （GP-08 判断记录），这里复用同一条规则，不发明第二套语义；其余目标类型（消耗型 collect、
        /// kill/interact/cast/explore/talk/event）没有"当前已持有多少"这个概念，一律从 0 开始。
        /// </para>
        /// <para>
        /// 判断记录 4（事件与状态收尾复用既有路径）：每个新索引的最终值与"迁移前的对应值"（匹配到的
        /// 旧计数，或无匹配时的 0）不同才发一次 <see cref="QuestObjectiveProgressEvent"/>——与
        /// <see cref="ApplyObjectiveCount"/> "值不变不发事件"同一条惯例。全部索引迁移完成后调用与
        /// <see cref="ApplyObjectiveCount"/> 共用的 <see cref="ReevaluateCompletionState"/>：
        /// 迁移后恰好全部达标（含目标减少到玩家已有进度足以覆盖的情形）从 Active 进入
        /// ObjectivesComplete 并照常发 <see cref="QuestCompletedEvent"/>；此前已 ObjectivesComplete
        /// 但新定义下不再全部达标（目标增加/上限提高）回落 Active，不合成任何"取消完成"新事件类型
        /// （与 ApplyObjectiveCount 判断记录同一条理由：08 文档事件词汇表没有定义这一事件）。
        /// </para>
        /// <para>
        /// 判断记录 5（定义被整体移除：保持现状）：新 <see cref="_definitions"/> 里已不存在的
        /// questId——按任务书"定义被移除而进度仍在：保持现状"直接跳过，不清理 <c>_progress</c>
        /// 里的记录、也不触碰其 <c>ObjectiveCounts</c>／<c>LastKnownObjectives</c>。这类记录本就
        /// 不会再被 <see cref="RequireDef"/> 之外的任何路径用新定义索引（<see cref="RequireDef"/>
        /// 对未登记 id 直接抛 <see cref="ArgumentException"/>，行为与迁移前一致，不在本次根治范围
        /// 内），单位级枚举/事件路径见 <see cref="TryGetDefinition"/> 判断记录。
        /// </para>
        /// <para>
        /// CORE-118-QUEST 根治（外部审计 audit-d6fda65-20260911）：判断记录 2 原本按"调用方传入的
        /// <c>oldDefinitions</c> 参数"取旧目标数组，该参数是 <see cref="Reload"/> 每次调用时现取的
        /// "当前 _definitions" 快照，只覆盖"相邻两次 Reload 之间"的变化——中间若插入过一次把该
        /// questId 整体删除的 Reload，之后恢复同一 id 时快照里已经没有它，旧代码把这种情况当成
        /// "理论上不会发生"直接 continue（跳过迁移，ObjectiveCounts 停留在删除前的旧长度），恢复后
        /// 对新目标数组越界索引直接 <see cref="IndexOutOfRangeException"/>（真实探针复现，见类型
        /// 判断记录）。改法：旧目标数组不再由 <see cref="Reload"/> 临时传入，直接读
        /// <c>rt.LastKnownObjectives</c>（每条进度自己持有、由 <see cref="Accept"/> 与本方法自身
        /// 维护，见该字段判断记录）——不管中间隔了多少次定义整体删除/恢复的 Reload，这条进度记录
        /// "当前 ObjectiveCounts 对应哪个目标数组形状"这一事实本身没有丢失，因此也就不存在"找不到
        /// 旧定义"这一分支，原有的防御性 continue 随之一并移除。
        /// </para>
        /// </summary>
        private void MigrateProgressAfterReload()
        {
            foreach (var kv in new List<KeyValuePair<(Id UnitId, Id QuestId), QuestRuntimeState>>(_progress))
            {
                var unitId = kv.Key.UnitId;
                var questId = kv.Key.QuestId;
                var rt = kv.Value;

                if (rt.State != QuestState.Active && rt.State != QuestState.ObjectivesComplete)
                {
                    continue;
                }
                if (!_definitions.TryGetValue(questId, out var newDef))
                {
                    continue; // 判断记录 5：定义被移除，保持现状。
                }

                var oldObjectives = rt.LastKnownObjectives;
                var matched = new bool[oldObjectives.Count];
                var newCounts = new int[newDef.Objectives.Count];
                var priorForCompare = new int[newDef.Objectives.Count];

                for (var i = 0; i < newCounts.Length; i++)
                {
                    var newObjective = newDef.Objectives[i];
                    int? sourceCount = null;

                    for (var j = 0; j < oldObjectives.Count; j++)
                    {
                        if (matched[j])
                        {
                            continue;
                        }
                        var oldObjective = oldObjectives[j];
                        if (oldObjective.Type == newObjective.Type && oldObjective.TargetRef.Equals(newObjective.TargetRef))
                        {
                            matched[j] = true;
                            sourceCount = rt.ObjectiveCounts[j];
                            break;
                        }
                    }

                    priorForCompare[i] = sourceCount ?? 0;
                    newCounts[i] = ResolveMigratedObjectiveCount(unitId, newObjective, sourceCount);
                }

                rt.ObjectiveCounts = newCounts;
                rt.LastKnownObjectives = newDef.Objectives;

                for (var i = 0; i < newCounts.Length; i++)
                {
                    if (newCounts[i] != priorForCompare[i])
                    {
                        Publish(new QuestObjectiveProgressEvent(unitId, questId, i, newCounts[i]));
                    }
                }

                ReevaluateCompletionState(unitId, questId, newDef, rt);
            }
        }

        /// <summary>供 <see cref="MigrateProgressAfterReload"/> 与存档读入迁移
        /// （<see cref="ReplaceAllProgress"/>）共用的单目标"旧计数 → 新计数"收尾规则：找到旧计数则
        /// clamp 到新 <paramref name="objective"/> 的 <see cref="QuestObjective.Count"/> 上限；没有
        /// 旧计数可继承时，<c>Collect</c> 且 <c>!ConsumeOnProgress</c> 按当前库存持有量重算（同
        /// <see cref="Accept"/> 的 GP-08 判断记录），其余类型置 0（见
        /// <see cref="MigrateProgressAfterReload"/> 判断记录 3）。</summary>
        private int ResolveMigratedObjectiveCount(Id unitId, QuestObjective objective, int? sourceCount)
        {
            if (sourceCount.HasValue)
            {
                return Clamp(sourceCount.Value, 0, objective.Count);
            }
            if (objective.Type == QuestObjectiveType.Collect && !objective.ConsumeOnProgress)
            {
                return Clamp(_inventoryHost.CountOf(unitId, objective.TargetRef), 0, objective.Count);
            }
            return 0;
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
                // CORE-118-QUEST 根治：记下本次接取用的目标数组形状，供之后任意次 Reload 迁移使用
                // （见 QuestRuntimeState.LastKnownObjectives 判断记录）。
                LastKnownObjectives = def.Objectives,
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
        /// 防御极端并发下的移除失败。</item>
        /// <item>发放奖励——经 <see cref="IRewardDispatcher.Grant"/>（N02：内部已对物品奖励做原子
        /// 发放与失败回滚，见其判断记录）；失败时（背包已满装不下奖励物品）整体失败于
        /// <see cref="QuestTurnInFailure.InventoryFull"/>，任务保持
        /// <see cref="QuestState.ObjectivesComplete"/>，玩家清出空间后可重新交付。</item>
        /// </list>
        /// <para>
        /// 第五轮外部审核相邻缺口根治（architecture/落地计划/audit-5e779c6-20260907）：步骤 2/3 此前
        /// 各自失败时都用"把已移除的 collect 物品重新 <c>AddItem</c> 放回背包"来回滚——这一"移除又
        /// 放回"会产生一条真实的 <c>item.added</c> 事件，可能被玩家对同一物品模板持有的另一个
        /// <c>consumeOnProgress</c> 目标误当作"新获得"而错误推进进度（该目标其实什么都没有新收到，
        /// 只是这次失败交付的内部纠正）。改为把步骤 2/3 整体包进一次 <see
        /// cref="IInventoryTransaction"/>（若 <see cref="_inventoryHost"/> 实现了 <see
        /// cref="IBatchableInventoryHost"/>，同 <c>RewardDispatcher.GrantItems</c> 判断记录）：事务
        /// 开启期间移除 collect 物品产生的 <c>item.removed</c> 先缓存、不送入总线；任一步失败时整体
        /// <see cref="IInventoryTransaction.Dispose"/>（未调用 <see cref="IInventoryTransaction.Commit"/>
        /// 即回滚），把背包状态与缓存事件一并撤销——不再需要手动调用 <c>AddItem</c> 放回，因此也就
        /// 不会再产生任何虚假的 <c>item.added</c>。<see cref="IRewardDispatcher.Grant"/>
        /// 内部若同样需要发放物品也会调用 <see cref="IBatchableInventoryHost.BeginBatch"/>——此时
        /// 本方法已持有的事务尚未提交，<c>InventoryHost.BeginBatch</c> 检测到已在事务中会返回一个
        /// 透传句柄（见其判断记录），加入同一个事务而不是另开一层，真正的提交/回滚权限仍归本方法
        /// 持有的这个最外层事务实例。宿主不支持 <see cref="IBatchableInventoryHost"/> 时（多数测试
        /// 用的最小 Fake）保留历史的"逐项 AddItem 放回"行为，不强制所有实现跟进。
        /// </para>
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

            // 步骤 2/3：见方法顶部判断记录——移除 collect 物品与发放奖励整体包进同一批库存事务。
            var transaction = _inventoryHost is IBatchableInventoryHost batchable ? batchable.BeginBatch() : null;
            using (transaction)
            {
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

                    if (transaction == null)
                    {
                        foreach (var prior in removed)
                        {
                            _inventoryHost.AddItem(unitId, prior.TemplateId, prior.Count);
                        }
                    }
                    // 宿主支持事务时不需要手动放回——using 块结束触发 Dispose 即整体回滚（含事件）。

                    failure = QuestTurnInFailure.InsufficientItems;
                    return false;
                }

                // 步骤 3：发放奖励；失败则回滚步骤 2 移除的物品（N02）。
                if (!_rewardDispatcher.Grant(unitId, def.Rewards, questId))
                {
                    if (transaction == null)
                    {
                        foreach (var prior in removed)
                        {
                            _inventoryHost.AddItem(unitId, prior.TemplateId, prior.Count);
                        }
                    }

                    failure = QuestTurnInFailure.InventoryFull;
                    return false;
                }

                transaction?.Commit();
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
                if (!TryGetDefinition(kv.Key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次跳过枚举，见 TryGetDefinition 判断记录。
                }
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

                // CORE114-01 收口（外部审计 audit-76d16a5-20260910）：此前这里只按位置直接
                // `counts[i] = progress.ObjectiveCounts[i]`——既不 clamp 到当前 def 的 Count 上限
                // （存档写出之后若内容改小了某目标的 Count，读档会把"超过新上限"的旧值原样保留），
                // 快照比当前定义短时多出来的新目标也一律留 0（哪怕是 Collect 且 !ConsumeOnProgress、
                // 背包里其实已经有货）。改为与 <see cref="MigrateProgressAfterReload"/> 共用同一个单
                // 目标收尾规则 <see cref="ResolveMigratedObjectiveCount"/>：存档快照按位置对应（同一份
                // 快照本就是针对写出时那份定义按顺序序列化的，不需要也没有旧 QuestDefinition 可供
                // 按 (Type, TargetRef) 重新匹配，位置对应即权威对应），超出快照长度的新目标与迁移场景
                // 走同一条"Collect 非消耗型按持有量重算，其余置 0"规则；命中的快照值统一 clamp 到当前
                // Count。本方法不重新评估 <c>rt.State</c>（仍按快照写入的 <c>progress.State</c> 为准，
                // 存档读入的状态一致性是 GP-01 既有契约，不在本次 CORE114-01 范围内）。
                var counts = new int[def.Objectives.Count];
                for (var i = 0; i < counts.Length; i++)
                {
                    int? sourceCount = i < progress.ObjectiveCounts.Count ? progress.ObjectiveCounts[i] : (int?)null;
                    counts[i] = ResolveMigratedObjectiveCount(unitId, def.Objectives[i], sourceCount);
                }

                newEntries.Add(((unitId, progress.QuestId), new QuestRuntimeState
                {
                    State = progress.State,
                    ObjectiveCounts = counts,
                    CompletionCount = progress.CompletionCount,
                    LastCompletedDay = progress.LastCompletedDay,
                    // CORE-118-QUEST 根治：读档恢复的进度同样要带上当时对应的目标数组形状，否则
                    // 读档后紧接一次 Reload 会因为 LastKnownObjectives 仍是默认空数组而把全部旧计数
                    // 当成"无匹配"丢弃（见 QuestRuntimeState.LastKnownObjectives／
                    // MigrateProgressAfterReload 判断记录）。
                    LastKnownObjectives = def.Objectives,
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

        /// <summary>
        /// CORE-118-QUEST 根治（外部审计 audit-d6fda65-20260911）：此前 <see
        /// cref="GetActiveObjectives"/> 与全部事件驱动进度处理方法（<see cref="HandleUnitDied"/>/
        /// <see cref="HandleItemAdded"/>/<see cref="HandleItemRemoved"/>/<see
        /// cref="HandleGobjInteracted"/>/<see cref="HandleSkillCastSuccess"/>/<see
        /// cref="HandleGossipOpened"/>/<see cref="HandleStoryNodeEntered"/>/<see
        /// cref="HandleAreaTriggerEntered"/>/<see cref="HandleGenericQuestEvent"/>）都直接
        /// <c>_definitions[key.QuestId]</c> 索引——这些路径按 <c>unitId</c> 或事件字段枚举
        /// <see cref="_progress"/>，从未把 <c>questId</c> 交给调用方校验过，一旦某条 Active 进度
        /// 引用的定义已被 <see cref="Reload"/> 删除（判断记录 5：删除定义保留进度是既定行为），
        /// 直接索引就是 <see cref="KeyNotFoundException"/>（真实探针复现：<c>GetActiveObjectives
        /// (unit)</c> 未传任何已删除 id，仍然抛出）。这不同于 <see cref="RequireDef"/> 服务的
        /// 显式按 id 调用（<see cref="GetState"/>/<see cref="Accept"/>/<see cref="UpdateProgress"/>/
        /// <see cref="TurnIn"/>/<see cref="Fail"/>——调用方主动传入一个可能不存在的 questId，抛
        /// <see cref="ArgumentException"/> 是既有合同，不在本次根治范围）。本方法统一收敛上述内部
        /// 枚举路径：定义缺失时返回 <c>false</c>，调用方按"该条进度本次跳过（保留进度、不枚举、
        /// 不推进）"处理，不抛异常——这也是任务书要求的"明确降级"：进度本身不受影响，只是暂停
        /// 暴露/推进，直到该定义被重新 Reload 恢复。
        /// </summary>
        private bool TryGetDefinition(Id questId, out QuestDefinition def) => _definitions.TryGetValue(questId, out def!);

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

            ReevaluateCompletionState(unitId, questId, def, rt);
        }

        /// <summary>CORE114-01 根治（外部审计 audit-76d16a5-20260910）：从 <see
        /// cref="ApplyObjectiveCount"/> 尾部收口出来的状态双向同步——单条目标计数变化（本方法原有
        /// 调用方）与 reload 迁移后整批计数变化（<see cref="MigrateProgressAfterReload"/> 新增调用方）
        /// 共用同一条"是否全部达标"判定与同一条事件路径，不为迁移场景另开一份平行逻辑、也不合成新的
        /// 事件类型（同 <see cref="ApplyObjectiveCount"/> 原有判断记录）。</summary>
        private void ReevaluateCompletionState(Id unitId, Id questId, QuestDefinition def, QuestRuntimeState rt)
        {
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
                if (!TryGetDefinition(key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次事件跳过推进，见 TryGetDefinition 判断记录。
                }
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
                if (!TryGetDefinition(key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次事件跳过推进，见 TryGetDefinition 判断记录。
                }
                var rt = _progress[key];
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
                if (!TryGetDefinition(key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次事件跳过推进，见 TryGetDefinition 判断记录。
                }
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
                if (!TryGetDefinition(key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次事件跳过推进，见 TryGetDefinition 判断记录。
                }
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
                if (!TryGetDefinition(key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次事件跳过推进，见 TryGetDefinition 判断记录。
                }
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
                if (!TryGetDefinition(key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次事件跳过推进，见 TryGetDefinition 判断记录。
                }
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
                if (!TryGetDefinition(key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次事件跳过推进，见 TryGetDefinition 判断记录。
                }
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
                if (!TryGetDefinition(key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次事件跳过推进，见 TryGetDefinition 判断记录。
                }
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
                if (!TryGetDefinition(key.QuestId, out var def))
                {
                    continue; // CORE-118-QUEST：定义已删除，进度保留但本次事件跳过推进，见 TryGetDefinition 判断记录。
                }
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

            /// <summary>CORE-118-QUEST 根治：<see cref="ObjectiveCounts"/> 当前形状对应的目标数组，
            /// 随本条进度记录本身持久保存，不依赖某次 <see cref="Reload"/> 调用时刚好还留着的
            /// <c>_definitions</c>／局部 oldDefinitions 快照——见 <see cref="MigrateProgressAfterReload"/>
            /// 判断记录"迁移锚点改为逐条进度自带"。</summary>
            public IReadOnlyList<QuestObjective> LastKnownObjectives = Array.Empty<QuestObjective>();
        }
    }
}
