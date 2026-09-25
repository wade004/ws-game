using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Rules.Common;
using Core.Rules.ExprHost;

namespace Core.Rules.Ai
{
    /// <summary>
    /// <see cref="IAiHost"/> 的默认实现：行为外壳状态机 + 优先级表（见 06_规则层_属性技能战斗AI.md
    /// 第 6 节）。构造期从 <see cref="IDataRegistryView"/> 一次性读取 <c>ai.behavior_profile</c>/
    /// <c>ai.rotation</c>/<c>ai.patrol_path</c> 三张表并解析（含 Expr 文本一次性解析，集成任务前
    /// 用本模块自带的临时占位 schema（阶段 3 整理后已删除），集成任务后默认改用
    /// <see cref="RulesExprSchema"/>，见构造函数 <c>exprSchema</c> 参数），之后只读，与
    /// <see cref="FactionMatrix"/> 的做法一致。
    /// <para>
    /// 除 <see cref="IAiHost"/> 声明的四个方法外，本类另外公开 <see cref="RegisterUnit"/>/
    /// <see cref="UnregisterUnit"/>/<see cref="Step"/>/<see cref="RegisteredUnitIds"/>——
    /// 这些不属于 06 §6.5/7 的抽象契约签名，是任务书拍板要求的"内部驱动版本"配套 API，供
    /// <see cref="AiTickHandler"/> 与遭遇脚本/测试直接调用（见本模块 README）。
    /// </para>
    /// </summary>
    public sealed class AiHost : IAiHost
    {
        private const string TransitionIdleToChase = "idle_to_chase";
        private const string TransitionChaseToCombat = "chase_to_combat";
        private const string TransitionChaseToReturn = "chase_to_return";
        private const string TransitionCombatToChase = "combat_to_chase";
        private const string TransitionCombatToReturn = "combat_to_return";
        private const string TransitionCombatToFlee = "combat_to_flee";
        private const string TransitionReturnToIdle = "return_to_idle";
        private const string TransitionReturnToPatrol = "return_to_patrol";
        private const string TransitionFleeToReturn = "flee_to_return";
        private const string TransitionFleeToCombat = "flee_to_combat";

        private readonly IUnitAccess _units;
        private readonly IFactionMatrix _factions;
        private readonly IPowerHost _powers;
        private readonly ISpatialQuery _spatialQuery;
        private readonly INavigation2D? _navigation;
        private readonly ISkillHost _skillHost;
        private readonly IThreatTable _threatTable;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly IEventBus _bus;
        private readonly IRngHost _rng;
        private readonly AiOptions _options;
        private readonly ICombatHost? _combatHost;
        private readonly IExprDiagnostics _exprDiagnostics = new ExprDiagnosticsRecorder();

        /// <summary>集成任务改动：解析 <c>ai.behavior_profile.transitions</c>（本类自己解析）与
        /// <c>ai.rotation.entries[].condition</c>（T-N3-10 起转交构造期传给 <see
        /// cref="RotationEvaluator"/>，见 <see cref="_rotationEvaluator"/>）用的 <see
        /// cref="IExprSchema"/>，默认 <see cref="RulesExprSchema.Base"/>（保留可注入口子，见构造函数
        /// <c>exprSchema</c> 参数与 <see cref="AiContentValidationRule"/> 同一惯例）。</summary>
        private readonly IExprSchema _exprSchema;

        private readonly Dictionary<string, AiBehaviorProfile> _profiles = new Dictionary<string, AiBehaviorProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, AiPatrolPath> _patrolPaths = new Dictionary<string, AiPatrolPath>(StringComparer.Ordinal);

        /// <summary>T-N3-10：优先级表求值抽成独立组件（见 <see cref="IRotationEvaluator"/>/
        /// <see cref="RotationEvaluator"/> 判断记录）——本类只在 <see cref="Evaluate"/> 里按
        /// <see cref="BehaviorState.Combat"/> 态门控委托给它，不再自己持有编译后的 Rotation 缓存。</summary>
        private readonly IRotationEvaluator _rotationEvaluator;

        // SortedDictionary 保证按 Id 序数遍历，供 RegisteredUnitIds / AiTickHandler 确定性遍历。
        private readonly SortedDictionary<string, AiUnitState> _states = new SortedDictionary<string, AiUnitState>(StringComparer.Ordinal);

