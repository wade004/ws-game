using System;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 击退请求（手感设计/02 第 6 节）：提交给 <see cref="MovementHost.BeginKnockback"/>。击退是 <c>forced</c> 来源的受控位移
    /// （ADR-0026 连续模式），用 <c>ease_out</c> 曲线在总时长内走完整段距离，经导航裁决截断；距离由受击组
    /// <c>knockback_distance</c> × (1 − 击退抗性) × 冲击等级倍率算出（见 <see cref="MotionKnockback"/>），方向取
    /// <c>combat.hit_confirmed.worldDirection</c>。
    /// </summary>
    public readonly struct KnockbackRequest
    {
        /// <summary>被击退的单位（目标）。</summary>
        public Id UnitId { get; }

        /// <summary>击退方向（世界方向；会被单位化，零向量非法）。</summary>
        public Vec2 Direction { get; }

        /// <summary>击退距离（世界单位，已含抗性与冲击倍率），必须为正。</summary>
        public double DistanceWorld { get; }

        /// <summary>总时长（秒）；≤ 0 表示用 <c>MovementOptions.KnockbackDurationSeconds</c>。</summary>
        public double DurationSeconds { get; }

        public KnockbackRequest(Id unitId, Vec2 direction, double distanceWorld, double durationSeconds = 0.0)
        {
            if (direction.Length <= 1e-9) throw new ArgumentException("击退方向不能为零向量", nameof(direction));
            if (!(distanceWorld > 0.0) || double.IsInfinity(distanceWorld)) throw new ArgumentException("击退距离必须为正的有限数", nameof(distanceWorld));
            UnitId = unitId;
            Direction = direction;
            DistanceWorld = distanceWorld;
            DurationSeconds = durationSeconds;
        }
    }
}
