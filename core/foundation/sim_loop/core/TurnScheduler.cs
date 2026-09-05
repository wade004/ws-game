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
            IEventBus bus)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _initiativeStatProvider = initiativeStatProvider ?? throw new ArgumentNullException(nameof(initiativeStatProvider));
            _isPlayerActor = isPlayerActor ?? throw new ArgumentNullException(nameof(isPlayerActor));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
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

            if (_isPlayerActor(actor) && !_hasPendingIntentForCurrentActor)
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

            if (_policy == InitiativePolicy.ActionPoints)
            {
                _actionPointsRemaining[actorId] = 0.0;
            }

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

            if (_policy == InitiativePolicy.ActionPoints)
            {
                _actionPointsRemaining[id] = _actionPointsPerTurn;
            }
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
        /// 判断记录（仅 <see cref="InitiativePolicy.ActionPoints"/> 策略下才真正记账）：只有该策略
        /// 下 <see cref="_actionPointsRemaining"/> 才会在 <see cref="ResetActionPointsForRound"/> 里
        /// 按 <see cref="_actionPointsPerTurn"/> 初始化；其余策略（<c>initiative_stat</c>/
        /// <c>fixed_order</c>）下该账本恒空，"移动预算按行动点计"若与这两种先攻策略搭配使用，
        /// 本方法找不到任何行动点账目可扣，为避免"账本不存在"被误判为"预算已耗尽"（进而阻塞一切
        /// 移动），本方法在非 <see cref="InitiativePolicy.ActionPoints"/> 策略下恒返回
        /// <c>true</c>（不做任何记账，等价于"不限制"）——04/06 未规定
        /// <c>movement_budget_rule: action_points</c> 必须搭配
        /// <c>initiative_policy: action_points</c> 使用，但任务书原文"与 TurnScheduler 的
        /// action_points 策略共享同一预算"暗示两者应当配套，本类型按此拍板，不配套时退化为
        /// "不限制"而不是抛异常/静默拒绝一切移动。
        /// </para>
        /// </summary>
        public bool TryConsumeActionPoints(Id actorId, double amount)
        {
            if (!_inCombat || _policy != InitiativePolicy.ActionPoints)
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

        private void ResetActionPointsForRound()
        {
            _actionPointsRemaining.Clear();

            if (_policy == InitiativePolicy.ActionPoints)
            {
                for (var i = 0; i < _order.Count; i++)
                {
                    _actionPointsRemaining[_order[i]] = _actionPointsPerTurn;
                }
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
