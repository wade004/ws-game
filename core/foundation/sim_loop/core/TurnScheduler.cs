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
        /// <summary>存档段 key（见 10_存档与持久化.md——该文档未定义本段，落地方案约定用此 key，
        /// 属于待勘误项，见本模块 README）。</summary>
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
