using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Feel;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 手感落地：时间线模式的空间命中（手感设计/03 第 2.2～2.5 节，ADR-0114）。本文件是 <see cref="CastPipeline"/> 时间线部分的第二块：
    /// 命中路径选择、<c>marker</c>/<c>continuous</c> 两种空间解析、攻击实例去重、无敌前置检查、<c>combat.hit_confirmed</c>、
    /// <c>release</c> 标记发射投射物、目标辅助。
    /// <para>
    /// 判断记录（命中路径由技能数据选择）：技能声明了 <c>timeline</c> 且目标选择链（<c>target_shape_ref</c>）声明了 <c>shape</c>
    /// 时走空间命中（<see cref="TimelineHitMode.Auto"/>）；链没有 <c>shape</c> 保持 S3a 的 instant 结算（<c>hit</c> 标记处按链解析并结算，
    /// 链无 <c>shape</c> 时 <c>TargetHost</c> 退化为缺省半径的圆）。<c>hit_mode</c> 可显式覆盖。没有 <c>timeline</c> 的技能根本不进本文件。
    /// </para>
    /// <para>
    /// 判断记录（显式目标不参与空间命中）：空间命中的候选完全由目标选择链按攻击方位姿解析，施法请求给的显式目标只用于
    /// 位移的 <c>toward_target</c>/<c>charge</c> 与链的 <c>current_target</c> 来源——否则"显式点选一个不在形状里的目标"会无视形状被打中。
    /// </para>
    /// <para>
    /// 判断记录（回避类也进命中集合）：无敌导致的 <c>Invulnerable</c> 同样记入攻击实例的命中集合——"同一攻击实例对同一目标只判定一次"，
    /// 否则 continuous 在无敌窗口内每个采样都会重复发一次回避事件。目标的无敌在这一挥里被判定过一次，同一段内不再重判。
    /// </para>
    /// </summary>
    public sealed partial class CastPipeline
    {
        /// <summary>单个时间区间内最多采样次数（防止形状极小而位移极大时的采样爆炸；超出按上限均匀采样，见 skill README 已知局限）。</summary>
        private const int MaxSamplesPerSpan = 256;

        /// <summary>命中几何：攻击方位姿与（可选）形状，用来算接触点/法线/世界方向。</summary>
        private readonly struct HitGeometry
        {
            public Vec2 Position { get; }

            public double Facing { get; }

            /// <summary>true = 接触点取形状与目标的最近点（continuous）；false = 取目标登记位置（marker/instant，手感设计/03 第 2.4 节）。</summary>
            public bool UseClosest { get; }

            public Shape Template { get; }

            public HitGeometry(Vec2 position, double facing, bool useClosest, Shape template)
            {
                Position = position;
                Facing = facing;
                UseClosest = useClosest;
                Template = template;
            }
        }

        private HitGeometry PoseGeometry(Id casterId, bool closest) =>
            new HitGeometry(_units.GetPosition(casterId), _units.GetFacing(casterId), closest, default);

        private static bool HasMarker(TimelineDef tl, string name)
        {
            for (var i = 0; i < tl.Markers.Count; i++)
            {
                if (tl.Markers[i].Name == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>hit 标记处结算的效果子集：声明了 release 标记的动作，投射物效果在 release 处发射，不在 hit 处重复。</summary>
        private EffectSubset EffectSubsetFor(ActionRun run) => run.HasReleaseMarker ? EffectSubset.NonProjectile : EffectSubset.All;

        // -----------------------------------------------------------------
        // 命中路径选择
        // -----------------------------------------------------------------

        /// <summary>该动作是否走空间命中（首次调用按技能数据解析并缓存；见类型判断记录"命中路径由技能数据选择"）。</summary>
        private bool IsSpatialHit(ActionRun run)
        {
            if (run.HitPathResolved)
            {
                return run.Spatial;
            }

            run.HitPathResolved = true;
            var tl = run.Timeline;
            var hasShape = _targetHost.TryGetChainShape(run.Def.TargetShapeRef, out var template);
            var spatial = tl.HitMode != TimelineHitMode.Instant && hasShape;
            if (tl.HitMode == TimelineHitMode.Spatial && !hasShape)
            {
                _diagnostics.Warn(
                    $"技能 \"{run.Def.Id}\" 声明 hit_mode: spatial，但目标选择链 \"{run.Def.TargetShapeRef}\" 没有 shape，退回 instant 结算（校验器本应已报错）");
            }

            if (tl.HitPolicy == TimelineHitPolicy.Continuous && !spatial)
            {
                _diagnostics.Warn(
                    $"技能 \"{run.Def.Id}\" 声明 hit_policy: continuous，但没有可用的命中形状，按 marker 处理（校验器本应已报错）");
            }

            run.Spatial = spatial;
            run.Template = template;
            return spatial;
        }

        // -----------------------------------------------------------------
        // marker 策略：hit 标记处以攻击方当时位姿解析一次
        // -----------------------------------------------------------------

        private void SpatialMarkerHit(Id casterId, CastState state, ActionRun run, int segment)
        {
            var geo = PoseGeometry(casterId, closest: false);
            Id? currentTarget = state.Targets.Count > 0 ? state.Targets[0] : (Id?)null;
            var resolution = _targetHost.ResolveAtPose(run.Def.TargetShapeRef, casterId, geo.Position, geo.Facing, currentTarget);
            SettleSpatial(casterId, state, run, resolution, segment, geo, run.Elapsed, enforceInterval: true);
        }

        // -----------------------------------------------------------------
        // continuous 策略：判定相内逐 tick（两 tick 位姿之间插值采样）解析
        // -----------------------------------------------------------------

        /// <summary>
        /// 落定一次推进的 continuous 采样计划：动作时间区间 (<paramref name="fromExclusive"/>, <paramref name="toInclusive"/>] 内落在判定相
        /// [起点, 终点) 的部分，沿攻击方上一次推进结束时的位姿到当前位姿线性插值（朝向取最短角度插值）。采样数取三者最大：时间步长
        /// （缺省一个 tick 的 1/4）、位移除以形状特征尺寸的一半、转角扫过的弧长除以同一尺寸——保证形状在两 tick 之间不会跳过目标
        /// （手感设计/03 第 2.2 节）。实际采样在 <see cref="EvaluateSamplesBefore"/> 里与调度事件按时间顺序交错执行。
        /// </summary>
        private void BeginContinuousSpan(Id casterId, ActionRun run, int fromExclusive, int toInclusive)
        {
            run.SpanCount = 0;
            if (run.Timeline.HitPolicy != TimelineHitPolicy.Continuous || _timeline?.HitResolver != null || !IsSpatialHit(run))
            {
                return;
            }

            var start = run.Schedule.StartupTicks;
            var end = start + run.Schedule.ActiveTicks;
            if (Math.Max(fromExclusive + 1, start) > Math.Min(toInclusive, end - 1))
            {
                return;
            }

            var span = toInclusive - fromExclusive;
            var curPos = _units.GetPosition(casterId);
            var curFacing = _units.GetFacing(casterId);
            var turn = WrapAngle(curFacing - run.PrevFacing);

            var tickMs = _options.ActionStepSeconds * 1000.0;
            var stepMs = run.Timeline.SampleStepMs > 0 ? run.Timeline.SampleStepMs : tickMs / 4.0;
            var count = (int)Math.Ceiling(span * tickMs / stepMs - 1e-9);

            ShapeScale(run.Template, out var minExtent, out var reach);
            if (minExtent > 1e-9)
            {
                var spacing = 0.5 * minExtent;
                count = Math.Max(count, (int)Math.Ceiling((curPos - run.PrevPosition).Length / spacing - 1e-9));
                count = Math.Max(count, (int)Math.Ceiling(Math.Abs(turn) * reach / spacing - 1e-9));
            }

            run.SpanFrom = fromExclusive;
            run.SpanTo = toInclusive;
            run.SpanCount = Math.Max(1, Math.Min(MaxSamplesPerSpan, count));
            run.SpanNext = 1;
            run.SpanStartPosition = run.PrevPosition;
            run.SpanEndPosition = curPos;
            run.SpanStartFacing = run.PrevFacing;
            run.SpanTurn = turn;
        }

        /// <summary>
        /// 执行当前推进区间内时间早于（<paramref name="inclusive"/> 为假）/不晚于 <paramref name="timeLimit"/> 的 continuous 采样点。
        /// 返回动作是否仍在进行。
        /// </summary>
        private bool EvaluateSamplesBefore(Id casterId, CastState state, ActionRun run, double timeLimit, bool inclusive)
        {
            if (run.SpanCount == 0)
            {
                return true;
            }

            var start = run.Schedule.StartupTicks;
            var end = start + run.Schedule.ActiveTicks;
            var span = run.SpanTo - run.SpanFrom;
            var rehitTicks = RehitTicks(run);
            while (run.SpanNext <= run.SpanCount)
            {
                var f = (double)run.SpanNext / run.SpanCount;
                var t = run.SpanFrom + f * span;
                if (inclusive ? t > timeLimit : t >= timeLimit)
                {
                    break;
                }

                run.SpanNext++;
                if (t < start || t >= end)
                {
                    continue;
                }

                var pos = run.SpanStartPosition + (run.SpanEndPosition - run.SpanStartPosition) * f;
                var facing = run.SpanStartFacing + run.SpanTurn * f;
                var segment = rehitTicks > 0 ? (int)Math.Floor((t - start) / rehitTicks) : 0;

                Id? currentTarget = state.Targets.Count > 0 ? state.Targets[0] : (Id?)null;
                var resolution = _targetHost.ResolveAtPose(run.Def.TargetShapeRef, casterId, pos, facing, currentTarget);
                var geo = new HitGeometry(pos, facing, useClosest: true, run.Template);
                SettleSpatial(casterId, state, run, resolution, segment, geo, (int)Math.Floor(t), enforceInterval: false);
                if (!IsLive(casterId, state))
                {
                    return false;
                }
            }

            return true;
        }

        private int RehitTicks(ActionRun run) =>
            run.Timeline.RehitIntervalMs > 0
                ? Foundation.Feel.FeelCalibration.MillisecondsToTicks(run.Timeline.RehitIntervalMs, _options.ActionStepSeconds)
                : 0;

        /// <summary>形状的特征尺寸（最窄处，采样间距上限的依据）与从攻击方向外的覆盖深度（转角扫过弧长的依据）。</summary>
        private static void ShapeScale(Shape shape, out double minExtent, out double reach)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Circle:
                    minExtent = shape.Radius;
                    reach = shape.Radius;
                    return;
                case ShapeKind.Cone:
                    // 扇形的"宽"随张角变化：取半径乘以弦宽比，张角很窄时不能按整个半径放宽采样间距。
                    minExtent = shape.Radius * Math.Max(0.1, Math.Min(1.0, 2.0 * Math.Sin(Math.Min(shape.Angle, Math.PI) / 2.0)));
                    reach = shape.Radius;
                    return;
                case ShapeKind.Line:
                    minExtent = Math.Min(shape.Length, shape.Width);
                    reach = shape.Length;
                    return;
                case ShapeKind.Rect:
                    minExtent = Math.Min(shape.HalfExtents.X, shape.HalfExtents.Y);
                    reach = Math.Sqrt(shape.HalfExtents.X * shape.HalfExtents.X + shape.HalfExtents.Y * shape.HalfExtents.Y);
                    return;
                default:
                    minExtent = 0.0;
                    reach = 0.0;
                    return;
            }
        }

        private static double WrapAngle(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle < -Math.PI) angle += 2 * Math.PI;
            return angle;
        }

        // -----------------------------------------------------------------
        // 去重与结算
        // -----------------------------------------------------------------

        /// <summary>
        /// 用一份链解析结果做一次空间命中：过滤掉已不在世/已死亡的目标与本攻击实例已命中过的 (目标, 段) 对，余下的一批按效果管线结算。
        /// <paramref name="atTick"/> 是本次命中的动作时间（tick），<paramref name="enforceInterval"/> 为真时额外要求同一目标两次命中（不同段）
        /// 相隔不少于 <c>rehit_interval_ms</c>（marker 策略；continuous 由按间隔切段本身保证）。
        /// </summary>
        private void SettleSpatial(
            Id casterId, CastState state, ActionRun run, TargetResolution resolution, int segment, in HitGeometry geo,
            int atTick, bool enforceInterval)
        {
            List<Id>? batch = null;
            Dictionary<Id, double>? coefficients = null;
            var rehitTicks = enforceInterval ? RehitTicks(run) : 0;

            foreach (var (target, coefficient) in resolution.Targets)
            {
                if (!_units.Exists(target) || !_units.IsAlive(target))
                {
                    continue;
                }

                if (run.HitLedger.ContainsKey((target, segment)))
                {
                    continue;
                }

                if (rehitTicks > 0 && run.LastHitTick.TryGetValue(target, out var lastTick) && atTick - lastTick < rehitTicks)
                {
                    continue;
                }

                run.HitLedger[(target, segment)] = atTick;
                run.LastHitTick[target] = atTick;
                batch ??= new List<Id>();
                batch.Add(target);
                if (coefficient != 1.0)
                {
                    coefficients ??= new Dictionary<Id, double>();
                    coefficients[target] = coefficient;
                }
            }

            if (batch == null)
            {
                return;
            }

            NoteFirstHit(casterId, state, run, EmptyArgs);
            SettleTimelineBatch(casterId, run, batch, coefficients, segment, geo, EffectSubsetFor(run));
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyArgs = new Dictionary<string, string>();

        private static bool IsAttackKind(EffectKind kind) => kind == EffectKind.SchoolDamage || kind == EffectKind.WeaponDamagePct;

        private static bool IsAvoidedResult(HitResult result) =>
            result == HitResult.Miss || result == HitResult.Dodge || result == HitResult.Parry
            || result == HitResult.Immune || result == HitResult.Invulnerable;

        private static bool HasProjectileEffect(SkillDef def)
        {
            foreach (var effect in def.Effects)
            {
                if (effect.Kind == EffectKind.Projectile)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 动作是否带攻击（<c>action.started.isAttack</c>）：含伤害类或投射物效果，或声明了 <c>hit</c>/<c>release</c> 标记。
        /// 反馈侧只为带攻击的动作开挥空窗口——闪避、位移、纯增益动作没有"打空"（手感设计/07 第 6 节）。
        /// </summary>
        private static bool IsAttackAction(SkillDef def, TimelineDef tl)
        {
            if (HasAttackEffect(def, EffectSubset.All) || HasProjectileEffect(def))
            {
                return true;
            }

            foreach (var marker in tl.Markers)
            {
                if (marker.Name == "hit" || marker.Name == "release")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAttackEffect(SkillDef def, EffectSubset subset)
        {
            foreach (var effect in def.Effects)
            {
                if (subset == EffectSubset.ProjectileOnly)
                {
                    continue;
                }

                if (IsAttackKind(effect.Kind))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 一批命中的结算（手感设计/03 第 2.3/2.4 节）：对声明了伤害类效果的技能先做无敌前置检查（目标处于无敌窗口 → 回避，
        /// 发 <c>combat.attack_avoided</c> 与 <c>combat.hit_confirmed</c>，不进效果管线、不扣血、不顿帧），
        /// 其余目标按既有效果管线结算（同一批共用一个攻击实例 id），结算后每个目标发一条 <c>combat.hit_confirmed</c>。
        /// 只有治疗/光环类效果的技能不做无敌检查也不发 hit_confirmed（那不是一次"命中"，无敌的友军照常被治疗）。
        /// </summary>
        private void SettleTimelineBatch(
            Id casterId, ActionRun run, IReadOnlyList<Id> targets, IReadOnlyDictionary<Id, double>? coefficients, int segment,
            in HitGeometry geo, EffectSubset subset)
        {
            var def = run.Def;
            var attack = HasAttackEffect(def, subset);
            var attackerFeel = attack ? _timeline?.Feel?.GetSnapshot(run.CastInstanceId)?.Judging : null;
            Id? attackInstanceId = null;

            List<Id> live;
            if (attack)
            {
                live = new List<Id>(targets.Count);
                foreach (var target in targets)
                {
                    if (IsInvulnerable(target))
                    {
                        attackInstanceId ??= NextCastInstanceId();
                        ContactFor(geo, casterId, target, out var contact, out var normal, out var worldDirection);
                        PublishAvoided(
                            attackInstanceId.Value, run.CastInstanceId, segment, casterId, target, def, HitResult.Invulnerable,
                            contact, normal, worldDirection, attackerFeel);
                        continue;
                    }

                    live.Add(target);
                }
            }
            else
            {
                live = targets as List<Id> ?? new List<Id>(targets);
            }

            if (live.Count == 0)
            {
                return;
            }

            var outcomes = attack ? new List<EffectOutcome>() : null;
            // S7b 修复（S11 缺口）：没有 release 标记的动作，投射物效果随 hit 标记在 All 子集里一起发射；
            // 其命中伤害原先既不经本路径确认（HasAttackEffect 只认直接伤害类效果），又被即时适配器按"时间线技能"跳过，
            // 于是没有 hit_confirmed、没有顿帧与僵直。这里给投射物带上与 release 路径同一个命中钩子，
            // 让每次投射物命中恰好由时间线路径确认一次（钩子内部做无敌前置检查与确认事件）。
            IProjectileHitHook? projectileHook = null;
            if (subset == EffectSubset.All && HasProjectileEffect(def))
            {
                projectileHook = new TimelineProjectileHook(this, casterId, def, run.CastInstanceId, segment);
            }

            var id = ExecuteEffectsOnly(
                casterId, def, live, targetCoefficients: coefficients, presetAttackInstanceId: attackInstanceId,
                subset: subset, outcomes: outcomes, projectileHook: projectileHook);
            if (outcomes == null)
            {
                return;
            }

            foreach (var target in live)
            {
                var results = new List<ResolveResult>();
                foreach (var outcome in outcomes)
                {
                    if (outcome.TargetId.Equals(target) && IsAttackKind(outcome.Kind))
                    {
                        results.Add(outcome.Result);
                    }
                }

                if (results.Count == 0)
                {
                    continue;
                }

                ContactFor(geo, casterId, target, out var contact, out var normal, out var worldDirection);
                PublishConfirmation(id, run.CastInstanceId, segment, casterId, target, def.Id, results, contact, normal, worldDirection, attackerFeel);
            }
        }

        /// <summary>接触几何（手感设计/03 第 2.4 节）：几何字段永不为空——没有接触几何时由本方法给出明确替代值。</summary>
        private void ContactFor(in HitGeometry geo, Id casterId, Id targetId, out Vec2 contact, out Vec2 normal, out Vec2 worldDirection)
        {
            var targetPosition = _units.GetPosition(targetId);
            var toTarget = targetPosition - geo.Position;
            var length = toTarget.Length;
            worldDirection = length > 1e-9 ? toTarget * (1.0 / length) : new Vec2(Math.Cos(geo.Facing), Math.Sin(geo.Facing));

            contact = targetPosition;
            if (geo.UseClosest)
            {
                contact = ShapeGeometry.ClosestPoint(ShapeGeometry.RebaseAt(geo.Template, geo.Position, geo.Facing), targetPosition);
            }

            var offset = contact - targetPosition;
            var offsetLength = offset.Length;
            normal = offsetLength > 1e-9 ? offset * (1.0 / offsetLength) : -worldDirection;
        }

        private void PublishAvoided(
            Id attackInstanceId, Id? castInstanceId, int segment, Id casterId, Id targetId, SkillDef def, HitResult hitResult,
            Vec2 contact, Vec2 normal, Vec2 worldDirection, JudgingFeelView? attackerFeel)
        {
            _bus.Enqueue(new CombatAttackAvoidedEvent(casterId, targetId, def.School, hitResult, def.Id, attackInstanceId));
            PublishHitConfirmed(
                attackInstanceId, castInstanceId, segment, casterId, targetId, def.Id, hitResult, 0.0, false, false,
                contact, normal, worldDirection, attackerFeel);
        }

        /// <summary>从一个目标的伤害类结算结果汇总成一条 <c>combat.hit_confirmed</c>：结局取第一个非回避结果（都回避则取第一个），伤害量求和。</summary>
        private void PublishConfirmation(
            Id attackInstanceId, Id? castInstanceId, int segment, Id casterId, Id targetId, Id skillId,
            IReadOnlyList<ResolveResult> results, Vec2 contact, Vec2 normal, Vec2 worldDirection, JudgingFeelView? attackerFeel)
        {
            var hitResult = results[0].Hit;
            for (var i = 0; i < results.Count; i++)
            {
                if (!IsAvoidedResult(results[i].Hit))
                {
                    hitResult = results[i].Hit;
                    break;
                }
            }

            var amount = 0.0;
            var crit = false;
            for (var i = 0; i < results.Count; i++)
            {
                if (IsAvoidedResult(results[i].Hit))
                {
                    continue;
                }

                amount += results[i].FinalAmount;
                crit |= results[i].Hit == HitResult.Crit;
            }

            var avoided = IsAvoidedResult(hitResult);
            var isKill = !avoided && (!_units.Exists(targetId) || !_units.IsAlive(targetId));
            PublishHitConfirmed(
                attackInstanceId, castInstanceId, segment, casterId, targetId, skillId, hitResult, avoided ? 0.0 : amount,
                !avoided && crit, isKill, contact, normal, worldDirection, attackerFeel);
        }

        private void PublishHitConfirmed(
            Id attackInstanceId, Id? castInstanceId, int segment, Id casterId, Id targetId, Id skillId, HitResult hitResult,
            double amount, bool isCrit, bool isKill, Vec2 contact, Vec2 normal, Vec2 worldDirection, JudgingFeelView? attackerFeel)
        {
            var outcome = _timeline?.HitFeel != null
                ? _timeline.HitFeel.Evaluate(new HitFeelInput(casterId, targetId, hitResult, amount, isKill, attackerFeel))
                : HitFeelOutcome.None();
            var maxHealth = SafeMaxHealth(targetId);
            var ratio = maxHealth > 0.0 ? amount / maxHealth : 0.0;
            _bus.Enqueue(new CombatHitConfirmedEvent(
                attackInstanceId, segment, casterId, targetId, skillId, hitResult, amount, ratio, isCrit, isKill,
                contact, normal, worldDirection, outcome.ImpactClass, outcome.AttackerHitStopTicks, outcome.TargetHitStopTicks,
                outcome.Reaction, castInstanceId));
        }

        private double SafeMaxHealth(Id unitId)
        {
            try
            {
                return _powerHost.GetPowerMax(unitId, WellKnownPowers.Health);
            }
            catch (ArgumentException)
            {
                return 0.0;
            }
            catch (InvalidOperationException)
            {
                return 0.0;
            }
        }

        // -----------------------------------------------------------------
        // release 标记：发射投射物（手感设计/03 第 2.5 节）
        // -----------------------------------------------------------------

        /// <summary>
        /// <c>release</c> 标记处执行技能的 <c>projectile</c> 效果：目标取显式目标、没有则取目标辅助挑出的目标（用于瞄准与 homing），都没有则以
        /// 施法者自己为"目标"（<c>ProjectileHost</c> 视为无目标、沿朝向直线飞行）。发射出的投射物带一个命中钩子，命中沿用本动作实例
        /// （<c>combat.hit_confirmed.castInstanceId</c>）、做无敌前置检查、取碰撞点为接触点；动作之后被打断/结束不影响已发射的投射物。
        /// </summary>
        private void ReleaseProjectiles(Id casterId, CastState state, ActionRun run, TimelineEvent ev)
        {
            var def = run.Def;
            var hasProjectile = false;
            foreach (var effect in def.Effects)
            {
                if (effect.Kind == EffectKind.Projectile)
                {
                    hasProjectile = true;
                    break;
                }
            }

            if (!hasProjectile)
            {
                return;
            }

            var segment = 0;
            if (ev.Args.TryGetValue("segment", out var segText))
            {
                int.TryParse(segText, NumberStyles.Integer, CultureInfo.InvariantCulture, out segment);
            }

            var aim = casterId;
            if (state.Targets.Count > 0 && _units.Exists(state.Targets[0]))
            {
                aim = state.Targets[0];
            }
            else if (run.AssistTarget.HasValue && _units.Exists(run.AssistTarget.Value))
            {
                aim = run.AssistTarget.Value;
            }

            var hook = new TimelineProjectileHook(this, casterId, def, run.CastInstanceId, segment);
            ExecuteEffectsOnly(casterId, def, new[] { aim }, subset: EffectSubset.ProjectileOnly, projectileHook: hook);
        }

        /// <summary>时间线投射物的命中钩子（见 <see cref="IProjectileHitHook"/>）：沿用发射动作的施法实例 id。</summary>
        private sealed class TimelineProjectileHook : IProjectileHitHook
        {
            private readonly CastPipeline _owner;
            private readonly Id _casterId;
            private readonly SkillDef _def;
            private readonly Id _castInstanceId;
            private readonly int _segment;

            public TimelineProjectileHook(CastPipeline owner, Id casterId, SkillDef def, Id castInstanceId, int segment)
            {
                _owner = owner;
                _casterId = casterId;
                _def = def;
                _castInstanceId = castInstanceId;
                _segment = segment;
            }

            /// <summary>投射物生成：发 <c>action.projectile_launched</c>（反馈侧据此把挥空判定推迟到投射物结局）。</summary>
            public void OnLaunched() =>
                _owner._bus.Enqueue(new ActionProjectileLaunchedEvent(_casterId, _castInstanceId, _segment));

            /// <summary>投射物结局确定：发 <c>action.projectile_ended</c>，与 <see cref="OnLaunched"/> 一一配对。</summary>
            public void OnEnded(ProjectileEndReason reason) =>
                _owner._bus.Enqueue(new ActionProjectileEndedEvent(_casterId, _castInstanceId, _segment, reason));

            public bool BeforeHit(in ProjectileHitInfo info, out Id attackInstanceId)
            {
                attackInstanceId = _owner.NextCastInstanceId();
                if (!_owner.IsInvulnerable(info.TargetId))
                {
                    return true;
                }

                Geometry(info, out var contact, out var normal);
                _owner.PublishAvoided(
                    attackInstanceId, _castInstanceId, _segment, _casterId, info.TargetId, _def, HitResult.Invulnerable,
                    contact, normal, info.FlightDirection, AttackerFeel());
                return false;
            }

            public void AfterHit(in ProjectileHitInfo info, Id attackInstanceId, IReadOnlyList<ResolveResult> results)
            {
                var attack = new List<ResolveResult>(results.Count);
                for (var i = 0; i < results.Count; i++)
                {
                    if (!results[i].IsHeal)
                    {
                        attack.Add(results[i]);
                    }
                }

                if (attack.Count == 0)
                {
                    return;
                }

                Geometry(info, out var contact, out var normal);
                _owner.PublishConfirmation(
                    attackInstanceId, _castInstanceId, _segment, _casterId, info.TargetId, _def.Id, attack,
                    contact, normal, info.FlightDirection, AttackerFeel());
            }

            private JudgingFeelView? AttackerFeel() => _owner._timeline?.Feel?.GetSnapshot(_castInstanceId)?.Judging;

            private void Geometry(in ProjectileHitInfo info, out Vec2 contact, out Vec2 normal)
            {
                contact = info.ContactPoint;
                var offset = contact - _owner._units.GetPosition(info.TargetId);
                var length = offset.Length;
                normal = length > 1e-9 ? offset * (1.0 / length) : -info.FlightDirection;
            }
        }

        // -----------------------------------------------------------------
        // 目标辅助（手感设计/02 第 5 节）
        // -----------------------------------------------------------------

        private readonly struct AssistResult
        {
            public ActionAssistOutcome Outcome { get; }

            public AssistResult(ActionAssistOutcome outcome)
            {
                Outcome = outcome;
            }
        }

        /// <summary>
        /// 动作被接受时的目标辅助：声明了 <c>target_assist</c> 且装配了 <see cref="TimelineServices.TargetAssist"/> 才生效（缺省关闭）。
        /// 朝向修正当场写入（不超过档案 <c>turn_assist_deg</c>；没有手感解析器时上限取 0，即不转向——"没有档案就不替玩家转"），
        /// 辅助目标与距离缩放返回给位移段快照，<c>action.target_assisted</c> 由调用方在 <c>action.started</c> 之后发布。
        /// </summary>
        private AssistResult? ResolveTargetAssist(Id casterId, Id castInstanceId, SkillDef def, TimelineDef tl, ResolvedFeel? feel)
        {
            var declaration = tl.TargetAssist;
            var service = _timeline?.TargetAssist;
            if (declaration == null || service == null)
            {
                return null;
            }

            var calibration = _timeline?.Feel?.Calibration;
            double World(double bodyHeights) =>
                calibration != null ? calibration.ToAbsolute(FeelUnit.BodyHeights, bodyHeights) : bodyHeights;

            var declared = tl.Motion.HasValue ? World(tl.Motion.Value.Distance) : 0.0;
            var reach = 0.0;
            if (_targetHost.TryGetChainShape(def.TargetShapeRef, out var template))
            {
                reach = ForwardReach(template);
            }

            var turnCap = feel != null ? FeelNumber(feel, FeelFieldNames.TurnAssistDeg, 0.0) : 0.0;
            var request = new ActionAssistRequest(
                casterId, castInstanceId, declaration.ChainRef, World(declaration.MaxDistance), declaration.MaxAngleDeg, declaration.Mode,
                turnCap, declared, reach);
            if (!service.TryAssist(request, out var outcome))
            {
                return null;
            }

            var cap = Math.Max(0.0, turnCap);
            var delta = Math.Max(-cap, Math.Min(cap, outcome.FacingDeltaDeg));
            if (delta != 0.0)
            {
                if (_units is IUnitFacingWriter writer)
                {
                    writer.SetFacing(casterId, WrapAngle(_units.GetFacing(casterId) + delta * Math.PI / 180.0));
                }
                else
                {
                    _diagnostics.Warn(
                        $"目标辅助要修正朝向（actor=\"{casterId}\"），但 IUnitAccess 实现没有提供 IUnitFacingWriter，朝向未改动，action.target_assisted 事件仍照发");
                }
            }

            return new AssistResult(new ActionAssistOutcome(outcome.TargetId, delta, outcome.DistanceAdjust));
        }

        /// <summary>判定形状从攻击方向前的覆盖深度（<c>close_distance</c> 缩放依据）。</summary>
        private static double ForwardReach(Shape shape)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Circle:
                case ShapeKind.Cone:
                    return shape.Radius;
                case ShapeKind.Line:
                    return shape.Length;
                case ShapeKind.Rect:
                    return shape.HalfExtents.X;
                default:
                    return 0.0;
            }
        }
    }
}