        /// <summary>ADR-0087：保留 1.69.0 及之前的公开构造签名（二进制兼容——可选参数是编译期糖，
        /// 给公开构造函数追加可选参数会改变物理签名，让只见过旧签名的已编译消费方在运行期抛
        /// <c>MissingMethodException</c>；发布 ABI 探针会拦下）。转调下方带 <see cref="ICombatHost"/>
        /// 的完整重载，行为与本次改动之前完全一致。</summary>
        public AiHost(
            IDataRegistryView registry,
            IUnitAccess units,
            IFactionMatrix factions,
            IPowerHost powers,
            ISpatialQuery spatialQuery,
            ISkillHost skillHost,
            IThreatTable threatTable,
            IExprHostFactory exprHostFactory,
            IEventBus bus,
            IRngHost rng,
            INavigation2D? navigation,
            AiOptions? options,
            IExprSchema? exprSchema)
            : this(registry, units, factions, powers, spatialQuery, skillHost, threatTable, exprHostFactory,
                bus, rng, navigation, options, exprSchema, combatHost: null)
        {
        }

        public AiHost(
            IDataRegistryView registry,
            IUnitAccess units,
            IFactionMatrix factions,
            IPowerHost powers,
            ISpatialQuery spatialQuery,
            ISkillHost skillHost,
            IThreatTable threatTable,
            IExprHostFactory exprHostFactory,
            IEventBus bus,
            IRngHost rng,
            INavigation2D? navigation = null,
            AiOptions? options = null,
            IExprSchema? exprSchema = null,
            ICombatHost? combatHost = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _factions = factions ?? throw new ArgumentNullException(nameof(factions));
            _powers = powers ?? throw new ArgumentNullException(nameof(powers));
            _spatialQuery = spatialQuery ?? throw new ArgumentNullException(nameof(spatialQuery));
            _skillHost = skillHost ?? throw new ArgumentNullException(nameof(skillHost));
            _threatTable = threatTable ?? throw new ArgumentNullException(nameof(threatTable));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _navigation = navigation;
            _options = options ?? new AiOptions();
            // ADR-0087：可选依赖，供 idle/patrol 态默认转移候选集判定"单位是否已经在战"（见
            // HandleIdleOrPatrol/FindIdleChaseCandidate 判断记录）；为 null（既有调用方，如各模块
            // 测试假实现）时该分支恒不触发，行为与本次改动之前完全一致——生产装配
            // （RulesAssembly）已改为传入真实 CombatHost。
            _combatHost = combatHost;
            // ADR-0084：CombatReentryRangeRatio 越界（<= 0 或 > 1）会让 chase_to_combat 判定退化为
            // 永远无法满足或超过攻击距离本身，两种情形都破坏"进出点之间隔着余量"的滞回设计——按任务
            // 书拍板做合法性校验，越界直接抛异常，不静默夹紧。
            if (_options.CombatReentryRangeRatio <= 0.0 || _options.CombatReentryRangeRatio > 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    _options.CombatReentryRangeRatio,
                    "AiOptions.CombatReentryRangeRatio 必须落在 (0, 1] 区间内");
            }
            _exprSchema = exprSchema ?? RulesExprSchema.Base;

            LoadPatrolPaths(registry);
            // T-N3-10：Rotation 编译与求值移交 RotationEvaluator（见该类型判断记录"无状态保证"）——
            // 用与本类一致的 registry/skillHost/exprHostFactory/exprSchema 构造，行为逐位不变。
            _rotationEvaluator = new RotationEvaluator(registry, skillHost, exprHostFactory, _exprSchema);
            LoadProfiles(registry);

