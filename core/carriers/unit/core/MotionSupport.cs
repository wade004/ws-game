using System;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Numbers.StatBlock;

namespace Core.Carriers.Unit
{
    /// <summary>目标辅助的计算结果（手感设计/02 第 5 节；对应 <c>action.target_assisted</c> 事件载荷）。</summary>
    public readonly struct TargetAssistResult
    {
        public Id TargetId { get; }

        /// <summary>朝向修正量（度，带符号；已按 <c>turn_assist_deg</c> 限幅）。</summary>
        public double FacingDeltaDeg { get; }

        /// <summary>位移距离修正量（世界单位；<c>face_only</c> 为 0，<c>close_distance</c> 为缩放后距离减声明距离，可为负）。</summary>
        public double DistanceAdjust { get; }

        public TargetAssistResult(Id targetId, double facingDeltaDeg, double distanceAdjust)
        {
            TargetId = targetId;
            FacingDeltaDeg = facingDeltaDeg;
            DistanceAdjust = distanceAdjust;
        }
    }

    /// <summary>
    /// 目标辅助（软锁定）的运动侧纯函数（手感设计/02 第 5 节）：给定候选目标，按 <c>max_distance</c>/<c>max_angle_deg</c> 过滤，
    /// 朝向修正不超过 <c>turn_assist_deg</c>，<c>close_distance</c> 把位移距离缩放到"判定相形状恰好覆盖目标"所需的值
    /// （不超过声明距离上限）。候选的解析（目标选择链）由 <see cref="ITargetAssistResolver"/> 提供，缺省关闭。
    /// </summary>
    public static class TargetAssistEvaluator
    {
        /// <summary>
        /// 计算目标辅助结果；候选超出距离或角度范围返回 false（静默，不发事件）。
        /// </summary>
        /// <param name="actorPosition">行动者位置。</param>
        /// <param name="actorFacing">行动者朝向（弧度）。</param>
        /// <param name="candidate">候选目标。</param>
        /// <param name="request">目标辅助请求（距离与角度范围、模式）。</param>
        /// <param name="turnAssistDeg">档案动作组 <c>turn_assist_deg</c>（度）：朝向修正上限。</param>
        /// <param name="declaredDistanceWorld">动作位移声明距离（世界单位，标定后）：距离缩放上限。</param>
        /// <param name="shapeReachWorld">判定相形状从行动者向前的覆盖深度（世界单位）：<c>close_distance</c> 缩放依据。</param>
        public static bool TryEvaluate(
            Vec2 actorPosition, double actorFacing, in TargetAssistCandidate candidate, in TargetAssistRequest request,
            double turnAssistDeg, double declaredDistanceWorld, double shapeReachWorld, out TargetAssistResult result)
        {
            result = default;
            var to = candidate.Position - actorPosition;
            var distance = to.Length;
            if (distance > request.MaxDistance) return false;

            var angleDeg = 0.0;
            if (distance > 1e-9)
            {
                angleDeg = MotionMath.WrapAngle(Math.Atan2(to.Y, to.X) - actorFacing) * (180.0 / Math.PI);
            }

            if (Math.Abs(angleDeg) > request.MaxAngleDeg) return false;

            var cap = Math.Max(0.0, turnAssistDeg);
            var facingDelta = Math.Max(-cap, Math.Min(cap, angleDeg));

            var distanceAdjust = 0.0;
            if (request.Mode == TargetAssistMode.CloseDistance)
            {
                var needed = Math.Max(0.0, distance - Math.Max(0.0, shapeReachWorld));
                var chosen = Math.Min(needed, Math.Max(0.0, declaredDistanceWorld));
                distanceAdjust = chosen - declaredDistanceWorld;
            }

            result = new TargetAssistResult(candidate.TargetId, facingDelta, distanceAdjust);
            return true;
        }
    }

