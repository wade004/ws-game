using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 挂在 <see cref="TickPhase.MovementAndNavigation"/> 的移动系统（见 05 第 6 节）：消费本 tick
    /// <see cref="IWorldSim.CurrentIntents"/> 里 <c>Kind == "move"</c> 的意图（<c>Args</c> 为
    /// <c>{x, y, mode}</c> 目标或 <c>{dx, dy, mode}</c> 方向，见 <see cref="MovementHost.Request"/>）
    /// 并推进每个 <see cref="Unit"/> 的 <see cref="Unit.MovementState"/>。
    /// <para>
    /// 两类意图的处理方式不同（判断记录，见本模块 README）：
    /// </para>
    /// <list type="bullet">
    /// <item><b>目标（x,y）</b>：本 tick 计算一次路径（有 <see cref="INavigation2D"/> 时调用
    /// <c>FindPath</c>，否则直线兜底），写入 <see cref="MovementState.CurrentPath"/>，随后每 tick
    /// （即便不再收到新意图）由本处理器沿路径继续推进，直至到达——<see cref="MovementState.CurrentPath"/>/
    /// <see cref="MovementState.PathIndex"/> 正是为"跨多个 tick 记住走到哪了"而存在。</item>
    /// <item><b>方向（dx,dy）</b>：只在收到意图的这一 tick 内按方向直接位移，不建立
    /// <see cref="MovementState.CurrentPath"/>，典型场景是玩家持续按住移动键、每 tick 重新提交。</item>
    /// </list>
    /// </summary>
    public sealed class MovementTickHandler : ITickPhaseHandler
    {
        private readonly IUnitAccess _units;
        private readonly IStatHost _stats;
        private readonly IAuraQuery _auras;
        private readonly MovementHost _movementHost;
        private readonly IEventBus _bus;
        private readonly INavigation2D? _navigation;
        private readonly MovementOptions _options;
        private readonly IExprDiagnostics _diagnostics;
        private readonly ISpatialQuery? _spatial;

        public MovementTickHandler(
            IUnitAccess units,
            IStatHost stats,
            IAuraQuery auras,
            MovementHost movementHost,
            IEventBus bus,
            INavigation2D? navigation = null,
            MovementOptions? options = null,
            IExprDiagnostics? diagnostics = null,
            ISpatialQuery? spatial = null)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _auras = auras ?? throw new ArgumentNullException(nameof(auras));
            _movementHost = movementHost ?? throw new ArgumentNullException(nameof(movementHost));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _navigation = navigation;
            _options = options ?? new MovementOptions();
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();
            // spatial 是新增可选依赖（加固任务：MovementOptions.UnitBlocking 落地）——放在参数列表
            // 末尾而不是插在 navigation 之前，是为了不破坏既有按位置传参的调用点（见本模块 tests）。
            _spatial = spatial;
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            // 离散步下"移动与导航"按该行动者的每回合移动预算结算位移，而非按连续时间的速度积分
            // （见 03 第 4.2 节步骤 4、ADR-0013 决策 6）：复用同一套"速度 × 时间"位移公式，只是
            // dt 换成 MovementOptions.DiscreteTurnEquivalentSeconds（一个固定的"每回合等效秒数"）
            // 而不是真实经过的秒数，见 MovementOptions.DiscreteTurnEquivalentSeconds 判断记录。
            var dt = step.Kind == SimStepKind.Continuous ? step.Dt : _options.DiscreteTurnEquivalentSeconds;

            // world.CurrentIntents 在离散步下已经只包含当前行动者的意图（见 WorldSim.Tick 判断
            // 记录），第一遍循环不需要额外按 step.ActorId 过滤。
            var intents = world.CurrentIntents;
            var processedThisTick = new HashSet<Id>();

            // 第一遍：消费本 tick 的 move 意图（(重新)确立移动状态并立即推进这一 tick 的位移）。
            for (var i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                if (intent.Kind != "move") continue;
                if (!(world.GetEntity(intent.ActorId) is Unit unit)) continue;

                ApplyIntent(unit, intent, dt, step.Kind == SimStepKind.Discrete);
                processedThisTick.Add(unit.EntityId);
            }

            if (step.Kind == SimStepKind.Discrete)
            {
                // 离散步下只处理当前行动者：其余单位在别人的回合既不产生新意图，也不消耗自己的
                // 移动预算继续沿旧路径推进（03 第 4.2 节"离散步只处理当前行动者的意图"，第二遍
                // "无新意图但继续走已有路径"是连续模式特有的"每 tick 都会被驱动"语义，离散模式下
                // 不成立——一名行动者两次轮到自己之间可能经过其他人的多个回合，若仍然沿用第二遍，
                // 会让该单位在不属于它的回合里凭空继续移动）。
                return;
            }

            // 第二遍：本 tick 未收到新意图、但仍有未走完路径的单位继续沿路径推进（仅连续模式）。
            foreach (var entity in world.QueryEntities(new EntityFilter(predicate: e => e is Unit)))
            {
                if (processedThisTick.Contains(entity.EntityId)) continue;

                var unit = (Unit)entity;
                if (unit.MovementState.CurrentPath == null) continue;

                if (IsLocked(unit)) continue;

                ContinuePathCore(unit, dt);
            }
        }

        private void ApplyIntent(Unit unit, Intent intent, double dt, bool isDiscrete)
        {
            if (IsLocked(unit))
            {
                return;
            }

            if (isDiscrete && !TryConsumeMovementActionPoints(unit, dt))
            {
                return; // 行动点不足：本次移动意图被拒绝，见 TryConsumeMovementActionPoints 注释。
            }

            if (TryReadTarget(intent.Args, out var target))
            {
                var mode = ReadMode(intent.Args, MoveMode.Run);
                BeginPathTo(unit, target, mode, dt);
                return;
            }

            if (TryReadDirection(intent.Args, out var direction))
            {
                var mode = ReadMode(intent.Args, MoveMode.Walk);
                ApplyDirectionalMove(unit, direction, mode, dt);
                return;
            }

            _diagnostics.Warn(
                $"MovementTickHandler: move 意图缺少 target(x,y) 或 direction(dx,dy) 参数，单位 " +
                $"\"{unit.EntityId}\" 本次意图被忽略");
        }

        /// <summary>
        /// ADR-0013 补齐：<see cref="MovementOptions.MovementBudgetRule"/> 为 <c>"action_points"</c>
        /// 且已装配 <see cref="MovementOptions.TryConsumeActionPoints"/> 时，按"该单位这一步会移动
        /// 的距离（速度 × <see cref="MovementOptions.DiscreteTurnEquivalentSeconds"/>，与
        /// <c>"distance"</c> 规则同一基准，见 <see cref="MovementOptions.DiscreteTurnEquivalentSeconds"/>
        /// 判断记录）× 每单位距离行动点消耗"算出本次移动需要的行动点，尝试一次性扣减；不足时拒绝
        /// （返回 <c>false</c>，调用方不产生任何位移）并调用
        /// <see cref="MovementOptions.RequestEndTurn"/> 结束该行动者的回合（06 第 6.2 节）。规则不是
        /// <c>action_points</c>，或未装配 <see cref="MovementOptions.TryConsumeActionPoints"/> 时
        /// 恒返回 <c>true</c>（不做任何检查，行为与本任务之前一致）。
        /// </summary>
        private bool TryConsumeMovementActionPoints(Unit unit, double dt)
        {
            if (_options.MovementBudgetRule != "action_points" || _options.TryConsumeActionPoints == null)
            {
                return true;
            }

            var speed = ResolveSpeed(unit.EntityId);
            var distance = speed * dt;
            var cost = distance * _options.MovementActionCostPerUnit;

            if (_options.TryConsumeActionPoints(unit.EntityId, cost))
            {
                return true;
            }

            _options.RequestEndTurn?.Invoke(unit.EntityId);
            _diagnostics.Warn(
                $"MovementTickHandler: 单位 \"{unit.EntityId}\" 行动点不足（需要 {cost}），移动意图被拒绝，回合结束");
            return false;
        }

        private void BeginPathTo(Unit unit, Vec2 target, MoveMode mode, double dt)
        {
            var from = unit.Position;
            IReadOnlyList<Vec2>? path = _navigation != null
                ? _navigation.FindPath(unit.MapId, from, target)
                : new List<Vec2> { from, target };

            if (path == null)
            {
                _movementHost.RaiseMoveFailed(unit.EntityId, from, target);
                return;
            }

            var oldMode = unit.MovementState.Mode;
            unit.MovementState = new MovementState(path, mode, unit.MovementState.MovementLocked, 0);
            RaiseStateChangedIfNeeded(unit.EntityId, oldMode, mode);

            ContinuePathCore(unit, dt);
        }

        private void ApplyDirectionalMove(Unit unit, Vec2 direction, MoveMode mode, double dt)
        {
            var length = direction.Length;
            if (length <= double.Epsilon)
            {
                return;
            }

            var normalized = new Vec2(direction.X / length, direction.Y / length);
            var speed = ResolveSpeed(unit.EntityId);
            var newPos = unit.Position + normalized * (speed * dt);

            if (IsBlockedByUnit(unit.EntityId, newPos))
            {
                return; // 判断记录见 IsBlockedByUnit：本次不位移，不改朝向/状态，原地不动。
            }

            _units.SetPosition(unit.EntityId, newPos);
            unit.Facing = Math.Atan2(normalized.Y, normalized.X);
            EnqueueMoved(unit.EntityId, newPos);

            var oldMode = unit.MovementState.Mode;
            unit.MovementState = new MovementState(null, mode, unit.MovementState.MovementLocked, 0);
            RaiseStateChangedIfNeeded(unit.EntityId, oldMode, mode);
        }

        /// <summary>沿 <see cref="Unit.MovementState"/> 已有路径推进本 tick 的位移（起点可能是刚由
        /// <see cref="BeginPathTo"/> 建立的新路径，也可能是延续上一 tick 的
        /// <see cref="MovementState.PathIndex"/>）。到达最终路点时把状态收回 <see cref="MoveMode.Idle"/>
        /// 并清空路径（见 05 第 6.2 节"寻路失败处理"之外的正常到达分支）。</summary>
        private void ContinuePathCore(Unit unit, double dt)
        {
            var state = unit.MovementState;
            var path = state.CurrentPath;
            if (path == null || path.Count == 0)
            {
                return;
            }

            var speed = ResolveSpeed(unit.EntityId);
            var remaining = speed * dt;
            var pos = unit.Position;
            var index = state.PathIndex < 0 ? 0 : state.PathIndex;
            var lastFacing = unit.Facing;

            while (remaining > 0 && index < path.Count)
            {
                var waypoint = path[index];
                var toWaypoint = waypoint - pos;
                var dist = toWaypoint.Length;

                if (dist <= _options.ArrivalEpsilon)
                {
                    pos = waypoint;
                    index++;
                    continue;
                }

                if (dist <= remaining)
                {
                    pos = waypoint;
                    remaining -= dist;
                    lastFacing = Math.Atan2(toWaypoint.Y, toWaypoint.X);
                    index++;
                }
                else
                {
                    var dir = new Vec2(toWaypoint.X / dist, toWaypoint.Y / dist);
                    pos = pos + dir * remaining;
                    lastFacing = Math.Atan2(dir.Y, dir.X);
                    remaining = 0;
                }
            }

            if (!pos.Equals(unit.Position))
            {
                if (IsBlockedByUnit(unit.EntityId, pos))
                {
                    // 判断记录见 IsBlockedByUnit：本次 tick 算出的终点被其他单位占据——本次 tick
                    // 完全不生效（不写位置/朝向、不改 MovementState），path/index 保持调用前的
                    // 既有值，下一 tick 用同样的路径与起点重试（简单确定性，不做滑动/绕行，见
                    // MovementOptions.UnitBlocking 判断记录"后续可扩展为滑动/绕行"）。
                    return;
                }

                _units.SetPosition(unit.EntityId, pos);
                unit.Facing = lastFacing;
                EnqueueMoved(unit.EntityId, pos);
            }

            var oldMode = state.Mode;
            if (index >= path.Count)
            {
                unit.MovementState = new MovementState(null, MoveMode.Idle, state.MovementLocked, 0);
                RaiseStateChangedIfNeeded(unit.EntityId, oldMode, MoveMode.Idle);
            }
            else
            {
                unit.MovementState = new MovementState(path, state.Mode, state.MovementLocked, index);
            }
        }

        /// <summary>
        /// 加固任务（05 §3.6 碰撞层规划落地）：<see cref="MovementOptions.UnitBlocking"/> 为
        /// <c>true</c> 时，查询 <paramref name="targetPosition"/> 附近携带
        /// <see cref="CollisionLayers.UnitBlock"/> 标签、非 <paramref name="selfId"/> 自身的对象；
        /// 命中即视为"目标落点已被占据"。
        /// <para>
        /// 判断记录（简单确定性：整体拒绝，不做滑动/绕行）：05 §3.6 只规划了"是否阻挡"这一个策略
        /// 开关，未规定阻挡后的位移应该如何调整；任务书拍板"命中则本次不位移（停在原地，不做滑动，
        /// 简单确定性）"——本方法因此只回答"是/否被阻挡"，由调用方（<see cref="ApplyDirectionalMove"/>/
        /// <see cref="ContinuePathCore"/>）决定"是"时整体放弃本次位移，不尝试贴着障碍物滑动或绕开，
        /// 后续如需要更自然的碰撞响应（滑动、pathfinding 绕行）可在此基础上扩展，不影响本方法契约。
        /// </para>
        /// <para>
        /// 判断记录（<see cref="_spatial"/> 为 null 时恒不阻挡）：本类型的 <c>spatial</c> 依赖是新增
        /// 可选参数（见构造函数判断记录），未注入时不应该让 <see cref="MovementOptions.UnitBlocking"/>
        /// 抛异常——静默视为"不阻挡"，行为退化为本任务之前的既有语义。
        /// </para>
        /// </summary>
        private bool IsBlockedByUnit(Id selfId, Vec2 targetPosition)
        {
            if (!_options.UnitBlocking || _spatial == null)
            {
                return false;
            }

            var filter = new QueryFilter(requiredTags: new[] { CollisionLayers.UnitBlock });
            var candidates = _spatial.QueryRadius(targetPosition, _options.UnitBlockRadius, filter);
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].Equals(selfId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>控制效果影响（见 05 第 6.2 节"控制效果影响...移动系统只读这些派生状态，不知道
        /// 具体是哪个光环造成的"）：<see cref="MovementState.MovementLocked"/> 或
        /// <see cref="IAuraQuery.GetControlFlags"/> 含 <see cref="ControlFlags.NoMove"/> 时，本 tick
        /// 完全不推进该单位（既不建立新路径，也不继续已有路径）。</summary>
        private bool IsLocked(Unit unit) =>
            unit.MovementState.MovementLocked ||
            (_auras.GetControlFlags(unit.EntityId) & ControlFlags.NoMove) != 0;

        /// <summary>速度来源（见 05 第 6.2 节"来自该 Unit 的 StatBlock 中的移动速度属性"）。判断记录：
        /// <c>IStatHost.GetStat</c> 对"属性未注册/未登记"与"属性显式设为 0"两种情形都返回相同的 0
        /// （见 <c>IStatHost.GetBase</c> 注释"未显式设置过时返回 default_base，缺省为 0"），本层无法
        /// 从返回值本身区分二者；任务书"缺失按 DefaultSpeed"按此实用近似实现：非正值一律回退到
        /// <see cref="MovementOptions.DefaultSpeed"/>（"移动速度为 0 或负数"本就不是有意义的移动速度，
        /// 回退不会掩盖任何合法配置）。</summary>
        private double ResolveSpeed(Id unitId)
        {
            var speed = _stats.GetStat(unitId, _options.MoveSpeedStat);
            return speed > 0 ? speed : _options.DefaultSpeed;
        }

        private void EnqueueMoved(Id unitId, Vec2 position) =>
            _bus.Enqueue(new UnitMovedEvent(unitId, position));

        /// <summary><c>unit.state_changed</c> 在 <see cref="MoveMode"/> 变化时发出（见任务书拍板，
        /// <c>OldState</c>/<c>NewState</c> 用 <see cref="MoveMode"/> 名称，见
        /// <c>Core.Carriers.Common.UnitStateChangedEvent</c> 顶部判断记录）。</summary>
        private void RaiseStateChangedIfNeeded(Id unitId, MoveMode oldMode, MoveMode newMode)
        {
            if (oldMode == newMode) return;

            _bus.Enqueue(new UnitStateChangedEvent(unitId, oldMode.ToString(), newMode.ToString()));
        }

        private static bool TryReadTarget(JsonObject args, out Vec2 target)
        {
            if (args.TryGetValue("x", out var xValue) && xValue is JsonNumber x &&
                args.TryGetValue("y", out var yValue) && yValue is JsonNumber y)
            {
                target = new Vec2(x.Value, y.Value);
                return true;
            }

            target = default;
            return false;
        }

        private static bool TryReadDirection(JsonObject args, out Vec2 direction)
        {
            if (args.TryGetValue("dx", out var dxValue) && dxValue is JsonNumber dx &&
                args.TryGetValue("dy", out var dyValue) && dyValue is JsonNumber dy)
            {
                direction = new Vec2(dx.Value, dy.Value);
                return true;
            }

            direction = default;
            return false;
        }

        private static MoveMode ReadMode(JsonObject args, MoveMode fallback)
        {
            if (args.TryGetValue("mode", out var modeValue) && modeValue is JsonString text &&
                Enum.TryParse<MoveMode>(text.Value, out var mode))
            {
                return mode;
            }

            return fallback;
        }
    }
}
