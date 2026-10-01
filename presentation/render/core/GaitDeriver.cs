using System;
using Core.Foundation.Feel;

namespace Presentation.Render
{
    /// <summary>
    /// 步态阈值（手感设计/02 第 7 节，运动档案"移动"组的呈现型字段，均为相对基础移速的比值）。
    /// <para>
    /// 缺省值：<c>idle_max_ratio 0.05</c>、<c>walk_max_ratio 0.6</c>、<c>sprint_min_ratio</c> 无（没有冲刺步态）、
    /// <c>gait_hysteresis_ratio 0.05</c>——与 02 第 7 节表格一致；读不到档案（没有手感数据、字段未设置）时用它们。
    /// </para>
    /// </summary>
    public readonly struct GaitThresholds : IEquatable<GaitThresholds>
    {
        public const double DefaultIdleMaxRatio = 0.05;
        public const double DefaultWalkMaxRatio = 0.6;
        public const double DefaultHysteresisRatio = 0.05;

        /// <summary>低于它为 idle。</summary>
        public double IdleMaxRatio { get; }

        /// <summary>低于它为 walk，其余为 run。</summary>
        public double WalkMaxRatio { get; }

        /// <summary>不低于它为 sprint；null 表示没有冲刺步态。</summary>
        public double? SprintMinRatio { get; }

        /// <summary>滞回宽度：升档在阈值处发生，降档要低于"阈值 减 滞回宽度"才发生（负值按 0 处理）。</summary>
        public double Hysteresis { get; }

        public GaitThresholds(double idleMaxRatio, double walkMaxRatio, double? sprintMinRatio, double hysteresis)
        {
            IdleMaxRatio = idleMaxRatio;
            WalkMaxRatio = walkMaxRatio;
            SprintMinRatio = sprintMinRatio;
            Hysteresis = hysteresis < 0 ? 0 : hysteresis;
        }

        public static GaitThresholds Default =>
            new GaitThresholds(DefaultIdleMaxRatio, DefaultWalkMaxRatio, null, DefaultHysteresisRatio);

        /// <summary>
        /// 从呈现型手感视图读阈值（<see cref="PresentingFeelView"/> 只能读呈现型字段，步态阈值正是呈现型）。读的是
        /// <b>标定前的相对值</b>（<c>GetRaw</c>）——这些字段是"基础移速倍数"，标定后的绝对值是世界速度，而步态比值
        /// 本身就是相对量。字段未设置、视图为 null 或字段未登记时该项取缺省。
        /// </summary>
        public static GaitThresholds FromView(PresentingFeelView? view)
        {
            if (view == null)
            {
                return Default;
            }

            return new GaitThresholds(
                ReadOptional(view, FeelFieldNames.IdleMaxRatio) ?? DefaultIdleMaxRatio,
                ReadOptional(view, FeelFieldNames.WalkMaxRatio) ?? DefaultWalkMaxRatio,
                ReadOptional(view, FeelFieldNames.SprintMinRatio),
                ReadOptional(view, FeelFieldNames.GaitHysteresisRatio) ?? DefaultHysteresisRatio);
        }

        private static double? ReadOptional(PresentingFeelView view, string field)
        {
            if (!view.Contains(field))
            {
                return null;
            }
            var v = view.GetRaw(field);
            return v.Kind == FeelValueKind.Number ? v.AsNumber() : (double?)null;
        }

        public bool Equals(GaitThresholds other) =>
            IdleMaxRatio.Equals(other.IdleMaxRatio) && WalkMaxRatio.Equals(other.WalkMaxRatio)
            && SprintMinRatio.Equals(other.SprintMinRatio) && Hysteresis.Equals(other.Hysteresis);

        public override bool Equals(object? obj) => obj is GaitThresholds other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(IdleMaxRatio, WalkMaxRatio, SprintMinRatio, Hysteresis);
    }

