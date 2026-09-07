using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="IWorldSim"/> 的默认实现：维护实体集合、按 03_运行时骨架.md 第 4.2 节
    /// 固定的八步 tick 顺序编排各阶段处理器、驱动通用计时器推进、经 <see cref="IEventBus"/>
    /// 派发本 tick 产生的事件。阶段 7"事件派发"、阶段 8"生命周期清理"由本类自己执行，
    /// 外部不允许为这两个阶段注册处理器（见 <see cref="RegisterPhaseHandler"/>）。
    /// </summary>
    public sealed class WorldSim : IWorldSim, IDisposable
    {
        private static readonly TickPhase[] RegistrablePhaseOrder =
        {
            TickPhase.IntentCollection,
            TickPhase.AiDecision,
            TickPhase.SkillPipeline,
            TickPhase.MovementAndNavigation,
            TickPhase.CombatResolution,
            TickPhase.TriggerEvaluation
        };

        private readonly IEventBus _bus;
        private readonly SortedDictionary<Id, Entity> _entities = new SortedDictionary<Id, Entity>();
        private readonly HashSet<Id> _pendingDestruction = new HashSet<Id>();

        private readonly Dictionary<TickPhase, List<ITickPhaseHandler>> _phaseHandlers =
            new Dictionary<TickPhase, List<ITickPhaseHandler>>();

        private readonly Dictionary<string, int> _idSequenceByKind = new Dictionary<string, int>();

        // 判断记录（FND-04 收口，代替此前"ClearAll 换新 SimTimers 实例"的做法）：早先的实现把
        // 本字段从 readonly 松绑为可重新赋值，ClearAll 直接换上一个全新的 SimTimers 实例——
        // 当时的任务书把改动范围限定为"只允许给 IWorldSim/WorldSim 增加 ClearAll()"，不允许连带
        // 修改 SimTimers.cs。但换实例有一个严重副作用：新实例的 TimerHandle 编号从 1 重新开始，
        // 任何跨 ClearAll 边界持有的旧 TimerHandle 值可能与清空后新创建的计时器句柄数值相同——
        // 由于 WorldSim.Timers 属性此后一直返回同一个（新）实例，调用方对旧句柄调用
        // ISimTimers.Cancel 会在数值碰撞时意外取消一个语义上完全无关的新计时器（详见外部审核
        // FND-04：D:/workespace/ws-game-review-b3b91ee 下 audit-b3b91ee-20260907/code-review.md）。
        // 现改为保持同一个 SimTimers 实例（宿主对象稳定），ClearAll 调用其新增的 Clear()
        // 原地清空存活计时器——Clear() 不重置编号计数器，句柄值在实例生命周期内单调递增、永不
        // 复用，彻底消除跨清空边界的数值碰撞（见 SimTimers.Clear 判断记录）。
        private readonly SimTimers _timers = new SimTimers();
        private readonly List<string> _diagnosticsWarnings = new List<string>();

        // H4 补齐（意图路由缺口 1）：可选持有的离散路由依赖，见 AttachDiscreteRouting 判断记录。
        // 持有具体类型 TurnScheduler（而不是 ITurnScheduler）——同 GameplayAssembly.TurnScheduler
        // 属性判断记录，避免为"内部路由用的记账方法"新增 03 文档之外的接口原语。未调用
        // AttachDiscreteRouting 时两个字段保持 null，SubmitIntent 行为与 H4 之前完全一致。
        private ISimClockHost? _clockHost;
        private TurnScheduler? _turnScheduler;

        // T1-9 新增：意图队列（见 IWorldSim.SubmitIntent/CurrentIntents 注释）。_pendingIntents
        // 收集 tick 外提交的意图；每次 Tick 阶段 1 开头整体搬到 _currentIntents（保持提交顺序，
        // 确定性），阶段 8 末清空 _currentIntents，_pendingIntents 换上新的空列表供下一 tick 使用。
        private List<Intent> _pendingIntents = new List<Intent>();
        private List<Intent> _currentIntentsList = new List<Intent>();

        // 集成任务新增：AppendCurrentIntent 的"tick 进行中"守卫（见 IWorldSim.AppendCurrentIntent
        // 注释）。true 的窗口覆盖整个 Tick() 方法体（含阶段 8 末尾清空 _currentIntentsList 之前），
        // 但实际调用方（各阶段 ITickPhaseHandler.Execute）只会在阶段 1～7 期间执行，不会真正触及
        // "已清空之后仍处于 true"这个边角。
        private bool _isTicking;

        private long _tickCounter;

        // FND-06 收口（见 ReplayPlayer.cs 判断记录）：保存构造函数里 _bus.Subscribe 返回的句柄，
        // 使本实例的 EventBus 订阅可以经 Dispose() 显式释放——此前没有任何途径取消这条订阅，
        // 一个不再被任何人持有引用的 WorldSim 实例（例如 ReplayPlayer 第二次 Load 时被替换掉的
        // 旧世界）仍会因为这条订阅继续被 _bus 的订阅者列表强引用，永久存活并对后续事件起反应
        // （即便只是推进一个已经没有任何读者的 SimTimers，也是不该发生的资源泄漏与阶段串扰）。
        private readonly SubscriptionHandle _roundEndedSubscription;
        private bool _disposed;

        public WorldSim(IEventBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));

            // H4 补齐（意图路由缺口 2）：离散步下 Tick 本身不推进全局计时器（见 Tick 方法内
            // 分支与其注释），改为"每轮结束推进一轮"——订阅 sim.round_ended，与
            // core/rules/combat.CombatTickHandler 的既有惯例完全一致（该类型构造函数同样
            // `bus?.Subscribe<SimRoundEndedEvent>(SimEventKeys.RoundEnded, _ => _host.Update(1.0))`，
            // 见其类型注释——"1.0"即"一轮"，与 CombatOptions.LeaveCombatDelay 已经在
            // TimeModelSwitch 切模式时按 seconds_per_turn 换算为等效轮数的做法同一单位约定）。
            // 本类构造函数 bus 恒非空（上面已判空），不需要像 CombatTickHandler 那样把 bus
            // 声明为可选——WorldSim 本来就总是持有一份 IEventBus。sim.round_ended 只在
            // TurnScheduler.BeginCombat 之后才可能触发（见该类型），未使用离散模式的调用方
            // 这条订阅永远不会被触发，对连续模式零副作用。
            _roundEndedSubscription = _bus.Subscribe<SimRoundEndedEvent>(SimEventKeys.RoundEnded, _ => _timers.Advance(1.0));
        }

        /// <summary>
        /// FND-06 收口新增：释放本实例在构造时对 <see cref="IEventBus"/> 建立的订阅（见上方
        /// <see cref="_roundEndedSubscription"/> 字段判断记录）。<see cref="SubscriptionHandle.Dispose"/>
        /// 本身幂等，本方法因此也是幂等的——可安全多次调用。不清空 <see cref="_entities"/> 等内部
        /// 状态（那不是"释放外部资源"，是"重置内容"，已有 <see cref="ClearAll"/> 承担；两者职责
        /// 不同：一个实例被 Dispose 之后不再可用，<see cref="ClearAll"/> 之后仍可继续使用）。调用方
        /// 典型场景：<see cref="Core.Foundation.SaveSystem.ReplayPlayer"/> 在用新一次
        /// <c>Load</c>/<c>LoadDiscrete</c> 换上一个新世界之前，先对旧世界调用本方法，避免旧世界
        /// 因为这条挂在共享 <see cref="IEventBus"/> 上的订阅而被永久强引用、继续对新世界产生的事件
        /// 起反应。<c>IWorldSim</c> 契约本身不要求 Dispose（只有本类型这个具体实现持有需要释放的
        /// 订阅），因此本类型实现的是标准 <see cref="System.IDisposable"/>，而不是在 <c>IWorldSim</c>
        /// 接口上新增该要求——那样会波及全部 <c>IWorldSim</c> 的调用方/测试替身，超出本条缺陷的
        /// 修复范围（见本模块 README 判断记录）。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _roundEndedSubscription.Dispose();
        }

        /// <summary>是否已经被 <see cref="Dispose"/> 过（同 <see cref="SubscriptionHandle.IsDisposed"/>
        /// 惯例）；不是 <see cref="IWorldSim"/> 契约的一部分，只读，供调用方/测试判断本实例是否
        /// 已经释放了其 <see cref="IEventBus"/> 订阅——不影响 <see cref="Tick"/> 等其它方法在
        /// Dispose 之后仍可调用（本类型未对已释放实例的继续使用做拦截，只保证订阅被取消；调用方
        /// 若需要"用后即弃"的强保证应自行不再持有该引用）。</summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// H4 补齐（意图路由缺口 1，见 03 §3.2 步骤 3、TurnScheduler 类型顶部"主循环驱动协议"）：
        /// 由 <c>Core.Gameplay.Assembly.GameplayAssembly</c> 在装配了 <paramref name="clockHost"/> +
        /// <paramref name="turnScheduler"/>（即启用了离散时间模型）时调用本方法接线，使
        /// <see cref="SubmitIntent"/> 在 <see cref="TimeModelMode.Discrete"/> 下能把玩家经
        /// <c>CastSkill</c>/<c>MovementHost.Request</c> 等既有调用链提交的意图正确路由到
        /// <see cref="TurnScheduler"/>（解除 <c>awaiting_input</c>、产出该行动者的离散步）——此前
        /// <see cref="SubmitIntent"/> 一律塞进 <see cref="_pendingIntents"/>，<see cref="TurnScheduler"/>
        /// 完全不知道玩家已经提交，<see cref="TurnScheduler.NextStep"/> 永远返回 null，战斗卡死在
        /// <c>awaiting_input</c>（H3a 如实记录的缺口 1）。未调用本方法时 <see cref="SubmitIntent"/>
        /// 行为与此前完全一致（两个字段保持 null）。
        /// </summary>
        public void AttachDiscreteRouting(ISimClockHost clockHost, TurnScheduler turnScheduler)
        {
            _clockHost = clockHost ?? throw new ArgumentNullException(nameof(clockHost));
            _turnScheduler = turnScheduler ?? throw new ArgumentNullException(nameof(turnScheduler));
        }

        public ISimTimers Timers => _timers;

        public int EntityCount => _entities.Count;

        /// <summary>本类自身维护的累计已处理 tick 数，独立于 <see cref="SimClockHost.TickIndex"/>
        /// ——后者只统计由它自己发起的连续 tick；二者在"每个 SimClockHost 推进的 tick 都对应
        /// 一次 WorldSim.Tick 调用、且不存在其它 Tick 调用来源"的纯连续模式场景下数值一致。
        /// 供 <c>sim.tick_started</c>/<c>sim.tick_finished</c> 事件的 <c>tickIndex</c> 字段使用。</summary>
        public long TickIndex => _tickCounter;

        /// <summary>
        /// 诊断警告列表：目前只用于记录"离散步下计时器未推进"（见 03 第 8 节）。
        /// 判断记录：任务书"诊断接口可用事件总线的 IEventDiagnostics 或本模块自带的简单
        /// 诊断列表，二选一说明"——这里选择本模块自带的简单列表，不引入对
        /// <c>Core.Foundation.EventBus.IEventDiagnostics</c> 的依赖：该接口语义上是"事件总线
        /// 派发过程"的诊断出口（未登记事件 key、类型不匹配、超过派发轮次上限等），
        /// 而"离散步下计时器不推进"是 sim_loop 模块内部与事件派发无关的关注点，
        /// 复用它会让两个不同来源的诊断信息混进同一个通道，不如各自独立更清晰。
        /// </summary>
        public IReadOnlyList<string> DiagnosticsWarnings => _diagnosticsWarnings;

        public void SubmitIntent(Intent intent)
        {
            // H4 补齐（意图路由缺口 1，见 AttachDiscreteRouting 判断记录）：仅当已接线离散路由
            // 且当前处于 Discrete 模式时才拦截——路由集中在本方法（调用方 CastSkill/
            // MovementHost.Request 等不用改），不是当前等待输入行动者的意图直接拒绝（记诊断、
            // 不入队，不产生离散步）；是当前行动者时，先经 TurnScheduler 做一次与
            // TurnScheduler.SubmitIntent 相同的记账（解除 awaiting_input、标记本行动者已有待处理
            // 意图），再落到本类统一的 _pendingIntents 队列——记账放在 TurnScheduler 一侧、真正
            // 入队放在这里，是为了避免 TurnScheduler.SubmitIntent（它自己也会调用
            // world.SubmitIntent，见该方法）与本方法互相调用造成递归，见
            // TurnScheduler.TryAcceptExternalIntent 判断记录。AI 在自己的离散步内经
            // IWorldSim.AppendCurrentIntent 产生的意图走另一条路径（tick 执行期间直接写入
            // CurrentIntents），不经过本方法，不受这里的路由影响（见该方法注释）。
            if (_clockHost != null && _turnScheduler != null && _clockHost.Mode == TimeModelMode.Discrete)
            {
                if (!_turnScheduler.TryAcceptExternalIntent(intent.ActorId))
                {
                    _diagnosticsWarnings.Add(
                        $"离散模式下拒绝意图：actorId=\"{intent.ActorId}\" 当前不是等待输入的行动者" +
                        "（见 TurnScheduler.GetCurrentActor），意图被丢弃，不入队、不产生离散步");
                    return;
                }
            }

            _pendingIntents.Add(intent);
        }

        public IReadOnlyList<Intent> CurrentIntents => _currentIntentsList;

        /// <summary>见 <see cref="IWorldSim.AppendCurrentIntent"/>：只在 <see cref="_isTicking"/>
        /// 为 true（本次 <see cref="Tick"/> 执行期间）时允许追加，直接写入
        /// <see cref="_currentIntentsList"/>（而不是像 <see cref="SubmitIntent"/> 那样进入下一 tick
        /// 待收集队列），追加顺序即调用顺序。</summary>
        public void AppendCurrentIntent(Intent intent)
        {
            if (!_isTicking)
            {
                throw new InvalidOperationException(
                    "AppendCurrentIntent 只能在 Tick 执行期间调用（阶段 1～7 之间），" +
                    "tick 外请改用 SubmitIntent");
            }

            _currentIntentsList.Add(intent);
        }

        public void Tick(SimStep step)
        {
            var tickIndex = _tickCounter;
            var dt = step.Kind == SimStepKind.Continuous ? step.Dt : 0.0;

            // 阶段 1（IntentCollection）开头之前把待提交队列整体搬到 CurrentIntents：保持
            // SubmitIntent 的调用顺序（List 保序），_pendingIntents 换上新的空列表供下一 tick
            // 期间提交的意图使用（与本 tick 的 CurrentIntents 互不干扰）。
            _currentIntentsList = _pendingIntents;
            _pendingIntents = new List<Intent>();

            // 离散步下第 1 步"输入意图收集"只收集当前行动者的意图（见 03 第 4.2 节步骤 1、
            // ADR-0013 决策 2）：过滤掉任何 ActorId 不等于 step.ActorId 的意图——正常情况下不应该
            // 存在这类"串门"意图（TurnScheduler.SubmitIntent 只允许当前行动者提交，见该类型），
            // 这里的过滤是防御性的，同时也是文档要求的"离散步只处理当前行动者的意图"在实现层面
            // 最直接、最不易遗漏的落点（放在这里而不是逐个 ITickPhaseHandler 各自过滤，保证任何
            // 后续新增的阶段处理器都自动获得这条保证，不需要每个处理器自己记得再判一次）。
            if (step.Kind == SimStepKind.Discrete && step.ActorId.HasValue)
            {
                var actorId = step.ActorId.Value;
                var filtered = new List<Intent>(_currentIntentsList.Count);
                for (var i = 0; i < _currentIntentsList.Count; i++)
                {
                    if (_currentIntentsList[i].ActorId.Equals(actorId))
                    {
                        filtered.Add(_currentIntentsList[i]);
                    }
                }
                _currentIntentsList = filtered;
            }

            _isTicking = true;

            // 判断记录：sim.tick_started 用 PublishImmediate 立即派发，先于本 tick 全部阶段
            // 处理器执行；这是一个"tick 开始"的边界标记事件，不属于 03 第 4.2 节步骤 7
            // 所指"本 tick 累积的事件"（那些事件由步骤 1~6 的处理器产生，仍走 Enqueue +
            // 本方法后段的 DispatchPending 批量派发，符合"同步派发 + tick 末批处理"）。
            _bus.PublishImmediate(new SimTickStartedEvent(tickIndex, dt));

            if (step.Kind == SimStepKind.Continuous)
            {
                _timers.Advance(step.Dt);
            }
            else
            {
                _diagnosticsWarnings.Add(
                    $"tick {tickIndex}：离散步（Discrete）不推进全局计时器——离散步以行动者为粒度，" +
                    "不代表统一的时间推进量，全局计时器（冷却/光环剩余时长等）的换算发生在连续/离散" +
                    "模式切换时刻（见 03 第 3.3 节步骤 2、TimeModelSwitch），不是每个离散步都线性推进");
            }

            for (var i = 0; i < RegistrablePhaseOrder.Length; i++)
            {
                ExecutePhase(RegistrablePhaseOrder[i], step);
            }

            // 阶段 7：事件派发——把步骤 1~6 产生的全部事件按入队顺序批量派发给订阅者。
            _bus.DispatchPending();

            // 阶段 8：生命周期清理——对每个待销毁实体先发出 entity.destroyed，再真正移除。
            if (_pendingDestruction.Count > 0)
            {
                var ids = new List<Id>(_pendingDestruction);
                ids.Sort();

                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    if (_entities.TryGetValue(id, out var entity))
                    {
                        _bus.Enqueue(new EntityDestroyedEvent(id));
                        entity.Lifecycle = EntityLifecycle.Destroyed;
                        _entities.Remove(id);
                    }
                }

                _pendingDestruction.Clear();
            }

            _bus.Enqueue(new SimTickFinishedEvent(tickIndex));

            // 再调用一次 DispatchPending，让本阶段刚入队的 entity.destroyed 与
            // sim.tick_finished 在本 tick 内送达（而不是留到下一次 Tick 才派发）。
            _bus.DispatchPending();

            // 阶段 8 末清空 CurrentIntents（见 IWorldSim.CurrentIntents 注释）：本 tick 的意图
            // 已在阶段 1~6 被处理器消费完毕，tick 结束后不应继续可见。
            _currentIntentsList = new List<Intent>();
            _isTicking = false;

            _tickCounter++;
        }

        public Entity? GetEntity(Id id) => _entities.TryGetValue(id, out var entity) ? entity : null;

        public IReadOnlyList<Entity> QueryEntities(EntityFilter filter)
        {
            var result = new List<Entity>();

            // _entities 是 SortedDictionary<Id, Entity>，遍历顺序已按 Id 序数升序，
            // 过滤后直接追加即得到按 EntityId 排序的结果，无需再次排序。
            foreach (var pair in _entities)
            {
                var entity = pair.Value;

                if (filter.Kind != null && !string.Equals(entity.Kind, filter.Kind, StringComparison.Ordinal))
                {
                    continue;
                }

                if (filter.MapId.HasValue && entity.MapId != filter.MapId.Value)
                {
                    continue;
                }

                if (filter.Predicate != null && !filter.Predicate(entity))
                {
                    continue;
                }

                result.Add(entity);
            }

            return result;
        }

        public void MarkForDestruction(Id id)
        {
            _pendingDestruction.Add(id);
        }

        public void AddEntity(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (_entities.ContainsKey(entity.EntityId))
            {
                throw new InvalidOperationException($"实体 id 重复：\"{entity.EntityId}\"");
            }

            entity.Lifecycle = EntityLifecycle.Active;
            _entities.Add(entity.EntityId, entity);

            var displayId = entity.TemplateId ?? entity.EntityId;
            _bus.Enqueue(new EntityCreatedEvent(entity.EntityId, entity.Kind, displayId));
        }

        public Id AllocateEntityId(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                throw new ArgumentException("kind 不能为空", nameof(kind));
            }

            var next = _idSequenceByKind.TryGetValue(kind, out var current) ? current + 1 : 1;
            _idSequenceByKind[kind] = next;
            return new Id($"{kind}.inst_{next}");
        }

        public void RegisterPhaseHandler(TickPhase phase, ITickPhaseHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (phase == TickPhase.EventDispatch || phase == TickPhase.LifecycleCleanup)
            {
                throw new ArgumentException(
                    $"阶段 \"{phase}\" 由 WorldSim 自己执行（事件派发/生命周期清理），不允许外部注册处理器",
                    nameof(phase));
            }

            if (!_phaseHandlers.TryGetValue(phase, out var list))
            {
                list = new List<ITickPhaseHandler>();
                _phaseHandlers[phase] = list;
            }

            list.Add(handler);
        }

        /// <summary>立即移除全部实体、清空计时器与待销毁列表（见 <see cref="IWorldSim.ClearAll"/>）。
        /// 按 <c>EntityId</c> 序数顺序 Enqueue，保持与 <see cref="Tick"/> 阶段 8"生命周期清理"
        /// 同样的确定性遍历顺序。</summary>
        public void ClearAll()
        {
            if (_entities.Count > 0)
            {
                // _entities 是 SortedDictionary<Id, Entity>，遍历顺序已按 Id 序数升序。
                foreach (var pair in _entities)
                {
                    _bus.Enqueue(new EntityDestroyedEvent(pair.Key));
                    pair.Value.Lifecycle = EntityLifecycle.Destroyed;
                }

                _entities.Clear();
            }

            _pendingDestruction.Clear();

            // 见构造函数上方字段注释（FND-04 收口）：原地清空同一个 SimTimers 实例，不再换实例，
            // 避免 TimerHandle 编号重新从 1 计数导致跨清空边界的句柄数值碰撞。
            _timers.Clear();
        }

        private void ExecutePhase(TickPhase phase, SimStep step)
        {
            if (!_phaseHandlers.TryGetValue(phase, out var list))
            {
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                list[i].Execute(step, this);
            }
        }
    }
}
