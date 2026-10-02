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
    /// <b>成对撞停</b>：阶段 A 只把"这 tick 不会动"的单位当静止障碍（<see cref="VolumeBody.Free"/> 为假），会动的单位彼此的接触在这里按
    /// <b>最终位置</b>求解——每个单位这 tick 实际走的折线（阶段 A 记下的 <see cref="VolumeBody.Trail"/>，滑动、折线路径都是逐段，不是起终点的弦）
    /// 对任意另一个单位（动或不动）按相对运动求首次接触（每个线性区间一个二次方程，连续扫掠，不隧穿），两个单位按同一比例缩短各自的位移
    /// （再退一个到达容差）；缩短会让别的对重新接触，所以用 Jacobi 迭代（每轮都用上一轮的比例，取最小）收敛，迭代耗尽时把仍冲突的单位退回起点。
    /// 折线的截断点必然落在阶段 A 已验证过的折线上；没有记下折线的位移（退回弦）缩短后的弦必须可走，不可走则退回起点。
    /// 跟在另一个单位后面走的单位因此不再被"别人起点的位置"拦住、不再滞后一个 tick。
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

        /// <summary>本 tick 有体积的单位的受控位移到达/受阻停止事件（延后到成对裁决之后、带最终位置发出）。</summary>
        private readonly List<(Id Unit, MoveStopReason Reason)> _deferredStops = new List<(Id, MoveStopReason)>();
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
                body.GhostLanding = false;
                body.PulledBack = false;
                body.Scale = 1.0;
                var current = body.Active ? unit!.Position : body.Start;
                body.Delta = current - body.Start;
                body.Final = current;
                if (body.Active)
                {
                    var t = GetMotionTick(unit!);
                    body.Ghost = t != null && t.PassedThroughUnits;
                    body.GhostLanding = body.Ghost && t!.PassThroughEnds;
                }
            }

            ResolveMovedPairs();
            CapturePassOutcome();
            ResolveSeparation(dt);
            ApplyPendingPushes(world, dt);
            FlushMovedEvents();
            FlushDeferredStops();
        }

        // ------------------------------------------------------------------ 成对撞停

        /// <summary>
        /// 一个单位这 tick 的位移轨迹：折线点（含起点）与累计弧长。时间模型是"本 tick 内沿折线匀速走完"：时刻 t∈[0,1] 的位置是折线上
        /// 弧长占比 <c>scale × t</c> 的点（<c>scale</c> 是被缩短后的比例，1 = 走完）。只有一条边时与"起点 + 位移向量 × 比例"逐位相同。
        /// </summary>
        private sealed class Trajectory
        {
            public Vec2[] Pts = Array.Empty<Vec2>();
            public double[] Cum = Array.Empty<double>();
            public int[]? LegIndex;

            /// <summary>轨迹开头吃掉零长度路点之后的路径下标（-1 = 没有），见 <see cref="VolumeBody.BaseIndex"/>。</summary>
            public int BaseIndex = -1;

            public double Length;

            /// <summary>折线来自阶段 A 的记录（每条边都经过地形裁决）；为假时是起终点的弦，缩短后必须重新验证可走。</summary>
            public bool Exact;

            /// <summary>本 tick 的速度剖面（弧长占比随时间的分布）；null = 沿折线匀速。</summary>
            public SpeedProfile? Prof;

            public int Legs => Pts.Length - 1;

            public double RateMax => Prof == null ? 1.0 : Prof.MaxRate;

            /// <summary>放慢到占比 <paramref name="scale"/> 后，时刻 <paramref name="t"/> 的位置：折线上弧长占比 <c>scale × u(t)</c> 处。</summary>
            public Vec2 AtTime(double scale, double t) => At(scale * (Prof == null ? t : Prof.U(t)));

            /// <summary>放慢到占比 <paramref name="scale"/> 后，时刻 <paramref name="t"/> 的速度向量（每单位 t）。</summary>
            public Vec2 VelocityAtTime(double scale, double t)
            {
                if (Legs <= 0 || !(scale > 0.0))
                {
                    return Vec2.Zero;
                }

                var u = Prof == null ? t : Prof.U(t);
                var rate = Prof == null ? 1.0 : Prof.Rate(t);
                var k = Legs == 1 ? 0 : LegAtArc(scale * u * Length);
                var len = Cum[k + 1] - Cum[k];
                return (Pts[k + 1] - Pts[k]) * (scale * rate * (Length / len));
            }

            public static Trajectory Still(Vec2 at) => new Trajectory { Pts = new[] { at }, Cum = new[] { 0.0 } };

            public static Trajectory Chord(Vec2 from, Vec2 to)
            {
                var length = (to - from).Length;
                return length > 0.0
                    ? new Trajectory { Pts = new[] { from, to }, Cum = new[] { 0.0, length }, Length = length }
                    : Still(from);
            }

            /// <summary>折线上弧长占比 <paramref name="u"/>（0..1）处的点。</summary>
            public Vec2 At(double u)
            {
                if (Legs <= 0 || u <= 0.0)
                {
                    return Pts[0];
                }

                if (Legs == 1)
                {
                    return Pts[0] + (Pts[1] - Pts[0]) * u;
                }

                if (u >= 1.0)
                {
                    return Pts[Pts.Length - 1];
                }

                var k = LegAtArc(u * Length);
                var len = Cum[k + 1] - Cum[k];
                return Pts[k] + (Pts[k + 1] - Pts[k]) * ((u * Length - Cum[k]) / len);
            }

            /// <summary>弧长 <paramref name="arc"/> 所在的边下标（边界归后一条，末尾夹在最后一条）。</summary>
            public int LegAtArc(double arc)
            {
                var k = 0;
                while (k < Legs - 1 && Cum[k + 1] <= arc)
                {
                    k++;
                }

                return k;
            }

            /// <summary>时间区间 (ta, tb) 内的速度向量（每单位 t）：沿时间中点所在的那条边，速率 = 比例 × 总弧长 / 该边长。</summary>
            public Vec2 Velocity(double scale, double ta, double tb)
            {
                if (Legs <= 0 || !(scale > 0.0))
                {
                    return Vec2.Zero;
                }

                var k = Legs == 1 ? 0 : LegAtArc(scale * 0.5 * (ta + tb) * Length);
                var len = Cum[k + 1] - Cum[k];
                return (Pts[k + 1] - Pts[k]) * (scale * (Length / len));
            }
        }

        private Trajectory BuildTrajectory(VolumeBody b)
        {
            var trail = b.Trail;
            if (trail != null && !b.TrailBroken && trail.Count >= 2 && trail[trail.Count - 1].Equals(b.Final) && trail[0].Equals(b.Start))
            {
                var pts = new List<Vec2> { trail[0] };
                var idx = new List<int>();
                var cum = new List<double> { 0.0 };
                for (var k = 1; k < trail.Count; k++)
                {
                    var len = (trail[k] - pts[pts.Count - 1]).Length;
                    if (!(len > 0.0))
                    {
                        continue;
                    }

                    pts.Add(trail[k]);
                    cum.Add(cum[cum.Count - 1] + len);
                    idx.Add(b.TrailIndex != null && k - 1 < b.TrailIndex.Count ? b.TrailIndex[k - 1] : -1);
                }

                if (pts.Count >= 2)
                {
                    return new Trajectory
                    {
                        Pts = pts.ToArray(), Cum = cum.ToArray(), LegIndex = idx.ToArray(), Length = cum[cum.Count - 1], Exact = true,
                        Prof = b.ProfileMixed ? null : b.Profile, BaseIndex = b.BaseIndex,
                    };
                }
            }

            var chord = Trajectory.Chord(b.Start, b.Final);
            chord.BaseIndex = b.BaseIndex;
            return chord;
        }

        /// <summary>幽灵（穿过式位移）落点修正与成对裁决互相迭代的最大轮数（落点变化 → 别人的裁决变化 → 落点再校验）。</summary>
        private const int GhostRounds = 4;

        private void ResolveMovedPairs()
        {
            var n = _volumes.Count;
            var participants = new List<int>();
            var trajs = new Trajectory[n];
            var proposal = new Vec2[n];
            var ghostPos = new Vec2[n];
            var anyMover = false;
            var anyLanding = false;
            for (var i = 0; i < n; i++)
            {
                var b = _volumes[i];
                proposal[i] = b.Final;
                ghostPos[i] = b.Final;
                if (b.Active)
                {
                    participants.Add(i);
                    if (b.Ghost)
                    {
                        // 幽灵本 tick 穿过别人：自己的位移不参与撞停；但别人的成对裁决看得到它的最终位置（当作静止的体积）。
                        trajs[i] = Trajectory.Still(b.Final);
                        anyLanding |= b.GhostLanding;
                    }
                    else
                    {
                        trajs[i] = BuildTrajectory(b);
                        anyMover |= trajs[i].Length > 0.0;
                    }
                }
                else
                {
                    trajs[i] = Trajectory.Still(b.Start);
                }
            }

            if ((!anyMover && !anyLanding) || participants.Count < 2)
            {
                return;
            }

            var a = new Vec2[n];
            var reach = new double[n];
            var scale = new double[n];
            var pullNormal = new Vec2[n];
            var hasPull = new bool[n];
            var finals = new Vec2[n];
            var near = new List<(int I, int J)>();
            var pairs = new List<(int I, int J)>();
            for (var round = 0; round <= GhostRounds; round++)
            {
                for (var i = 0; i < n; i++)
                {
                    var b = _volumes[i];
                    if (b.Active && b.Ghost)
                    {
                        trajs[i] = Trajectory.Still(ghostPos[i]);
                        a[i] = ghostPos[i];
                    }
                    else
                    {
                        a[i] = b.Start;
                    }

                    reach[i] = b.Radius + trajs[i].Length;
                    scale[i] = 1.0;
                    hasPull[i] = false;
                }

                near.Clear();
                pairs.Clear();
                CollectNearPairs(participants, a, reach, near);
                for (var k = 0; k < near.Count; k++)
                {
                    if (trajs[near[k].I].Length > 0.0 || trajs[near[k].J].Length > 0.0)
                    {
                        pairs.Add(near[k]);
                    }
                }

                if (pairs.Count > 0)
                {
                    SolveContactScales(
                        trajs, scale, pairs, (index, s) => trajs[index].Legs <= 0 || ValidTruncation(trajs[index], index, s),
                        pullNormal, hasPull);
                }

                for (var i = 0; i < n; i++)
                {
                    var b = _volumes[i];
                    finals[i] = b.Active && b.Ghost
                        ? ghostPos[i]
                        : (b.Active && scale[i] < 1.0 && trajs[i].Legs > 0 ? trajs[i].At(scale[i]) : (b.Active ? proposal[i] : b.Start));
                }

                if (!anyLanding)
                {
                    break;
                }

                // 幽灵落点修正：窗口最后一个 tick 的幽灵不得终止在别的单位体积里（别人的最终位置已定），就近挪到可行位置；
                // 落点变了，别人的成对裁决要对新落点重算（下一轮）。按快照下标顺序逐个修正，结果与处理顺序无关。
                var changed = false;
                for (var k = 0; k < participants.Count; k++)
                {
                    var i = participants[k];
                    var b = _volumes[i];
                    if (!(b.Ghost && b.GhostLanding))
                    {
                        continue;
                    }

                    if (TryRelocateGhost(i, finals, out var landing) && !landing.Equals(ghostPos[i]))
                    {
                        ghostPos[i] = landing;
                        finals[i] = landing;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            for (var k = 0; k < participants.Count; k++)
            {
                var i = participants[k];
                var b = _volumes[i];
                if (b.Ghost)
                {
                    if (!ghostPos[i].Equals(proposal[i]))
                    {
                        b.Final = ghostPos[i];
                        _units.SetPosition(b.Id, ghostPos[i]);
                        _movedDeferredSet.Add(b.Id);
                    }

                    continue;
                }

                if (scale[i] >= 1.0)
                {
                    continue;
                }

                var p = trajs[i].At(scale[i]);
                b.Scale = scale[i];
                b.PulledBack = true;
                b.PullNormal = pullNormal[i];
                b.HasPullNormal = hasPull[i];
                b.Final = p;
                b.RestoreIndex = IndexAtScale(trajs[i], scale[i]);
                _units.SetPosition(b.Id, p);
                _movedDeferredSet.Add(b.Id);
            }
        }

        /// <summary>被缩短到弧长占比 <paramref name="scale"/> 时路径下标应回到的值：走完的最后一条边走完时的下标；没走完任何一条边 / 没有记录返回 -1。</summary>
        private static int IndexAtScale(Trajectory traj, double scale)
        {
            if (traj.LegIndex == null)
            {
                return traj.BaseIndex;
            }

            var arc = scale * traj.Length;
            var result = traj.BaseIndex;
            for (var k = 0; k < traj.Legs; k++)
            {
                if (traj.Cum[k + 1] <= arc + 1e-12)
                {
                    result = traj.LegIndex[k] >= 0 ? traj.LegIndex[k] : result;
                }
            }

            return result;
        }

        /// <summary>
        /// 折线缩短到占比 <paramref name="s"/> 后是否仍合法：阶段 A 记下的折线逐边裁决过地形，截断点只需可走；
        /// 起终点的弦（没有记下折线）缩短后的弦必须整条可走。没有导航时恒合法。
        /// </summary>
        private bool ValidTruncation(Trajectory traj, int index, double s)
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

            var p = traj.At(s);
            if (p.Equals(b.Start))
            {
                return true;
            }

            return traj.Exact
                ? _navigation.IsWalkable(b.Unit.MapId, p)
                : !NavRaycast(b.Unit, b.Start, p).HasValue && _navigation.IsWalkable(b.Unit.MapId, p);
        }

        // ------------------------------------------------------------------ 重叠分离

        private void ResolveSeparation(double dt)
        {
            var n = _volumes.Count;
            var participants = new List<int>();
            for (var i = 0; i < n; i++)
            {
                var b = _volumes[i];
                if (b.Active && (!b.Ghost || b.GhostLanding))
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
            var pushTrajs = new Trajectory[n];
            for (var i = 0; i < n; i++)
            {
                scale[i] = 1.0;
                pushTrajs[i] = Trajectory.Chord(pos[i], pos[i] + push[i]);
            }

            SolveContactScales(pushTrajs, scale, pushPairs, null);
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

        /// <summary>一次首次接触的现场：时刻、命中的线性区间长度与相对位移平方、接触时两个单位的位置与速度（每单位 t）。</summary>
        private struct Contact
        {
            public bool Found;
            public double Tau;
            public double Dt;
            public double RelSq;
            public Vec2 Pi;
            public Vec2 Pj;
            public Vec2 Vi;
            public Vec2 Vj;
        }

        /// <summary>
        /// 两个单位的轨迹（各自当前被缩短到占比 <paramref name="si"/>、<paramref name="sj"/>）在本 tick 内的首次接触：
        /// 中心距首次小于 <c>min(<paramref name="limit"/>, 起始距离)</c>（"不得变得更深"）。两个单位的位置在时间上是分段线性的
        /// （折线的每个拐点是一个断点），每个区间解一个二次方程，取最早命中——所以折线位移与弦不同，弦会误拦折线绕开的接触、
        /// 也会漏掉只发生在折线某一边上的接触。两个轨迹都只有一条边时，区间只有 [0,1]，算式与"起点 + 位移向量 × 比例"的弦逐位相同。
        /// </summary>
        private static Contact FirstContact(Trajectory ti, double si, Trajectory tj, double sj, double limit)
        {
            if (ti.Prof != null || tj.Prof != null)
            {
                return FirstContactProfiled(ti, si, tj, sj, limit);
            }

            const double tol = 1e-9;
            var result = new Contact();
            var f0 = ti.Pts[0] - tj.Pts[0];
            var f2 = f0.Dot(f0);
            var dist0 = Math.Sqrt(f2);
            var thr = Math.Min(limit, dist0) - tol;
            if (thr <= 0.0)
            {
                return result;
            }

            var breaks = new List<double> { 0.0 };
            AddBreaks(ti, si, breaks);
            AddBreaks(tj, sj, breaks);
            breaks.Add(1.0);
            breaks.Sort();

            for (var k = 0; k + 1 < breaks.Count; k++)
            {
                var ta = breaks[k];
                var tb = breaks[k + 1];
                var dt = tb - ta;
                if (!(dt > 0.0))
                {
                    continue;
                }

                var fa = k == 0 ? f0 : ti.At(si * ta) - tj.At(sj * ta);
                var vi = ti.Velocity(si, ta, tb);
                var vj = tj.Velocity(sj, ta, tb);
                var r = (vi - vj) * dt;
                var a = r.Dot(r);
                if (a <= 1e-24)
                {
                    continue;
                }

                var b = fa.Dot(r);
                var c = fa.Dot(fa) - thr * thr;
                double tau;
                if (c <= 0.0)
                {
                    // 上一个区间的末端恰好落在阈值上（数值贴边）：在这个区间起点接触，前提是在靠近。
                    if (b >= 0.0)
                    {
                        continue;
                    }

                    tau = ta;
                }
                else
                {
                    if (b >= 0.0)
                    {
                        continue; // 不是在靠近。
                    }

                    var disc = b * b - a * c;
                    if (disc <= 0.0)
                    {
                        continue;
                    }

                    var w = (-b - Math.Sqrt(disc)) / a;
                    if (w >= 1.0)
                    {
                        continue;
                    }

                    tau = ta + w * dt;
                }

                result.Found = true;
                result.Tau = tau;
                result.Dt = dt;
                result.RelSq = a;
                result.Pi = ti.At(si * tau);
                result.Pj = tj.At(sj * tau);
                result.Vi = vi;
                result.Vj = vj;
                return result;
            }

            return result;
        }

        /// <summary>折线内部拐点被走到的时刻（占比 <paramref name="scale"/> 下，拐点 k 在 <c>t = (Cum[k]/Length)/scale</c>）。</summary>
        private static void AddBreaks(Trajectory traj, double scale, List<double> breaks)
        {
            if (traj.Legs <= 1 || !(scale > 0.0))
            {
                return;
            }

            for (var k = 1; k < traj.Legs; k++)
            {
                var t = traj.Cum[k] / traj.Length / scale;
                if (t > 0.0 && t < 1.0)
                {
                    breaks.Add(t);
                }
            }
        }

        /// <summary>
        /// 只有一方在"靠近"时让这一方让路：找最大的比例 <c>s ∈ [0, sCurrent)</c> 使它放慢到 <c>s</c> 之后与对方整个 tick 的轨迹不再接触
        /// （阈值取 <c>半径之和 + 一个到达容差</c>，所以最终停在体积边界外一个容差处）。二分法保持"下界不接触、上界接触"，所以返回值必然不接触；
        /// 对方走远的话（跟在前一个单位后面走）让路的比例由对方的最终位置决定——靠近到恰好贴着对方的最终位置，不被对方起点的位置拦住。
        /// </summary>
        private double YieldScale(Trajectory yielder, double sCurrent, Trajectory other, double sOther, double sum, bool yielderIsFirst)
        {
            var limit = sum + _options.ArrivalEpsilon;
            bool Free(double s) => !(yielderIsFirst
                ? FirstContact(yielder, s, other, sOther, limit).Found
                : FirstContact(other, sOther, yielder, s, limit).Found);

            if (!Free(0.0))
            {
                return 0.0; // 就算原地不动也会被对方撞上：让路无解，交给迭代与兜底。
            }

            var lo = 0.0;
            var hi = sCurrent;
            for (var iter = 0; iter < 48; iter++)
            {
                var mid = 0.5 * (lo + hi);
                if (Free(mid))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        /// <summary>
        /// Jacobi 迭代：每轮用上一轮的比例对所有候选对求接触，每个单位的新比例取"自己的比例 × 各对 τ"的最小值（取最小与对的遍历顺序无关）。
        /// <para>
        /// <b>谁让路</b>：接触点上两个单位的速度沿连线方向的分量决定"谁在靠近"。两方都在靠近（相向）或都不是（贴边擦过）时对称缩短，
        /// 两个单位按同一比例缩短、再退一个到达容差；只有一方在靠近时只有这一方让路（<see cref="YieldScale"/>），走开的那一方不受影响——
        /// 所以快的跟随者追上慢的单位时，慢的单位不会被它拖住，跟随者贴着对方的最终位置停下。
        /// </para>
        /// 一轮没有任何对接触即收敛；<see cref="ContactIterations"/> 轮仍未收敛时把仍冲突的单位退回起点（比例 0）。
        /// <paramref name="valid"/> 非空时，比例被缩短的单位的新位置必须通过它，否则该单位退回起点。
        /// </summary>
        private void SolveContactScales(
            Trajectory[] trajs, double[] scale, List<(int I, int J)> pairs, Func<int, double, bool>? valid,
            Vec2[]? pullNormal = null, bool[]? hasPull = null)
        {
            const double approachTolerance = 1e-9;
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
                    var sum = _volumes[i].Radius + _volumes[j].Radius;
                    var contact = FirstContact(trajs[i], scale[i], trajs[j], scale[j], sum);
                    if (!contact.Found)
                    {
                        continue;
                    }

                    any = true;
                    var line = contact.Pj - contact.Pi;
                    var lineLength = line.Length;
                    var approachI = 0.0;
                    var approachJ = 0.0;
                    var normal = Vec2.Zero;
                    if (lineLength > 1e-12)
                    {
                        normal = new Vec2(line.X / lineLength, line.Y / lineLength);
                        approachI = contact.Vi.Dot(normal);
                        approachJ = -contact.Vj.Dot(normal);
                    }

                    var iApproaches = approachI > approachTolerance;
                    var jApproaches = approachJ > approachTolerance;
                    if (iApproaches != jApproaches)
                    {
                        var yielderIsFirst = iApproaches;
                        var yielder = yielderIsFirst ? i : j;
                        var other = yielderIsFirst ? j : i;
                        var s = YieldScale(trajs[yielder], scale[yielder], trajs[other], scale[other], sum, yielderIsFirst);
                        if (s < next[yielder])
                        {
                            next[yielder] = s;
                            // 法线从对方指向让路者（让路者是 i 时对方在 +normal 一侧，所以取 −normal）。
                            NotePull(pullNormal, hasPull, yielder, yielderIsFirst ? normal * -1.0 : normal, lineLength > 1e-12);
                        }

                        continue;
                    }

                    var tau = contact.Tau - eps * contact.Dt / Math.Sqrt(contact.RelSq);
                    if (tau < 0.0) tau = 0.0;
                    var si = scale[i] * (trajs[i].Prof == null ? tau : trajs[i].Prof!.U(tau));
                    var sj = scale[j] * (trajs[j].Prof == null ? tau : trajs[j].Prof!.U(tau));
                    if (si < next[i])
                    {
                        next[i] = si;
                        NotePull(pullNormal, hasPull, i, normal * -1.0, lineLength > 1e-12);
                    }

                    if (sj < next[j])
                    {
                        next[j] = sj;
                        NotePull(pullNormal, hasPull, j, normal, lineLength > 1e-12);
                    }
                }

                if (!any)
                {
                    return;
                }

                if (valid != null)
                {
                    for (var k = 0; k < n; k++)
                    {
                        if (next[k] < scale[k] && !valid(k, next[k]))
                        {
                            next[k] = 0.0;
                            NotePull(pullNormal, hasPull, k, Vec2.Zero, false);
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
                    if (FirstContact(trajs[i], scale[i], trajs[j], scale[j], _volumes[i].Radius + _volumes[j].Radius).Found)
                    {
                        next[i] = 0.0;
                        next[j] = 0.0;
                        NotePull(pullNormal, hasPull, i, Vec2.Zero, false);
                        NotePull(pullNormal, hasPull, j, Vec2.Zero, false);
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

        private static void NotePull(Vec2[]? normals, bool[]? has, int index, Vec2 normal, bool valid)
        {
            if (normals == null || has == null)
            {
                return;
            }

            normals[index] = normal;
            has[index] = valid;
        }

        /// <summary>
        /// 收集"位移包络可能相交"的单位对（<c>|pos_i − pos_j| ≤ reach_i + reach_j</c>），i &lt; j、按 (i, j) 排序。<paramref name="idx"/> 是参与者的快照下标。
        /// 宽相：包络半径不大于"中位数的两倍"的单位按均匀网格取 3×3 邻格（格边长 = 该上限的两倍），包络特别大的少数单位（冲刺、位移很远）
        /// 逐个与全部参与者比较——两条路径用的是同一个判定式 <c>delta·delta ≤ lim²</c>，所以结果集合与暴力两两比较完全相同（再按 (i, j) 排序，
        /// 顺序也相同）；<see cref="VolumeBroadPhaseBruteForce"/> 打开时直接暴力两两比较，作为测试的对照基准。
        /// </summary>
        private void CollectNearPairs(List<int> idx, Vec2[] pos, double[] reach, List<(int I, int J)> result)
        {
            var count = idx.Count;
            if (VolumeBroadPhaseBruteForce || count <= 16)
            {
                for (var x = 0; x < count; x++)
                {
                    for (var y = x + 1; y < count; y++)
                    {
                        AddIfNear(idx[x], idx[y], pos, reach, result);
                    }
                }
            }
            else
            {
                var sorted = new double[count];
                for (var k = 0; k < count; k++) sorted[k] = reach[idx[k]];
                Array.Sort(sorted);
                var limit = 2.0 * Math.Max(sorted[count / 2], 1e-6);
                var cell = 2.0 * limit;
                var small = new List<int>(count);
                var large = new List<int>();
                for (var k = 0; k < count; k++)
                {
                    (reach[idx[k]] <= limit ? small : large).Add(k);
                }

                var cells = new Dictionary<long, List<int>>();
                for (var k = 0; k < small.Count; k++)
                {
                    var p = pos[idx[small[k]]];
                    var key = GridKey(CellOf(p.X, cell), CellOf(p.Y, cell));
                    if (!cells.TryGetValue(key, out var list))
                    {
                        list = new List<int>(4);
                        cells[key] = list;
                    }

                    list.Add(small[k]); // 参与者序号（idx 里的位置）。
                }

                for (var k = 0; k < small.Count; k++)
                {
                    var ordI = small[k];
                    var p = pos[idx[ordI]];
                    var cx = CellOf(p.X, cell);
                    var cy = CellOf(p.Y, cell);
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            if (!cells.TryGetValue(GridKey(cx + dx, cy + dy), out var list))
                            {
                                continue;
                            }

                            for (var m = 0; m < list.Count; m++)
                            {
                                if (list[m] > ordI)
                                {
                                    AddIfNear(idx[ordI], idx[list[m]], pos, reach, result);
                                }
                            }
                        }
                    }
                }

                for (var k = 0; k < large.Count; k++)
                {
                    var ordL = large[k];
                    for (var m = 0; m < count; m++)
                    {
                        if (m == ordL) continue;
                        if (reach[idx[m]] > limit && m < ordL) continue; // 两个都"大"的对只记一次。
                        AddIfNear(idx[ordL], idx[m], pos, reach, result);
                    }
                }
            }

            result.Sort((p, q) =>
            {
                var c = p.I.CompareTo(q.I);
                return c != 0 ? c : p.J.CompareTo(q.J);
            });
        }

        private static int CellOf(double v, double cell)
        {
            var c = Math.Floor(v / cell);
            return c > int.MaxValue / 2 ? int.MaxValue / 2 : (c < int.MinValue / 2 ? int.MinValue / 2 : (int)c);
        }

        private static void AddIfNear(int i, int j, Vec2[] pos, double[] reach, List<(int I, int J)> result)
        {
            var lim = reach[i] + reach[j];
            var delta = pos[i] - pos[j];
            if (delta.Dot(delta) <= lim * lim)
            {
                result.Add(i < j ? (i, j) : (j, i));
            }
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

            var toTarget = BodyAt(body) - volume.End;
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

        /// <summary>同 tick 推人的连锁轮数上限（被推的单位又把别人推出去，每轮一批）；超出的推人退回"下一 tick 起推进"。</summary>
        private const int PushRounds = 8;

        /// <summary>
        /// 受控位移推人在<b>同一 tick</b> 生效：被推单位（同一目标的多个推人先按向量和合并）开一段受控位移后，立刻用真实的受控位移推进（
        /// <see cref="AdvanceDisplacement"/>，地形、体积、到达/受阻事件与下一 tick 起的推进同一套代码）走完这个 tick 的位移——不再等到下一 tick。
        /// 一轮里所有被推单位互相是"会动的"（不当静止障碍），对别的单位按它们已裁决完的最终位置扫掠（<see cref="_microPhase"/>），所以一轮的结果
        /// 与被推单位的处理顺序无关；被推单位之间在一轮结束后按它们这个 tick 的位移弦做一次成对接触求解（<see cref="SolveContactScales"/>，
        /// 与阶段 B 主体同一个求解器），互相撞上的缩短位移、结束位移（受阻）。被推单位自己的位移被体积挡住又触发推人时进入下一轮（连锁，
        /// <see cref="PushRounds"/> 轮封顶，余下的退回下一 tick 起推进）。
        /// </summary>
        private void ApplyPendingPushes(IWorldSim world, double dt)
        {
            for (var round = 0; round < PushRounds && _pendingPushes.Count > 0; round++)
            {
                var batch = new List<PendingPush>(_pendingPushes);
                _pendingPushes.Clear();
                var targets = AggregatePushes(world, batch);
                if (targets.Count == 0)
                {
                    continue;
                }

                var pushed = new List<(Unit Unit, VolumeBody? Body, Vec2 Before)>();
                var free = new HashSet<Id>();
                for (var k = 0; k < targets.Count; k++)
                {
                    var (unit, sum, length, best) = targets[k];
                    BeginPushedDisplacement(unit, sum, length, best);
                    _volumeById.TryGetValue(unit.EntityId, out var body);
                    pushed.Add((unit, body, unit.Position));
                    free.Add(unit.EntityId);
                }

                if (!VolumeSnapshotCurrent)
                {
                    continue; // 本 tick 没有体积快照（理论上不会发生，推人只来自体积裁决）：只开了位移，下一 tick 起推进。
                }

                var wasFree = new bool[_volumes.Count];
                for (var i = 0; i < _volumes.Count; i++)
                {
                    wasFree[i] = _volumes[i].Free;
                    _volumes[i].Free = free.Contains(_volumes[i].Id);
                }

                _microPhase = true;
                try
                {
                    for (var k = 0; k < pushed.Count; k++)
                    {
                        AdvanceDisplacement(pushed[k].Unit, dt, false);
                        if (pushed[k].Body != null)
                        {
                            pushed[k].Body!.Final = pushed[k].Unit.Position;
                        }
                    }

                    ResolvePushedPairs(pushed);
                }
                finally
                {
                    _microPhase = false;
                    for (var i = 0; i < _volumes.Count; i++)
                    {
                        _volumes[i].Free = wasFree[i];
                    }
                }
            }

            if (_pendingPushes.Count > 0)
            {
                // 连锁超出轮数上限：余下的推人只开位移（下一 tick 起推进）。
                var rest = new List<PendingPush>(_pendingPushes);
                _pendingPushes.Clear();
                var targets = AggregatePushes(world, rest);
                for (var k = 0; k < targets.Count; k++)
                {
                    BeginPushedDisplacement(targets[k].Unit, targets[k].Sum, targets[k].Length, targets[k].Best);
                }
            }
        }

        /// <summary>同一目标的多个推人合并成向量和（按目标 id、推人者 id 排序，顺序无关）；已死亡、销毁、正在受控位移的目标不被推。</summary>
        private List<(Unit Unit, Vec2 Sum, double Length, PendingPush Best)> AggregatePushes(IWorldSim world, List<PendingPush> batch)
        {
            var result = new List<(Unit, Vec2, double, PendingPush)>();
            batch.Sort((p, q) =>
            {
                var c = p.Target.CompareTo(q.Target);
                return c != 0 ? c : p.Pusher.CompareTo(q.Pusher);
            });

            var i = 0;
            while (i < batch.Count)
            {
                var target = batch[i].Target;
                var sum = Vec2.Zero;
                var best = batch[i];
                var j = i;
                while (j < batch.Count && batch[j].Target.Equals(target))
                {
                    var push = batch[j];
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

                result.Add((unit, sum, length, best));
            }

            return result;
        }

        /// <summary>
        /// 一轮推人之后，被推单位这个 tick 的位移弦（推之前 → 推之后）与全体单位的最终位置做成对接触求解：被推单位之间相互撞上（或撞上
        /// 位移前没有被扫掠算到的单位）时按同一比例缩短、结束受控位移（受阻，位置是缩短后的位置）。
        /// </summary>
        private void ResolvePushedPairs(List<(Unit Unit, VolumeBody? Body, Vec2 Before)> pushed)
        {
            var n = _volumes.Count;
            var trajs = new Trajectory[n];
            var participants = new List<int>();
            var movers = new bool[n];
            var a = new Vec2[n];
            var reach = new double[n];
            var scale = new double[n];
            var any = false;
            for (var i = 0; i < n; i++)
            {
                var b = _volumes[i];
                trajs[i] = Trajectory.Still(b.Active ? b.Final : b.Start);
                if (b.Active)
                {
                    participants.Add(i);
                }
            }

            for (var k = 0; k < pushed.Count; k++)
            {
                var body = pushed[k].Body;
                if (body == null || !body.Active)
                {
                    continue;
                }

                var chord = Trajectory.Chord(pushed[k].Before, body.Final);
                if (chord.Length > 0.0)
                {
                    trajs[body.Index] = chord;
                    movers[body.Index] = true;
                    any = true;
                }
            }

            if (!any)
            {
                return;
            }

            for (var i = 0; i < n; i++)
            {
                a[i] = trajs[i].Pts[0];
                reach[i] = _volumes[i].Radius + trajs[i].Length;
                scale[i] = 1.0;
            }

            var near = new List<(int I, int J)>();
            CollectNearPairs(participants, a, reach, near);
            var pairs = new List<(int I, int J)>();
            for (var k = 0; k < near.Count; k++)
            {
                if (movers[near[k].I] || movers[near[k].J])
                {
                    pairs.Add(near[k]);
                }
            }

            if (pairs.Count == 0)
            {
                return;
            }

            SolveContactScales(
                trajs, scale, pairs,
                (index, s) => !movers[index] || _navigation == null || _volumes[index].Unit == null ||
                              _navigation.IsWalkable(_volumes[index].Unit!.MapId, trajs[index].At(s)));
            for (var k = 0; k < pushed.Count; k++)
            {
                var body = pushed[k].Body;
                if (body == null || !movers[body.Index] || !(scale[body.Index] < 1.0))
                {
                    continue;
                }

                var p = trajs[body.Index].At(scale[body.Index]);
                body.Final = p;
                _units.SetPosition(body.Id, p);
                _movedDeferredSet.Add(body.Id);
                if (pushed[k].Unit.MovementState.Displacement.HasValue)
                {
                    EndDisplacement(pushed[k].Unit, MoveStopReason.DisplacementBlocked);
                }
            }
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
                    EmitBus(new UnitMovedEvent(id, position));
                }
            }

            _movedDeferredSet.Clear();
        }

        /// <summary>
        /// 有体积的单位的受控位移到达/受阻事件（<see cref="MovementHost.OnMoveStopped"/>）也延后到成对裁决之后，带单位的最终位置
        /// （与 <c>unit.moved</c> 延后到 tick 末的口径一致）；按（单位 id，发生先后）排序，所以与单位处理顺序无关。事件的原因值不变——
        /// 位移在同 tick 被成对裁决拉回时，原因仍是当时记下的到达/受阻，位置是拉回之后的位置。
        /// </summary>
        private void FlushDeferredStops()
        {
            if (_deferredStops.Count == 0)
            {
                return;
            }

            var stops = new List<(Id Unit, int Seq, MoveStopReason Reason)>();
            for (var i = 0; i < _deferredStops.Count; i++)
            {
                stops.Add((_deferredStops[i].Unit, i, _deferredStops[i].Reason));
            }

            _deferredStops.Clear();
            stops.Sort((p, q) =>
            {
                var c = p.Unit.CompareTo(q.Unit);
                return c != 0 ? c : p.Seq.CompareTo(q.Seq);
            });
            for (var i = 0; i < stops.Count; i++)
            {
                var (id, _, reason) = stops[i];
                if (_units.Exists(id))
                {
                    EmitMoveStopped(id, _units.GetPosition(id), reason);
                }
            }
        }

        /// <summary>
        /// 写运动学状态：被阶段 B 缩短位移的单位保留沿接触面的切向速度（法向分量清零；没有法线可用时整体清零），仍在沿路径走的单位
        /// 路径进度回到被缩短后的位置（<see cref="RestorePathProgress"/>）。
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
                    var velocity = Vec2.Zero;
                    if (body.HasPullNormal)
                    {
                        // 撞上的法线方向（从对方指向本单位）：速度朝向对方的分量清零，沿接触面的切向分量保留（贴着对方滑过去，不是撞停归零）。
                        var into = kin.Velocity.Dot(body.PullNormal);
                        velocity = into < 0.0
                            ? new Vec2(kin.Velocity.X - body.PullNormal.X * into, kin.Velocity.Y - body.PullNormal.Y * into)
                            : kin.Velocity;
                    }

                    kin = new MotionKinematics(velocity, kin.DesiredDirection, kin.Mode, kin.BaseMode, kin.Source, kin.BaseSpeed);
                    RestorePathProgress(unit, item.Tick, body);
                }

                if (!SameKinematics(unit.MovementState.Motion, kin))
                {
                    unit.MovementState = unit.MovementState.WithMotion(kin);
                }
            }

            _deferredKin.Clear();
        }

        /// <summary>
        /// 路径进度回到被缩短后的位置：阶段 A 记下了轨迹里每条边走完时的路径下标与路径对象本身（<see cref="VolumeBody.TrailPath"/>），
        /// 所以不依赖"路径与 tick 开始时是同一条"——本 tick 新建的路径、替换过的路径一样退回到被缩短位置之前最后走完的路点。
        /// 本 tick 走到终点（状态已写成到达/Idle）而被拉回时，到达作废：状态退回仍沿原路径走的"未到达"，撤销到达事件。
        /// 追击把走完的路径清空时同样把路径接回（追击目标与状态不变）。
        /// </summary>
        private void RestorePathProgress(Unit unit, MotionTick tick, VolumeBody body)
        {
            var trailPath = body.TrailPath;
            var state = unit.MovementState;
            if (trailPath == null || state.Displacement.HasValue)
            {
                return;
            }

            var restoreIndex = body.RestoreIndex >= body.TrailStartIndex ? body.RestoreIndex : body.TrailStartIndex;
            if (restoreIndex >= trailPath.Count)
            {
                return; // 缩短后仍走完了全部路点（容差内）：到达有效。
            }

            if (state.CurrentPath != null)
            {
                if (ReferenceEquals(state.CurrentPath, trailPath) && state.PathIndex > restoreIndex)
                {
                    unit.MovementState = new MovementState(
                        state.CurrentPath, state.Mode, state.MovementLocked, restoreIndex, state.NavVersion,
                        null, state.Chase, state.RequestedTarget, state.Motion);
                }

                return;
            }

            if (state.Chase.HasValue)
            {
                unit.MovementState = new MovementState(
                    trailPath, state.Mode, state.MovementLocked, restoreIndex, state.NavVersion,
                    null, state.Chase, state.RequestedTarget, state.Motion);
                return;
            }

            var template = body.UnarriveTemplate;
            if (template.HasValue && ReferenceEquals(template.Value.CurrentPath, trailPath))
            {
                var t = template.Value;
                unit.MovementState = new MovementState(
                    t.CurrentPath, t.Mode, state.MovementLocked, restoreIndex, t.NavVersion, null, null, t.RequestedTarget, state.Motion);
                OutboxCancel(body.ArrivalOutboxIndex);
            }
        }
    }
}
