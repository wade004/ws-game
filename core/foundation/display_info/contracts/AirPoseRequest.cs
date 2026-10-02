using System;
using System.Collections.Generic;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// 空中姿势请求（ADR-0130 追加决定"空中姿势键"，手感设计/04 第 2 节的空中扩展）：与 <see cref="PoseRequest"/> 并列的结构化请求，
    /// 键与回落链是<b>固定约定</b>（不是 <see cref="PoseRequest"/> 那种按维度逐段去尾的通用链）：
    /// <list type="bullet">
    /// <item><c>jump.rise</c> / <c>jump.fall</c> → <c>jump</c> → <c>idle</c>；</item>
    /// <item><c>jump.land</c> → <c>idle</c>；</item>
    /// <item><c>hit.air</c> → <c>hit.launch</c> → <c>hit</c>；</item>
    /// <item><c>attack.air.&lt;family&gt;</c> → <c>attack.air</c> → <c>attack.&lt;family&gt;</c> → <c>attack</c>（无武器族时 <c>attack.air</c> → <c>attack</c>）。</item>
    /// </list>
    /// 判断记录：①空中键不带姿态/步态/变体维度（空中没有步态；战斗姿态与变体在空中不分剪辑）——要按姿态分的美术用武器族之外的
    /// 做法是拆成不同姿势集，不在本约定内；②<see cref="AnimState"/> 枚举不变，空中姿势只是同一状态（跳跃/受击/攻击）的不同键；
    /// ③链的末端（<c>idle</c>/<c>hit</c>/<c>attack</c>）恒作为兜底，不咨询可用性探针（同 <see cref="PoseResolver"/> 基础键规则）。
    /// </summary>
    public readonly struct AirPoseRequest : IEquatable<AirPoseRequest>
    {
        public const string PhaseRise = "rise";
        public const string PhaseFall = "fall";
        public const string PhaseLand = "land";
        public const string AirToken = "air";
        public const string LaunchToken = "launch";

        /// <summary>状态键：<c>jump</c> / <c>hit</c> / <c>attack</c>。</summary>
        public string State { get; }

        /// <summary>跳跃阶段（<c>rise/fall/land</c>），仅 <c>jump</c> 状态有；其它为 null。</summary>
        public string? Phase { get; }

        /// <summary>武器族，仅 <c>attack</c> 状态有；null 表示不指定。</summary>
        public string? Family { get; }

        private AirPoseRequest(string state, string? phase, string? family)
        {
            State = state;
            Phase = phase;
            Family = family;
        }

        /// <summary>跳跃：<paramref name="phase"/> 取 <see cref="PhaseRise"/>/<see cref="PhaseFall"/>/<see cref="PhaseLand"/>。</summary>
        public static AirPoseRequest Jump(string phase)
        {
            if (phase != PhaseRise && phase != PhaseFall && phase != PhaseLand)
            {
                throw new ArgumentException("跳跃阶段只能是 rise/fall/land", nameof(phase));
            }
            return new AirPoseRequest("jump", phase, null);
        }

        /// <summary>空中受击。</summary>
        public static AirPoseRequest HitAir() => new AirPoseRequest("hit", null, null);

        /// <summary>空中攻击；<paramref name="family"/> 为 null 或空表示不指定武器族。</summary>
        public static AirPoseRequest AttackAir(string? family = null) =>
            new AirPoseRequest("attack", null, string.IsNullOrEmpty(family) ? null : family);

        /// <summary>最具体的候选键。</summary>
        public string FullKey() => Chain()[0];

        /// <summary>固定回落链：最具体的键在前，末端是兜底键。</summary>
        public IReadOnlyList<string> Chain()
        {
            switch (State)
            {
                case "jump":
                    return Phase == PhaseLand
                        ? new[] { "jump.land", "idle" }
                        : new[] { "jump." + Phase, "jump", "idle" };
                case "hit":
                    return new[] { "hit.air", "hit.launch", "hit" };
                default:
                    return Family == null
                        ? new[] { "attack.air", "attack" }
                        : new[] { "attack.air." + Family, "attack.air", "attack." + Family, "attack" };
            }
        }

        public bool Equals(AirPoseRequest other) =>
            State == other.State && Phase == other.Phase && Family == other.Family;

        public override bool Equals(object? obj) => obj is AirPoseRequest other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(State, Phase, Family);

        public override string ToString() => FullKey();
    }
}
