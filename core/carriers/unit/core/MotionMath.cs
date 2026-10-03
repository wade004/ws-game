using System;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 运动积分的纯函数（手感设计/02 第 3.3/3.4 节）：曲线求值与反求、速度趋近（加速/制动）、方向反转策略、
    /// 朝向限速转向。无状态、无时钟、无随机，同一输入同一输出。
    /// <para>
    /// 判断记录：
    /// </para>
    /// <para>
    /// 1) <b>时间折成 tick 数再积分</b>：<c>accel_ms</c>/<c>decel_ms</c> 先按步长折成"到达所需 tick 数"
    /// （<see cref="ToTicks"/>，9 位小数预舍入，同 <c>FeelCalibration.MillisecondsToTicks</c> 的浮点边界处理，但<b>不取整</b>），
    /// 每 tick 沿曲线前进 <c>1/ticks</c> 的进度；进度达到 1（容差 1e-9）即吸附到目标。这样 <c>accel_ms = A</c> 达速的 tick 数恰为
    /// <c>ceil(A / 步长)</c>（设计文档第 8 节第 1 条），不会因 <c>6 × (1/60) / 0.1</c> 这类浮点误差多走一拍。
    /// 小于一个 tick 的正毫秒值按一个 tick 处理（同标定"非零至少 1 tick"）。
    /// </para>
    /// <para>
    /// 2) <b>曲线状态无状态化</b>：速度是唯一的积分状态。加速时从当前速度占目标速度的比例反求曲线进度（二分，固定 48 次迭代，
    /// 确定性），前进一格再正求；制动同理（比例取 <c>1 − 曲线</c>）。线性曲线时这就是等差。目标速度中途改变（走/跑切换、
    /// 动作倍率）时进度自然续接，不需要额外的"曲线进度"状态。
    /// </para>
    /// <para>
    /// 3) <b>制动参照速度</b>：制动速率按"<c>decel_ms</c> 内从基础移速降到零"定（参照速度 = 基础移速，速度高于基础移速时取当前速度），
    /// 所以任何速度的急停距离 ≤ <c>v × decel / 2</c>，满速（= 基础移速）时恰为 <c>v × decel / 2</c>（设计文档第 8 节第 2 条）。
    /// </para>
    /// </summary>
    public static class MotionMath
    {
        private const double Epsilon = 1e-12;
        private const double ProgressSnap = 1e-9;
        private const int InvertIterations = 48;

        /// <summary>毫秒按步长折成 tick 数（实数）：非正为 0；正值至少 1。9 位小数预舍入。</summary>
        public static double ToTicks(double milliseconds, double stepSeconds)
        {
            if (!double.IsFinite(stepSeconds) || stepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stepSeconds));
            if (!double.IsFinite(milliseconds) || milliseconds <= 0) return 0.0;
            var ticks = Math.Round(milliseconds / (stepSeconds * 1000.0), 9);
            return ticks < 1.0 ? 1.0 : ticks;
        }

        // ------------------------------------------------------------------ 曲线

        /// <summary>
        /// 曲线求值（[0,1] → [0,1]）：<c>linear</c>、内建 <c>ease_in</c>（t²）、<c>ease_out</c>（1−(1−t)²）、
        /// <c>ease_in_out</c>（smoothstep），或 <c>custom:&lt;id&gt;</c>（经 <paramref name="curves"/> 解析的断点曲线）。
        /// 引用无法解析抛 <see cref="InvalidOperationException"/>（不静默改为线性）。
        /// </summary>
        public static double EvalCurve(string curve, double t, IMotionCurveSource? curves)
        {
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            switch (curve)
            {
                case "linear": return t;
                case "ease_in": return t * t;
                case "ease_out": return 1.0 - (1.0 - t) * (1.0 - t);
                case "ease_in_out": return t * t * (3.0 - 2.0 * t);
                default:
                {
                    const string prefix = "custom:";
                    if (curve != null && curve.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        var id = curve.Substring(prefix.Length);
                        var c = curves?.GetCurve(id);
                        if (c == null)
                        {
                            throw new InvalidOperationException($"运动曲线引用 \"{curve}\" 无法解析（未注入曲线来源或曲线不存在）");
                        }

                        var y = c.Evaluate(t);
                        return y < 0.0 ? 0.0 : (y > 1.0 ? 1.0 : y);
                    }

                    throw new InvalidOperationException(
                        $"运动曲线引用 \"{curve}\" 不是 linear|ease_in|ease_out|ease_in_out|custom:<curve_id>");
                }
            }
        }

        /// <summary>曲线反求：给定 y∈[0,1] 求 t（曲线单调不减；二分 48 次）。线性曲线精确返回 y。</summary>
        public static double InvertCurve(string curve, double y, IMotionCurveSource? curves)
        {
            if (y <= 0.0) return 0.0;
            if (y >= 1.0) return 1.0;
            if (curve == "linear") return y;
            double lo = 0.0, hi = 1.0;
            for (var i = 0; i < InvertIterations; i++)
            {
                var mid = 0.5 * (lo + hi);
                if (EvalCurve(curve, mid, curves) < y) lo = mid; else hi = mid;
            }

            return 0.5 * (lo + hi);
        }

        // ------------------------------------------------------------------ 速度趋近

        /// <summary>
        /// 速率标量趋近目标一个 tick：<paramref name="target"/> &gt; <paramref name="speed"/> 沿加速曲线在 <c>accel_ms</c> 内趋近目标；
        /// 小于时沿制动曲线在 <c>decel_ms</c> 内按基础移速参照制动到目标；<c>accel_ms = 0</c>/<c>decel_ms = 0</c> 时直接取目标
        /// （与既有"瞬时达速/停止"逐位一致：返回值就是 <paramref name="target"/> 本身，不经过任何运算）。
        /// </summary>
        public static double ApproachSpeed(
            double speed, double target, double baseSpeed, in MotionProfile profile, double stepSeconds, IMotionCurveSource? curves)
        {
            if (speed < Epsilon) speed = 0.0;

            // 速度状态由向量长度还原（sqrt(x²+y²)），与上一 tick 写下的标量速率可能差最后一位浮点；
            // 差在相对 1e-9 之内按"已在目标速度"处理，免得末位噪声被当成多出的速度而触发一拍制动。
            if (Math.Abs(speed - target) <= 1e-9 * Math.Max(1.0, Math.Abs(target))) return target;
            if (target > speed)
            {
                var accelTicks = ToTicks(profile.AccelMs, stepSeconds);
                if (accelTicks <= 0.0) return target;
                var s = speed / target;
                var p1 = InvertCurve(profile.AccelCurve, s, curves) + 1.0 / accelTicks;
                if (p1 >= 1.0 - ProgressSnap) return target;
                var v = target * EvalCurve(profile.AccelCurve, p1, curves);
                return v < speed ? speed : (v > target ? target : v);
            }

            if (target < speed)
            {
                var decelTicks = ToTicks(profile.DecelMs, stepSeconds);
                if (decelTicks <= 0.0) return target;
                var reference = speed > baseSpeed ? speed : baseSpeed;
                var range = reference - target;
                if (range <= Epsilon) return target;
                var f = (speed - target) / range; // 1 = 参照速度处，0 = 目标
                var q1 = InvertCurve(profile.BrakeCurve, 1.0 - f, curves) + 1.0 / decelTicks;
                if (q1 >= 1.0 - ProgressSnap) return target;
                var v = target + range * (1.0 - EvalCurve(profile.BrakeCurve, q1, curves));
                return v > speed ? speed : (v < target ? target : v);
            }

            return speed;
        }

        /// <summary>
        /// <see cref="ApproachSpeed"/> 在一个 tick 内走过 <paramref name="fraction"/>（0..1）时的速率：同一套曲线与进度，只是本 tick 的进度增量
        /// 乘以 <paramref name="fraction"/>。<paramref name="fraction"/> 大于等于 1 时就是 <see cref="ApproachSpeed"/> 本身；瞬时达速/停止
        /// （<c>accel_ms</c>/<c>decel_ms</c> 为零）时任何正的 <paramref name="fraction"/> 都返回目标速度。给体积阻挡的成对接触按 tick 内的速度剖面
        /// 求时刻用（只读，不改积分）。
        /// </summary>
        public static double ApproachSpeedAt(
            double speed, double target, double baseSpeed, in MotionProfile profile, double stepSeconds, IMotionCurveSource? curves,
            double fraction)
        {
            if (fraction >= 1.0) return ApproachSpeed(speed, target, baseSpeed, profile, stepSeconds, curves);
            if (speed < Epsilon) speed = 0.0;
            if (fraction <= 0.0) return speed;
            if (Math.Abs(speed - target) <= 1e-9 * Math.Max(1.0, Math.Abs(target))) return target;
            if (target > speed)
            {
                var accelTicks = ToTicks(profile.AccelMs, stepSeconds);
                if (accelTicks <= 0.0) return target;
                var s = speed / target;
                var p1 = InvertCurve(profile.AccelCurve, s, curves) + fraction / accelTicks;
                if (p1 >= 1.0 - ProgressSnap) return target;
                var v = target * EvalCurve(profile.AccelCurve, p1, curves);
                return v < speed ? speed : (v > target ? target : v);
            }

            if (target < speed)
            {
                var decelTicks = ToTicks(profile.DecelMs, stepSeconds);
                if (decelTicks <= 0.0) return target;
                var reference = speed > baseSpeed ? speed : baseSpeed;
                var range = reference - target;
                if (range <= Epsilon) return target;
                var f = (speed - target) / range;
                var q1 = InvertCurve(profile.BrakeCurve, 1.0 - f, curves) + fraction / decelTicks;
                if (q1 >= 1.0 - ProgressSnap) return target;
                var v = target + range * (1.0 - EvalCurve(profile.BrakeCurve, q1, curves));
                return v > speed ? speed : (v < target ? target : v);
            }

            return speed;
        }

        /// <summary>
        /// 一个 tick 的常规（<c>regular</c>）速度积分：给定当前速度向量与本 tick 期望方向（单位向量或零向量）及目标速率，
        /// 返回新的方向（单位向量）与速率。无期望方向时沿原方向制动到零；<c>through_zero</c> 策略下反向输入（夹角大于 90°）先沿
        /// 原方向制动到零、再沿新方向加速；<c>instant</c> 策略方向立即对齐期望方向、速率沿加速/制动曲线趋近目标（保留速率）。
        /// </summary>
        public static void StepRegular(
            Vec2 velocity, Vec2 desiredDirection, double targetSpeed, double baseSpeed, in MotionProfile profile,
            double stepSeconds, IMotionCurveSource? curves, out Vec2 direction, out double speed)
        {
            var current = velocity.Length;
            if (current < Epsilon) current = 0.0;
            var hasInput = desiredDirection.X != 0.0 || desiredDirection.Y != 0.0;

            if (!hasInput)
            {
                if (current == 0.0)
                {
                    direction = Vec2.Zero;
                    speed = 0.0;
                    return;
                }

                direction = new Vec2(velocity.X / current, velocity.Y / current);
                speed = ApproachSpeed(current, 0.0, baseSpeed, profile, stepSeconds, curves);
                return;
            }

            if (current > 0.0 && profile.Reverse == ReversePolicy.ThroughZero)
            {
                var unit = new Vec2(velocity.X / current, velocity.Y / current);
                if (unit.Dot(desiredDirection) < 0.0)
                {
                    direction = unit;
                    speed = ApproachSpeed(current, 0.0, baseSpeed, profile, stepSeconds, curves);
                    return;
                }
            }

            direction = desiredDirection;
            speed = ApproachSpeed(current, targetSpeed, baseSpeed, profile, stepSeconds, curves);
        }

        // ------------------------------------------------------------------ 朝向

        /// <summary>
        /// 朝向限速趋近（手感设计/02 第 3.4 节）：<paramref name="turnRateDegS"/> ≤ 0 即瞬时到位（直接返回
        /// <paramref name="targetFacing"/>，与既有行为逐位一致）；否则每 tick 最多转 <c>rate × dt</c>，走最短弧。
        /// </summary>
        public static double StepFacing(double facing, double targetFacing, double turnRateDegS, double dt)
        {
            if (turnRateDegS <= 0.0) return targetFacing;
            var maxDelta = turnRateDegS * dt * (Math.PI / 180.0);
            var delta = WrapAngle(targetFacing - facing);
            if (Math.Abs(delta) <= maxDelta) return targetFacing;
            return facing + (delta > 0.0 ? maxDelta : -maxDelta);
        }

        /// <summary>角度规整到 (−π, π]。</summary>
        public static double WrapAngle(double radians)
        {
            var twoPi = 2.0 * Math.PI;
            var a = radians % twoPi;
            if (a > Math.PI) a -= twoPi;
            else if (a <= -Math.PI) a += twoPi;
            return a;
        }

        // ------------------------------------------------------------------ 到达减速

        /// <summary>
        /// 路径跟随到达终点前的限速（<c>arrival_decel</c>）：剩余路径长度 <paramref name="remainingLength"/> 内以
        /// <c>a = 基础移速 / decel 秒数</c> 的匀减速恰好停在终点所允许的最大速率 <c>sqrt(2·a·L)</c>，与 <paramref name="target"/> 取小。
        /// <c>decel_ms = 0</c> 时不限速（到达即停，与既有一致）。制动曲线非线性时按线性减速率估算（判断记录：到达限速只需要"不越过
        /// 终点"，不要求曲线形状；曲线形状对松开输入的急停生效）。
        /// </summary>
        public static double ArrivalLimitedSpeed(double target, double baseSpeed, double decelMs, double remainingLength)
        {
            if (decelMs <= 0.0) return target;
            var a = baseSpeed / (decelMs / 1000.0);
            var limit = Math.Sqrt(2.0 * a * Math.Max(0.0, remainingLength));
            return limit < target ? limit : target;
        }
    }
}