    /// <summary>
    /// 供表现层派生步态的输入（手感设计/02 第 7 节）：当前速度与基础移速的比值，以及档案移动组的呈现型阈值。
    /// 步态派生本身（带滞回的 idle/walk/run/sprint 判定、播放速率匹配）在表现层做，本类型只导出输入。
    /// </summary>
    public readonly struct GaitInputs
    {
        /// <summary><c>|velocity| / 基础移速</c>。</summary>
        public double SpeedRatio { get; }

        /// <summary>当前速度向量（世界单位/秒）。</summary>
        public Vec2 Velocity { get; }

        public double IdleMaxRatio { get; }

        public double WalkMaxRatio { get; }

        /// <summary>sprint 下界；未声明冲刺时为 null。</summary>
        public double? SprintMinRatio { get; }

        public double HysteresisRatio { get; }

        public GaitInputs(
            double speedRatio, Vec2 velocity, double idleMaxRatio, double walkMaxRatio, double? sprintMinRatio, double hysteresisRatio)
        {
            SpeedRatio = speedRatio;
            Velocity = velocity;
            IdleMaxRatio = idleMaxRatio;
            WalkMaxRatio = walkMaxRatio;
            SprintMinRatio = sprintMinRatio;
            HysteresisRatio = hysteresisRatio;
        }

        /// <summary>由运动学状态与呈现型视图组装步态输入（呈现型阈值只经呈现型视图读取）。</summary>
        public static GaitInputs From(in MotionKinematics kinematics, PresentingFeelView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            double? sprint = null;
            var sprintRaw = view.GetRaw(FeelFieldNames.SprintMinRatio);
            if (sprintRaw.Kind == FeelValueKind.Number) sprint = sprintRaw.AsNumber();
            return new GaitInputs(
                kinematics.SpeedRatio,
                kinematics.Velocity,
                RawRatio(view, FeelFieldNames.IdleMaxRatio),
                RawRatio(view, FeelFieldNames.WalkMaxRatio),
                sprint,
                RawRatio(view, FeelFieldNames.GaitHysteresisRatio));
        }

        // 阈值是"相对基础移速的倍数"，要与 SpeedRatio（同为倍数）直接比较，取标定前的相对值。
        private static double RawRatio(PresentingFeelView view, string field)
        {
            var raw = view.GetRaw(field);
            if (raw.Kind != FeelValueKind.Number) throw new InvalidOperationException($"步态阈值字段 \"{field}\" 未设置");
            return raw.AsNumber();
        }
    }

    /// <summary>
    /// 击退的运动侧计算（手感设计/02 第 6 节）：距离 = 受击组 <c>knockback_distance</c>（身高倍数，经标定换算为世界单位）×
    /// (1 − 目标击退抗性) × 冲击等级倍率。冲击等级倍率由受击裁决（03）给出，本类只按传入值相乘（缺省 1）。
    /// 执行见 <see cref="MovementHost.BeginKnockback"/>。
    /// </summary>
    public static class MotionKnockback
    {
        /// <summary>读目标的击退抗性（0～1）：档案 <c>knockback_resistance_stat</c> 指向的属性；未声明视为 0；越界值夹取。</summary>
        public static double ReadResistance(JudgingFeelView targetView, IStatHost stats, Id targetId)
        {
            if (targetView == null) throw new ArgumentNullException(nameof(targetView));
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            var raw = targetView.GetAbsolute(FeelFieldNames.KnockbackResistanceStat);
            if (raw.IsNone) return 0.0;
            var r = stats.GetStat(targetId, new Id(raw.AsText()));
            return r < 0.0 ? 0.0 : (r > 1.0 ? 1.0 : r);
        }

        /// <summary>击退距离（世界单位）。<paramref name="impactMultiplier"/> 缺省 1。</summary>
        public static double ComputeDistanceWorld(JudgingFeelView targetView, double resistance01, double impactMultiplier = 1.0)
        {
            if (targetView == null) throw new ArgumentNullException(nameof(targetView));
            var r = resistance01 < 0.0 ? 0.0 : (resistance01 > 1.0 ? 1.0 : resistance01);
            return targetView.GetNumber(FeelFieldNames.KnockbackDistance) * (1.0 - r) * impactMultiplier;
        }
    }
}
