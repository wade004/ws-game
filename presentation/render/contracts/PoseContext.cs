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

        public PoseContext(LocomotionGait gait, string? family = null, string? variant = null)
        {
            Gait = gait;
            Family = string.IsNullOrEmpty(family) ? null : family;
            Variant = string.IsNullOrEmpty(variant) ? null : variant;
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
            && string.Equals(Variant, other.Variant, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is PoseContext other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = (int)Gait;
                h = (h * 397) ^ (Family == null ? 0 : StringComparer.Ordinal.GetHashCode(Family));
                h = (h * 397) ^ (Variant == null ? 0 : StringComparer.Ordinal.GetHashCode(Variant));
                return h;
            }
        }

        public override string ToString() => $"{Gait}/{Family ?? "-"}/{Variant ?? "-"}";
    }
}