            // 场景卸载级联清理（见 core/carriers/assembly/README.md、ADR-0016 决策 7 背景一节
            // 联动发现的既有缺口）：IWorldSim.ClearAll/单个实体销毁都会（分别经批量或逐条）派发
            // entity.destroyed；本类此前只能通过显式 UnregisterUnit 移除登记，场景整体卸载时无人
            // 调用它，导致 _states 残留已销毁单位的行为外壳状态——下次 AiTickHandler 推进到这些
            // 残留 id 时，Step 内 _units.IsAlive(unitId) 会因实体已不存在而抛
            // InvalidOperationException（WorldUnitAccess.Require）。订阅 entity.destroyed 静默移除
            // 对应登记（找不到对应登记时无操作，不等价于公开的 UnregisterUnit——后者找不到会抛异常，
            // 语义是"调用方明知已注册、要求显式注销"，本订阅是被动清理，不应该因为"这个销毁的实体
            // 本来就不是已注册单位"而抛异常）。
            _bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, evt => _states.Remove(evt.EntityId.Value));
        }

        /// <summary>全部已注册单位，按 <see cref="Id"/> 序数排序（供 <see cref="AiTickHandler"/>
        /// 确定性遍历）。</summary>
        public IReadOnlyList<Id> RegisteredUnitIds
        {
            get
            {
                var list = new List<Id>(_states.Count);
                foreach (var key in _states.Keys)
                {
                    list.Add(new Id(key));
                }
                return list;
            }
        }

        // -----------------------------------------------------------------
        // 注册
        // -----------------------------------------------------------------

        public void RegisterUnit(Id unitId, Id profileId, Vec2 spawnPoint)
        {
            if (_states.ContainsKey(unitId.Value))
            {
                throw new InvalidOperationException($"单位 \"{unitId}\" 已注册过 AI 行为外壳");
            }

            if (!_profiles.TryGetValue(profileId.Value, out var profile))
            {
                throw new ArgumentException($"未知的 ai.behavior_profile \"{profileId}\"", nameof(profileId));
            }

            var initialState = profile.PatrolPathRef.HasValue ? BehaviorState.Patrol : BehaviorState.Idle;

            _states[unitId.Value] = new AiUnitState
            {
                State = initialState,
                ProfileId = profileId,
                RotationId = profile.RotationRef,
                Target = null,
                SpawnPoint = spawnPoint,
                PatrolIndex = 0,
                PatrolDir = 1,
                DecisionAccumulator = 0.0,
            };
        }

        public void UnregisterUnit(Id unitId)
        {
            if (!_states.Remove(unitId.Value))
            {
                throw new InvalidOperationException($"单位 \"{unitId}\" 未注册 AI 行为外壳，无法注销");
            }
        }

        // -----------------------------------------------------------------
        // IAiHost
        // -----------------------------------------------------------------

        public BehaviorState GetBehaviorState(Id unitId) => GetState(unitId).State;

        public void ForceState(Id unitId, BehaviorState state)
        {
            var s = GetState(unitId);
            TransitionTo(unitId, s, state);
        }

        public void SetRotation(Id unitId, Id rotationId)
        {
            var s = GetState(unitId);
            if (!_rotationEvaluator.HasRotation(rotationId))
            {
                throw new ArgumentException($"未知的 ai.rotation \"{rotationId}\"", nameof(rotationId));
            }

            s.RotationId = rotationId;
        }

        /// <summary>供测试/调试观察当前追击/交战目标；未注册单位抛异常，无目标返回 null。</summary>
        public Id? GetTarget(Id unitId) => GetState(unitId).Target;

        public SkillCastRequest? Evaluate(Id unitId)
        {
            var state = GetState(unitId);
            if (state.State != BehaviorState.Combat)
            {
                return null;
            }

            // T-N3-10：选技能逻辑委托给 RotationEvaluator（见该类型判断记录）——本类只保留
            // "行为档/战斗态门控"（上面的 Combat 态判断）与"选中后发 ai.decision_made 事件"两件
            // 独属于 AiHost 的事，组件本身不检查、也不知道调用方是否处于战斗、是否注册了行为档。
            var result = _rotationEvaluator.Evaluate(unitId, state.RotationId, state.Target);
            if (result.HasValue)
            {
                _bus.Enqueue(new AiDecisionMadeEvent(unitId, result.Value.SkillId));
            }

            return result;
        }

        // -----------------------------------------------------------------
        // 内部驱动版本
        // -----------------------------------------------------------------

        /// <summary>
        /// 推进单个单位一个模拟步：状态机转移 + （combat 态）按节奏求值 Rotation + 位移。
        /// 返回本次调用产生的 <c>move</c> 意图（0 或 1 条）——本方法不持有 <see cref="IWorldSim"/>
        /// 引用，不直接提交意图，由 <see cref="AiTickHandler"/> 负责 <c>world.SubmitIntent</c>。
        /// </summary>
        public IReadOnlyList<Intent> Step(Id unitId, double dt)
        {
            var state = GetState(unitId);

            if (state.State != BehaviorState.Dead && !_units.IsAlive(unitId))
            {
                TransitionTo(unitId, state, BehaviorState.Dead);
                return Array.Empty<Intent>();
            }

            switch (state.State)
            {
                case BehaviorState.Idle:
                case BehaviorState.Patrol:
                    return HandleIdleOrPatrol(unitId, state, dt);
                case BehaviorState.Chase:
                    return HandleChase(unitId, state, dt);
                case BehaviorState.Combat:
                    return HandleCombat(unitId, state, dt);
                case BehaviorState.Return:
                    return HandleReturn(unitId, state, dt);
                case BehaviorState.Flee:
                    return HandleFlee(unitId, state, dt);
                case BehaviorState.Dead:
                default:
                    return Array.Empty<Intent>();
            }
        }

        // -----------------------------------------------------------------
        // 状态处理
        // -----------------------------------------------------------------

        private IReadOnlyList<Intent> HandleIdleOrPatrol(Id unitId, AiUnitState state, double dt)
        {
            var profile = _profiles[state.ProfileId.Value];
            var position = _units.GetPosition(unitId);

            var nearestHostile = FindIdleChaseCandidate(unitId, state, position, profile);
            var shouldChase = EvaluateNamedCondition(profile, TransitionIdleToChase, unitId, nearestHostile,
                () => nearestHostile.HasValue);

            if (shouldChase && nearestHostile.HasValue)
            {
                state.Target = nearestHostile;
                TransitionTo(unitId, state, BehaviorState.Chase);
                return Array.Empty<Intent>();
            }

            if (state.State == BehaviorState.Patrol
                && profile.PatrolPathRef.HasValue
                && _patrolPaths.TryGetValue(profile.PatrolPathRef.Value.Value, out var path))
            {
                var intent = MoveAlongPatrol(unitId, state, path, position, dt);
                return intent.HasValue ? new[] { intent.Value } : Array.Empty<Intent>();
            }

            return Array.Empty<Intent>();
        }

        private IReadOnlyList<Intent> HandleChase(Id unitId, AiUnitState state, double dt)
        {
            var profile = _profiles[state.ProfileId.Value];
            var position = _units.GetPosition(unitId);

            if (!state.Target.HasValue)
            {
                TransitionTowardReturn(unitId, state, profile);
                return Array.Empty<Intent>();
            }

            var target = state.Target.Value;
            var targetGone = !_units.Exists(target) || !_units.IsAlive(target);
            var leashExceeded = Vec2.Distance(position, state.SpawnPoint) > profile.LeashRange;

            var shouldReturn = EvaluateNamedCondition(profile, TransitionChaseToReturn, unitId, state.Target,
                () => targetGone || leashExceeded);
            if (shouldReturn)
            {
                TransitionTowardReturn(unitId, state, profile);
                return Array.Empty<Intent>();
            }

            if (targetGone)
            {
                // 覆盖条件判定"不返回"，但目标确已消失/死亡：本 tick 无处可追，原地不动。
                return Array.Empty<Intent>();
            }

            var targetPos = _units.GetPosition(target);
            var distanceToTarget = Vec2.Distance(position, targetPos);
            var shouldFight = EvaluateNamedCondition(profile, TransitionChaseToCombat, unitId, state.Target,
                () => distanceToTarget <= _options.AttackRange * _options.CombatReentryRangeRatio);
            if (shouldFight)
            {
                TransitionTo(unitId, state, BehaviorState.Combat);
                return Array.Empty<Intent>();
            }

            var moveIntent = TryMoveToward(unitId, position, targetPos, dt);
            return moveIntent.HasValue ? new[] { moveIntent.Value } : Array.Empty<Intent>();
        }

        private IReadOnlyList<Intent> HandleCombat(Id unitId, AiUnitState state, double dt)
        {
            var profile = _profiles[state.ProfileId.Value];
            var position = _units.GetPosition(unitId);

            // ADR-0088（消费方第三十三批反馈2护栏）：选目标时跳过 IsHostile 为假的顶端来源，继续
            // 取下一个仍敌对的（见 GetTopHostileThreat 判断记录）——目标运行期转为友方后（消费方
            // 直接改阵营，仇恨表理应已被 ThreatTable 的 unit.faction_changed 订阅清理，见该类型
            // 判断记录），即便某条路径遗留了一条非敌对的僵尸条目，AI 也不会继续把它当目标（永不锁定
            // 非敌对目标的不变量，与源头修复各自独立存在）。全部不敌对则视为无仇恨（topThreat 为
            // null），走下方既有 noThreat/combat_to_return 判定，不额外分支。
            var topThreat = GetTopHostileThreat(unitId);
            if (topThreat.HasValue)
            {
                state.Target = topThreat;
            }

            if (profile.FleeHpPctThreshold.HasValue)
            {
                var hpPct = GetHpPct(unitId);
                var threshold = profile.FleeHpPctThreshold.Value;
                var shouldFlee = EvaluateNamedCondition(profile, TransitionCombatToFlee, unitId, state.Target,
                    () => hpPct < threshold);
                if (shouldFlee)
                {
                    TransitionTo(unitId, state, BehaviorState.Flee);
                    return Array.Empty<Intent>();
                }
            }

            var noThreat = !topThreat.HasValue;
            Id? freshHostile = null;
            if (noThreat)
            {
                freshHostile = FindNearestHostile(unitId, position, profile.PerceptionRadius);
            }

            var shouldReturn = EvaluateNamedCondition(profile, TransitionCombatToReturn, unitId, state.Target,
                () => noThreat && !freshHostile.HasValue);
            if (shouldReturn)
            {
                TransitionTowardReturn(unitId, state, profile);
                return Array.Empty<Intent>();
            }

            if (noThreat && freshHostile.HasValue)
            {
                state.Target = freshHostile;
            }

            // ADR-0084：combat_to_chase——当前目标仍存在，但距离已超出 AttackRange（不加余量）时
            // 回到 chase，复用 chase 态既有的移动意图与 leash_range 拴绳判定（HandleChase 内
            // chase_to_return），本方法不重复实现位移/拴绳。余量放在 chase_to_combat 一侧（见
            // AiOptions.CombatReentryRangeRatio）：combat 期间只要目标没有真正脱离攻击距离本身就
            // 不会被判定回追，不存在"处于 combat、目标却已经打不到"的死区。每 tick 判定（不受
            // decision_interval 节流），保证目标一旦超距、单位下一个 Step 就能开始朝它移动，不会
            // 像根治前那样在原地无限反复施法失败。
            if (state.Target.HasValue)
            {
                var target = state.Target.Value;
                if (_units.Exists(target) && _units.IsAlive(target))
                {
                    var targetPos = _units.GetPosition(target);
                    var distanceToTarget = Vec2.Distance(position, targetPos);
                    var shouldChase = EvaluateNamedCondition(profile, TransitionCombatToChase, unitId, state.Target,
                        () => distanceToTarget > _options.AttackRange);
                    if (shouldChase)
                    {
                        TransitionTo(unitId, state, BehaviorState.Chase);
                        return Array.Empty<Intent>();
                    }
                }
            }

            state.DecisionAccumulator += dt;
            if (state.DecisionAccumulator >= profile.DecisionInterval)
            {
                state.DecisionAccumulator -= profile.DecisionInterval;
                Evaluate(unitId);
            }

            return Array.Empty<Intent>();
        }

        private IReadOnlyList<Intent> HandleReturn(Id unitId, AiUnitState state, double dt)
        {
            var profile = _profiles[state.ProfileId.Value];
            var position = _units.GetPosition(unitId);

            AiPatrolPath? patrolPath = null;
            if (profile.CombatReturnPolicy == CombatReturnPolicy.Patrol && profile.PatrolPathRef.HasValue)
            {
                _patrolPaths.TryGetValue(profile.PatrolPathRef.Value.Value, out patrolPath);
            }

            var returnsToPatrol = patrolPath != null;
            var destination = returnsToPatrol ? patrolPath!.Points[0] : state.SpawnPoint;
            var arrivedDefault = Vec2.Distance(position, destination) < _options.ArrivalEpsilon;
            var transitionName = returnsToPatrol ? TransitionReturnToPatrol : TransitionReturnToIdle;

            var arrived = EvaluateNamedCondition(profile, transitionName, unitId, null, () => arrivedDefault);
            if (arrived)
            {
                TransitionTo(unitId, state, returnsToPatrol ? BehaviorState.Patrol : BehaviorState.Idle);
                return Array.Empty<Intent>();
            }

            var moveIntent = TryMoveToward(unitId, position, destination, dt);
            return moveIntent.HasValue ? new[] { moveIntent.Value } : Array.Empty<Intent>();
        }

        private IReadOnlyList<Intent> HandleFlee(Id unitId, AiUnitState state, double dt)
        {
            var profile = _profiles[state.ProfileId.Value];
            var position = _units.GetPosition(unitId);

            // 在脱战追击上限距离内找最近敌对单位：找不到即视为"距最近敌对 > leash_range"
            // （范围外还有没有敌对单位、具体多远都不影响这一判定的结果）。
            var nearestHostile = FindNearestHostile(unitId, position, profile.LeashRange);
            var exceedsLeash = !nearestHostile.HasValue;

            var shouldReturn = EvaluateNamedCondition(profile, TransitionFleeToReturn, unitId, nearestHostile,
                () => exceedsLeash);
            if (shouldReturn)
            {
                TransitionTowardReturn(unitId, state, profile);
                return Array.Empty<Intent>();
            }

            if (!nearestHostile.HasValue)
            {
                return Array.Empty<Intent>();
            }

            var nearestPos = _units.GetPosition(nearestHostile.Value);
            var caughtUp = Vec2.Distance(position, nearestPos) <= _options.AttackRange;

            if (_options.FleeReengage)
            {
                var shouldReengage = EvaluateNamedCondition(profile, TransitionFleeToCombat, unitId, nearestHostile,
                    () => caughtUp);
                if (shouldReengage)
                {
                    state.Target = nearestHostile;
                    TransitionTo(unitId, state, BehaviorState.Combat);
                    return Array.Empty<Intent>();
                }
            }

            var away = Normalize(position - nearestPos);
            var delta = away * (_options.MoveSpeed * dt);
            return new[] { BuildMoveIntent(unitId, delta) };
        }

        // -----------------------------------------------------------------
        // 转移判定辅助
        // -----------------------------------------------------------------

        private void TransitionTowardReturn(Id unitId, AiUnitState state, AiBehaviorProfile profile)
        {
            if (profile.CombatReturnPolicy == CombatReturnPolicy.Stay)
            {
                TransitionTo(unitId, state, BehaviorState.Idle);
            }
            else
            {
                TransitionTo(unitId, state, BehaviorState.Return);
            }
        }

        private void TransitionTo(Id unitId, AiUnitState state, BehaviorState newState)
        {
            if (state.State == newState)
            {
                return;
            }

            var old = state.State;
            state.State = newState;

            switch (newState)
            {
                case BehaviorState.Combat:
                    var profile = _profiles[state.ProfileId.Value];
                    // 进入 combat 立即把累加器拨满，紧接着的下一次 Step 就会求值一次 Rotation。
                    state.DecisionAccumulator = profile.DecisionInterval;
                    break;
                case BehaviorState.Idle:
                    state.Target = null;
                    break;
                case BehaviorState.Patrol:
                    state.Target = null;
                    state.PatrolIndex = 0;
                    state.PatrolDir = 1;
                    break;
                case BehaviorState.Dead:
                    state.Target = null;
                    state.DecisionAccumulator = 0.0;
                    break;
            }

            _bus.Enqueue(new AiStateChangedEvent(unitId, old, newState));
        }

        private bool EvaluateNamedCondition(AiBehaviorProfile profile, string name, Id unitId, Id? targetId, Func<bool> defaultCheck)
        {
            if (profile.Transitions.TryGetValue(name, out var node))
            {
                var host = _exprHostFactory.CreateFor(unitId, targetId, null);
                return ExprEvaluator.EvaluateBool(node, host, _exprDiagnostics);
            }

            return defaultCheck();
        }

        // -----------------------------------------------------------------
        // 感知 / 目标
        // -----------------------------------------------------------------

        /// <summary>加固任务（05 §3.6 碰撞层落地）：查询排除 <see cref="CollisionLayers.TriggerOnly"/>
        /// 标签——区域触发实体现在会登记进空间索引（见 <c>CarriersAssembly.DefaultSpatialSyncKinds</c>
        /// 判断记录）。本方法下面紧跟 <c>_units.Exists(candidate)</c> 防御性检查，理论上即便不排除也
        /// 不会因触发体混入而抛异常（<c>WorldUnitAccess.Exists</c> 只是 <c>is Unit</c> 判断，触发体不
        /// 是 <c>Unit</c> 会被判 false 后 <c>continue</c>）；这里仍然显式排除，避免每次查询都把触发体
        /// 拉进候选集合再逐个探测存在性的无谓开销，也与其余查询点的处理方式保持一致。</summary>
        private static readonly QueryFilter ExcludeTriggerOnly =
            new QueryFilter(excludedTags: new[] { CollisionLayers.TriggerOnly });

        /// <summary>
        /// ADR-0087（消费方第三十三批反馈1根治）：<c>idle</c>/<c>patrol</c> 态默认转移候选——仇恨表
        /// 是战斗中的目标权威，感知半径只用于获取新目标（见该 ADR 决定 1）。优先取感知范围内最近
        /// 敌对单位；找不到时，若该单位 <see cref="ICombatHost.IsInCombat"/>（如召唤物
        /// <c>SyncCombatState</c> 联动主人进战）且仇恨表顶端来源存活、仍敌对
        /// （<see cref="IFactionMatrix.IsHostile"/>）、且在 <see cref="AiBehaviorProfile.LeashRange"/>
        /// 内（以 <see cref="AiUnitState.SpawnPoint"/> 到来源当前位置的距离判定，与
        /// <see cref="HandleChase"/> 的拴绳判定同一基准点），才以它为候选——使
        /// <c>SummonOptions.ShareThreat</c> 合并进来的主人仇恨真正驱动召唤物从 idle/patrol 转入
        /// chase，不再需要感知范围覆盖到目标才会动。<see cref="_combatHost"/> 未注入（既有调用方）
        /// 时本分支恒不触发，只回退感知候选，行为与本次改动之前完全一致。
        /// <para>
        /// 不新增转移名：<c>idle_to_chase</c> 的 Expr 覆盖语义不变（覆盖的仍是"是否转移"的判定，
        /// 见 <see cref="HandleIdleOrPatrol"/> 调用点），本方法只是扩大了默认候选集的来源。
        /// </para>
        /// </summary>
        private Id? FindIdleChaseCandidate(Id unitId, AiUnitState state, Vec2 position, AiBehaviorProfile profile)
        {
            var nearestHostile = FindNearestHostile(unitId, position, profile.PerceptionRadius);
            if (nearestHostile.HasValue)
            {
                return nearestHostile;
            }

            if (_combatHost == null || !_combatHost.IsInCombat(unitId))
            {
                return null;
            }

            var topThreat = _threatTable.GetTopThreat(unitId);
            if (!topThreat.HasValue)
            {
                return null;
            }

            var source = topThreat.Value;
            if (!_units.Exists(source) || !_units.IsAlive(source))
            {
                return null;
            }

            if (!_factions.IsHostile(_units.GetFaction(unitId), _units.GetFaction(source)))
            {
                return null;
            }

            var sourcePos = _units.GetPosition(source);
            if (Vec2.Distance(state.SpawnPoint, sourcePos) > profile.LeashRange)
            {
                return null;
            }

            return source;
        }

        /// <summary>
        /// ADR-0088（消费方第三十三批反馈2护栏）：仇恨表顶端来源已运行期转为非敌对时不选它作目标——
        /// 按 <see cref="IThreatTable.GetAll"/> 的原始顺序（按 <see cref="Id"/> 序数升序，见
        /// <see cref="Core.Rules.Combat.ThreatTable.GetAll"/> 判断记录）复刻
        /// <see cref="IThreatTable.GetTopThreat"/> 同一套"最高仇恨值、并列取 Id 序数最小者"的选取
        /// 规则，额外跳过不存在/已死亡/已不再敌对的来源，继续看仇恨值次高的一个。全部候选都被跳过
        /// 时返回 null（视为无仇恨，<see cref="HandleCombat"/> 走既有 <c>combat_to_return</c>/
        /// 感知重新拾取新目标判定，不额外分支）。</summary>
        private Id? GetTopHostileThreat(Id unitId)
        {
            var entries = _threatTable.GetAll(unitId);
            if (entries.Count == 0)
            {
                return null;
            }

            var selfFaction = _units.GetFaction(unitId);
            Id? best = null;
            var bestValue = double.NegativeInfinity;

            foreach (var (source, amount) in entries)
            {
                if (!_units.Exists(source) || !_units.IsAlive(source)) continue;
                if (!_factions.IsHostile(selfFaction, _units.GetFaction(source))) continue;

                if (amount > bestValue)
                {
                    bestValue = amount;
                    best = source;
                }
            }

            return best;
        }

        private Id? FindNearestHostile(Id unitId, Vec2 position, double radius)
        {
            var candidates = _spatialQuery.QueryRadius(position, radius, ExcludeTriggerOnly);
            var selfFaction = _units.GetFaction(unitId);

            var bestDistSqr = double.MaxValue;
            var ties = new List<Id>();

            foreach (var candidate in candidates)
            {
                if (candidate.Equals(unitId)) continue;
                if (!_units.Exists(candidate) || !_units.IsAlive(candidate)) continue;
                if (!_factions.IsHostile(selfFaction, _units.GetFaction(candidate))) continue;

                var distSqr = (_units.GetPosition(candidate) - position).SqrLength;
                if (distSqr < bestDistSqr - 1e-9)
                {
                    bestDistSqr = distSqr;
                    ties.Clear();
                    ties.Add(candidate);
                }
                else if (Math.Abs(distSqr - bestDistSqr) <= 1e-9)
                {
                    ties.Add(candidate);
                }
            }

            if (ties.Count == 0)
            {
                return null;
            }

            if (ties.Count == 1)
            {
                return ties[0];
            }

            ties.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));

            if (_options.RandomTieBreak)
            {
                var index = _rng.NextInt(_options.RngStream, 0, ties.Count - 1);
                return ties[index];
            }

            return ties[0];
        }

        private double GetHpPct(Id unitId)
        {
            var max = _powers.GetPowerMax(unitId, WellKnownPowers.Health);
            if (max <= 0.0)
            {
                return 0.0;
            }

            return _powers.GetPower(unitId, WellKnownPowers.Health) / max;
        }

        // -----------------------------------------------------------------
        // 位移
        // -----------------------------------------------------------------

        private Intent? MoveAlongPatrol(Id unitId, AiUnitState state, AiPatrolPath path, Vec2 position, double dt)
        {
            var waypoint = path.Points[state.PatrolIndex];
            var distance = Vec2.Distance(position, waypoint);

            if (distance < _options.ArrivalEpsilon)
            {
                AdvancePatrolIndex(state, path);
                return null;
            }

            return TryMoveToward(unitId, position, waypoint, dt);
        }

        private static void AdvancePatrolIndex(AiUnitState state, AiPatrolPath path)
        {
            if (path.Mode == PatrolMode.Loop)
            {
                state.PatrolIndex = (state.PatrolIndex + 1) % path.Points.Count;
                return;
            }

            var next = state.PatrolIndex + state.PatrolDir;
            if (next < 0 || next >= path.Points.Count)
            {
                state.PatrolDir = -state.PatrolDir;
                next = state.PatrolIndex + state.PatrolDir;
            }

            state.PatrolIndex = next;
        }

        private Intent? TryMoveToward(Id unitId, Vec2 from, Vec2 to, double dt)
        {
            var distance = Vec2.Distance(from, to);
            if (distance < _options.ArrivalEpsilon)
            {
                return null;
            }

            var direction = ComputeDirection(unitId, from, to);
            var delta = direction * (_options.MoveSpeed * dt);
            return BuildMoveIntent(unitId, delta);
        }

        /// <summary>
        /// 判断记录（契约缺口，集成任务已补齐）：地图 id 优先经 <see cref="IUnitAccess.GetMapId"/>
        /// 取得（真正按单位所在地图分别寻路），只有它返回 <c>null</c>（未接入地图概念的实现，如
        /// 测试假实现，行为与集成前完全一致）时才回退 <see cref="AiOptions.MapId"/> 这个单地图
        /// 场景的兜底值——见 <see cref="AiOptions.MapId"/> 上方判断记录。
        /// </summary>
        private Vec2 ComputeDirection(Id unitId, Vec2 from, Vec2 to)
        {
#pragma warning disable CS0618 // AiOptions.MapId 已标记过时，这里是文档化的唯一兜底读取点
            var mapId = _units.GetMapId(unitId) ?? _options.MapId;
#pragma warning restore CS0618

            if (_navigation != null && mapId.HasValue)
            {
                var path = _navigation.FindPath(mapId.Value, from, to);
                if (path != null && path.Count >= 2)
                {
                    return Normalize(path[1] - from);
                }
            }

            return Normalize(to - from);
        }

        private static Vec2 Normalize(Vec2 v)
        {
            var length = v.Length;
            return length <= double.Epsilon ? Vec2.Zero : v * (1.0 / length);
        }

        private static Intent BuildMoveIntent(Id unitId, Vec2 delta)
        {
            var args = new JsonObjectBuilder()
                .Add("dx", new JsonNumber(delta.X))
                .Add("dy", new JsonNumber(delta.Y))
                .Build();
            return new Intent(unitId, "move", args);
        }

        // -----------------------------------------------------------------
        // 数据加载
        // -----------------------------------------------------------------

        private void LoadPatrolPaths(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll(AiSchemas.PatrolPath.Name))
            {
                var id = record.GetId("id");
                var pointsArray = record.GetArray("points");
                var points = new List<Vec2>(pointsArray.Count);
                foreach (var item in pointsArray)
                {
                    var obj = (JsonObject)item;
                    var x = ((JsonNumber)obj["x"]).Value;
                    var y = ((JsonNumber)obj["y"]).Value;
                    points.Add(new Vec2(x, y));
                }

                var mode = record.GetString("mode") == "pingpong" ? PatrolMode.PingPong : PatrolMode.Loop;
                _patrolPaths[id.Value] = new AiPatrolPath(id, points, mode);
            }
        }

        private void LoadProfiles(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll(AiSchemas.BehaviorProfile.Name))
            {
                var id = record.GetId("id");
                var perception = record.GetNumber("perception_radius");
                Id? patrolRef = record.TryGetId("patrol_path_ref", out var patrolPathRef) ? patrolPathRef : (Id?)null;
                var leash = record.GetNumber("leash_range");
                double? fleeThreshold = record.TryGetNumber("flee_hp_pct_threshold", out var threshold) ? threshold : (double?)null;
                var returnPolicy = ParseCombatReturnPolicy(record.GetString("combat_return_policy"));
                var rotationRef = record.GetId("rotation_ref");
                var decisionInterval = record.TryGetNumber("decision_interval", out var interval) ? interval : 0.5;

                var transitions = new Dictionary<string, ExprNode>(StringComparer.Ordinal);
                if (record.TryGetObject("transitions", out var transitionsObj))
                {
                    foreach (var kv in transitionsObj)
                    {
                        var text = ((JsonString)kv.Value).Value;
                        transitions[kv.Key] = ExprParser.Parse(text, _exprSchema);
                    }
                }

                _profiles[id.Value] = new AiBehaviorProfile(
                    id, perception, patrolRef, leash, fleeThreshold, returnPolicy, rotationRef, decisionInterval, transitions);
            }
        }

        private static CombatReturnPolicy ParseCombatReturnPolicy(string value)
        {
            switch (value)
            {
                case "return_to_spawn": return CombatReturnPolicy.ReturnToSpawn;
                case "stay": return CombatReturnPolicy.Stay;
                case "patrol": return CombatReturnPolicy.Patrol;
                default: throw new ArgumentException($"未知 combat_return_policy 取值 \"{value}\"", nameof(value));
            }
        }

        private AiUnitState GetState(Id unitId)
        {
            if (_states.TryGetValue(unitId.Value, out var state))
            {
                return state;
            }

            throw new InvalidOperationException($"单位 \"{unitId}\" 未注册 AI 行为外壳（先调用 RegisterUnit）");
        }
    }
}
