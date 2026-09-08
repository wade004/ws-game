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

        /// <summary>零长度目标的判定阈值（游戏侧通用能力需求，05 第 6 节勘误"零长度目标：不建路径、
        /// 不动、不回调"），与 <see cref="Core.Foundation.EngineAdapter.INavigation2D.FindPath"/> 端点
        /// 契约"<c>|from-to| &lt;= 1e-6</c>"取同一常量，保证 <see cref="BeginPathTo"/> 的短路判断与
        /// 导航实现自身对零长度目标的判断一致（不会出现"移动系统认为非零长度、但导航实现认为零长度"
        /// 的边界不一致）。</summary>
        private const double ZeroLengthEpsilon = 1e-6;

        public void Execute(SimStep step, IWorldSim world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            // 离散步下"移动与导航"按该行动者的每回合移动预算结算位移，而非按连续时间的速度积分
            // （见 03 第 4.2 节步骤 4、ADR-0013 决策 6）：复用同一套"速度 × 时间"位移公式，只是
            // dt 换成 MovementOptions.DiscreteTurnEquivalentSeconds（一个固定的"每回合等效秒数"）
            // 而不是真实经过的秒数，见 MovementOptions.DiscreteTurnEquivalentSeconds 判断记录。
            var dt = step.Kind == SimStepKind.Continuous ? step.Dt : _options.DiscreteTurnEquivalentSeconds;

            // world.CurrentIntents 在离散步下已经只包含当前行动者的意图（见 WorldSim.Tick 判断
            // 记录），下面几遍循环不需要额外按 step.ActorId 过滤。
            var intents = world.CurrentIntents;
            var processedThisTick = new HashSet<Id>();

            // 游戏侧通用能力需求（05 第 6 节勘误"先处理 move_stop 意图 → 再处理 move 意图"）：先算出
            // 每个单位本 tick 最后一条 move_stop 意图的下标——同一 tick 内该下标之前提交的 move 意图
            // 被丢弃（视为从未提交过），之后提交的 move 意图照常生效；本类只关心"是否存在、下标"，
            // 具体停止的落地效果由 ApplyStop 统一处理。
            var lastStopIndex = new Dictionary<Id, int>();
            for (var i = 0; i < intents.Count; i++)
            {
                if (intents[i].Kind == "move_stop")
                {
                    lastStopIndex[intents[i].ActorId] = i;
                }
            }

            // 第一遍 A：处理 move_stop 意图（先于 move，同一单位同一 tick 内多条 move_stop 只需按其中
            // 一条处理一次——落地效果只取决于"处理时的当前状态"，重复处理是幂等的，这里用
            // processedStop 去重只是避免重复调用 ApplyStop 做多余工作，不影响结果正确性）。
            var processedStop = new HashSet<Id>();
            for (var i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                if (intent.Kind != "move_stop") continue;
                if (!(world.GetEntity(intent.ActorId) is Unit unit)) continue;
                if (!processedStop.Add(unit.EntityId)) continue;

                var discardedMoveIntent = HasPrecedingMoveIntent(intents, unit.EntityId, lastStopIndex[unit.EntityId]);
                ApplyStop(unit, MoveStopReason.Requested, discardedMoveIntent);
                processedThisTick.Add(unit.EntityId);
            }

            // 第一遍 B：消费本 tick 存活的 move 意图（(重新)确立移动状态并立即推进这一 tick 的位移）。
            // 被同一 tick 内更晚提交的 move_stop 丢弃的 move 意图在这里跳过（见上方 lastStopIndex）。
            for (var i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                if (intent.Kind != "move") continue;
                if (!(world.GetEntity(intent.ActorId) is Unit unit)) continue;
                if (lastStopIndex.TryGetValue(unit.EntityId, out var stopIdx) && stopIdx > i) continue;

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

            // 第二遍：本 tick 未收到新意图、但仍有未走完路径的单位——先比较该地图的动态阻挡版本，
            // 按 MovementOptions.BlockingChangePolicy 处理变化，再继续沿路径推进（仅连续模式，见
            // 05 第 6 节勘误 tick 步骤 4"先处理 move_stop → 再处理 move → 重验阻挡版本 → 才推进"）。
            foreach (var entity in world.QueryEntities(new EntityFilter(predicate: e => e is Unit)))
            {
                if (processedThisTick.Contains(entity.EntityId)) continue;

                var unit = (Unit)entity;
                if (unit.MovementState.CurrentPath == null) continue;

                if (IsLocked(unit)) continue;

                if (!RevalidateBlocking(unit)) continue;

                ContinuePathCore(unit, dt);
            }
        }

        /// <summary>本 tick 的意图列表 <paramref name="intents"/> 中，<paramref name="stopIndex"/>
        /// （某单位最后一条 <c>move_stop</c> 意图的下标）之前是否存在该单位的 <c>move</c> 意图——用于
        /// <see cref="MovementHost.Stop"/> 的幂等判断："即便该单位当前没有活动路径，只要有一条本会被
        /// 丢弃的待处理 move 意图，也算'确有东西被取消'，需要触发 <see cref="MovementHost.OnMoveStopped"/>"
        /// （见 <see cref="MovementHost.Stop"/> 判断记录"幂等：无活动路径且无待处理意图时静默不回调"）。
        /// </summary>
        private static bool HasPrecedingMoveIntent(IReadOnlyList<Intent> intents, Id unitId, int stopIndex)
        {
            for (var i = 0; i < stopIndex; i++)
            {
                if (intents[i].Kind == "move" && intents[i].ActorId.Equals(unitId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>落地 <see cref="MovementHost.Stop"/> 的效果：存在活动路径时清空路径、状态收回
        /// <see cref="MoveMode.Idle"/>；<paramref name="hadDiscardedMoveIntent"/> 为 true 时即便没有
        /// 活动路径也视为"确有东西被取消"（见 <see cref="HasPrecedingMoveIntent"/> 判断记录）。二者
        /// 皆否时静默返回，不触发 <see cref="MovementHost.OnMoveStopped"/>（幂等）。
        /// <para>
        /// 判断记录：本方法不检查 <see cref="IsLocked"/>——停止是"取消"操作而非"产生位移"的操作，
        /// 被控制效果禁止移动的单位仍然应该能够被停止（例如取消一条位移类控制效果结束后不该继续
        /// 生效的旧路径），与 <c>ApplyIntent</c>/<c>ContinuePathCore</c> 等真正推进位移的路径不同，
        /// 不受 <see cref="MovementOptions.MovementBudgetRule"/>/行动点预算约束（停止不消耗预算）。
        /// </para>
        /// </summary>
        private void ApplyStop(Unit unit, MoveStopReason reason, bool hadDiscardedMoveIntent)
        {
            var state = unit.MovementState;
            var hadPath = state.CurrentPath != null;

            if (!hadPath && !hadDiscardedMoveIntent)
            {
                return;
            }

            if (hadPath)
            {
                var oldMode = state.Mode;
                unit.MovementState = new MovementState(null, MoveMode.Idle, state.MovementLocked, 0);
                RaiseStateChangedIfNeeded(unit.EntityId, oldMode, MoveMode.Idle);
            }

            _movementHost.RaiseMoveStopped(unit.EntityId, unit.Position, reason);
        }

        /// <summary>寻路失败的统一处理（<see cref="BeginPathTo"/> 建路失败、<see cref="ReplanPath"/>
        /// 重算失败共用）：先同时触发 <see cref="MovementHost.OnMoveFailed"/>（保持原签名，既有订阅方
        /// 不受影响）与 <see cref="MovementHost.OnMoveFailedDetailed"/>（携带 <paramref name="reason"/>），
        /// 再按 <see cref="MovementOptions.PathFailurePolicy"/> 决定后续：<c>KeepOldPath</c>（默认）
        /// 不做任何状态改动，沿用既有路径（若有）继续推进，向后兼容本任务之前的唯一行为；<c>Stop</c>
        /// 清空路径并触发 <see cref="MovementHost.OnMoveStopped"/>（<see cref="MoveStopReason.PathFailed"/>）。
        /// 重入安全：本方法每次调用只触发一次失败回调，调用方（游戏层）若在回调内同步调用
        /// <see cref="MovementHost.Stop"/>/<see cref="MovementHost.Request"/>，二者都只是
        /// <see cref="Core.Foundation.SimLoop.IWorldSim.SubmitIntent"/>（下一 tick 才生效，见两方法
        /// 判断记录），不会在本次 <see cref="Execute"/> 内递归触发新的失败回调。</summary>
        private void HandlePathFailure(Unit unit, Vec2 from, Vec2 to, MoveFailReason reason)
        {
            _movementHost.RaiseMoveFailed(unit.EntityId, from, to);
            _movementHost.RaiseMoveFailedDetailed(unit.EntityId, from, to, reason);

            if (_options.PathFailurePolicy == PathFailurePolicy.Stop)
            {
                ApplyStop(unit, MoveStopReason.PathFailed, hadDiscardedMoveIntent: false);
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

            // 游戏侧通用能力需求（05 第 6 节勘误"零长度目标：不建路径、不动、不回调"）：与
            // ZeroLengthEpsilon 判断记录同一常量，短路在调用 FindPath 之前——无论是否装配导航，
            // "已经在目标点"都不构成一次有意义的移动请求。
            if ((target - from).Length <= ZeroLengthEpsilon)
            {
                return;
            }

            var oldState = unit.MovementState;
            IReadOnlyList<Vec2>? path = _navigation != null
                ? _navigation.FindPath(unit.MapId, from, target)
                : new List<Vec2> { from, target };

            if (path == null)
            {
                HandlePathFailure(unit, from, target, MoveFailReason.NoPath);
                return;
            }

            // 游戏侧通用能力需求（05 第 6 节勘误"替换"）：新路径整体替换仍在进行中的旧路径时，
            // 触发 OnMoveStopped(Replaced)——仅当旧路径确实存在（寻路失败分支已在上面提前返回，
            // 不会走到这里，因此这里的"替换"必然是一次成功的重新建路）。
            var hadPath = oldState.CurrentPath != null;
            var navVersion = _navigation?.GetBlockingVersion(unit.MapId) ?? 0;

            var oldMode = oldState.Mode;
            unit.MovementState = new MovementState(path, mode, oldState.MovementLocked, 0, navVersion);
            RaiseStateChangedIfNeeded(unit.EntityId, oldMode, mode);

            if (hadPath)
            {
                _movementHost.RaiseMoveStopped(unit.EntityId, from, MoveStopReason.Replaced);
            }

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
                unit.MovementState = new MovementState(path, state.Mode, state.MovementLocked, index, state.NavVersion);
            }
        }

        /// <summary>
        /// 游戏侧通用能力需求（05 第 6 节勘误 tick 步骤 4）：在继续推进 <paramref name="unit"/> 已有
        /// 路径之前，比较该地图当前的 <see cref="Core.Foundation.EngineAdapter.INavigation2D.GetBlockingVersion"/>
        /// 与建路时记录的 <see cref="MovementState.NavVersion"/>，按 <see cref="MovementOptions.BlockingChangePolicy"/>
        /// 处理变化。返回 <c>false</c> 表示本 tick 不应再调用 <see cref="ContinuePathCore"/>（路径已被
        /// 清空，见 <see cref="ApplyStop"/>/<see cref="ReplanPath"/>）；返回 <c>true</c> 表示可以（
        /// 照常）继续推进——涵盖"未装配导航"“导航不支持版本追踪（恒为 0）”“版本未变化”
        /// “<see cref="Core.Carriers.Unit.BlockingChangePolicy.Ignore"/>”“重验/重算后路径仍然存在”
        /// 等多种情形，调用方不需要关心具体是哪一种。
        /// <para>
        /// 判断记录（调用前提：<c>unit.MovementState.CurrentPath</c> 非 null）：本方法只在
        /// <see cref="Execute"/> 第二遍循环（continuous 模式下"本 tick 未收到新意图但仍在走旧路径"
        /// 的单位）前调用，不在 <see cref="BeginPathTo"/> 里对刚建立的新路径调用——新路径的
        /// <see cref="MovementState.NavVersion"/> 就是建路那一刻读到的当前版本，两者必然相等，调用
        /// 本方法只会是一次没有任何效果的空判断。
        /// </para>
        /// </summary>
        private bool RevalidateBlocking(Unit unit)
        {
            if (_navigation == null)
            {
                return true;
            }

            var state = unit.MovementState;
            var currentVersion = _navigation.GetBlockingVersion(unit.MapId);
            if (currentVersion == 0 || currentVersion == state.NavVersion)
            {
                // 0：导航实现不支持版本追踪（见 INavigation2D.GetBlockingVersion 判断记录"视为不做
                // 自动重验"）；相等：建路之后该地图的动态阻挡未发生变化，无需重验。
                return true;
            }

            switch (_options.BlockingChangePolicy)
            {
                case BlockingChangePolicy.Ignore:
                    return true;

                case BlockingChangePolicy.Stop:
                    ApplyStop(unit, MoveStopReason.BlockingChanged, hadDiscardedMoveIntent: false);
                    return false;

                case BlockingChangePolicy.Revalidate:
                    return RevalidateRemainingSegments(unit, currentVersion);

                case BlockingChangePolicy.Replan:
                default:
                    return ReplanPath(unit, currentVersion);
            }
        }

        /// <summary><see cref="Core.Carriers.Unit.BlockingChangePolicy.Revalidate"/>：对
        /// <paramref name="unit"/> 剩余路段（从当前实际位置到 <see cref="MovementState.PathIndex"/>
        /// 之后的每个路点依次相连）逐段调用 <see cref="Core.Foundation.EngineAdapter.INavigation2D.Raycast"/>
        /// ——与 <see cref="Core.Foundation.EngineAdapter.INavigation2D.FindPath"/> 共用同一套阻挡判定
        /// （见该接口第 1.8 节勘误）。全程无阻挡：只把 <see cref="MovementState.NavVersion"/> 更新为
        /// <paramref name="currentVersion"/>（标记"已按这个版本验证过"），路径/索引不变——比
        /// <see cref="Core.Carriers.Unit.BlockingChangePolicy.Replan"/> 更省一次寻路开销。任意一段受阻：
        /// 委托 <see cref="ReplanPath"/> 对剩余目标重新整体寻路。</summary>
        private bool RevalidateRemainingSegments(Unit unit, int currentVersion)
        {
            var state = unit.MovementState;
            var path = state.CurrentPath!;
            var index = state.PathIndex < 0 ? 0 : state.PathIndex;

            var blocked = false;
            var segStart = unit.Position;
            for (var i = index; i < path.Count; i++)
            {
                if (_navigation!.Raycast(unit.MapId, segStart, path[i]) != null)
                {
                    blocked = true;
                    break;
                }

                segStart = path[i];
            }

            if (!blocked)
            {
                unit.MovementState = new MovementState(state.CurrentPath, state.Mode, state.MovementLocked, state.PathIndex, currentVersion);
                return true;
            }

            return ReplanPath(unit, currentVersion);
        }

        /// <summary><see cref="Core.Carriers.Unit.BlockingChangePolicy.Replan"/>（默认策略）与
        /// <see cref="RevalidateRemainingSegments"/> 判定受阻后的共同落点：以 <paramref name="unit"/>
        /// 当前实际位置为起点、原路径最终目标点为终点，重新整体调用一次
        /// <see cref="Core.Foundation.EngineAdapter.INavigation2D.FindPath"/>。成功：新路径整体替换
        /// （<see cref="MovementState.PathIndex"/> 归零、<see cref="MovementState.NavVersion"/> 更新为
        /// <paramref name="currentVersion"/>），不触发 <see cref="MovementHost.OnMoveStopped"/>（这是
        /// 系统内部的自动重算，不是 <see cref="MovementHost.Request"/> 意义上的"替换"，见该事件判断
        /// 记录）。失败：经 <see cref="HandlePathFailure"/> 触发失败回调并按
        /// <see cref="MovementOptions.PathFailurePolicy"/> 处理。返回值即失败处理之后
        /// <c>unit.MovementState.CurrentPath</c> 是否仍非 null（据此决定调用方是否还应该继续本 tick
        /// 的 <see cref="ContinuePathCore"/>），不需要调用方重复判断 <see cref="PathFailurePolicy"/>
        /// 具体是哪一支。</summary>
        private bool ReplanPath(Unit unit, int currentVersion)
        {
            var state = unit.MovementState;
            var path = state.CurrentPath!;
            var target = path[path.Count - 1];
            var from = unit.Position;

            var newPath = _navigation!.FindPath(unit.MapId, from, target);
            if (newPath == null)
            {
                HandlePathFailure(unit, from, target, MoveFailReason.BlockingChanged);

                // 判断记录（不应重复触发失败回调）：HandlePathFailure 按 PathFailurePolicy 处理——
                // Stop 分支已经把路径清空（下面 CurrentPath 为 null，本方法调用点循环不会再碰这个
                // 单位）；KeepOldPath（默认）分支旧路径原样保留，但如果不把 NavVersion 前移到
                // currentVersion，下一 tick RevalidateBlocking 会发现"仍然是建路时的旧版本 vs 当前
                // 版本不等"，对同一次阻挡变化重新调用一次 ReplanPath、再次失败、再次触发回调——每
                // 个后续 tick 都重复一次，破坏"重新请求已被围住的目标应恰好触发一次
                // OnMoveFailedDetailed"契约（该 bug 已被 PlayMode 用例
                // ReRequestBlockedTarget_DefaultPolicy_KeepsAdvancingOldPath_FailsOnce 用真实数值
                // 复现：failCount 从预期 1 涨到 11，与"多跑 10 个 tick"逐 tick 各触发一次完全吻合）。
                // 把 NavVersion 前移到 currentVersion，标记"已经按这个版本处理过（虽然重算失败）"，
                // 语义与 RevalidateRemainingSegments 未受阻分支"只更新版本号"一致——下一次真正触发
                // 重验的前提是版本号再次变化（又一次 SetBlocking/Clear/BuildNavMesh）。
                var keptState = unit.MovementState;
                if (keptState.CurrentPath != null)
                {
                    unit.MovementState = new MovementState(
                        keptState.CurrentPath, keptState.Mode, keptState.MovementLocked,
                        keptState.PathIndex, currentVersion);
                }

                return unit.MovementState.CurrentPath != null;
            }

            unit.MovementState = new MovementState(newPath, state.Mode, state.MovementLocked, 0, currentVersion);
            return true;
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
