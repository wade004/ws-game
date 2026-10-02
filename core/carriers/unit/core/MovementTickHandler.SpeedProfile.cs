using System;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 单位间体积阻挡的"速度剖面"（M4-W2，ADR-0128 追加决定）：阶段 B 的成对接触时刻按单位本 tick <b>实际的速度剖面</b>求，而不是假设沿折线匀速。
    /// <para>
    /// 运动仲裁给出的信息：常规位移与路径跟随有"本 tick 起速 <c>v0</c>、末速 <c>v1</c>"（加速/制动曲线在 tick 内的变化）；动作位移与带曲线的受控位移
    /// 有"本 tick 对应曲线上的区间 [p0, p1]"。两者都决定"位移在 tick 内的时间分布"，位移总量仍是运动层已经定下的（末速 × dt 或曲线差值）。
    /// 剖面把它写成"弧长占比随时间的函数" <c>u(t)</c>（<c>u(0)=0</c>、<c>u(1)=1</c>、单调不减）：单位在 tick 内时刻 t 的位置 = 折线上弧长占比
    /// <c>scale × u(t)</c> 处（<c>scale</c> 是被缩短后的比例）。剖面为 null 时 <c>u(t)=t</c>，沿用旧的解析式，逐位不变。
    /// </para>
    /// </summary>
    public sealed partial class MovementTickHandler
    {
        private sealed class SpeedProfile
        {
            private const int Nodes = 64;

            private readonly bool _ramp;
            private readonly double _v0;
            private readonly double _v1;
            private readonly double[]? _cum;

            /// <summary>u'(t) 在 [0,1] 上的上界（保守推进的相对速率上界用）。</summary>
            public double MaxRate { get; }

            private SpeedProfile(double v0, double v1)
            {
                _ramp = true;
                _v0 = v0;
                _v1 = v1;
                MaxRate = 2.0 * Math.Max(v0, v1) / (v0 + v1);
            }

            private SpeedProfile(double[] cum)
            {
                _cum = cum;
                var best = 0.0;
                for (var k = 0; k < Nodes; k++)
                {
                    var rate = (cum[k + 1] - cum[k]) * Nodes;
                    if (rate > best) best = rate;
                }

                MaxRate = Math.Max(1.0, best);
            }

            /// <summary>
            /// 斜坡剖面（线性变化的速率）：起速 <paramref name="v0"/>、末速 <paramref name="v1"/>，速率标量非负、和为正。
            /// 起末速相同（相对 1e-9 内）或和为零返回 null（匀速）。
            /// </summary>
            public static SpeedProfile? Ramp(double v0, double v1)
            {
                if (!(v0 >= 0.0) || !(v1 >= 0.0) || !(v0 + v1 > 1e-12))
                {
                    return null;
                }

                if (Math.Abs(v1 - v0) <= 1e-9 * Math.Max(1.0, Math.Max(v0, v1)))
                {
                    return null;
                }

                return new SpeedProfile(v0, v1);
            }

            /// <summary>
            /// 任意速率函数的剖面：<paramref name="speedAt"/>(f) 是 tick 内 f∈[0,1] 处的速率（非负）。64 个等分区间上用辛普森积分累计、归一化。
            /// 速率在 f∈{0.25, 0.5, 0.75, 1} 处全都相同（相对 1e-9）、或总积分为零返回 null（匀速）。
            /// </summary>
            public static SpeedProfile? FromSpeed(Func<double, double> speedAt)
            {
                var end = speedAt(1.0);
                var uniform = true;
                for (var q = 1; q <= 3 && uniform; q++)
                {
                    uniform = Math.Abs(speedAt(q * 0.25) - end) <= 1e-9 * Math.Max(1.0, Math.Abs(end));
                }

                if (uniform)
                {
                    return null;
                }

                var cum = new double[Nodes + 1];
                var h = 1.0 / Nodes;
                var prev = speedAt(0.0);
                for (var k = 0; k < Nodes; k++)
                {
                    var mid = speedAt((k + 0.5) * h);
                    var next = speedAt((k + 1) * h);
                    cum[k + 1] = cum[k] + h / 6.0 * (prev + 4.0 * mid + next);
                    prev = next;
                }

                return Normalize(cum);
            }

            /// <summary>
            /// 位置曲线的剖面：<paramref name="curve"/> 在本 tick 对应曲线上的区间 [<paramref name="p0"/>, <paramref name="p1"/>]，
            /// <c>u(t) = (C(p0 + t·(p1 − p0)) − C(p0)) / (C(p1) − C(p0))</c>。线性曲线、区间为空或曲线增量为零返回 null（匀速）。
            /// </summary>
            public static SpeedProfile? OfCurve(string curve, double p0, double p1, IMotionCurveSource? curves)
            {
                if (curve == "linear" || !(p1 > p0))
                {
                    return null;
                }

                var c0 = MotionMath.EvalCurve(curve, p0, curves);
                var span = MotionMath.EvalCurve(curve, p1, curves) - c0;
                if (!(span > 1e-12))
                {
                    return null;
                }

                var cum = new double[Nodes + 1];
                for (var k = 1; k <= Nodes; k++)
                {
                    var y = (MotionMath.EvalCurve(curve, p0 + (double)k / Nodes * (p1 - p0), curves) - c0) / span;
                    cum[k] = y < cum[k - 1] ? cum[k - 1] : y;
                }

                return Normalize(cum);
            }

            private static SpeedProfile? Normalize(double[] cum)
            {
                var total = cum[Nodes];
                if (!(total > 1e-12))
                {
                    return null;
                }

                for (var k = 0; k <= Nodes; k++)
                {
                    cum[k] /= total;
                }

                cum[Nodes] = 1.0;
                return new SpeedProfile(cum);
            }

            /// <summary>时间 <paramref name="t"/>∈[0,1] 时已走过的弧长占比。</summary>
            public double U(double t)
            {
                if (t <= 0.0) return 0.0;
                if (t >= 1.0) return 1.0;
                if (_ramp)
                {
                    return (2.0 * _v0 * t + (_v1 - _v0) * t * t) / (_v0 + _v1);
                }

                var x = t * Nodes;
                var k = (int)x;
                if (k >= Nodes) k = Nodes - 1;
                return _cum![k] + (_cum[k + 1] - _cum[k]) * (x - k);
            }

            /// <summary>u'(t)：弧长占比对时间的变化率（匀速为 1）。表格剖面是分段常数（与 <see cref="U"/> 的分段线性一致）。</summary>
            public double Rate(double t)
            {
                var tt = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
                if (_ramp)
                {
                    return 2.0 * (_v0 + (_v1 - _v0) * tt) / (_v0 + _v1);
                }

                var k = (int)(tt * Nodes);
                if (k >= Nodes) k = Nodes - 1;
                return (_cum![k + 1] - _cum[k]) * Nodes;
            }
        }

        /// <summary>
        /// 速度积分一个 tick 的剖面：<paramref name="current"/> 是本 tick 起速、<paramref name="goal"/> 是积分的目标速率、<paramref name="v1"/> 是积分给出的末速。
        /// 瞬时达速/停止（<c>accel_ms</c>/<c>decel_ms</c> 为零）、起末速相同的 tick 是匀速（null）；中途速率在 tick 内线性变化（线性曲线，没有被吸附截断）
        /// 取闭式斜坡，否则按 <see cref="MotionMath.ApproachSpeedAt"/> 逐点积分成表格剖面。只在单位有体积时调用。
        /// </summary>
        private SpeedProfile? IntegratedSpeedProfile(MotionTick t, double current, double goal, double baseSpeed, double v1, double dt)
        {
            if (current < 1e-12) current = 0.0;
            if (!(Math.Abs(v1 - current) > 1e-9 * Math.Max(1.0, Math.Max(current, v1))))
            {
                return null;
            }

            var profile = t.Profile;
            var curves = _mot!.Curves;
            double At(double f) => MotionMath.ApproachSpeedAt(current, goal, baseSpeed, profile, dt, curves, f);
            var half = At(0.5);
            if (Math.Abs(half - v1) <= 1e-9 * Math.Max(1.0, Math.Abs(v1)))
            {
                return null; // 瞬时达速/停止：整个 tick 是末速。
            }

            var tol = 1e-9 * Math.Max(1.0, Math.Max(current, v1));
            var linear = Math.Abs(At(0.25) - (current + (v1 - current) * 0.25)) <= tol &&
                         Math.Abs(half - (current + (v1 - current) * 0.5)) <= tol &&
                         Math.Abs(At(0.75) - (current + (v1 - current) * 0.75)) <= tol;
            return linear ? SpeedProfile.Ramp(current, v1) : SpeedProfile.FromSpeed(At);
        }

        // ------------------------------------------------------------------ 带剖面的首次接触（数值）

        private const double ProfiledMinStep = 1.0 / 4096.0;
        private const int ProfiledMaxSteps = 4096;

        /// <summary>
        /// <see cref="FirstContact"/> 的带剖面版本：至少一方有速度剖面时，两个单位的位置按 <c>At(scale × u(t))</c> 随时间变化，没有闭式解，
        /// 数值求首次接触：取中心距首次小于 <c>min(limit, 起始距离) − 容差</c> 的时刻（"不得变得更深"，同解析版口径）。保守推进：每步前进
        /// <c>间隙 ÷ 相对速率上界</c>（区间内不可能接触），越过阈值后在最后一步内二分到 1e-18。返回的 <see cref="Contact.Dt"/> 固定为 1、
        /// <see cref="Contact.RelSq"/> 是接触时刻相对速率的平方，所以 <c>Tau − ε·Dt/√RelSq</c> 的回退算式与匀速情形同形（回退一个到达容差对应的时间）。
        /// </summary>
        private static Contact FirstContactProfiled(Trajectory ti, double si, Trajectory tj, double sj, double limit)
        {
            const double tol = 1e-9;
            var result = new Contact();
            var f0 = ti.Pts[0] - tj.Pts[0];
            var dist0 = Math.Sqrt(f0.Dot(f0));
            var thr = Math.Min(limit, dist0) - tol;
            if (thr <= 0.0)
            {
                return result;
            }

            var vmax = si * ti.Length * ti.RateMax + sj * tj.Length * tj.RateMax;
            if (!(vmax > 1e-12))
            {
                return result;
            }

            double Gap(double t)
            {
                var d = ti.AtTime(si, t) - tj.AtTime(sj, t);
                return Math.Sqrt(d.Dot(d)) - thr;
            }

            var tPrev = 0.0;
            var gPrev = dist0 - thr;
            for (var iter = 0; iter < ProfiledMaxSteps; iter++)
            {
                var step = gPrev / vmax;
                if (step < ProfiledMinStep) step = ProfiledMinStep;
                var tNext = tPrev + step;
                if (tNext > 1.0) tNext = 1.0;
                var gNext = Gap(tNext);
                if (gNext <= 0.0)
                {
                    var lo = tPrev;
                    var hi = tNext;
                    for (var k = 0; k < 60; k++)
                    {
                        var mid = 0.5 * (lo + hi);
                        if (Gap(mid) <= 0.0) hi = mid; else lo = mid;
                    }

                    var tau = hi;
                    var vi = ti.VelocityAtTime(si, tau);
                    var vj = tj.VelocityAtTime(sj, tau);
                    var rel = vi - vj;
                    result.Found = true;
                    result.Tau = tau;
                    result.Dt = 1.0;
                    result.RelSq = Math.Max(rel.Dot(rel), 1e-24);
                    result.Pi = ti.AtTime(si, tau);
                    result.Pj = tj.AtTime(sj, tau);
                    result.Vi = vi;
                    result.Vj = vj;
                    return result;
                }

                if (tNext >= 1.0)
                {
                    return result;
                }

                tPrev = tNext;
                gPrev = gNext;
            }

            return result;
        }
    }
}
