using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 单位间体积阻挡（手感设计/02 第 9 节；运动档案字段 <c>unit_body_radius</c> / <c>dodge_through_units</c>）：<see cref="MovementTickHandler"/>
    /// 运动层的一部分。
    /// <para>
    /// <b>开关与逐位等价</b>：只有运动层启用（见 <see cref="MovementTickHandler.Motion"/> 文件头）且<b>本单位</b>的档案声明了
    /// <c>unit_body_radius &gt; 0</c> 时才查询别的单位；没有声明体积半径的单位（含所有既有预设）不会进入本文件的任何分支，
    /// 既有算式一字未动。体积是<b>成对</b>语义：两个都声明了半径的单位之间，中心距不得小于半径之和；一方没有体积则双方互不影响。
    /// </para>
    /// <para>
    /// <b>算法</b>：把"本 tick 从 <c>from</c> 走到 <c>to</c>"当成一段线段，对每个有体积的其他存活同图单位（以它此刻的位置为圆心、
    /// 半径之和为半径）求线段首次进入圆的位置（连续扫掠，高速位移也不会隧穿）。命中后停在入射点之前一小段
    /// （<see cref="MovementOptions.ArrivalEpsilon"/>，同墙体阻挡的回退约定，保证下一 tick 不会从圆里起步）；允许滑动的来源
    /// （<c>wall_slide</c> 为真的输入/路径位移、<c>blocking: slide</c> 的动作位移）把剩余位移去掉沿圆心连线方向的分量后再走一段（最多一次，
    /// 切向位移仍经地形裁决与第二次扫掠，不递归），否则整体停下。起点已在别人体积内（出生重叠、穿过式闪避的落点）时只拦"让距离变近"的位移，
    /// 允许走开，不做强制推开。末尾有一道守卫：任何结果若落进体积且比起点更深，一律退回起点。
    /// </para>
    /// </summary>
    public sealed partial class MovementTickHandler
    {
        private readonly struct VolumeEntry
        {
            public readonly Id Id;
            public readonly double Radius;

            public VolumeEntry(Id id, double radius)
            {
                Id = id;
                Radius = radius;
            }
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
        }

        // 本 tick 内有体积的单位快照（只存 id 与半径；位置每次现读，因为同 tick 内先走的单位会改位置）。每 tick 懒建一次。
        private readonly List<VolumeEntry> _volumes = new List<VolumeEntry>();
        private long _volumesStamp = -1;

        /// <summary>未启用运动层、离散步或本单位没有声明体积时返回 0。</summary>
        private double SelfVolumeRadius(Unit unit)
        {
            var t = GetMotionTick(unit);
            return t == null ? 0.0 : t.Profile.UnitBodyRadius;
        }

        /// <summary>任一单位的体积半径（读它自己的档案；与 <see cref="GetMotionTick"/> 共用档案缓存）。仅在运动层启用时调用。</summary>
        private double VolumeRadiusOf(Id id)
        {
            var view = _feel!.ResolveJudging(id);
            if (!_motionProfiles.TryGetValue(id, out var profile) || profile.Version != view.Version)
            {
                profile = MotionProfile.Read(view);
                _motionProfiles[id] = profile;
            }

            return profile.UnitBodyRadius;
        }

        private void EnsureVolumeSnapshot()
        {
            if (_volumesStamp == _motionStamp)
            {
                return;
            }

            _volumesStamp = _motionStamp;
            _volumes.Clear();
            var all = _units.AllUnits;
            for (var i = 0; i < all.Count; i++)
            {
                var radius = VolumeRadiusOf(all[i]);
                if (radius > 0.0)
                {
                    _volumes.Add(new VolumeEntry(all[i], radius));
                }
            }
        }

        /// <summary>
        /// 线段 <paramref name="from"/> → <paramref name="to"/> 与其他单位体积的首次接触：命中返回 true，并给出沿线段方向的入射距离
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

                if (!_units.IsAlive(entry.Id))
                {
                    continue;
                }

                var mapId = _units.GetMapId(entry.Id);
                if (mapId.HasValue && !mapId.Value.Equals(unit.MapId))
                {
                    continue;
                }

                var center = _units.GetPosition(entry.Id);
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

        /// <summary>结果守卫：<paramref name="end"/> 落进某个体积、且比起点 <paramref name="from"/> 更深（或起点在体积外）即违规。</summary>
        private bool ViolatesVolumes(Unit unit, double selfRadius, Vec2 from, Vec2 end)
        {
            const double tolerance = 1e-9;
            for (var i = 0; i < _volumes.Count; i++)
            {
                var entry = _volumes[i];
                if (entry.Id.Equals(unit.EntityId) || !_units.IsAlive(entry.Id))
                {
                    continue;
                }

                var mapId = _units.GetMapId(entry.Id);
                if (mapId.HasValue && !mapId.Value.Equals(unit.MapId))
                {
                    continue;
                }

                var center = _units.GetPosition(entry.Id);
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
        private VolumeClip ClipByUnitVolumes(Unit unit, double selfRadius, Vec2 from, Vec2 to, bool slide)
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

            if (!SweepVolumes(unit, selfRadius, from, to, null, out var hitDistance, out var normal, out var hitId))
            {
                return clip;
            }

            var d = to - from;
            var len = d.Length;
            var dir = new Vec2(d.X / len, d.Y / len);
            var pullBack = Math.Min(hitDistance, _options.ArrivalEpsilon);
            var advance = hitDistance - pullBack;
            var p1 = from + dir * advance;
            clip.Blocked = true;
            clip.End = p1;

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
                            clip.Normal = normal;
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
    }
}
