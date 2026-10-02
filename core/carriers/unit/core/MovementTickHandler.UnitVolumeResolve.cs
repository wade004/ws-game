using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 单位间体积阻挡的阶段 B（tick 末，<see cref="FinishMotionTick"/> 里所有位移都算完之后）：把本 tick 里有体积的单位的"提议位移"
    /// （tick 起点快照 → 阶段 A 写下的位置）放在一起做对称裁决，再统一写回位置、发出 <c>unit.moved</c>、写运动学状态。
    /// <list type="number">
    /// <item><description>
    /// <b>成对撞停</b>：阶段 A 对别的单位用的是 tick 起点位置，对"同一 tick 里也在移动的单位"不够——两个单位相向走，各自都没撞到对方的起点位置，
    /// 终点却重叠。这里对"本 tick 都有位移"的单位对，按相对运动求首次接触（二次方程，连续扫掠，不隧穿），两个单位按同一比例缩短各自的位移
    /// （再退一个到达容差）；缩短会让别的对重新接触，所以用 Jacobi 迭代（每轮都用上一轮的比例，取最小）收敛，迭代耗尽时把仍冲突的单位退回起点。
    /// 缩短后的位置经地形检查（弦必须可走），不可走则退回起点。缩短只发生在同 tick 互相靠近的两个动单位之间；对静止的单位阶段 A 已经精确裁决。
    /// </description></item>
    /// <item><description>
    /// <b>重叠分离</b>（<c>unit_separation_speed_ratio</c>）：成对重叠（中心距小于半径之和）的单位沿连线互相推开，按各自分离速率（倍数 × 移动速度属性）
    /// 分摊重叠深度，每个单位每 tick 的总位移不超过自己的速率 × dt；被控制（定身/顿帧）、受控位移中的单位不被推（权重 0，对方承担全部）；
    /// 推开位移同样过地形裁决，且不得把单位推进第三个单位的体积（同一套成对接触检查）。
    /// </description></item>
    /// <item><description>
    /// <b>受控位移推人</b>（<c>forced_push_units</c>）：阶段 A 记下的"击退撞上了某个单位"在这里统一生效，给被撞单位开一段受控位移（下一 tick 起推进）。
    /// </description></item>
    /// </list>
    /// 所有成对量都在按单位 id 排序的快照下标上计算，累加顺序是下标的函数，不依赖单位处理顺序或枚举顺序，所以结果逐位可复现。
    /// </summary>
    public sealed partial class MovementTickHandler
    {
        private readonly struct DeferredKin
        {
            public readonly Unit Unit;
            public readonly MotionTick Tick;
            public readonly MotionKinematics Kin;

            public DeferredKin(Unit unit, MotionTick tick, MotionKinematics kin)
            {
                Unit = unit;
                Tick = tick;
                Kin = kin;
            }
        }

        private readonly struct PendingPush
        {
            public readonly Id Pusher;
            public readonly Id Target;
            public readonly Vec2 Vector;
            public readonly double Speed;
            public readonly string? Curve;
            public readonly double TotalLength;
            public readonly double TotalDuration;
            public readonly double SampleStep;

            public PendingPush(
                Id pusher, Id target, Vec2 vector, double speed, string? curve, double totalLength, double totalDuration, double sampleStep)
            {
                Pusher = pusher;
                Target = target;
                Vector = vector;
                Speed = speed;
                Curve = curve;
                TotalLength = totalLength;
                TotalDuration = totalDuration;
                SampleStep = sampleStep;
            }
        }

        private readonly List<DeferredKin> _deferredKin = new List<DeferredKin>();
        private readonly List<PendingPush> _pendingPushes = new List<PendingPush>();

        private const int ContactIterations = 16;

        /// <summary>
        /// tick 末的体积裁决（见类型注释）。快照未建（本 tick 没有有体积的单位）时什么也不做。
        /// </summary>
        private void ResolveUnitVolumes(IWorldSim world, double dt)
        {
            if (!VolumeSnapshotCurrent)
            {
                return;
            }

            var n = _volumes.Count;
            for (var i = 0; i < n; i++)
            {
                var body = _volumes[i];
                var unit = world.GetEntity(body.Id) as Unit;
                body.Unit = unit;
                body.Active = unit != null && !world.IsPendingDestruction(body.Id) && unit.Alive;
                body.Ghost = false;
                body.PulledBack = false;
                body.Scale = 1.0;
                var current = body.Active ? unit!.Position : body.Start;
                body.Delta = current - body.Start;
                body.Final = current;
                if (body.Active)
                {
                    var t = GetMotionTick(unit!);
                    body.Ghost = t != null && t.PassedThroughUnits;
                }
            }

            ResolveMovedPairs();
            ResolveSeparation(dt);
            ApplyPendingPushes(world);
            FlushMovedEvents();
        }

        // ------------------------------------------------------------------ 成对撞停

        private void ResolveMovedPairs()
        {
            var n = _volumes.Count;
            var movers = new List<int>();
            for (var i = 0; i < n; i++)
            {
                var b = _volumes[i];
                if (b.Active && !b.Ghost && (b.Delta.X != 0.0 || b.Delta.Y != 0.0))
                {
                    movers.Add(i);
                }
            }

            if (movers.Count < 2)
            {
                return;
            }

            var a = new Vec2[n];
            var d = new Vec2[n];
            var reach = new double[n];
            var scale = new double[n];
            for (var i = 0; i < n; i++)
            {
                var b = _volumes[i];
                a[i] = b.Start;
                d[i] = b.Delta;
                reach[i] = b.Radius + b.Delta.Length;
                scale[i] = 1.0;
            }

            var pairs = new List<(int I, int J)>();
            CollectNearPairs(movers, a, reach, pairs);
            if (pairs.Count == 0)
            {
                return;
            }

            SolveContactScales(a, d, scale, pairs, ValidScaledChord);
            for (var k = 0; k < movers.Count; k++)
            {
                var i = movers[k];
                if (scale[i] >= 1.0)
                {
                    continue;
                }

                var b = _volumes[i];
                var p = a[i] + d[i] * scale[i];
                b.Scale = scale[i];
                b.PulledBack = true;
                b.Final = p;
                _units.SetPosition(b.Id, p);
                _movedDeferredSet.Add(b.Id);
            }
        }

        /// <summary>缩短后的弦 <c>start → p</c> 必须可走（弦可能与阶段 A 的折线不同）；没有导航时恒可走。</summary>
        private bool ValidScaledChord(int index, Vec2 p)
        {
            if (_navigation == null)
            {
                return true;
            }

            var b = _volumes[index];
            if (b.Unit == null)
            {
                return false;
            }

            if (p.Equals(b.Start))
            {
                return true;
            }

            return !NavRaycast(b.Unit, b.Start, p).HasValue && _navigation.IsWalkable(b.Unit.MapId, p);
        }

        // ------------------------------------------------------------------ 重叠分离

        private void ResolveSeparation(double dt)
        {
            var n = _volumes.Count;
            var participants = new List<int>();
            for (var i = 0; i < n; i++)
            {
                var b = _volumes[i];
                if (b.Active && !b.Ghost)
                {
                    participants.Add(i);
                }
            }

            if (participants.Count < 2)
            {
                return;
            }

            var pos = new Vec2[n];
            var reach = new double[n];
            for (var i = 0; i < n; i++)
            {
                pos[i] = _volumes[i].Final;
                reach[i] = _volumes[i].Radius;
            }

            var near = new List<(int I, int J)>();
            CollectNearPairs(participants, pos, reach, near);
            var overlaps = new List<(int I, int J)>();
            for (var k = 0; k < near.Count; k++)
            {
                var (i, j) = near[k];
                var sum = _volumes[i].Radius + _volumes[j].Radius;
                if ((pos[i] - pos[j]).Length < sum - 1e-9)
                {
                    overlaps.Add((i, j));
                }
            }

            if (overlaps.Count == 0)
            {
                return;
            }

            // 每个单位的分离速率（世界单位/秒）：不可被推的单位权重 0。
            var weight = new double[n];
            for (var k = 0; k < participants.Count; k++)
            {
                var i = participants[k];
                var b = _volumes[i];
                var u = b.Unit!;
                var t = GetMotionTick(u);
                if (t == null || t.Frozen || t.Rooted || t.Dead || u.MovementState.Displacement.HasValue)
                {
                    continue;
                }

                weight[i] = b.SeparationRatio * MotionBaseSpeed(u, t);
            }

            var push = new Vec2[n];
            for (var k = 0; k < overlaps.Count; k++)
            {
                var (i, j) = overlaps[k];
                var wi = weight[i];
                var wj = weight[j];
                if (!(wi + wj > 0.0))
                {
                    continue;
                }

                var f = pos[i] - pos[j];
                var dist = f.Length;
                var depth = _volumes[i].Radius + _volumes[j].Radius - dist;
                // 完全重合：沿 x 轴按下标分开（i < j 恒成立，确定）。
                var dir = dist > 1e-12 ? new Vec2(f.X / dist, f.Y / dist) : new Vec2(-1.0, 0.0);
                push[i] = push[i] + dir * (depth * (wi / (wi + wj)));
                push[j] = push[j] - dir * (depth * (wj / (wi + wj)));
            }

            var any = false;
            for (var i = 0; i < n; i++)
            {
                if (!(weight[i] > 0.0))
                {
                    push[i] = Vec2.Zero;
                    continue;
                }

                var len = push[i].Length;
                var cap = weight[i] * dt;
                if (len > cap && len > 0.0)
                {
                    push[i] = push[i] * (cap / len);
                    len = cap;
                }

                if (len > ZeroLengthEpsilon && _navigation != null)
                {
                    var b = _volumes[i];
                    var from = pos[i];
                    var to = from + push[i];
                    var hit = NavRaycast(b.Unit!, from, to);
                    if (hit.HasValue)
                    {
                        var hitDistance = (hit.Value - from).Length;
                        var allowed = hitDistance - Math.Min(hitDistance, _options.ArrivalEpsilon);
                        push[i] = len > 0.0 ? push[i] * (allowed / len) : Vec2.Zero;
                        to = from + push[i];
                    }

                    if (push[i].Length > ZeroLengthEpsilon && !_navigation.IsWalkable(b.Unit!.MapId, to))
                    {
                        push[i] = Vec2.Zero;
                    }
                }

                if (push[i].Length <= ZeroLengthEpsilon)
                {
                    push[i] = Vec2.Zero;
                }
                else
                {
                    any = true;
                }
            }

            if (!any)
            {
                return;
            }

            // 推开位移不得把单位推进第三个单位的体积：用同一套成对接触检查（被推单位的弦 vs 别的单位的位置/弦）。
            var pushReach = new double[n];
            for (var i = 0; i < n; i++)
            {
                pushReach[i] = _volumes[i].Radius + push[i].Length;
            }

            var pushPairs = new List<(int I, int J)>();
            CollectNearPairs(participants, pos, pushReach, pushPairs);
            var scale = new double[n];
            for (var i = 0; i < n; i++)
            {
                scale[i] = 1.0;
            }

            SolveContactScales(pos, push, scale, pushPairs, null);
            for (var i = 0; i < n; i++)
            {
                if (push[i].X == 0.0 && push[i].Y == 0.0)
                {
                    continue;
                }

                var b = _volumes[i];
                var p = pos[i] + push[i] * scale[i];
                if (p.Equals(pos[i]))
                {
                    continue;
                }

                b.Final = p;
                _units.SetPosition(b.Id, p);
                _movedDeferredSet.Add(b.Id);
            }
        }

        // ------------------------------------------------------------------ 成对接触求解

        /// <summary>
        /// 成对相对运动的首次接触比例：两个单位从 <c>A_i, A_j</c> 出发、本 tick 的位移向量差为 <paramref name="r"/>（<c>f = A_i − A_j</c>），
        /// 求中心距首次小于 <c>min(半径之和, 起始距离)</c>（"不得变得更深"）的比例 τ∈[0,1)；不接触返回 1。命中时再退一个到达容差对应的比例。
        /// </summary>
        private static double PairContactFraction(Vec2 f, Vec2 r, double sum, double eps)
        {
            const double tol = 1e-9;
            var f2 = f.Dot(f);
            var dist0 = Math.Sqrt(f2);
            var thr = Math.Min(sum, dist0) - tol;
            if (thr <= 0.0)
            {
                return 1.0;
            }

            var a = r.Dot(r);
            if (a <= 1e-24)
            {
                return 1.0;
            }

            var b = f.Dot(r);
            if (b >= 0.0)
            {
                return 1.0; // 不是在靠近。
            }

            var c = f2 - thr * thr;
            var disc = b * b - a * c;
            if (disc <= 0.0)
            {
                return 1.0;
            }

            var tau = (-b - Math.Sqrt(disc)) / a;
            if (tau >= 1.0)
            {
                return 1.0;
            }

            var res = tau - eps / Math.Sqrt(a);
            return res < 0.0 ? 0.0 : res;
        }

        /// <summary>
        /// Jacobi 迭代：每轮用上一轮的比例对所有候选对求接触，每个单位的新比例取"自己的比例 × 各对 τ"的最小值（取最小与对的遍历顺序无关）。
        /// 一轮没有任何对接触即收敛；<see cref="ContactIterations"/> 轮仍未收敛时把仍冲突的单位退回起点（比例 0）。
        /// <paramref name="valid"/> 非空时，比例被缩短的单位的新位置必须通过它，否则该单位退回起点。
        /// </summary>
        private void SolveContactScales(
            Vec2[] a, Vec2[] d, double[] scale, List<(int I, int J)> pairs, Func<int, Vec2, bool>? valid)
        {
            var eps = _options.ArrivalEpsilon;
            var n = scale.Length;
            var next = new double[n];
            for (var iter = 0; iter < ContactIterations; iter++)
            {
                Array.Copy(scale, next, n);
                var any = false;
                for (var k = 0; k < pairs.Count; k++)
                {
                    var (i, j) = pairs[k];
                    var tau = PairContactFraction(
                        a[i] - a[j], d[i] * scale[i] - d[j] * scale[j], _volumes[i].Radius + _volumes[j].Radius, eps);
                    if (tau >= 1.0)
                    {
                        continue;
                    }

                    any = true;
                    var si = scale[i] * tau;
                    var sj = scale[j] * tau;
                    if (si < next[i]) next[i] = si;
                    if (sj < next[j]) next[j] = sj;
                }

                if (!any)
                {
                    return;
                }

                if (valid != null)
                {
                    for (var k = 0; k < n; k++)
                    {
                        if (next[k] < scale[k] && !valid(k, a[k] + d[k] * next[k]))
                        {
                            next[k] = 0.0;
                        }
                    }
                }

                Array.Copy(next, scale, n);
            }

            // 兜底：迭代没收敛，把仍然冲突的两个单位都退回起点，反复到没有冲突（每轮至少一个单位归零，必然终止）。
            for (var guard = 0; guard <= n; guard++)
            {
                var any = false;
                Array.Copy(scale, next, n);
                for (var k = 0; k < pairs.Count; k++)
                {
                    var (i, j) = pairs[k];
                    var tau = PairContactFraction(
                        a[i] - a[j], d[i] * scale[i] - d[j] * scale[j], _volumes[i].Radius + _volumes[j].Radius, eps);
                    if (tau < 1.0)
                    {
                        next[i] = 0.0;
                        next[j] = 0.0;
                        any = true;
                    }
                }

                if (!any)
                {
                    return;
                }

                Array.Copy(next, scale, n);
            }
        }

        /// <summary>
        /// 收集"位移包络可能相交"的单位对（<c>|pos_i − pos_j| ≤ reach_i + reach_j</c>），i &lt; j、按 (i, j) 排序：先按 x 排序再扫描，
        /// 单位多时不是 O(n²)。<paramref name="idx"/> 是参与者的快照下标。
        /// </summary>
        private static void CollectNearPairs(List<int> idx, Vec2[] pos, double[] reach, List<(int I, int J)> result)
        {
            var order = idx.ToArray();
            Array.Sort(order, (p, q) =>
            {
                var c = pos[p].X.CompareTo(pos[q].X);
                return c != 0 ? c : p.CompareTo(q);
            });
            var maxReach = 0.0;
            for (var k = 0; k < order.Length; k++)
            {
                if (reach[order[k]] > maxReach) maxReach = reach[order[k]];
            }

            for (var x = 0; x < order.Length; x++)
            {
                var i = order[x];
                for (var y = x + 1; y < order.Length; y++)
                {
                    var j = order[y];
                    if (pos[j].X - pos[i].X > reach[i] + maxReach)
                    {
                        break;
                    }

                    var lim = reach[i] + reach[j];
                    var delta = pos[i] - pos[j];
                    if (delta.Dot(delta) <= lim * lim)
                    {
                        result.Add(i < j ? (i, j) : (j, i));
                    }
                }
            }

            result.Sort((p, q) =>
            {
                var c = p.I.CompareTo(q.I);
                return c != 0 ? c : p.J.CompareTo(q.J);
            });
        }

        // ------------------------------------------------------------------ 受控位移推人

        /// <summary>
        /// 受控位移（击退）被别的单位体积挡住、且位移单位的档案声明了 <c>forced_push_units</c> 时，记下一次"推人"：被撞单位获得的位移
        /// = 撞停时剩余位移 × <c>forced_push_ratio</c> ×（1 − 被撞单位的击退抗性），方向 = 撞停位置指向被撞单位中心。tick 末统一生效。
        /// </summary>
        private void QueueForcedPush(
            Unit unit, MotionProfile profile, in VolumeClip volume, ControlledDisplacementState disp)
        {
            if (!profile.ForcedPushUnits || !volume.Blocked || disp.Blocking == DisplacementBlockingPolicy.Revert)
            {
                return;
            }

            if (!TryGetBody(volume.HitId, out var body))
            {
                return;
            }

            var toTarget = body.Start - volume.End;
            var distance = toTarget.Length;
            if (distance <= ZeroLengthEpsilon)
            {
                return;
            }

            var remaining = (disp.Target - volume.End).Length;
            var resistance = MotionKnockback.ReadResistance(_feel!.ResolveJudging(body.Id), _stats, body.Id);
            var transfer = remaining * profile.ForcedPushRatio * (1.0 - resistance);
            if (!(transfer > ZeroLengthEpsilon))
            {
                return;
            }

            var dir = new Vec2(toTarget.X / distance, toTarget.Y / distance);
            _pendingPushes.Add(new PendingPush(
                unit.EntityId, body.Id, dir * transfer, disp.Speed, disp.Curve, (disp.Target - disp.Origin).Length,
                disp.DurationSeconds, disp.SampleStep));
        }

        private void ApplyPendingPushes(IWorldSim world)
        {
            if (_pendingPushes.Count == 0)
            {
                return;
            }

            _pendingPushes.Sort((p, q) =>
            {
                var c = p.Target.CompareTo(q.Target);
                return c != 0 ? c : p.Pusher.CompareTo(q.Pusher);
            });

            var i = 0;
            while (i < _pendingPushes.Count)
            {
                var target = _pendingPushes[i].Target;
                var sum = Vec2.Zero;
                var best = _pendingPushes[i];
                var j = i;
                while (j < _pendingPushes.Count && _pendingPushes[j].Target.Equals(target))
                {
                    var push = _pendingPushes[j];
                    sum = sum + push.Vector;
                    if (push.Vector.Length > best.Vector.Length)
                    {
                        best = push;
                    }

                    j++;
                }

                i = j;
                if (!(world.GetEntity(target) is Unit unit) || world.IsPendingDestruction(target) || !unit.Alive ||
                    unit.MovementState.Displacement.HasValue)
                {
                    continue;
                }

                var length = sum.Length;
                if (!(length > ZeroLengthEpsilon))
                {
                    continue;
                }

                BeginPushedDisplacement(unit, sum, length, best);
            }

            _pendingPushes.Clear();
        }

        /// <summary>
        /// 给被推的单位开一段受控位移（下一 tick 起推进；同 <see cref="BeginDisplacement"/> 的状态约定：路径按策略挂起、状态进入 Forced）。
        /// 带曲线的推人沿用撞人者位移的曲线，时长按转移距离占总距离的比例缩短；匀速位移沿用其速度。
        /// </summary>
        private void BeginPushedDisplacement(Unit unit, Vec2 vector, double length, PendingPush source)
        {
            var origin = unit.Position;
            string? curve = null;
            var duration = 0.0;
            if (source.Curve != null)
            {
                curve = source.Curve;
                duration = source.TotalLength > ZeroLengthEpsilon ? source.TotalDuration * (length / source.TotalLength) : 0.0;
                if (!(duration > 0.0))
                {
                    duration = source.Speed > 0.0 ? length / source.Speed : _motionDt;
                }
            }

            var speed = source.Speed > 0.0 ? source.Speed : length / _motionDt;
            var displacement = new ControlledDisplacementState(
                origin, origin + vector, speed, DisplacementBlockingPolicy.Stop, source.SampleStep, curve, duration, 0.0);
            var oldState = unit.MovementState;
            MotionSuspendPath(unit, oldState);
            unit.MovementState = new MovementState(null, MoveMode.Forced, oldState.MovementLocked, 0, 0, displacement);
            RaiseStateChangedIfNeeded(unit.EntityId, oldState.Mode, MoveMode.Forced);
        }

        // ------------------------------------------------------------------ 写回

        private void FlushMovedEvents()
        {
            if (_movedDeferredSet.Count == 0)
            {
                return;
            }

            var ids = new List<Id>(_movedDeferredSet);
            ids.Sort((p, q) => p.CompareTo(q));
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (!_volumeById.TryGetValue(id, out var body) || !_units.Exists(id))
                {
                    continue;
                }

                var position = _units.GetPosition(id);
                if (!position.Equals(body.Start))
                {
                    _bus.Enqueue(new UnitMovedEvent(id, position));
                }
            }

            _movedDeferredSet.Clear();
        }

        /// <summary>
        /// 写运动学状态：被阶段 B 缩短位移的单位速度归零（撞停），仍在沿路径走的单位路径下标退回本 tick 开始的位置
        /// （路径与 tick 开始时是同一条才退——新建的路径没有"之前"）。
        /// </summary>
        private void WriteDeferredKin()
        {
            for (var i = 0; i < _deferredKin.Count; i++)
            {
                var item = _deferredKin[i];
                var unit = item.Unit;
                var kin = item.Kin;
                if (_volumeById.TryGetValue(unit.EntityId, out var body) && body.PulledBack)
                {
                    kin = new MotionKinematics(Vec2.Zero, kin.DesiredDirection, kin.Mode, kin.BaseMode, kin.Source, kin.BaseSpeed);
                    var state = unit.MovementState;
                    if (state.CurrentPath != null && ReferenceEquals(state.CurrentPath, item.Tick.StartPath) &&
                        state.PathIndex > item.Tick.StartPathIndex && !state.Displacement.HasValue)
                    {
                        unit.MovementState = new MovementState(
                            state.CurrentPath, state.Mode, state.MovementLocked, item.Tick.StartPathIndex, state.NavVersion,
                            null, state.Chase, state.RequestedTarget, state.Motion);
                    }
                }

                if (!SameKinematics(unit.MovementState.Motion, kin))
                {
                    unit.MovementState = unit.MovementState.WithMotion(kin);
                }
            }

            _deferredKin.Clear();
        }
    }
}
