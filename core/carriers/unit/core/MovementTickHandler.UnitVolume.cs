using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 单位间体积阻挡（手感设计/02 第 3.5 节；ADR-0128；运动档案字段 <c>unit_body_radius</c> 等）：<see cref="MovementTickHandler"/>
    /// 运动层的一部分。本文件放"逐单位的扫掠裁决"（阶段 A：对别的单位 tick 起点位置的快照做连续扫掠、绕行），
    /// <c>MovementTickHandler.UnitVolumeResolve.cs</c> 放"tick 末的成对裁决"（阶段 B：两个都在动的单位互相撞停、重叠分离、受控位移推人）。
    /// <para>
    /// <b>开关与逐位等价</b>：只有运动层启用（见 <see cref="MovementTickHandler.Motion"/> 文件头）且<b>本单位</b>的档案声明了
    /// <c>unit_body_radius &gt; 0</c> 时才进入本文件的任何分支；没有声明体积半径的单位（含所有既有预设）既不触发快照、也不被延迟写回，
    /// 既有算式一字未动。体积是<b>成对</b>语义：两个都声明了半径的单位之间，中心距不得小于半径之和；一方没有体积则双方互不影响。
    /// </para>
    /// <para>
    /// <b>顺序无关</b>：每个有体积的单位在本 tick 内读到的"别的单位的位置"一律是 tick 起点的快照（<see cref="EnsureVolumeSnapshot"/>：
    /// 第一个有体积的单位被处理前一次性取下，按单位 id 排序），而不是"先走的单位已经写回的新位置"，所以阶段 A 的结果只依赖快照与单位自己的状态，
    /// 不依赖单位处理顺序；快照里的单位位置在 tick 末由阶段 B 统一裁决（对称地处理两个单位同时互相靠近）后才一次性写回、发出 <c>unit.moved</c>。
    /// </para>
    /// <para>
    /// <b>扫掠算法</b>：把"本 tick 从 <c>from</c> 走到 <c>to</c>"当成线段（折线时逐段），对每个有体积的其他存活同图单位（以其快照位置为圆心、
    /// 半径之和为半径）求线段首次进入圆的位置（连续扫掠，高速位移也不会隧穿）。命中后停在入射点之前一小段
    /// （<see cref="MovementOptions.ArrivalEpsilon"/>，同墙体阻挡的回退约定，保证下一 tick 不会从圆里起步）；允许滑动的来源
    /// （<c>wall_slide</c> 为真的输入位移、<c>blocking: slide</c> 的动作位移）把剩余位移去掉沿圆心连线方向的分量后再走一段（最多一次，
    /// 切向位移仍经地形裁决与第二次扫掠，不递归），否则整体停下。起点已在别人体积内（出生重叠、穿过式闪避的落点）时只拦"让距离变近"的位移，
    /// 允许走开；真正把重叠推开的是阶段 B 的分离。末尾有一道守卫：任何结果若落进体积且比起点更深，一律退回起点。
    /// </para>
    /// </summary>
    public sealed partial class MovementTickHandler
    {
        /// <summary>本 tick 快照里一个有体积的单位：id、半径、tick 起点位置、所在地图与分离速率倍数；其余字段是阶段 B 的草稿区。</summary>
        private sealed class VolumeBody
        {
            public Id Id;
            public double Radius;
            public Vec2 Start;
            public Id? Map;
            public double SeparationRatio;

            /// <summary>按 id 排序后的下标（成对计算与累加的规范顺序）。</summary>
            public int Index;

            // ---- 阶段 B 草稿区 ----
            public Unit? Unit;
            public bool Active;
            public bool Ghost;
            public Vec2 Delta;
            public double Scale;
            public bool PulledBack;
            public Vec2 Final;
        }

        private struct VolumeClip
        {
            /// <summary>裁决后的终点（未阻挡时等于传入的 <c>to</c>）。</summary>
            public Vec2 End;

            public bool Blocked;

            /// <summary>阻挡后改为沿体积切向滑开（<see cref="Normal"/> 有效）。</summary>
            public bool Slid;

            /// <summary>第一个被撞体积在入射点的外法线（从被撞单位中心指向入射点）。</summary>
            public Vec2 Normal;

            /// <summary>滑动那一段又撞上第二个体积（<see cref="SecondNormal"/> 有效）。</summary>
            public bool SecondBlocked;

            public Vec2 SecondNormal;

            /// <summary>第一个被撞单位（<see cref="Blocked"/> 为真时有效）。</summary>
            public Id HitId;
        }

        // 本 tick 内有体积的单位快照（id、半径、tick 起点位置，按 id 排序）。每 tick 在第一个有体积的单位被处理前懒建一次。
        private readonly List<VolumeBody> _volumes = new List<VolumeBody>();
        private readonly Dictionary<Id, VolumeBody> _volumeById = new Dictionary<Id, VolumeBody>();
        private long _volumesStamp = -1;

        /// <summary>本 tick 位置写回被延迟到阶段 B 的单位（它们的 <c>unit.moved</c> 也延迟到阶段 B 之后、按 id 顺序发出）。</summary>
        private readonly HashSet<Id> _movedDeferredSet = new HashSet<Id>();

        /// <summary>未启用运动层、离散步或本单位没有声明体积时返回 0。</summary>
        private double SelfVolumeRadius(Unit unit)
        {
            var t = GetMotionTick(unit);
            return t == null ? 0.0 : t.Profile.UnitBodyRadius;
        }

        /// <summary>任一单位的运动档案（与 <see cref="GetMotionTick"/> 共用档案缓存）。仅在运动层启用时调用。</summary>
        private MotionProfile ProfileOf(Id id)
        {
            var view = _feel!.ResolveJudging(id);
            if (!_motionProfiles.TryGetValue(id, out var profile) || profile.Version != view.Version)
            {
                profile = MotionProfile.Read(view);
                _motionProfiles[id] = profile;
            }

            return profile;
        }

        /// <summary>任一单位的体积半径。仅在运动层启用时调用。</summary>
        private double VolumeRadiusOf(Id id) => ProfileOf(id).UnitBodyRadius;

        private bool VolumeSnapshotCurrent => _volumesStamp == _motionStamp;

        private bool TryGetBody(Id id, out VolumeBody body)
        {
            if (VolumeSnapshotCurrent && _volumeById.TryGetValue(id, out body!))
            {
                return true;
            }

            body = null!;
            return false;
        }

        private void EnsureVolumeSnapshot()
        {
            if (_volumesStamp == _motionStamp)
            {
                return;
            }

            _volumesStamp = _motionStamp;
            _volumes.Clear();
            _volumeById.Clear();
            _movedDeferredSet.Clear();
            _deferredKin.Clear();
            _pendingPushes.Clear();
            var all = _units.AllUnits;
            for (var i = 0; i < all.Count; i++)
            {
                var id = all[i];
                var profile = ProfileOf(id);
                if (!(profile.UnitBodyRadius > 0.0) || !_units.IsAlive(id))
                {
                    continue;
                }

                _volumes.Add(new VolumeBody
                {
                    Id = id,
                    Radius = profile.UnitBodyRadius,
                    Start = _units.GetPosition(id),
                    Map = _units.GetMapId(id),
                    SeparationRatio = profile.UnitSeparationSpeedRatio,
                });
            }

            _volumes.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (var i = 0; i < _volumes.Count; i++)
            {
                _volumes[i].Index = i;
                _volumeById[_volumes[i].Id] = _volumes[i];
            }
        }

        /// <summary>
        /// 线段 <paramref name="from"/> → <paramref name="to"/> 与其他单位体积（快照位置）的首次接触：命中返回 true，并给出沿线段方向的入射距离
        /// <paramref name="hitDistance"/> 与入射点处的外法线。<paramref name="ignore"/> 非空时跳过该单位（滑动第二段不再和刚撞的那个比）。
        /// 起点已在体积内（<c>c &lt;= 0</c>）时只有"朝圆心走"才算命中，入射距离为 0。相切不算命中（判别式 &lt;= 0）。
        /// </summary>
        private bool SweepVolumes(
            Unit unit, double selfRadius, Vec2 from, Vec2 to, Id? ignore, out double hitDistance, out Vec2 normal, out Id hitId)
        {
            hitDistance = 0.0;
            normal = Vec2.Zero;
            hitId = default;
            var d = to - from;
            var len = d.Length;
            if (len <= ZeroLengthEpsilon)
            {
                return false;
            }

            var dir = new Vec2(d.X / len, d.Y / len);
            var best = double.MaxValue;
            var found = false;
            for (var i = 0; i < _volumes.Count; i++)
            {
                var entry = _volumes[i];
                if (entry.Id.Equals(unit.EntityId) || (ignore.HasValue && entry.Id.Equals(ignore.Value)))
                {
                    continue;
                }

                if (entry.Map.HasValue && !entry.Map.Value.Equals(unit.MapId))
                {
                    continue;
                }

                var center = entry.Start;
                var sum = selfRadius + entry.Radius;
                var f = from - center;
                var c = f.Dot(f) - sum * sum;
                var b = f.Dot(dir);
                double s;
                Vec2 n;
                if (c <= 0.0)
                {
                    if (!(b < 0.0))
                    {
                        continue; // 已在体积内但在走开（或切向）：放行。
                    }

                    s = 0.0;
                    var fl = f.Length;
                    n = fl > 1e-12 ? new Vec2(f.X / fl, f.Y / fl) : new Vec2(-dir.X, -dir.Y);
                }
                else
                {
                    if (b >= 0.0)
                    {
                        continue;
                    }

                    var disc = b * b - c;
                    if (disc <= 0.0)
                    {
                        continue;
                    }

                    s = -b - Math.Sqrt(disc);
                    if (s < 0.0 || s > len)
                    {
                        continue;
                    }

                    var hitPoint = from + dir * s;
                    n = new Vec2((hitPoint.X - center.X) / sum, (hitPoint.Y - center.Y) / sum);
                }

                // 同距离命中保留 id 更小的那个（快照按 id 排序、严格小于才替换）：结果不依赖单位处理顺序。
                if (s < best)
                {
                    best = s;
                    found = true;
                    normal = n;
                    hitId = entry.Id;
                }
            }

            if (found)
            {
                hitDistance = best;
            }

            return found;
        }

        /// <summary>结果守卫：<paramref name="end"/> 落进某个体积（快照位置）、且比起点 <paramref name="from"/> 更深（或起点在体积外）即违规。</summary>
        private bool ViolatesVolumes(Unit unit, double selfRadius, Vec2 from, Vec2 end)
        {
            const double tolerance = 1e-9;
            for (var i = 0; i < _volumes.Count; i++)
            {
                var entry = _volumes[i];
                if (entry.Id.Equals(unit.EntityId))
                {
                    continue;
                }

                if (entry.Map.HasValue && !entry.Map.Value.Equals(unit.MapId))
                {
                    continue;
                }

                var center = entry.Start;
                var sum = selfRadius + entry.Radius;
                var endDist = (end - center).Length;
                if (endDist >= sum - tolerance)
                {
                    continue;
                }

                var startDist = (from - center).Length;
                if (startDist < sum && endDist >= startDist - tolerance)
                {
                    continue; // 起点本来就在里面，这次没有更深。
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 把本 tick 的位移 <paramref name="from"/> → <paramref name="to"/> 按单位体积裁决。<paramref name="selfRadius"/> 为 0 或没有别的体积单位时
        /// 原样返回（<see cref="VolumeClip.Blocked"/> 为假）。
        /// </summary>
        private VolumeClip ClipByUnitVolumes(Unit unit, double selfRadius, Vec2 from, Vec2 to, bool slide) =>
            ClipPathByUnitVolumes(unit, selfRadius, from, to, null, slide);

        /// <summary>
        /// 折线版：位移实际走的是 <paramref name="from"/> → <paramref name="via"/> → <paramref name="to"/>（先撞墙截断、再沿墙切向滑一段）时，
        /// 两段各自做精确扫掠，而不是把起终点连成一条弦（弦会漏掉折线绕开的体积、也会误拦折线没碰到的体积）。<paramref name="via"/> 为空时是单段。
        /// 命中发生在哪一段就用那一段的方向与长度做停止/滑开；命中前的段完整走过。
        /// </summary>
        private VolumeClip ClipPathByUnitVolumes(Unit unit, double selfRadius, Vec2 from, Vec2 to, Vec2? via, bool slide)
        {
            var clip = new VolumeClip { End = to };
            if (!(selfRadius > 0.0))
            {
                return clip;
            }

            EnsureVolumeSnapshot();
            if (_volumes.Count == 0)
            {
                return clip;
            }

            var legStart = from;
            var legCount = via.HasValue ? 2 : 1;
            for (var leg = 0; leg < legCount; leg++)
            {
                var legEnd = leg == legCount - 1 ? to : via!.Value;
                if (!SweepVolumes(unit, selfRadius, legStart, legEnd, null, out var hitDistance, out var normal, out var hitId))
                {
                    legStart = legEnd;
                    continue;
                }

                var d = legEnd - legStart;
                var len = d.Length;
                var dir = new Vec2(d.X / len, d.Y / len);
                var pullBack = Math.Min(hitDistance, _options.ArrivalEpsilon);
                var advance = hitDistance - pullBack;
                var p1 = legStart + dir * advance;
                clip.Blocked = true;
                clip.End = p1;
                clip.HitId = hitId;
                clip.Normal = normal;

                if (slide)
                {
                    var remaining = dir * (len - advance);
                    var into = remaining.Dot(normal);
                    if (into < 0.0)
                    {
                        var tangent = new Vec2(remaining.X - normal.X * into, remaining.Y - normal.Y * into);
                        var tangentLen = tangent.Length;
                        if (tangentLen > ZeroLengthEpsilon)
                        {
                            var tangentDir = new Vec2(tangent.X / tangentLen, tangent.Y / tangentLen);
                            var p2 = p1 + tangent;
                            var secondBlocked = false;
                            var secondNormal = Vec2.Zero;
                            if (SweepVolumes(unit, selfRadius, p1, p2, hitId, out var hit2, out var n2, out _))
                            {
                                p2 = p1 + tangentDir * (hit2 - Math.Min(hit2, _options.ArrivalEpsilon));
                                secondBlocked = true;
                                secondNormal = n2;
                            }

                            if (_navigation != null && (p2 - p1).Length > ZeroLengthEpsilon)
                            {
                                // 切向那一段同样要过地形裁决（滑开不能滑进墙里）。
                                var wallHit = _navigation.Raycast(unit.MapId, p1, p2);
                                if (wallHit.HasValue)
                                {
                                    var wallDistance = (wallHit.Value - p1).Length;
                                    p2 = p1 + tangentDir * (wallDistance - Math.Min(wallDistance, _options.ArrivalEpsilon));
                                }

                                if (!_navigation.IsWalkable(unit.MapId, p2))
                                {
                                    p2 = p1;
                                }
                            }

                            if ((p2 - p1).Length > ZeroLengthEpsilon)
                            {
                                clip.End = p2;
                                clip.Slid = true;
                                clip.SecondBlocked = secondBlocked;
                                clip.SecondNormal = secondNormal;
                            }
                        }
                    }
                }

                if (ViolatesVolumes(unit, selfRadius, from, clip.End))
                {
                    clip.End = from;
                    clip.Slid = false;
                    clip.SecondBlocked = false;
                }

                return clip;
            }

            return clip;
        }

        // ================================================================== 路径跟随与追击：局部绕行

        /// <summary>
        /// 路径跟随与追击被体积挡住后的局部绕行（<c>path_avoid_units</c>，缺省真）：<paramref name="blocked"/> 是紧贴体积边界的那次裁决，
        /// <paramref name="pos"/> 是停下的位置（边界前一个到达容差），<paramref name="goal"/> 是当前路点，<paramref name="budget"/> 是本 tick 剩余的位移预算。
        /// 绕行方向 = 目标方向去掉沿"被撞单位中心 → 本单位"法向的分量后的切向（正对着撞上、切向分量为零时取法向逆时针的垂线，保证确定），
        /// 以满速度走完剩余预算：沿体积边界外侧擦过去，直到目标方向不再穿过体积，下一 tick 起恢复沿路径直走。
        /// 目标方向偏向的一侧被地形或别的体积挡死就停下，只有正对着撞上（没有偏向）时才再试另一侧；两侧都走不动、或目标点本身就落在被撞单位的体积里（绕过去也到不了）时返回 false，
        /// 调用方保持"停在体积前"。绕行步同样经别的体积扫掠与地形裁决，不穿墙、不进别的体积。
        /// </summary>
        private bool TryAvoidUnits(Unit unit, double selfRadius, Vec2 pos, Vec2 goal, double budget, in VolumeClip blocked, out Vec2 end)
        {
            end = pos;
            if (!blocked.Blocked || blocked.Slid || !(budget > ZeroLengthEpsilon))
            {
                return false;
            }

            if (!TryGetBody(blocked.HitId, out var hitBody))
            {
                return false;
            }

            var center = hitBody.Start;
            var sum = selfRadius + hitBody.Radius;
            if ((goal - center).Length < sum + 2.0 * _options.ArrivalEpsilon)
            {
                return false; // 目标点就在挡路单位的体积里：绕过去也到不了，停下。
            }

            var toGoal = goal - pos;
            var goalLen = toGoal.Length;
            if (goalLen <= ZeroLengthEpsilon)
            {
                return false;
            }

            var g = new Vec2(toGoal.X / goalLen, toGoal.Y / goalLen);
            var fromCenter = pos - center;
            var fl = fromCenter.Length;
            var n = fl > 1e-12 ? new Vec2(fromCenter.X / fl, fromCenter.Y / fl) : blocked.Normal;
            var gn = g.Dot(n);
            var tangent = new Vec2(g.X - n.X * gn, g.Y - n.Y * gn);
            var tl = tangent.Length;
            var t = tl > 1e-9 ? new Vec2(tangent.X / tl, tangent.Y / tl) : new Vec2(-n.Y, n.X);

            if (TryAvoidStep(unit, selfRadius, pos, t, budget, blocked.HitId, out end))
            {
                return true;
            }

            // 只有正对着撞上（切向分量为零、侧由垂线任取）时才试另一侧；目标方向已经偏向某一侧时，那一侧被挡死就停下——
            // 否则窄道里会在两侧之间来回弹（贴墙的一侧走不动 → 换到另一侧 → 沿体积边界滑回正中 → 又换回来）。
            return tl > 1e-9 ? false : TryAvoidStep(unit, selfRadius, pos, new Vec2(n.Y, -n.X), budget, blocked.HitId, out end);
        }

        private bool TryAvoidStep(Unit unit, double selfRadius, Vec2 pos, Vec2 dir, double budget, Id ignoreId, out Vec2 end)
        {
            end = pos;
            var length = budget;
            var p2 = pos + dir * length;

            // 沿切向走不会更深地进入刚撞的那个单位（法向分量 ≥ 0），所以扫掠时忽略它，只防别的体积。
            if (SweepVolumes(unit, selfRadius, pos, p2, ignoreId, out var hit, out _, out _))
            {
                length = hit - Math.Min(hit, _options.ArrivalEpsilon);
                p2 = pos + dir * length;
            }

            if (_navigation != null && length > ZeroLengthEpsilon)
            {
                var wallHit = _navigation.Raycast(unit.MapId, pos, p2);
                if (wallHit.HasValue)
                {
                    var wallDistance = (wallHit.Value - pos).Length;
                    length = Math.Min(length, wallDistance - Math.Min(wallDistance, _options.ArrivalEpsilon));
                    p2 = pos + dir * length;
                }

                if (length > ZeroLengthEpsilon && !_navigation.IsWalkable(unit.MapId, p2))
                {
                    return false;
                }
            }

            if (!(length > ZeroLengthEpsilon) || ViolatesVolumes(unit, selfRadius, pos, p2))
            {
                return false;
            }

            end = p2;
            return true;
        }
    }
}
