using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Feel;

namespace Presentation.Render
{
    /// <summary>
    /// 移动呈现参数的纯函数（手感设计/02 第 7 节，ADR-0147）。
    /// <para>
    /// 判断记录：①<b>剪辑的制作速度（参考速度比）</b>按步态取呈现型字段——走剪辑按 <c>walk_max_ratio</c>（走档上沿）制作，跑剪辑按基础移速
    /// （1.0）制作，冲刺剪辑按 <c>sprint_min_ratio</c> 制作（没声明时按跑）。参考速度只取呈现型字段、不读判定型字段（半属隔离，00 第 154 行）；
    /// ②播放速率 = <c>stride_scale</c> × 实际速度比 ÷ 参考速度比，夹在 [<see cref="MinRate"/>, <see cref="MaxRate"/>]（起步/急停的低速段不让剪辑几近静止，
    /// 过快的瞬时速度不让脚步变成抖动），再按 <see cref="RateQuantum"/> 取整（避免逐 tick 微小变化反复调播放器）；
    /// ③身体前倾角 = <c>lean_deg_per_accel</c> × 加速度（速度比的变化率，单位 1/秒；加速前倾为正，减速后仰为负），夹在 ±<see cref="MaxLeanDeg"/>
    /// （字段范围上限 45），再按 <see cref="LeanQuantum"/> 取整。
    /// </para>
    /// </summary>
    public static class LocomotionPresentationMath
    {
        public const double MinRate = 0.5;
        public const double MaxRate = 2.0;
        public const double RateQuantum = 0.05;
        public const double MaxLeanDeg = 45.0;
        public const double LeanQuantum = 0.25;

        /// <summary>该步态剪辑的参考（制作）速度比。<paramref name="walkMaxRatio"/>、<paramref name="sprintMinRatio"/> 取呈现型字段值。</summary>
        public static double ReferenceRatio(LocomotionGait gait, double walkMaxRatio, double? sprintMinRatio)
        {
            switch (gait)
            {
                case LocomotionGait.Sprint:
                    return sprintMinRatio.HasValue && sprintMinRatio.Value > 0 ? sprintMinRatio.Value : 1.0;
                case LocomotionGait.Run:
                    return 1.0;
                default: // Idle（被推着蹭动按最慢的走档取姿势，同 PoseContext.ToRequest）与 Walk
                    return walkMaxRatio > 0 ? walkMaxRatio : 1.0;
            }
        }

        /// <summary>播放速率（已夹取、已取整）。速度比不大于 0（停下）返回 1。</summary>
        public static double StrideRate(double speedRatio, double referenceRatio, double strideScale)
        {
            if (!(speedRatio > 0) || !(referenceRatio > 0) || !(strideScale > 0)) return 1.0;
            var rate = strideScale * speedRatio / referenceRatio;
            if (rate < MinRate) rate = MinRate;
            if (rate > MaxRate) rate = MaxRate;
            return Math.Round(rate / RateQuantum) * RateQuantum;
        }

        /// <summary>身体前倾角（度，已夹取、已取整）。</summary>
        public static double LeanDeg(double accelRatioPerSecond, double degPerAccel)
        {
            if (!(degPerAccel > 0)) return 0.0;
            var lean = degPerAccel * accelRatioPerSecond;
            if (lean > MaxLeanDeg) lean = MaxLeanDeg;
            if (lean < -MaxLeanDeg) lean = -MaxLeanDeg;
            return Math.Round(lean / LeanQuantum) * LeanQuantum;
        }
    }

    /// <summary>移动呈现参数的存储与变化通知（<see cref="ILocomotionPresentationSource"/> 的框架实现）；由 <see cref="PoseGaitFeeder"/> 喂入。</summary>
    public sealed class LocomotionPresentation : ILocomotionPresentationSource
    {
        private readonly Dictionary<Id, (double Rate, double Lean)> _values = new Dictionary<Id, (double, double)>();

        public event Action<Id>? Changed;

        public double GetStrideRate(Id entityId) => _values.TryGetValue(entityId, out var v) ? v.Rate : 1.0;

        public double GetLeanDeg(Id entityId) => _values.TryGetValue(entityId, out var v) ? v.Lean : 0.0;

        /// <summary>写入一个实体的参数；与当前值相同不触发 <see cref="Changed"/>。回到缺省（1、0）即移除记录。</summary>
        public void Set(Id entityId, double rate, double leanDeg)
        {
            var current = (Rate: GetStrideRate(entityId), Lean: GetLeanDeg(entityId));
            if (current.Rate == rate && current.Lean == leanDeg) return;
            if (rate == 1.0 && leanDeg == 0.0) _values.Remove(entityId);
            else _values[entityId] = (rate, leanDeg);
            Changed?.Invoke(entityId);
        }

        /// <summary>实体销毁时清理（不触发事件）。</summary>
        public void Forget(Id entityId) => _values.Remove(entityId);
    }
}