    /// <summary>
    /// 步态派生器（手感设计/02 第 7 节）：按速度比 <c>|velocity| / 基础移速</c> 在 idle/walk/run/sprint 之间切换，带滞回；
    /// 一个实体一个实例，有状态（滞回需要记住当前档）。纯呈现，不读写任何判定状态。
    /// <para>
    /// 滞回规则（判断记录）：02 只给出"滞回宽度 <c>gait_hysteresis_ratio</c> 避免阈值附近抖动"，没有规定它落在阈值哪一侧。本实现取
    /// <b>升档在阈值处、降档在 阈值 减 滞回宽度 处</b>：升档判据与 02 表格的无滞回条件逐字一致（比值不小于阈值进入更高档），
    /// 滞回只作用于回落方向——速度在 [阈值 减 宽度, 阈值) 内保持当前档，因此从静止起步或匀速运动时与无滞回的结果相同，
    /// 只在阈值附近来回抖动时才不闪。比值一次跨过多个阈值（如急加速）时逐档连升或连降，一次 <see cref="Update(double)"/> 内收敛。
    /// </para>
    /// </summary>
    public sealed class GaitDeriver
    {
        private GaitThresholds _thresholds;

        public GaitDeriver(GaitThresholds thresholds)
        {
            _thresholds = thresholds;
            Current = LocomotionGait.Idle;
        }

        public GaitDeriver() : this(GaitThresholds.Default)
        {
        }

        /// <summary>当前步态。</summary>
        public LocomotionGait Current { get; private set; }

        public GaitThresholds Thresholds => _thresholds;

        /// <summary>换阈值（手感重算后）。若冲刺阈值被撤销而当前正在冲刺，下一次 <see cref="Update(double)"/> 会落回 run。</summary>
        public void SetThresholds(GaitThresholds thresholds) => _thresholds = thresholds;

        /// <summary>
        /// 无滞回地按比值直接定档（首次观测、重生等"没有历史"的场合），返回新档。
        /// </summary>
        public LocomotionGait Seed(double speedRatio)
        {
            var t = _thresholds;
            if (speedRatio < t.IdleMaxRatio) Current = LocomotionGait.Idle;
            else if (t.SprintMinRatio.HasValue && speedRatio >= t.SprintMinRatio.Value) Current = LocomotionGait.Sprint;
            else if (speedRatio < t.WalkMaxRatio) Current = LocomotionGait.Walk;
            else Current = LocomotionGait.Run;
            return Current;
        }

        /// <summary>输入当前速度比，返回（可能已切换的）当前步态。</summary>
        public LocomotionGait Update(double speedRatio)
        {
            var t = _thresholds;
            var h = t.Hysteresis;

            // 升档（阈值处）
            while (true)
            {
                if (Current == LocomotionGait.Idle && speedRatio >= t.IdleMaxRatio) Current = LocomotionGait.Walk;
                else if (Current == LocomotionGait.Walk && speedRatio >= t.WalkMaxRatio) Current = LocomotionGait.Run;
                else if (Current == LocomotionGait.Run && t.SprintMinRatio.HasValue && speedRatio >= t.SprintMinRatio.Value) Current = LocomotionGait.Sprint;
                else break;
            }

            // 降档（阈值 减 滞回宽度）
            while (true)
            {
                if (Current == LocomotionGait.Sprint && (!t.SprintMinRatio.HasValue || speedRatio < t.SprintMinRatio.Value - h)) Current = LocomotionGait.Run;
                else if (Current == LocomotionGait.Run && speedRatio < t.WalkMaxRatio - h) Current = LocomotionGait.Walk;
                else if (Current == LocomotionGait.Walk && speedRatio < t.IdleMaxRatio - h) Current = LocomotionGait.Idle;
                else break;
            }

            return Current;
        }

        /// <summary>便捷重载：速度与基础移速（世界单位/秒）求比；基础移速非正时视为静止（比值 0）。</summary>
        public LocomotionGait Update(double speed, double baseSpeed) =>
            Update(baseSpeed > 0 ? Math.Abs(speed) / baseSpeed : 0.0);
    }
}
