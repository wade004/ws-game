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
    /// 用本模块自带的临时 <see cref="AiExprSchema"/>，集成任务后默认改用
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
        private readonly IExprDiagnostics _exprDiagnostics = new ExprDiagnosticsRecorder();

        /// <summary>集成任务改动：解析 <c>ai.rotation.entries[].condition</c>/
        /// <c>ai.behavior_profile.transitions</c> 用的 <see cref="IExprSchema"/>，默认
        /// <see cref="RulesExprSchema.Instance"/>（保留可注入口子，见构造函数 <c>exprSchema</c>
        /// 参数与 <see cref="AiContentValidationRule"/> 同一惯例）。</summary>
        private readonly IExprSchema _exprSchema;

        private readonly Dictionary<string, AiBehaviorProfile> _profiles = new Dictionary<string, AiBehaviorProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<CompiledRotationEntry>> _rotations = new Dictionary<string, IReadOnlyList<CompiledRotationEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, AiPatrolPath> _patrolPaths = new Dictionary<string, AiPatrolPath>(StringComparer.Ordinal);

        // SortedDictionary 保证按 Id 序数遍历，供 RegisteredUnitIds / AiTickHandler 确定性遍历。
        private readonly SortedDictionary<string, AiUnitState> _states = new SortedDictionary<string, AiUnitState>(StringComparer.Ordinal);

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
            IExprSchema? exprSchema = null)
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
            _exprSchema = exprSchema ?? RulesExprSchema.Instance;

            LoadPatrolPaths(registry);
            LoadRotations(registry);
            LoadProfiles(registry);
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
            if (!_rotations.ContainsKey(rotationId.Value))
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

            if (!_rotations.TryGetValue(state.RotationId.Value, out var entries))
            {
                return null;
            }

            foreach (var entry in entries)
            {
                var host = _exprHostFactory.CreateFor(unitId, state.Target, null);
                if (!ExprEvaluator.EvaluateBool(entry.Condition, host, _exprDiagnostics))
                {
                    continue;
                }

                var targets = state.Target.HasValue
                    ? new[] { state.Target.Value }
                    : Array.Empty<Id>();

                var result = _skillHost.CastSkill(unitId, entry.SkillId, targets);
                if (result.Success)
                {
                    _bus.Enqueue(new AiDecisionMadeEvent(unitId, entry.SkillId));
                    return new SkillCastRequest(unitId, entry.SkillId, targets);
                }
            }

            return null;
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

            var nearestHostile = FindNearestHostile(unitId, position, profile.PerceptionRadius);
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
                () => distanceToTarget <= _options.AttackRange);
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

            var topThreat = _threatTable.GetTopThreat(unitId);
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
                var position = _units.GetPosition(unitId);
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

        private Id? FindNearestHostile(Id unitId, Vec2 position, double radius)
        {
            var candidates = _spatialQuery.QueryRadius(position, radius, QueryFilter.None);
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

        private void LoadRotations(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll(AiSchemas.Rotation.Name))
            {
                var id = record.GetId("id");
                var entriesArray = record.GetArray("entries");
                var compiled = new List<CompiledRotationEntry>(entriesArray.Count);

                foreach (var item in entriesArray)
                {
                    var obj = (JsonObject)item;
                    var priority = (int)((JsonNumber)obj["priority"]).Value;
                    var conditionText = ((JsonString)obj["condition"]).Value;
                    var skillId = new Id(((JsonString)obj["skill_id"]).Value);
                    var node = ExprParser.Parse(conditionText, _exprSchema);
                    compiled.Add(new CompiledRotationEntry(priority, node, skillId));
                }

                compiled.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                _rotations[id.Value] = compiled;
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
