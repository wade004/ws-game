using System;
using Core.Foundation.DisplayInfo;

namespace Presentation.Render
{
    /// <summary>
    /// 步态（呈现型派生，手感设计/02 第 7 节）：表现层按 <c>|velocity| / 基础移速</c> 派生，只用于姿势解析，
    /// 逻辑层没有步态概念，也不回写判定。
    /// </summary>
    public enum LocomotionGait
    {
        Idle,
        Walk,
        Run,
        Sprint,
    }

    /// <summary>
    /// 空中阶段（呈现型派生，ADR-0130 追加决定"空中姿势"）：表现层按竖直运动服务的竖直速度与腾空/落地派生，只用于姿势解析，
    /// 逻辑层没有这个概念，也不回写判定。<see cref="None"/> = 在地面（缺省，与未启用空中姿势时逐位一致）。
    /// </summary>
    public enum AirPhase
    {
        None = 0,

        /// <summary>腾空且向上运动。</summary>
        Rise = 1,

        /// <summary>腾空且向下运动（含顶点）。</summary>
        Fall = 2,

        /// <summary>刚落地的保持窗口（只持续数个 tick，供落地姿势播放）。</summary>
        Land = 3,
    }

    /// <summary>
    /// 姿势解析的呈现侧上下文（手感设计/04 第 2 节的步态、武器族、变体三个维度；状态与姿态由动画状态机给）。
    /// 纯值对象。缺省值（<c>default</c>）= 没有任何额外维度，解析结果恒为改动前的"状态 + 战斗姿态"两维。
    /// </summary>
    public readonly struct PoseContext : IEquatable<PoseContext>
    {
        public LocomotionGait Gait { get; }

        /// <summary>主手武器的武器族（<c>feel.weapon.family</c>）；null 表示未指定。</summary>
        public string? Family { get; }

        /// <summary>游戏层变体（<c>wounded/carrying/mounted …</c>）；null 表示未指定。</summary>
        public string? Variant { get; }

        /// <summary>空中阶段；缺省 <see cref="AirPhase.None"/>。</summary>
        public AirPhase Air { get; }

        public PoseContext(LocomotionGait gait, string? family = null, string? variant = null)
        {
            Gait = gait;
            Family = string.IsNullOrEmpty(family) ? null : family;
            Variant = string.IsNullOrEmpty(variant) ? null : variant;
            Air = AirPhase.None;
        }

        public PoseContext(LocomotionGait gait, string? family, string? variant, AirPhase air)
        {
            Gait = gait;
            Family = string.IsNullOrEmpty(family) ? null : family;
            Variant = string.IsNullOrEmpty(variant) ? null : variant;
            Air = air;
        }

        /// <summary>此刻是否在空中（上升或下降；落地保持窗口不算）。</summary>
        public bool IsAirborne => Air == AirPhase.Rise || Air == AirPhase.Fall;

        /// <summary>
        /// 空中姿势请求（ADR-0130 追加决定）：<paramref name="stateKey"/> 为 <c>jump</c> 且有空中阶段（含落地保持）时取跳跃阶段键；
        /// 为 <c>hit</c>/<c>attack</c> 且此刻在空中时取 <c>hit.air</c>/<c>attack.air[.&lt;family&gt;]</c>；其它情形返回 false，
        /// 调用方走 <see cref="ToRequest"/> 的地面解析（与改动前逐位一致）。
        /// </summary>
        public bool TryGetAirRequest(string stateKey, out AirPoseRequest request)
        {
            request = default;
            if (Air == AirPhase.None) return false;
            switch (stateKey)
            {
                case "jump":
                    request = AirPoseRequest.Jump(
                        Air == AirPhase.Rise ? AirPoseRequest.PhaseRise
                        : Air == AirPhase.Fall ? AirPoseRequest.PhaseFall
                        : AirPoseRequest.PhaseLand);
                    return true;
                case "hit":
                    if (!IsAirborne) return false;
                    request = AirPoseRequest.HitAir();
                    return true;
                case "attack":
                    if (!IsAirborne) return false;
                    request = AirPoseRequest.AttackAir(Family);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>无任何额外维度。</summary>
        public static PoseContext Empty => default;

        /// <summary>
        /// 拼姿势请求。判断记录：<c>move</c> 状态下步态 <see cref="LocomotionGait.Idle"/>（动画状态机判定在移动、但速度低于
        /// <c>idle_max_ratio</c>，如被推着蹭动）按最慢的 <c>walk</c> 步态取姿势，不用无步态的基础 <c>move</c>——脚步节奏
        /// 与最慢档一致比换成无步态剪辑更不违和；其它状态忽略步态（<see cref="PoseRequest"/> 对非 move 状态不生成步态段）。
        /// 表里没有 <c>move.walk</c> 时回落链自然落到基础键 <c>move</c>，没有步态剪辑的旧姿势集行为不变。
        /// </summary>
        public PoseRequest ToRequest(string stateKey, bool inCombat)
        {
            string? gait = null;
            if (stateKey == PoseKeys.GaitState)
            {
                switch (Gait)
                {
                    case LocomotionGait.Run: gait = PoseKeys.GaitRun; break;
                    case LocomotionGait.Sprint: gait = PoseKeys.GaitSprint; break;
                    default: gait = PoseKeys.GaitWalk; break;
                }
            }
            return new PoseRequest(stateKey, gait, inCombat ? PoseKeys.StanceCombat : null, Family, Variant);
        }

        public bool Equals(PoseContext other) =>
            Gait == other.Gait && string.Equals(Family, other.Family, StringComparison.Ordinal)
            && string.Equals(Variant, other.Variant, StringComparison.Ordinal) && Air == other.Air;

        public override bool Equals(object? obj) => obj is PoseContext other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = (int)Gait;
                h = (h * 397) ^ (Family == null ? 0 : StringComparer.Ordinal.GetHashCode(Family));
                h = (h * 397) ^ (Variant == null ? 0 : StringComparer.Ordinal.GetHashCode(Variant));
                h = (h * 397) ^ (int)Air;
                return h;
            }
        }

        public override string ToString() => $"{Gait}/{Family ?? "-"}/{Variant ?? "-"}" + (Air == AirPhase.None ? string.Empty : "/" + Air);
    }
}
