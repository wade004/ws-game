using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 运动档案与运动仲裁器（手感设计/02；ADR-0116）：<see cref="MovementTickHandler"/> 的运动层。
    /// <para>
    /// <b>开关与逐位等价</b>：只有 <see cref="MovementHost.Motion"/> 非空且 <see cref="MotionServices.Feel"/> 非空、本步是连续步且
    /// <c>dt &gt; 0</c> 时运动层才启用（离散步从不启用：离散步按回合移动预算结算，没有速度积分的语义）。未启用时本文件里所有入口
    /// 立即返回，主文件的既有代码路径一字未动，既有行为逐位不变。启用后，档案取"瞬时达速/瞬时转向/不滑墙"那组缺省值
    /// （<see cref="MotionProfile.LegacyEquivalent"/>，即 <c>feel.preset.rpg_classic</c>）时，方向移动的每一步算式与既有实现相同
    /// （<c>from + 单位方向 × (速率 × dt)</c>，速率恰为目标速度本身），结果逐位一致（测试锁定）。
    /// </para>
    /// <para>
    /// <b>每 tick 的流程</b>：<see cref="BeginMotionTick"/>（建本 tick 的仲裁上下文）→ 既有的 move_stop/move_displace 处理 →
    /// <see cref="MotionActionPass"/>（动作位移段/根运动，来源 <c>action|root_motion</c>）→ 既有的 move 意图与第二遍续推（输入位移、
    /// 路径、追击、受控位移，各自按仲裁结果放行或压制）→ <see cref="FinishMotionTick"/>（无意图单位的减速滑行、
    /// 运动学状态写回 <see cref="MovementState.Motion"/>）。步骤 4 之外没有任何代码写位置的既有约束不变。
    /// </para>
    /// <para>
    /// <b>优先级</b>（设计文档第 3.2 节）：<c>dead &gt; frozen &gt; forced &gt; staggered &gt; rooted &gt; action|root_motion &gt; regular</c>。
    /// 本类用"各来源入口处的放行判断"实现它：dead/frozen 压制全部位移（含受控位移）；forced（受控位移进行中）压制其余全部输入位移，
    /// 且<b>不受 rooted/staggered 影响</b>（forced 优先级更高，所以被控制的目标仍会被击退）；staggered/rooted 压制 regular 与 action；
    /// action 位移段胜出的 tick 压制 regular。每 tick 恰好一个来源产生位移。
    /// </para>
    /// </summary>
    public sealed partial class MovementTickHandler
    {
        private sealed class MotionTick
        {
            public long Stamp;
            public MotionProfile Profile;
            public MotionKinematics Prev;
            public Vec2 StartVelocity;
            public bool Dead;
            public bool Frozen;
            public bool Staggered;
            public bool Rooted;
            public bool ForcedAtStart;
            public ActionState? Action;
            public MotionMode BaseMode;
            public bool ActionActive;
            public double BaseSpeed = double.NaN;
            public Vec2? VelocityOut;
            public Vec2 Desired;
            public MotionSource Source;
            public bool ForcedThisTick;

            /// <summary>本 tick 开始时的路径与下标（体积裁决把位移缩短时退回路径下标用）。</summary>
            public IReadOnlyList<Vec2>? StartPath;
            public int StartPathIndex;

            /// <summary>本 tick 的动作位移穿过了别的单位的体积（不参与体积的成对撞停与重叠分离）。</summary>
            public bool PassedThroughUnits;
        }

        private readonly struct SuspendedPath
        {
            public readonly Vec2 Target;
            public readonly MoveMode Mode;

            public SuspendedPath(Vec2 target, MoveMode mode)
            {
                Target = target;
                Mode = mode;
            }
        }

        private bool _motionOn;
        private MotionServices? _mot;
        private IFeelJudgingSource? _feel;
        private double _motionDt;
        private long _motionStamp;
        private bool _suppressFacingWrite;
        private readonly Dictionary<Id, MotionTick> _motionTicks = new Dictionary<Id, MotionTick>();
        private readonly Dictionary<Id, MotionProfile> _motionProfiles = new Dictionary<Id, MotionProfile>();
        private readonly Dictionary<Id, SuspendedPath> _suspendedPaths = new Dictionary<Id, SuspendedPath>();
        private readonly List<Id> _pendingResume = new List<Id>();
        private readonly Dictionary<Id, (Id Cast, double Traveled)> _chargeTraveled = new Dictionary<Id, (Id, double)>();

        // 动作位移窗口"已经开过"的单位（动作位移段胜出过至少一个 tick，窗口还没有结束）：窗口结束的那个 tick 据此一次性处理末速度
        // （keep_momentum_on_motion_end）。不进存档，同 _chargeTraveled 的运行期簿记惯例。
        private readonly HashSet<Id> _actionMotionLive = new HashSet<Id>();

        // ================================================================== 每 tick 上下文

        /// <summary>本 tick 是否启用运动层（连续步、已装配运动服务与判定型手感入口、dt 为正）。</summary>
        private void BeginMotionTick(SimStep step, double dt)
        {
            _motionOn = false;
            _mot = _movementHost.Motion;
            _feel = _mot?.Feel;
            if (step.Kind != SimStepKind.Continuous || _feel == null || !(dt > 0.0))
            {
                return;
            }

            _motionOn = true;
            _motionDt = dt;
            _motionStamp++;
        }

        private MotionTick? GetMotionTick(Unit unit)
        {
            if (!_motionOn) return null;
            var id = unit.EntityId;
            if (_motionTicks.TryGetValue(id, out var t))
            {
                if (t.Stamp == _motionStamp) return t;
            }
            else
            {
                t = new MotionTick();
                _motionTicks[id] = t;
            }

            var view = _feel!.ResolveJudging(id);
            if (!_motionProfiles.TryGetValue(id, out var profile) || profile.Version != view.Version)
            {
                profile = MotionProfile.Read(view);
                _motionProfiles[id] = profile;
            }

            var mot = _mot!;
            t.Stamp = _motionStamp;
            t.Profile = profile;
            t.Prev = unit.MovementState.Motion;
            t.Dead = !unit.Alive;
            t.Frozen = !t.Dead && (mot.ActionClock?.IsPaused(id) ?? false);
            t.Staggered = mot.Stagger?.IsStaggered(id) ?? false;
            t.Rooted = IsLocked(unit);
            t.Action = mot.Actions?.Current(id);
            t.ForcedAtStart = unit.MovementState.Displacement.HasValue;
            t.ForcedThisTick = false;
            t.ActionActive = false;
            t.BaseSpeed = double.NaN;
            t.VelocityOut = null;
            t.Desired = Vec2.Zero;
            t.Source = MotionSource.None;
            t.BaseMode = DeriveBaseMode(t, t.ForcedAtStart);
            t.StartPath = unit.MovementState.CurrentPath;
            t.StartPathIndex = unit.MovementState.PathIndex;
            t.PassedThroughUnits = false;

            // 单位体积阻挡：第一个有体积的单位被处理前取下本 tick 的快照（别的有体积单位此刻都还没写位置）。
            if (profile.UnitBodyRadius > 0.0)
            {
                EnsureVolumeSnapshot();
            }

            // 模式切换时的速度：退出规则明确写"no"的模式清零动量（grounded/dead 的"none"不清零，
            // 所以从 grounded 进入 action 时保留速度，随后按 action 倍率减速）；顿帧叠加态下底层模式不变，速度保留。
            var v = t.Prev.Velocity;
            if (!t.Frozen && t.Prev.BaseMode != t.BaseMode && (v.X != 0.0 || v.Y != 0.0) &&
                mot.ModeRules.ZeroesMomentumOnExit(t.Prev.BaseMode, profile))
            {
                v = Vec2.Zero;
            }

            // 动作位移窗口结束（窗口曾胜出过、本 tick 不再胜出：窗口走完，或动作被取消/受控/死亡等中断）：末速度按
            // keep_momentum_on_motion_end 处理，缺省清零——位移距离等于声明值，后摇里不再滑行（手感设计/02 第 4 节）。
            // 顿帧叠加态下动作时钟暂停、窗口不前进，标志保留到解冻后。
            if (!t.Frozen && _actionMotionLive.Contains(id) && !ActionMotionWindowOpen(unit, t))
            {
                _actionMotionLive.Remove(id);
                if (!profile.KeepMomentumOnMotionEnd)
                {
                    v = Vec2.Zero;
                }
            }

            t.StartVelocity = v;
            return t;
        }

        /// <summary>本 tick 动作位移段是否有资格胜出：动作带位移声明、当前 tick 落在窗口内、没有更高优先级的来源压着。</summary>
        private static bool ActionMotionWindowOpen(Unit unit, MotionTick t)
        {
            if (!t.Action.HasValue || t.Dead || t.Frozen || t.Staggered || t.Rooted || unit.MovementState.Displacement.HasValue)
            {
                return false;
            }

            var act = t.Action.Value;
            return act.Motion.HasValue && act.Motion.Value.IsActiveAt(act.ElapsedTicks);
        }

        private static MotionMode DeriveBaseMode(MotionTick t, bool forced)
        {
            if (t.Dead) return MotionMode.Dead;
            if (forced) return MotionMode.Forced;
            if (t.Staggered) return MotionMode.Staggered;
            if (t.Rooted) return MotionMode.Rooted;
            return t.Action.HasValue ? MotionMode.Action : MotionMode.Grounded;
        }

        private double MotionBaseSpeed(Unit unit, MotionTick t)
        {
            if (double.IsNaN(t.BaseSpeed)) t.BaseSpeed = ResolveSpeed(unit.EntityId);
            return t.BaseSpeed;
        }

        /// <summary>该 tick 输入位移/路径/追击是否被仲裁压制（dead/frozen/forced/staggered/rooted，或动作位移段胜出）。</summary>
        private bool MotionBlocksRegular(Unit unit, MotionTick t) =>
            t.Dead || t.Frozen || t.Staggered || t.Rooted || t.ActionActive || t.ForcedThisTick ||
            unit.MovementState.Displacement.HasValue;

        /// <summary>未启用运动层返回 false；启用时返回该单位 regular 来源是否被压制。</summary>
        private bool MotionRegularBlocked(Unit unit)
        {
            var t = GetMotionTick(unit);
            return t != null && MotionBlocksRegular(unit, t);
        }

        // ================================================================== 速度/朝向小工具

        private double RatioFor(MotionTick t, MoveMode mode)
        {
            if (t.BaseMode == MotionMode.Action) return t.Profile.ActionMoveSpeedRatio;
            return mode == MoveMode.Walk ? t.Profile.WalkSpeedRatio : 1.0;
        }

        /// <summary>朝向写入口（追击朝目标等既有的"每次调用都朝目标"写法）：未启用运动层返回原值；启用时按模式规则与转向速率处理。</summary>
        private double MotionFacing(Unit unit, double targetFacing)
        {
            var t = GetMotionTick(unit);
            if (t == null) return targetFacing;
            if (!_mot!.ModeRules.AllowsTurn(t.BaseMode, t.Profile) || t.Frozen) return unit.Facing;
            return MotionMath.StepFacing(unit.Facing, targetFacing, t.Profile.TurnRateDegS, _motionDt);
        }

        // ================================================================== 输入方向位移（regular）

        /// <summary>
        /// 方向类位移的运动层入口：未启用返回 false（调用方走既有路径）。启用时无论放行与否都返回 true。被仲裁压制时只做"允许转向的模式
        /// 下转向"，不位移。
        /// </summary>
        private bool MotionDirectional(Unit unit, Vec2 direction, MoveMode mode, double dt)
        {
            var t = GetMotionTick(unit);
            if (t == null) return false;

            if (MotionBlocksRegular(unit, t))
            {
                MotionTurnOnly(unit, t, direction);
                return true;
            }

            ApplyMotionRegular(unit, t, direction, mode, dt, fromInput: true);
            return true;
        }

        /// <summary>被压制时的转向：模式规则允许转向（rooted 缺省允许）且有输入方向时，按转向速率朝输入方向转；不位移、不改速度状态。</summary>
        private void MotionTurnOnly(Unit unit, MotionTick t, Vec2 rawDirection)
        {
            var length = rawDirection.Length;
            if (length <= double.Epsilon) return;
            var desired = new Vec2(rawDirection.X / length, rawDirection.Y / length);
            t.Desired = desired;
            if (t.Frozen || t.Dead) return;
            // 本 tick 中途开始的受控位移（击退）已经把单位带进 forced 模式：forced 不允许转向，不能再按 tick 开始时的模式放行。
            if (unit.MovementState.Displacement.HasValue) return;
            if (!_mot!.ModeRules.AllowsTurn(t.BaseMode, t.Profile)) return;
            unit.Facing = MotionMath.StepFacing(unit.Facing, Math.Atan2(desired.Y, desired.X), t.Profile.TurnRateDegS, _motionDt);
        }

        /// <summary>
        /// 被控制（rooted）单位收到方向意图时的转向（<see cref="ApplyIntent"/> 在 <c>IsLocked</c> 短路处调用）：既有行为是整条意图被忽略，
        /// 运动层下 rooted 模式"不位移但可转向"。
        /// </summary>
        private void MotionLockedIntent(Unit unit, Intent intent)
        {
            var t = GetMotionTick(unit);
            if (t == null) return;
            if (TryReadDirection(intent.Args, out var direction))
            {
                MotionTurnOnly(unit, t, direction);
            }
        }

        /// <summary>
        /// 一个 tick 的 regular 积分与位移（输入方向或减速滑行）。算式与既有 <c>ApplyDirectionalMove</c> 同构：
        /// <c>from + 单位方向 × (速率 × dt)</c> → <c>Raycast</c> 截断（回退 <c>ArrivalEpsilon</c>）→ 截断后不可走则不位移 → 单位阻挡 → 写回。
        /// 差异只有：速率由积分器给出（缺省档案下恰为目标速度）、<c>wall_slide</c> 为真时截断后沿墙切向再裁决一次（最多一次）、
        /// 被阻挡后速度的法向分量置零（滑墙）或整体置零（不滑墙）。
        /// </summary>
        private void ApplyMotionRegular(Unit unit, MotionTick t, Vec2 rawDirection, MoveMode mode, double dt, bool fromInput)
        {
            var length = rawDirection.Length;
            var hasInput = fromInput && length > double.Epsilon;
            var desired = hasInput ? new Vec2(rawDirection.X / length, rawDirection.Y / length) : Vec2.Zero;
            var profile = t.Profile;
            var rules = _mot!.ModeRules;
            var accepts = rules.AcceptsInput(t.BaseMode, profile);
            var baseSpeed = MotionBaseSpeed(unit, t);

            var steer = hasInput && accepts ? desired : Vec2.Zero;
            var target = 0.0;
            if (hasInput && accepts)
            {
                var ratio = RatioFor(t, mode);
                target = ratio == 1.0 ? baseSpeed : baseSpeed * ratio;
                target = AirScaled(unit, target);
            }

            MotionMath.StepRegular(
                t.StartVelocity, steer, target, baseSpeed, profile, dt, _mot.Curves, out var dir, out var speed);

            t.Desired = desired;
            var from = unit.Position;
            var allowsTurn = hasInput && rules.AllowsTurn(t.BaseMode, profile);
            var moved = false;
            var velocity = new Vec2(dir.X * speed, dir.Y * speed);

            if (speed > 0.0)
            {
                var stepLen = speed * dt;
                var newPos = from + dir * stepLen;
                var blocked = false;
                var slid = false;
                var slideNormal = Vec2.Zero;
                var slideSecondBlocked = false;
                var slideSecondNormal = Vec2.Zero;
                var candidate = true;
                Vec2? wallVia = null; // 撞墙后沿墙滑动时位移实际走的折线拐点（体积扫掠逐段精确裁决）。

                if (_navigation != null)
                {
                    // 不滑墙时仍走 Raycast（既有查询，行为与此前逐字一致）；滑墙才需要法线，改用带法线的查询取命中点与法线。
                    var hitNormal = Vec2.Zero;
                    Vec2? hit;
                    if (profile.WallSlide)
                    {
                        var withNormal = NavRaycastWithNormal(unit, from, newPos);
                        hit = withNormal?.Point;
                        hitNormal = withNormal?.Normal ?? Vec2.Zero;
                    }
                    else
                    {
                        hit = NavRaycast(unit, from, newPos);
                    }

                    if (hit.HasValue)
                    {
                        blocked = true;
                        var hitDistance = (hit.Value - from).Length;
                        var pullBack = Math.Min(hitDistance, _options.ArrivalEpsilon);
                        newPos = from + dir * (hitDistance - pullBack);
                        if (profile.WallSlide &&
                            TrySlide(
                                unit, from, dir, stepLen, hitDistance, pullBack, hitNormal,
                                out var slidPos, out slideSecondBlocked, out slideSecondNormal))
                        {
                            newPos = slidPos;
                            slid = true;
                            slideNormal = hitNormal;
                            wallVia = from + dir * (hitDistance - pullBack);
                        }
                    }

                    if ((newPos - from).Length <= ZeroLengthEpsilon)
                    {
                        candidate = false;
                    }
                    else if (!_navigation.IsWalkable(unit.MapId, newPos))
                    {
                        candidate = false;
                    }
                }

                // 单位体积阻挡（手感设计/02 第 9 节）：本单位声明了 unit_body_radius 才进入；阻挡后沿 wall_slide 的口径滑开或停下。
                if (candidate && profile.UnitBodyRadius > 0.0)
                {
                    var volume = ClipPathByUnitVolumes(unit, profile.UnitBodyRadius, from, newPos, wallVia, profile.WallSlide);
                    if (volume.Blocked)
                    {
                        blocked = true;
                        newPos = volume.End;
                        slid = volume.Slid;
                        slideNormal = volume.Normal;
                        slideSecondBlocked = volume.SecondBlocked;
                        slideSecondNormal = volume.SecondNormal;
                        if ((newPos - from).Length <= ZeroLengthEpsilon)
                        {
                            candidate = false;
                        }
                    }
                }

                if (candidate && IsBlockedByUnit(unit.EntityId, newPos))
                {
                    candidate = false;
                    blocked = true;
                    slid = false;
                }

                if (candidate)
                {
                    _units.SetPosition(unit.EntityId, newPos);
                    EnqueueMoved(unit.EntityId, newPos);
                    moved = true;
                }

                if (blocked || !candidate)
                {
                    // 被阻挡：滑墙时去掉法向分量，否则整体置零，保证下一 tick 不会重新撞上同一堵墙产生抖动。
                    velocity = slid ? RemoveNormalComponent(velocity, slideNormal) : Vec2.Zero;
                    if (slid && slideSecondBlocked)
                    {
                        // 滑动那一段又撞上第二面墙（内角）：速度同样去掉沿第二个法线的分量；法线不可用，或去完之后仍指向第一面墙
                        // （夹在内角里没有可行方向）时整体置零。
                        var along = slideSecondNormal.X == 0.0 && slideSecondNormal.Y == 0.0
                            ? Vec2.Zero
                            : RemoveNormalComponent(velocity, slideSecondNormal);
                        velocity = along.X * slideNormal.X + along.Y * slideNormal.Y < -1e-12 ? Vec2.Zero : along;
                    }
                }
            }

            if (hasInput)
            {
                if (allowsTurn && (moved || profile.TurnRateDegS > 0.0))
                {
                    unit.Facing = MotionMath.StepFacing(
                        unit.Facing, Math.Atan2(desired.Y, desired.X), profile.TurnRateDegS, dt);
                }

                if (moved)
                {
                    var oldMode = unit.MovementState.Mode;
                    unit.MovementState = new MovementState(null, mode, unit.MovementState.MovementLocked, 0);
                    RaiseStateChangedIfNeeded(unit.EntityId, oldMode, mode);
                }
            }

            t.VelocityOut = velocity;
            t.Source = moved ? MotionSource.Regular : MotionSource.None;
        }

        /// <summary>
        /// <c>wall_slide</c>：截断后把剩余位移沿阻挡面切向再裁决一次（最多一次，不递归）。法线由调用方从带法线的射线查询
        /// （<see cref="Core.Foundation.EngineAdapter.INavigation2D.RaycastWithNormal"/>）取得——任意朝向的墙面、擦过的墙角都能给出切向；
        /// 法线为零向量（实现不能确定，或起点已在阻挡内部）时不滑动、整体停下，本类不自己猜一个方向。
        /// 切向位移 = 剩余位移 − 法向分量（<c>rem − (rem·n)n</c>）；轴对齐法线 <c>(±1, 0)</c>/<c>(0, ±1)</c> 下这条算式的每一步都是精确运算，
        /// 结果与此前逐轴处理（轴向探测）逐位相同。切向那一段仍经射线查询裁决：内角处被第二面墙截断、不穿模，并把第二个命中面的法线
        /// 经 <paramref name="secondNormal"/> 交给调用方（<paramref name="secondBlocked"/> 为真时速度也要去掉沿它的分量，内角里两个法线
        /// 去完速度即为零）。
        /// </summary>
        private bool TrySlide(
            Unit unit, Vec2 from, Vec2 dir, double stepLen, double hitDistance, double pullBack, Vec2 normal,
            out Vec2 endPos, out bool secondBlocked, out Vec2 secondNormal)
        {
            endPos = from;
            secondBlocked = false;
            secondNormal = Vec2.Zero;
            if (_navigation == null) return false;
            if (normal.X == 0.0 && normal.Y == 0.0) return false;

            var advance = hitDistance - pullBack;
            var p1 = from + dir * advance;
            var remainingLen = stepLen - advance;
            if (remainingLen <= ZeroLengthEpsilon) return false;

            var rem = dir * remainingLen;
            var into = rem.X * normal.X + rem.Y * normal.Y;
            if (into >= 0.0) return false; // 剩余位移没有指向墙面：不是一次"撞上"，不做切向改写。

            var slide = new Vec2(rem.X - normal.X * into, rem.Y - normal.Y * into);
            var slideLen = slide.Length;
            if (slideLen <= ZeroLengthEpsilon) return false;

            var p2 = p1 + slide;
            var hit2 = NavRaycastWithNormal(unit, p1, p2);
            if (hit2.HasValue)
            {
                var d2 = (hit2.Value.Point - p1).Length;
                var pb2 = Math.Min(d2, _options.ArrivalEpsilon);
                p2 = p1 + slide * ((d2 - pb2) / slideLen);
                secondBlocked = true;
                secondNormal = hit2.Value.Normal;
            }

            endPos = p2;
            return true;
        }

        /// <summary>速度去掉沿 <paramref name="normal"/> 的分量（<c>v − (v·n)n</c>）：滑墙后下一 tick 不再把速度推向同一堵墙。</summary>
        private static Vec2 RemoveNormalComponent(Vec2 velocity, Vec2 normal)
        {
            var into = velocity.X * normal.X + velocity.Y * normal.Y;
            return new Vec2(velocity.X - normal.X * into, velocity.Y - normal.Y * into);
        }

        // ================================================================== 路径跟随/追击（regular）

        /// <summary>
        /// 路径跟随与追击的运动层预算：启用且 <c>apply_to_path_following</c> 为真时，速率由积分器给出（目标速度 = 属性速度 × 倍率，
        /// <c>arrival_decel</c> 为真时在终点前按 <c>decel_ms</c> 限速），返回本 tick 的位移预算 <c>速率 × dt</c>；否则返回既有的
        /// <c>属性速度 × dt</c>。<paramref name="t"/> 为 null 表示未启用。
        /// </summary>
        private double MotionPathBudget(
            Unit unit, MotionTick? t, MoveMode mode, IReadOnlyList<Vec2> path, int index, double dt, out bool integrated)
        {
            integrated = false;
            if (t == null || !t.Profile.ApplyToPathFollowing)
            {
                return AirScaled(unit, ResolveSpeed(unit.EntityId)) * dt;
            }

            var profile = t.Profile;
            var baseSpeed = MotionBaseSpeed(unit, t);
            var accepts = _mot!.ModeRules.AcceptsInput(t.BaseMode, profile);
            var ratio = RatioFor(t, mode);
            var target = !accepts ? 0.0 : AirScaled(unit, ratio == 1.0 ? baseSpeed : baseSpeed * ratio);
            if (profile.ArrivalDecel && target > 0.0)
            {
                var remainingLength = 0.0;
                var cursor = unit.Position;
                for (var i = index < 0 ? 0 : index; i < path.Count; i++)
                {
                    remainingLength += (path[i] - cursor).Length;
                    cursor = path[i];
                }

                target = MotionMath.ArrivalLimitedSpeed(target, baseSpeed, profile.DecelMs, remainingLength);
            }

            var speed = MotionMath.ApproachSpeed(
                t.StartVelocity.Length, target, baseSpeed, profile, dt, _mot.Curves);
            integrated = true;
            return speed * dt;
        }

        /// <summary>
        /// 路径/追击推进完成后记录运动学：<paramref name="integrated"/> 为真且未到达终点时速度 = 末段方向 × 积分速率；到达终点速度归零
        /// （终点就是停止点，不再滑行越过路点）；档案不作用于路径跟随时速度取实际位移/dt（仅供步态派生）。
        /// </summary>
        private void MotionRecordPath(
            Unit unit, MotionTick t, Vec2 from, Vec2 to, bool arrived, bool integrated, double speedUsed, double dt)
        {
            if (!(dt > 0.0)) return;
            var moved = to - from;
            var movedLen = moved.Length;
            Vec2 velocity;
            if (arrived || movedLen <= 0.0)
            {
                velocity = Vec2.Zero;
            }
            else if (integrated)
            {
                velocity = new Vec2(moved.X / movedLen * speedUsed, moved.Y / movedLen * speedUsed);
            }
            else
            {
                velocity = new Vec2(moved.X / dt, moved.Y / dt);
            }

            t.VelocityOut = velocity;
            t.Desired = movedLen > 0.0 ? new Vec2(moved.X / movedLen, moved.Y / movedLen) : Vec2.Zero;
            t.Source = movedLen > 0.0 ? MotionSource.Regular : MotionSource.None;
        }

        // ================================================================== 受控位移（forced）

        /// <summary>
        /// 受控位移推进的运动层包装：dead/frozen 压制（frozen 期间不推进、状态保留，解冻后继续）；forced 优先于 rooted/staggered，所以
        /// 控制状态不再结束位移；转向按 forced 模式规则（缺省不允许，所以击退不改变朝向）；带曲线的位移（击退 <c>ease_out</c>）走曲线推进。
        /// </summary>
        private void MotionAdvanceDisplacement(Unit unit, MotionTick t, double dt, bool isDiscrete)
        {
            if (t.Frozen) return;

            var start = unit.Position;
            var restore = _suppressFacingWrite;
            _suppressFacingWrite = !_mot!.ModeRules.AllowsTurn(MotionMode.Forced, t.Profile);
            try
            {
                var disp = unit.MovementState.Displacement;
                if (disp.HasValue && disp.Value.Curve != null)
                {
                    AdvanceCurvedDisplacement(unit, t, dt);
                }
                else
                {
                    AdvanceDisplacementCore(unit, dt, isDiscrete);
                }
            }
            finally
            {
                _suppressFacingWrite = restore;
            }

            t.ForcedThisTick = true;
            t.Source = MotionSource.Forced;
            var moved = unit.Position - start;
            t.VelocityOut = new Vec2(moved.X / dt, moved.Y / dt);
            t.Desired = Vec2.Zero;
        }

        /// <summary>带曲线的受控位移（击退）：位置 = 起点 + (终点 − 起点) × 曲线(已过时间/总时长)，每 tick 从当前位置走到曲线给出的点，经导航裁决截断。</summary>
        private void AdvanceCurvedDisplacement(Unit unit, MotionTick t, double dt)
        {
            var state = unit.MovementState;
            var disp = state.Displacement!.Value;

            if (!unit.Alive)
            {
                EndDisplacement(unit, MoveStopReason.DisplacementCasterDead);
                return;
            }

            var elapsed = disp.ElapsedSeconds + dt;
            var progress = disp.DurationSeconds > 0.0 ? Math.Min(1.0, elapsed / disp.DurationSeconds) : 1.0;
            var fraction = MotionMath.EvalCurve(disp.Curve!, progress, _mot!.Curves);
            var reaches = progress >= 1.0;
            var delta = disp.Target - disp.Origin;
            var to = reaches ? disp.Target : disp.Origin + delta * fraction;
            var from = unit.Position;
            var volumeRadius = t.Profile.UnitBodyRadius;

            if (_navigation != null)
            {
                var hit = NavRaycast(unit, from, to);
                if (hit.HasValue)
                {
                    if (disp.Blocking == DisplacementBlockingPolicy.Revert)
                    {
                        WriteDisplacementPosition(unit, disp.Origin);
                    }
                    else
                    {
                        var travel = to - from;
                        var travelLen = travel.Length;
                        var hitDistance = (hit.Value - from).Length;
                        var pullBack = Math.Min(hitDistance, _options.ArrivalEpsilon);
                        var back = travelLen > ZeroLengthEpsilon
                            ? new Vec2(travel.X / travelLen, travel.Y / travelLen) * pullBack
                            : Vec2.Zero;
                        var stopPos = hit.Value - back;
                        if (volumeRadius > 0.0)
                        {
                            // 地形截断点之前若先撞上别的单位体积，停在体积前（单位体积阻挡，手感设计/02 第 9 节）。
                            var stopClip = ClipByUnitVolumes(unit, volumeRadius, from, stopPos, false);
                            stopPos = stopClip.End;
                            QueueForcedPush(unit, t.Profile, stopClip, disp);
                        }

                        WriteDisplacementPosition(unit, stopPos);
                    }

                    EndDisplacement(unit, MoveStopReason.DisplacementBlocked);
                    return;
                }
            }

            if (volumeRadius > 0.0)
            {
                // 受控位移（击退/冲锋）同样被单位体积挡住：受阻按位移自带的 blocking 策略（Stop 停在体积前，Revert 退回起点），
                // 以 DisplacementBlocked 结束；不推开被撞单位。
                var volume = ClipByUnitVolumes(unit, volumeRadius, from, to, false);
                if (volume.Blocked)
                {
                    WriteDisplacementPosition(unit, disp.Blocking == DisplacementBlockingPolicy.Revert ? disp.Origin : volume.End);
                    QueueForcedPush(unit, t.Profile, volume, disp);
                    EndDisplacement(unit, MoveStopReason.DisplacementBlocked);
                    return;
                }
            }

            WriteDisplacementPosition(unit, to);
            if (reaches)
            {
                EndDisplacement(unit, MoveStopReason.DisplacementArrived);
                return;
            }

            var refreshed = unit.MovementState;
            unit.MovementState = new MovementState(null, refreshed.Mode, refreshed.MovementLocked, 0, 0, disp.WithElapsed(elapsed));
        }

        /// <summary>
        /// <see cref="BeginDisplacement"/> 的运动层前置：返回 true 表示继续开始新位移；false 表示本次忽略。已有位移时仅击退（带
        /// <c>knockback</c> 标记）且 <see cref="MovementOptions.KnockbackStack"/> 为 <c>Replace</c> 才替换（旧位移以
        /// <see cref="MoveStopReason.Replaced"/> 结束）；<c>forced</c> 优先于 rooted，所以被控制的单位也可以被击退。
        /// </summary>
        private bool MotionBeginDisplacementGate(Unit unit, Intent intent)
        {
            var t = GetMotionTick(unit);
            if (t == null) return true;
            // 顿帧（frozen）期间可以开始受控位移（命中顿帧与击退常在同一 tick 到达），只是顿帧结束前不推进。
            if (t.Dead) return false;

            if (unit.MovementState.Displacement.HasValue)
            {
                if (!IsKnockback(intent) || _options.KnockbackStack == KnockbackStackPolicy.Ignore) return false;
                _movementHost.RaiseMoveStopped(unit.EntityId, unit.Position, MoveStopReason.Replaced);
            }

            return true;
        }

        private static bool IsKnockback(Intent intent) =>
            intent.Args.TryGetValue("knockback", out var kb) && kb is JsonBool flag && flag.Value;

        /// <summary>
        /// 运动层下构造受控位移状态：带 <c>curve</c> 的位移（击退 <c>ease_out</c>）按总时长走曲线；起点取开始处理那一刻的当前位置
        /// （意图提交到处理之间单位可能已被输入位移移动过，沿用提交时的起点会让位置在第一拍回跳），位移向量不变。
        /// </summary>
        private ControlledDisplacementState MotionBuildDisplacement(
            Unit unit, Intent intent, Vec2 origin, Vec2 target, double speed, DisplacementBlockingPolicy blocking, double sampleStep)
        {
            var args = intent.Args;
            string? curve = null;
            var duration = 0.0;
            if (args.TryGetValue("curve", out var c) && c is JsonString cs && !string.IsNullOrEmpty(cs.Value))
            {
                curve = cs.Value;
                if (args.TryGetValue("duration", out var d) && d is JsonNumber dn && dn.Value > 0.0)
                {
                    duration = dn.Value;
                }
                else if (IsKnockback(intent))
                {
                    duration = _options.KnockbackDurationSeconds;
                }
                else
                {
                    duration = (target - origin).Length / speed;
                }

                var vector = target - origin;
                origin = unit.Position;
                target = origin + vector;
            }

            return new ControlledDisplacementState(origin, target, speed, blocking, sampleStep, curve, duration, 0.0);
        }

        /// <summary>开始受控位移时按 <see cref="MovementOptions.ResumePathAfterForced"/> 记下挂起路径的最终目标（只在 <c>Resume</c> 策略下）。</summary>
        private void MotionSuspendPath(Unit unit, MovementState oldState)
        {
            if (!_motionOn || _options.ResumePathAfterForced != ResumePathAfterForcedPolicy.Resume) return;
            if (oldState.Displacement.HasValue) return; // 已在受控位移中（替换击退）：沿用最初挂起的那条
            if (oldState.CurrentPath == null || oldState.CurrentPath.Count == 0) return;
            var target = oldState.RequestedTarget ?? oldState.CurrentPath[oldState.CurrentPath.Count - 1];
            _suspendedPaths[unit.EntityId] = new SuspendedPath(target, oldState.Mode);
        }

        /// <summary>受控位移结束时的运动层处理：自然结束（到达/受阻）按策略恢复路径，其余原因丢弃挂起记录。</summary>
        private void MotionEndDisplacement(Unit unit, MoveStopReason reason)
        {
            if (!_suspendedPaths.ContainsKey(unit.EntityId)) return;
            if ((reason == MoveStopReason.DisplacementArrived || reason == MoveStopReason.DisplacementBlocked) && _motionOn)
            {
                _pendingResume.Add(unit.EntityId);
            }
            else
            {
                _suspendedPaths.Remove(unit.EntityId);
            }
        }

        // ================================================================== 动作位移（action / root_motion）

        /// <summary>
        /// 动作位移段：对处于 <c>action</c> 模式且当前 tick 落在 <c>motion_start..motion_end</c> 内的单位，按动作快照给出的方向、距离、曲线
        /// 算出本 tick 位移（<c>code</c> 驱动）或取适配层累加的根位移（<c>root_motion</c> 驱动，不支持则报错不降级），经导航裁决后写回。
        /// 胜出的 tick 压制该单位的输入位移（<c>action|root_motion &gt; regular</c>）；forced/frozen/staggered/rooted/dead 压制它。
        /// </summary>
        private void MotionActionPass(IWorldSim world, double dt)
        {
            if (!_motionOn || _mot!.Actions == null) return;

            foreach (var entity in world.QueryEntities(new EntityFilter(predicate: e => e is Unit)))
            {
                var unit = (Unit)entity;
                if (world.IsPendingDestruction(unit.EntityId)) continue;
                var t = GetMotionTick(unit);
                if (t == null || !t.Action.HasValue) continue;
                var act = t.Action.Value;
                if (!ActionMotionWindowOpen(unit, t)) continue;
                var m = act.Motion!.Value;

                t.ActionActive = true;
                _actionMotionLive.Add(unit.EntityId);
                ApplyActionMotion(world, unit, t, act, m, dt);
            }
        }

        private void ApplyActionMotion(IWorldSim world, Unit unit, MotionTick t, ActionState act, ActionMotionState m, double dt)
        {
            var decl = m.Declaration;
            var from = unit.Position;
            var dir = m.Direction;
            double step;
            var source = MotionSource.Action;

            if (decl.Driver == ActionMotionDriver.RootMotion)
            {
                var src = _mot!.RootMotion;
                if (src == null || !src.SupportsRootMotion)
                {
                    throw new InvalidOperationException(
                        $"动作 \"{act.SkillId}\" 声明了 root_motion 驱动的位移，但适配层没有提供根运动能力（supportsRootMotion）；" +
                        "不静默改为代码驱动（手感设计/02 第 4 节）");
                }

                var delta = src.ConsumeRootMotionDelta(unit.EntityId);
                step = delta.Length;
                if (step > 0.0) dir = new Vec2(delta.X / step, delta.Y / step);
                source = MotionSource.RootMotion;
            }
            else
            {
                var len = Math.Max(1, m.EndTick - m.StartTick);
                var p0 = (double)(act.ElapsedTicks - m.StartTick) / len;
                var p1 = (double)(act.ElapsedTicks + 1 - m.StartTick) / len;
                step = m.DistanceWorld *
                       (MotionMath.EvalCurve(decl.Curve, p1, _mot!.Curves) - MotionMath.EvalCurve(decl.Curve, p0, _mot.Curves));

                var toward = decl.Kind == ActionMotionKind.Charge || decl.Direction == ActionMotionDirection.TowardTarget;
                if (toward && m.TargetId.HasValue && world.GetEntity(m.TargetId.Value) is Unit target && target.Alive)
                {
                    var toTarget = target.Position - from;
                    var dist = toTarget.Length;
                    if (dist > ZeroLengthEpsilon)
                    {
                        var maxTurn = decl.MaxTurnDeg * (Math.PI / 180.0);
                        var turn = MotionMath.WrapAngle(Math.Atan2(toTarget.Y, toTarget.X) - unit.Facing);
                        turn = Math.Max(-maxTurn, Math.Min(maxTurn, turn));
                        var angle = unit.Facing + turn;
                        dir = new Vec2(Math.Cos(angle), Math.Sin(angle));
                        if (decl.Kind == ActionMotionKind.Charge)
                        {
                            unit.Facing = angle;
                        }
                    }

                    step = Math.Min(step, Math.Max(0.0, dist - m.StopDistanceWorld));
                }

                if (decl.Kind == ActionMotionKind.Charge)
                {
                    var traveled = _chargeTraveled.TryGetValue(unit.EntityId, out var rec) && rec.Cast.Equals(act.CastInstanceId)
                        ? rec.Traveled
                        : 0.0;
                    step = Math.Min(step, Math.Max(0.0, m.DistanceWorld - traveled));
                }
            }

            t.Source = source;
            t.Desired = Vec2.Zero;
            var movedVec = Vec2.Zero;

            if (step > 0.0)
            {
                var newPos = from + dir * step;
                var candidate = true;
                Vec2? wallVia = null;
                if (_navigation != null)
                {
                    var slideBlocking = decl.Blocking == ActionMotionBlocking.Slide;
                    var hitNormal = Vec2.Zero;
                    Vec2? hit;
                    if (slideBlocking)
                    {
                        var withNormal = NavRaycastWithNormal(unit, from, newPos);
                        hit = withNormal?.Point;
                        hitNormal = withNormal?.Normal ?? Vec2.Zero;
                    }
                    else
                    {
                        hit = NavRaycast(unit, from, newPos);
                    }

                    if (hit.HasValue)
                    {
                        var hitDistance = (hit.Value - from).Length;
                        var pullBack = Math.Min(hitDistance, _options.ArrivalEpsilon);
                        newPos = from + dir * (hitDistance - pullBack);
                        if (slideBlocking &&
                            TrySlide(unit, from, dir, step, hitDistance, pullBack, hitNormal, out var slidPos, out _, out _))
                        {
                            newPos = slidPos;
                            wallVia = from + dir * (hitDistance - pullBack);
                        }
                    }

                    if ((newPos - from).Length <= ZeroLengthEpsilon || !_navigation.IsWalkable(unit.MapId, newPos))
                    {
                        candidate = false;
                    }
                }

                // 单位体积阻挡：档案声明 dodge_through_units 且位移种类在 pass_through_motion_kinds 里（缺省 dash、step_back）时穿过体积；
                // 其余位移恒被挡，受阻按动作声明的 blocking（stop 停在体积前，slide 沿体积切向滑开）。
                var volumeRadius = t.Profile.UnitBodyRadius;
                var passesThrough = t.Profile.DodgeThroughUnits && t.Profile.PassesThroughKind(decl.Kind);
                if (candidate && volumeRadius > 0.0 && passesThrough)
                {
                    t.PassedThroughUnits = true;
                }

                if (candidate && volumeRadius > 0.0 && !passesThrough)
                {
                    var volume = ClipPathByUnitVolumes(unit, volumeRadius, from, newPos, wallVia, decl.Blocking == ActionMotionBlocking.Slide);
                    if (volume.Blocked)
                    {
                        newPos = volume.End;
                        if ((newPos - from).Length <= ZeroLengthEpsilon)
                        {
                            candidate = false;
                        }
                    }
                }

                if (candidate && IsBlockedByUnit(unit.EntityId, newPos))
                {
                    candidate = false;
                }

                if (candidate)
                {
                    _units.SetPosition(unit.EntityId, newPos);
                    EnqueueMoved(unit.EntityId, newPos);
                    movedVec = newPos - from;
                }
            }

            if (decl.Kind == ActionMotionKind.Charge)
            {
                var prior = _chargeTraveled.TryGetValue(unit.EntityId, out var rec2) && rec2.Cast.Equals(act.CastInstanceId)
                    ? rec2.Traveled
                    : 0.0;
                _chargeTraveled[unit.EntityId] = (act.CastInstanceId, prior + movedVec.Length);
            }

            t.VelocityOut = new Vec2(movedVec.X / dt, movedVec.Y / dt);
        }

        // ================================================================== 每 tick 收尾

        /// <summary>
        /// 收尾：恢复路径（<c>resume_path_after_forced</c>）、无意图单位的减速滑行、运动学状态写回 <see cref="MovementState.Motion"/>。
        /// </summary>
        private void FinishMotionTick(IWorldSim world, double dt)
        {
            if (!_motionOn) return;

            for (var i = 0; i < _pendingResume.Count; i++)
            {
                var id = _pendingResume[i];
                if (!_suspendedPaths.TryGetValue(id, out var suspended)) continue;
                _suspendedPaths.Remove(id);
                if (world.GetEntity(id) is Unit u && u.Alive && !world.IsPendingDestruction(id) && !u.MovementState.Displacement.HasValue)
                {
                    // dt = 0：只重新建立路径状态（从当前位置重规划到挂起时的最终目标），下一 tick 才开始沿它移动。
                    BeginPathTo(u, suspended.Target, suspended.Mode, 0.0, false);
                }
            }

            _pendingResume.Clear();

            var rules = _mot!.ModeRules;
            var unitCount = 0;
            foreach (var entity in world.QueryEntities(new EntityFilter(predicate: e => e is Unit)))
            {
                unitCount++;
                var unit = (Unit)entity;
                if (world.IsPendingDestruction(unit.EntityId)) continue;
                var t = GetMotionTick(unit);
                if (t == null) continue;

                var state = unit.MovementState;
                var baseMode = DeriveBaseMode(t, t.ForcedThisTick || state.Displacement.HasValue);

                if (!t.Frozen && !t.VelocityOut.HasValue)
                {
                    var accepts = rules.AcceptsInput(baseMode, t.Profile);
                    var startsAtRest = t.StartVelocity.X == 0.0 && t.StartVelocity.Y == 0.0;
                    if (baseMode == MotionMode.Dead || baseMode == MotionMode.Forced ||
                        (!accepts && baseMode != MotionMode.Action) || startsAtRest)
                    {
                        t.VelocityOut = Vec2.Zero;
                    }
                    else if (state.Displacement.HasValue || state.Chase.HasValue || state.CurrentPath != null)
                    {
                        // 有未走完的路径/追击但本 tick 没有推进（例如阻挡重验失败）：速度按制动衰减，不位移。
                        MotionMath.StepRegular(
                            t.StartVelocity, Vec2.Zero, 0.0, MotionBaseSpeed(unit, t), t.Profile, dt, _mot.Curves,
                            out var d, out var s);
                        t.VelocityOut = new Vec2(d.X * s, d.Y * s);
                    }
                    else
                    {
                        // 没有输入也没有路径：残余速度沿原方向按 decel 滑行到零（decel_ms = 0 时直接归零，不位移）。
                        ApplyMotionRegular(unit, t, Vec2.Zero, MoveMode.Idle, dt, fromInput: false);
                    }
                }

                var velocity = t.Frozen ? t.Prev.Velocity : (t.VelocityOut ?? Vec2.Zero);
                var mode = t.Frozen ? MotionMode.Frozen : baseMode;
                var source = t.Frozen || t.Dead ? MotionSource.None : t.Source;
                var baseSpeed = double.IsNaN(t.BaseSpeed) ? t.Prev.BaseSpeed : t.BaseSpeed;
                var kin = new MotionKinematics(velocity, t.Frozen ? t.Prev.DesiredDirection : t.Desired, mode, baseMode, source, baseSpeed);

                // 单位体积阻挡：有体积的单位的运动学状态等阶段 B（成对撞停可能缩短它的位移）算完再写。
                if (VolumeSnapshotCurrent && _volumeById.ContainsKey(unit.EntityId))
                {
                    _deferredKin.Add(new DeferredKin(unit, t, kin));
                    continue;
                }

                var cur = unit.MovementState.Motion;
                if (!SameKinematics(cur, kin))
                {
                    unit.MovementState = unit.MovementState.WithMotion(kin);
                }
            }

            ResolveUnitVolumes(world, dt);
            WriteDeferredKin();

            // 已销毁单位的残留条目清理（条目数远超在场单位时才扫一遍）。
            if (_motionTicks.Count > unitCount + 64)
            {
                var stale = new List<Id>();
                foreach (var kv in _motionTicks)
                {
                    if (kv.Value.Stamp != _motionStamp) stale.Add(kv.Key);
                }

                for (var i = 0; i < stale.Count; i++)
                {
                    _motionTicks.Remove(stale[i]);
                    _motionProfiles.Remove(stale[i]);
                    _chargeTraveled.Remove(stale[i]);
                    _actionMotionLive.Remove(stale[i]);
                    _suspendedPaths.Remove(stale[i]);
                }
            }
        }

        private static bool SameKinematics(in MotionKinematics a, in MotionKinematics b) =>
            a.Velocity.Equals(b.Velocity) && a.DesiredDirection.Equals(b.DesiredDirection) && a.Mode == b.Mode &&
            a.BaseMode == b.BaseMode && a.Source == b.Source && a.BaseSpeed.Equals(b.BaseSpeed);
    }
}
