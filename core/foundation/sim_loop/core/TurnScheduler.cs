using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="ITurnScheduler"/> 的默认实现（见 03_运行时骨架.md 第 3.2、9 节、ADR-0013 决策 3）。
    /// 只负责"按先攻规则维护本回合行动顺序、产生离散步序列、管理等待输入与回合/轮次边界"，
    /// 不知道任何具体游戏内容（技能、AI 优先级表等）——玩家判定经注入的 <c>isPlayerActor</c>
    /// 委托，先攻属性读取经注入的 <c>initiativeStatProvider</c> 委托（本模块不依赖 L1
    /// <c>Core.Numbers.StatBlock</c>，保持 L0 不向上依赖）。
    /// <para>
    /// 主循环驱动协议（由 <c>core/gameplay/assembly.TimeModelSwitch</c>/<c>GameplayAssembly.Advance</c>
    /// 承担，见二者判断记录）：<see cref="NextStep"/> 产出一个 <see cref="SimStep"/> 后，调用方须
    /// <c>world.Tick(step)</c>，随后调用 <see cref="NotifyStepConsumed"/> 让调度器决定是否推进到
    /// 下一行动者（<c>action_points</c> 策略下行动点未耗尽时继续同一行动者）。<see cref="NextStep"/>
    /// 本身不产生副作用（可安全重复调用而不推进状态），只有 <see cref="NotifyStepConsumed"/>/
    /// <see cref="EndTurn"/>/<see cref="SubmitIntent"/> 会改变内部状态。
    /// </para>
    /// </summary>
    public sealed class TurnScheduler : ITurnScheduler, IPersistable
    {
        /// <summary>存档段 key（见 10_存档与持久化.md 第 3 节固定段序步骤 7b、ADR-0013 补齐任务
        /// 勘误——与 <c>world.dropped_loot</c>/<c>world.vendor_stock</c> 等"世界附属段"同一惯例，
        /// 登记进固定段序的叙述顺序，但不登记进 <see cref="Core.Foundation.SaveSystem.SaveSections.KnownOrder"/>
        /// 这份全序数组，作为"自定义段"按 key 序数排在已知段之后，见该数组类型注释、本模块 README）。</summary>
        public const string SectionKeyConst = "sim.turn_state";

        private readonly IWorldSim _world;
        private readonly Func<Id, double> _initiativeStatProvider;
        private readonly Func<Id, bool> _isPlayerActor;
        private readonly IEventBus _bus;

        // H4 补齐（读条跨回合，见 SkillHost.AdvanceCastForActor/CastPipeline.AdvanceOne 判断记录）：
        // 可选委托，供调用方（GameplayAssembly）告知"该行动者当前是否正忙于一个跨越多轮的动作
        // （读条/引导）"——为 true 时 NextStep 对玩家行动者也照常产步，不等待新的输入意图（该行动者
        // 本回合没有新选择要做，只是继续上一次已经开始的动作）。未传入（null）时行为与此前完全
        // 一致：玩家行动者恒等待新意图。不新增 03 文档之外的必需契约——本参数为可选（默认 null），
        // 现有调用方不受影响。
        private readonly Func<Id, bool>? _isBusyContinuing;

        private InitiativePolicy _policy = InitiativePolicy.FixedOrder;
        private double _actionPointsPerTurn = 1.0;
        private bool _resortEachRound;

        private List<Id> _order = new List<Id>();
        private int _currentIndex = -1;
        private bool _inCombat;
        private int _roundIndex;
        private readonly Dictionary<Id, double> _actionPointsRemaining = new Dictionary<Id, double>();

        private bool _hasPendingIntentForCurrentActor;
        private bool _awaitingInputSignaled;

        public TurnScheduler(
            IWorldSim world,
            Func<Id, double> initiativeStatProvider,
            Func<Id, bool> isPlayerActor,
            IEventBus bus,
            Func<Id, bool>? isBusyContinuing = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _initiativeStatProvider = initiativeStatProvider ?? throw new ArgumentNullException(nameof(initiativeStatProvider));
            _isPlayerActor = isPlayerActor ?? throw new ArgumentNullException(nameof(isPlayerActor));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _isBusyContinuing = isBusyContinuing;
        }

        public string SectionKey => SectionKeyConst;

        public void Configure(InitiativePolicy policy, IReadOnlyDictionary<string, object> parameters)
        {
            if (policy == InitiativePolicy.Atb)
            {
                throw new NotSupportedException(
                    "initiative_policy=atb 是预留扩展位，本版不展开（见 ADR-0013 决策第 3 条）");
            }

            _policy = policy;
            _actionPointsPerTurn = 1.0;
            _resortEachRound = false;

            if (parameters != null)
            {
                if (parameters.TryGetValue("action_points_per_turn", out var apRaw))
                {
                    _actionPointsPerTurn = Convert.ToDouble(apRaw);
                }

                if (parameters.TryGetValue("resort_each_round", out var resortRaw) && resortRaw is bool resortValue)
                {
                    _resortEachRound = resortValue;
                }
            }
        }

        public void BeginCombat(IReadOnlyList<Id> participants)
        {
            if (participants == null || participants.Count == 0)
            {
                throw new ArgumentException("participants 不能为空", nameof(participants));
            }

            _order = OrderParticipants(participants);
            _currentIndex = 0;
            _roundIndex = 0;
            _inCombat = true;
            _hasPendingIntentForCurrentActor = false;
            _awaitingInputSignaled = false;
            ResetActionPointsForRound();

            _bus.PublishImmediate(new SimTurnStartedEvent(_order[0], _roundIndex));
        }

        public void EndCombat()
        {
            _inCombat = false;
            _order = new List<Id>();
            _currentIndex = -1;
            _actionPointsRemaining.Clear();
            _hasPendingIntentForCurrentActor = false;
            _awaitingInputSignaled = false;
        }

        public SimStep? NextStep()
        {
            if (!_inCombat || _order.Count == 0)
            {
                return null;
            }

            var actor = _order[_currentIndex];

            // H4 补齐（读条跨回合，见 _isBusyContinuing 字段判断记录）：该行动者正忙于一个跨越
            // 多轮的动作时，即便是玩家、即便本回合还没有新的待处理意图，也照常产步——本回合对他
            // 而言没有新选择要做，只是继续上一次已经开始的动作（典型：读条 2 回合的技能，在其
            // 第 2 个己方回合自动继续，不应卡在 awaiting_input 等一个不存在的新意图）。
            var isBusy = _isBusyContinuing != null && _isBusyContinuing(actor);

            if (_isPlayerActor(actor) && !_hasPendingIntentForCurrentActor && !isBusy)
            {
                if (!_awaitingInputSignaled)
                {
                    _awaitingInputSignaled = true;
                    _bus.PublishImmediate(new SimAwaitingInputEvent(actor));
                }

                return null;
            }

            return SimStep.Discrete(actor, StepPhase.Act);
        }

        /// <summary>
        /// H4 补齐（意图路由缺口 1）：供 <see cref="WorldSim.SubmitIntent"/> 在 Discrete 模式下
        /// 路由外部（通常是玩家 UI，经 <c>CastSkill</c>/<c>MovementHost.Request</c> 等既有调用链）
        /// 提交的意图使用——只做"是否为当前等待输入的行动者"校验 + 与
        /// <see cref="SubmitIntent"/> 相同的记账（清除 <c>awaiting_input</c> 标志、标记本行动者
        /// 已有待处理意图），<b>不</b>再调用 <see cref="IWorldSim.SubmitIntent"/>——真正把
        /// <see cref="Intent"/> 加入 world 待处理队列的操作留给调用方（<see cref="WorldSim"/>）自己
        /// 做，避免与调用方 <c>WorldSim.SubmitIntent</c> 之间产生递归调用。返回 <c>true</c> 表示
        /// <paramref name="actorId"/> 确实是当前等待输入的行动者（调用方应正常入队）；返回
        /// <c>false</c> 表示不是（不在战斗中，或不是当前行动者）——调用方应拒绝，不入队、不产生
        /// 离散步。<see cref="SubmitIntent"/>（既有的、供直接持有 <see cref="ITurnScheduler"/> 的
        /// 调用方/测试使用的入口）不受影响，仍是原来的"校验 + 记账 + 调用 world.SubmitIntent"，
        /// 与本方法各自独立、互不依赖。
        /// </summary>
        public bool TryAcceptExternalIntent(Id actorId)
        {
            if (!_inCombat)
            {
                return false;
            }

            var current = GetCurrentActor();
            if (current == null || !current.Value.Equals(actorId))
            {
                return false;
            }

            _hasPendingIntentForCurrentActor = true;
            _awaitingInputSignaled = false;
            return true;
        }

        public void SubmitIntent(Id actorId, Intent intent)
        {
            if (!_inCombat)
            {
                throw new InvalidOperationException("SubmitIntent 只能在战斗进行中（BeginCombat 之后、EndCombat 之前）调用");
            }

            var current = GetCurrentActor();
            if (current == null || !current.Value.Equals(actorId))
            {
                throw new ArgumentException(
                    $"只能为当前行动者提交意图：当前行动者是 \"{current}\"，收到 \"{actorId}\"", nameof(actorId));
            }

            if (!intent.ActorId.Equals(actorId))
            {
                throw new ArgumentException("intent.ActorId 必须与 actorId 一致", nameof(intent));
            }

            _world.SubmitIntent(intent);
            _hasPendingIntentForCurrentActor = true;
            _awaitingInputSignaled = false;
        }

        /// <summary>
        /// 由主循环在 <c>world.Tick(step)</c> 完成后调用（见类型顶部"主循环驱动协议"），决定是否
        /// 推进到下一行动者：<c>action_points</c> 策略下扣 1 点行动点，未耗尽则继续同一行动者
        /// （<see cref="NextStep"/> 下次调用仍返回同一 actor 的新一步）；其余策略每步即一整个回合，
        /// 立即推进（见 ADR-0013 决策 3"action_points（行动点耗尽即结束回合）"，
        /// <c>initiative_stat</c>/<c>fixed_order</c> 两策略未提及"单回合多行动"，按一步一回合处理，
        /// 见本模块 README 判断记录）。<paramref name="actorId"/> 与当前行动者不一致时空操作
        /// （防御性，覆盖调用方误用/竞态）。
        /// </summary>
        public void NotifyStepConsumed(Id actorId)
        {
            if (!_inCombat)
            {
                return;
            }

            var current = GetCurrentActor();
            if (current == null || !current.Value.Equals(actorId))
            {
                return;
            }

            _hasPendingIntentForCurrentActor = false;

            if (_policy == InitiativePolicy.ActionPoints)
            {
                var remaining = (_actionPointsRemaining.TryGetValue(actorId, out var r) ? r : 0.0) - 1.0;
                _actionPointsRemaining[actorId] = remaining;

                if (remaining > 0)
                {
                    return; // 行动点未耗尽，继续同一行动者。
                }
            }

            AdvanceToNextActor();
        }

        public void EndTurn(Id actorId)
        {
            if (!_inCombat)
            {
                return;
            }

            var current = GetCurrentActor();
            if (current == null || !current.Value.Equals(actorId))
            {
                return;
            }

            // 收边任务修正：与 ResetActionPointsForRound 同一判断记录，不再按 _policy 分支——
            // 无条件清零移动预算账本（该行动者本回合结束，不管是被先攻策略本身的行动点耗尽推进，
            // 还是被移动预算的行动点耗尽拒绝移动后调用本方法，遗留的账目都不应该带到下一次
            // GetOrder 里这个位置的新占用者身上；下一轮 ResetActionPointsForRound 会重新分配）。
            _actionPointsRemaining[actorId] = 0.0;

            AdvanceToNextActor();
        }

        /// <summary>
        /// 中途加入本轮战斗（见 03 第 3.3 节步骤 1 判断记录、ADR-0013 补齐任务拍板：
        /// <c>Core.Gameplay.Assembly.TimeModelSwitch</c> 在离散模式中收到 <c>combat.entered</c> 时
        /// 调用）。不改变已经行动过的顺序，只在"当前轮尚未行动的序列"（<c>_currentIndex</c> 之后，
        /// 不含正在行动的位置本身）里插入：<see cref="InitiativePolicy.InitiativeStat"/>/
        /// <see cref="InitiativePolicy.ActionPoints"/> 两种策略按先攻值降序插入（同值按 Id 序数，
        /// 同 <see cref="OrderParticipants"/> 的排序规则；<c>action_points</c> 策略本身不按先攻值
        /// 排序初始顺序，但插入新参与者时仍按先攻值定位——04/06 均未规定 <c>action_points</c> 策略
        /// 下"插入点"该如何决定，任务书明确拍板"按先攻值插入"，本方法按此实现），
        /// <see cref="InitiativePolicy.FixedOrder"/> 固定追加到整个 <see cref="_order"/> 末尾
        /// （任务书原文）。<see cref="InitiativePolicy.ActionPoints"/> 策略额外给新参与者分配本轮
        /// 满额行动点（不分配会被 <see cref="NotifyStepConsumed"/> 当成"已耗尽"，见该方法字典
        /// 缺省值处理）。不在战斗中，或该 id 已在 <see cref="GetOrder"/> 里时是空操作（幂等：
        /// <c>combat.entered</c> 在同一单位身上可能因 <c>CombatHost.NotifyCombatEvent</c> 的调用
        /// 路径重复触发，见该方法"若已在战斗中直接返回"以外的场景）。
        /// </summary>
        public void AddParticipant(Id id)
        {
            if (!_inCombat)
            {
                return;
            }

            if (_order.Contains(id))
            {
                return;
            }

            if (_policy == InitiativePolicy.FixedOrder)
            {
                _order.Add(id);

                // FND-05 收口：此前这里在给 fixed_order 新参与者定好插入位置后就直接 return，
                // 跳过了下面（原本给 initiative_stat/action_points 两个分支共用的）行动点账本
                // 初始化——本方法类型注释与 TryConsumeActionPoints 判断记录都已经说清楚"移动预算
                // 账本的存在与否不该由先攻策略决定，无条件为全部参与者维护"，fixed_order 分支
                // 却因为提前 return 单独漏掉了这一步，导致中途加入的单位本轮 GetActionPointsRemaining
                // 恒为 0、TryConsumeActionPoints 恒返回 false（见外部审核 FND-05、
                // validation-repros.txt R5）。三种策略现在都会走到下面这一行。
                _actionPointsRemaining[id] = _actionPointsPerTurn;
                return;
            }

            var insertAt = _order.Count;
            var newValue = _initiativeStatProvider(id);
            for (var i = _currentIndex + 1; i < _order.Count; i++)
            {
                var existingValue = _initiativeStatProvider(_order[i]);
                var cmp = newValue.CompareTo(existingValue);
                if (cmp > 0 || (cmp == 0 && string.CompareOrdinal(id.Value, _order[i].Value) < 0))
                {
                    insertAt = i;
                    break;
                }
            }

            _order.Insert(insertAt, id);

            // 收边任务修正：与 ResetActionPointsForRound 同一判断记录——移动预算账本的存在与否
            // 不该由先攻策略决定，中途加入的参与者无条件分配本轮满额行动点（不分配会被
            // TryConsumeActionPoints 当成"已耗尽"，也会被 NotifyStepConsumed 在 action_points
            // 先攻策略下当成"已耗尽"，见该方法字典缺省值处理）。
            _actionPointsRemaining[id] = _actionPointsPerTurn;
        }

        /// <summary>
        /// 死亡/离场移除（见 03 第 3.3 节步骤 1 判断记录、ADR-0013 补齐任务拍板：
        /// <c>Core.Gameplay.Assembly.TimeModelSwitch</c> 在离散模式中收到 <c>unit.died</c> 时
        /// 调用）。不在 <see cref="GetOrder"/> 里、或不在战斗中时是空操作。移除位置在
        /// <see cref="_currentIndex"/> 之前时，<see cref="_currentIndex"/> 相应减一以继续指向同一个
        /// 当前行动者；移除的正是当前行动者本人时（如死于自己回合内的反伤/DoT），清空该位置遗留的
        /// "待提交意图/等待输入已发出"标志——这两个标志属于被移除的旧占用者，<see cref="_currentIndex"/>
        /// 位置移位后由新占用者顶替，不能沿用旧标志（否则 <see cref="NextStep"/> 可能把旧标志误用到
        /// 新占用者身上）；若该位置恰好是本轮最后一位，按"轮结束"处理（同
        /// <see cref="AdvanceToNextActor"/> 回绕逻辑），但不为被移除者发 <c>sim.turn_ended</c>
        /// ——它没有正常结束自己的回合。移除后 <see cref="_order"/> 变空时清空当前行动者指针（整场
        /// 战斗是否结束由调用方经 <see cref="EndCombat"/> 另行决定，本方法不自作主张调用它）。
        /// </summary>
        public void RemoveParticipant(Id id)
        {
            if (!_inCombat)
            {
                return;
            }

            var index = _order.IndexOf(id);
            if (index < 0)
            {
                return;
            }

            _order.RemoveAt(index);
            _actionPointsRemaining.Remove(id);

            if (_order.Count == 0)
            {
                _currentIndex = -1;
                _hasPendingIntentForCurrentActor = false;
                _awaitingInputSignaled = false;
                return;
            }

            if (index < _currentIndex)
            {
                _currentIndex--;
                return;
            }

            if (index > _currentIndex)
            {
                return;
            }

            // index == _currentIndex：被移除的正是当前行动者。
            _hasPendingIntentForCurrentActor = false;
            _awaitingInputSignaled = false;

            if (_currentIndex >= _order.Count)
            {
                _bus.PublishImmediate(new SimRoundEndedEvent(_roundIndex));
                _roundIndex++;

                if (_resortEachRound)
                {
                    _order = OrderParticipants(_order);
                }

                _currentIndex = 0;
                ResetActionPointsForRound();
            }

            _bus.PublishImmediate(new SimTurnStartedEvent(_order[_currentIndex], _roundIndex));
        }

        /// <summary>
        /// 供离散模式下"移动预算按行动点计"（04 第 3.1 节勘误
        /// <c>movement_budget_rule: action_points</c>，见 <c>Core.Carriers.Unit.MovementTickHandler</c>
        /// 判断记录）等其它系统尝试扣减 <paramref name="actorId"/> 剩余行动点：与
        /// <see cref="NotifyStepConsumed"/> 读写同一份 <see cref="_actionPointsRemaining"/> 账本
        /// （任务书拍板"与 TurnScheduler 的 action_points 策略共享同一预算"），但不像
        /// <see cref="NotifyStepConsumed"/> 那样在耗尽时自动推进到下一行动者——调用方（本方法的
        /// 消费者）自行决定耗尽后做什么（通常是拒绝本次意图 + 调用 <see cref="EndTurn"/>，见
        /// <c>MovementTickHandler</c> 判断记录），本方法只负责记账。
        /// <para>
        /// 判断记录（收边任务修正：不再按先攻策略分支——先攻策略只决定顺序）：04 第 3.1 节字段表
        /// <c>action_points_per_turn</c> 行原文"<c>initiative_policy</c> 或 <c>movement_budget_rule</c>
        /// 任一为 <c>action_points</c> 时，两者共享同一份每回合行动点总额度"，本就写清楚"任一"，
        /// 与"先攻策略是否为 <c>action_points</c>"无关。此前实现把本方法的记账门槛误绑定到
        /// <see cref="InitiativePolicy.ActionPoints"/> 先攻策略（<c>initiative_stat</c>/
        /// <c>fixed_order</c> 搭配 <c>movement_budget_rule: action_points</c> 时账本恒空、本方法
        /// 恒返回 true 变相"不限制"），按 12 第 5 节"实现错、文档已经是对的"处理规则直接修订实现：
        /// <see cref="ResetActionPointsForRound"/>/<see cref="AddParticipant"/> 现在无条件为全部
        /// 参与者维护 <see cref="_actionPointsRemaining"/> 账本（不再按 <see cref="_policy"/> 分支），
        /// 本方法因此只需检查 <see cref="_inCombat"/> 即可正确记账，不区分先攻策略。
        /// </para>
        /// </summary>
        public bool TryConsumeActionPoints(Id actorId, double amount)
        {
            if (!_inCombat)
            {
                return true;
            }

            var remaining = _actionPointsRemaining.TryGetValue(actorId, out var r) ? r : 0.0;
            if (remaining < amount)
            {
                return false;
            }

            _actionPointsRemaining[actorId] = remaining - amount;
            return true;
        }

        public IReadOnlyList<Id> GetOrder() => _order;

        /// <summary>
        /// GP-PRES-09 收口新增（09 第 7.1 节"行动点显示"）：某参与者本轮剩余行动点的正式只读查询，
        /// 供 <c>Presentation.Ui.HudViewModel</c> 一类只读消费方使用——此前 <see cref="_actionPointsRemaining"/>
        /// 只能经 <see cref="TryConsumeActionPoints"/>（写操作，会真的扣减）间接探测，没有不产生
        /// 副作用的读接口。语义与 <see cref="TryConsumeActionPoints"/> 判断记录一致：账本现在无条件
        /// 为全部参与者维护（不区分先攻策略），<paramref name="actorId"/> 不在账本里（未参战/已经
        /// 脱战）时返回 0，不抛异常——0 与"账本里显式记着 0"在展示语义上没有区别，调用方不需要额外
        /// 判空。
        /// </summary>
        public double GetActionPointsRemaining(Id actorId) =>
            _actionPointsRemaining.TryGetValue(actorId, out var remaining) ? remaining : 0.0;

        public Id? GetCurrentActor() =>
            _inCombat && _currentIndex >= 0 && _currentIndex < _order.Count ? _order[_currentIndex] : (Id?)null;

        /// <summary>当前所处轮次（从 0 起，供存档/调试/Expr <c>time.round_index</c> 读取）。</summary>
        public int RoundIndex => _roundIndex;

        /// <summary>当前行动者在 <see cref="GetOrder"/> 中的下标（从 0 起）；不在战斗中返回 -1
        /// （供 Expr <c>time.turn_index</c> 读取）。</summary>
        public int CurrentTurnIndex => _currentIndex;

        private void AdvanceToNextActor()
        {
            var endedActor = _order[_currentIndex];
            _bus.PublishImmediate(new SimTurnEndedEvent(endedActor));

            _currentIndex++;
            _hasPendingIntentForCurrentActor = false;
            _awaitingInputSignaled = false;

            if (_currentIndex >= _order.Count)
            {
                _bus.PublishImmediate(new SimRoundEndedEvent(_roundIndex));
                _roundIndex++;

                if (_resortEachRound)
                {
                    _order = OrderParticipants(_order);
                }

                _currentIndex = 0;
                ResetActionPointsForRound();
            }

            _bus.PublishImmediate(new SimTurnStartedEvent(_order[_currentIndex], _roundIndex));
        }

        private List<Id> OrderParticipants(IReadOnlyList<Id> participants)
        {
            var list = new List<Id>(participants);

            if (_policy == InitiativePolicy.InitiativeStat)
            {
                // 按先攻属性降序排序，同值按 Id 字典序（Ordinal）升序，保证稳定确定性（见任务书）。
                list.Sort((a, b) =>
                {
                    var cmp = _initiativeStatProvider(b).CompareTo(_initiativeStatProvider(a));
                    return cmp != 0 ? cmp : string.CompareOrdinal(a.Value, b.Value);
                });
            }

            // action_points/fixed_order：沿用传入顺序（fixed_order 定义如此；04 未规定
            // action_points 策略的排序规则，本实现按传入顺序处理，见本模块 README 判断记录）。
            return list;
        }

        /// <summary>
        /// 收边任务修正（04 第 3.1 节字段表 <c>action_points_per_turn</c> 行原文"<c>initiative_policy</c>
        /// 或 <c>movement_budget_rule</c> 任一为 <c>action_points</c> 时，两者共享同一份每回合行动点
        /// 总额度"——文档本就写清楚"任一"，此前实现在此处误收窄成"仅 <see cref="InitiativePolicy.ActionPoints"/>
        /// 策略下才初始化账本"，导致 <c>movement_budget_rule: action_points</c> 搭配
        /// <c>initiative_stat</c>/<c>fixed_order</c> 先攻策略时 <see cref="TryConsumeActionPoints"/>
        /// 找不到账本、被迫退化为"不限制"，这正是 12 第 5 节"实现错，文档已经是对的"的情形，按该节
        /// 处理规则直接修订实现，不改文档）：不再按 <see cref="_policy"/> 分支，每轮开始时无条件
        /// 给全部参与者按 <see cref="_actionPointsPerTurn"/> 初始化账本——先攻策略只决定行动顺序，
        /// 不决定"是否记账"；<see cref="NotifyStepConsumed"/>/<see cref="EndTurn"/> 里"是否按行动点
        /// 耗尽与否推进到下一行动者"这条判断仍然按 <see cref="_policy"/> 分支（那是先攻策略本身的
        /// 语义，与移动预算账本是否存在是两回事，见 <see cref="TryConsumeActionPoints"/> 判断记录）。
        /// </summary>
        private void ResetActionPointsForRound()
        {
            _actionPointsRemaining.Clear();

            for (var i = 0; i < _order.Count; i++)
            {
                _actionPointsRemaining[_order[i]] = _actionPointsPerTurn;
            }
        }

        // -----------------------------------------------------------------
        // IPersistable：段名 sim.turn_state（见类型顶部判断记录，10 号文档未定义，待勘误）。
        // -----------------------------------------------------------------

        public JsonValue Save()
        {
            var orderArray = new List<JsonValue>(_order.Count);
            for (var i = 0; i < _order.Count; i++)
            {
                orderArray.Add(new JsonString(_order[i].Value));
            }

            var apBuilder = new JsonObjectBuilder();
            foreach (var pair in _actionPointsRemaining)
            {
                apBuilder.Add(pair.Key.Value, new JsonNumber(pair.Value));
            }

            return new JsonObjectBuilder()
                .Add("in_combat", JsonBool.Of(_inCombat))
                .Add("policy", new JsonString(_policy.ToString()))
                .Add("action_points_per_turn", new JsonNumber(_actionPointsPerTurn))
                .Add("resort_each_round", JsonBool.Of(_resortEachRound))
                .Add("order", new JsonArray(orderArray))
                .Add("current_index", new JsonNumber(_currentIndex))
                .Add("round_index", new JsonNumber(_roundIndex))
                .Add("action_points_remaining", apBuilder.Build())
                .Build();
        }

        public void Load(JsonValue data)
        {
            if (data is JsonNull)
            {
                EndCombat();
                return;
            }

            if (!(data is JsonObject obj))
            {
                throw new FormatException($"{SectionKeyConst} 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            _inCombat = obj.TryGetValue("in_combat", out var inCombatValue) && inCombatValue is JsonBool b && b.Value;
            _policy = obj.TryGetValue("policy", out var policyValue) && policyValue is JsonString ps
                ? Enum.Parse<InitiativePolicy>(ps.Value)
                : InitiativePolicy.FixedOrder;
            _actionPointsPerTurn = obj.TryGetValue("action_points_per_turn", out var apRaw) && apRaw is JsonNumber apn ? apn.Value : 1.0;
            _resortEachRound = obj.TryGetValue("resort_each_round", out var resortRaw) && resortRaw is JsonBool rb && rb.Value;

            _order = new List<Id>();
            if (obj.TryGetValue("order", out var orderRaw) && orderRaw is JsonArray orderArr)
            {
                foreach (var v in orderArr)
                {
                    if (v is JsonString s)
                    {
                        _order.Add(new Id(s.Value));
                    }
                }
            }

            _currentIndex = obj.TryGetValue("current_index", out var idxRaw) && idxRaw is JsonNumber idxN ? (int)idxN.Value : -1;
            _roundIndex = obj.TryGetValue("round_index", out var roundRaw) && roundRaw is JsonNumber roundN ? (int)roundN.Value : 0;

            _actionPointsRemaining.Clear();
            if (obj.TryGetValue("action_points_remaining", out var apRemainingRaw) && apRemainingRaw is JsonObject apObj)
            {
                foreach (var pair in apObj)
                {
                    if (pair.Value is JsonNumber n)
                    {
                        _actionPointsRemaining[new Id(pair.Key)] = n.Value;
                    }
                }
            }

            _hasPendingIntentForCurrentActor = false;
            _awaitingInputSignaled = false;
        }
    }
}
