using System;
using System.Collections.Generic;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// 空中姿势请求（ADR-0130 追加决定"空中姿势键"，手感设计/04 第 2 节的空中扩展）：与 <see cref="PoseRequest"/> 并列的结构化请求。
    /// 空中基础键：<c>jump.rise</c> / <c>jump.fall</c> / <c>jump.land</c>、<c>hit.air</c>、<c>attack.air</c>。
    /// <para>
    /// <b>变体维度（手感落地 M4-W1b）</b>：空中基础键之后按与地面键同样的顺序追加 <c>[.&lt;stance&gt;][.&lt;family&gt;][.&lt;variant&gt;]</c>
    /// （姿态只有 <c>combat</c> 生成段、<c>peace</c> 省略；武器族与变体是自由集合），例如 <c>jump.rise.combat</c>、<c>hit.air.wounded</c>、
    /// <c>attack.air.combat.sword</c>。<b>回落链 = 空中键的维度前缀逐段去尾（先去变体，再去武器族，再去姿态，规则同
    /// <see cref="PoseRequest.Chain"/>），之后是固定尾链</b>：
    /// </para>
    /// <list type="bullet">
    /// <item><c>jump.rise</c> / <c>jump.fall</c>：… → <c>jump.rise</c> → <c>jump</c> → <c>idle</c>；</item>
    /// <item><c>jump.land</c>：… → <c>jump.land</c> → <c>idle</c>；</item>
    /// <item><c>hit.air</c>：… → <c>hit.air</c> → <c>hit.launch</c> → <c>hit</c>；</item>
    /// <item><c>attack.air</c>：… → <c>attack.air</c> → <c>attack.&lt;family&gt;</c> → <c>attack</c>（无武器族时 <c>attack.air</c> → <c>attack</c>）。</item>
    /// </list>
    /// 不带任何维度的请求（旧调用方）链与改动前逐位一致：<c>attack.air.&lt;family&gt;</c> 里的武器族就是这里的武器族维度。
    /// 判断记录：①姿态与变体只在空中键这一段做细分，尾链不再带维度（空中专用剪辑缺失时落到既有的"跳跃/受击/攻击"通用键，
    /// 这些通用键自己的战斗姿态/变体细分由地面解析负责，不在空中链里重复）；②<see cref="AnimState"/> 枚举不变，空中姿势只是同一状态
    /// （跳跃/受击/攻击）的不同键；③链的末端（<c>idle</c>/<c>hit</c>/<c>attack</c>）恒作为兜底，不咨询可用性探针（同 <see cref="PoseResolver"/> 基础键规则）；
    /// ④武器族对跳跃与受击同样可选（双手武器的起跳姿势），没有对应剪辑时按上面的去尾规则回落，不要求美术必须配。
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

        /// <summary>武器族；null 表示不指定。</summary>
        public string? Family { get; }

        /// <summary>姿态：只有 <c>combat</c> 生成 <c>.combat</c> 段；null 表示缺省姿态（不生成）。</summary>
        public string? Stance { get; }

        /// <summary>游戏层变体（<c>wounded/carrying/mounted …</c>）；null 表示未指定。</summary>
        public string? Variant { get; }

        private AirPoseRequest(string state, string? phase, string? family, string? stance, string? variant)
        {
            State = state;
            Phase = phase;
            Family = string.IsNullOrEmpty(family) ? null : family;
            Stance = string.IsNullOrEmpty(stance) || stance == PoseKeys.StancePeace ? null : stance;
            Variant = string.IsNullOrEmpty(variant) ? null : variant;
        }

        /// <summary>跳跃：<paramref name="phase"/> 取 <see cref="PhaseRise"/>/<see cref="PhaseFall"/>/<see cref="PhaseLand"/>。</summary>
        public static AirPoseRequest Jump(string phase) => Jump(phase, null, null, null);

        /// <summary>带变体维度的跳跃（手感落地 M4-W1b）：姿态、武器族、变体都可缺省（null）。</summary>
        public static AirPoseRequest Jump(string phase, string? stance, string? family, string? variant)
        {
            if (phase != PhaseRise && phase != PhaseFall && phase != PhaseLand)
            {
                throw new ArgumentException("跳跃阶段只能是 rise/fall/land", nameof(phase));
            }
            return new AirPoseRequest("jump", phase, family, stance, variant);
        }

        /// <summary>空中受击。</summary>
        public static AirPoseRequest HitAir() => new AirPoseRequest("hit", null, null, null, null);

        /// <summary>带变体维度的空中受击（手感落地 M4-W1b）。</summary>
        public static AirPoseRequest HitAir(string? stance, string? family, string? variant) =>
            new AirPoseRequest("hit", null, family, stance, variant);

        /// <summary>空中攻击；<paramref name="family"/> 为 null 或空表示不指定武器族。</summary>
        public static AirPoseRequest AttackAir(string? family = null) =>
            new AirPoseRequest("attack", null, family, null, null);

        /// <summary>带变体维度的空中攻击（手感落地 M4-W1b）。</summary>
        public static AirPoseRequest AttackAir(string? family, string? stance, string? variant) =>
            new AirPoseRequest("attack", null, family, stance, variant);

        /// <summary>空中基础键（不带任何维度）：<c>jump.&lt;phase&gt;</c> / <c>hit.air</c> / <c>attack.air</c>。</summary>
        public string BaseKey()
        {
            switch (State)
            {
                case "jump": return "jump." + Phase;
                case "hit": return "hit.air";
                default: return "attack.air";
            }
        }

        /// <summary>最具体的候选键。</summary>
        public string FullKey() => Chain()[0];

        /// <summary>
        /// 回落链：空中键的维度前缀（最具体的在前，逐段去变体/武器族/姿态）接固定尾链，末端是兜底键。
        /// </summary>
        public IReadOnlyList<string> Chain()
        {
            var tokens = new List<string>(4) { BaseKey() };
            if (Stance != null) tokens.Add(Stance);
            if (Family != null) tokens.Add(Family);
            if (Variant != null) tokens.Add(Variant);

            var chain = new List<string>(tokens.Count + 3);
            for (var n = tokens.Count; n >= 1; n--)
            {
                chain.Add(PoseKeys.Join(tokens, n));
            }

            switch (State)
            {
                case "jump":
                    if (Phase != PhaseLand) chain.Add("jump");
                    chain.Add("idle");
                    break;
                case "hit":
                    chain.Add("hit.launch");
                    chain.Add("hit");
                    break;
                default:
                    if (Family != null) chain.Add("attack." + Family);
                    chain.Add("attack");
                    break;
            }
            return chain;
        }

        public bool Equals(AirPoseRequest other) =>
            State == other.State && Phase == other.Phase && Family == other.Family
            && Stance == other.Stance && Variant == other.Variant;

        public override bool Equals(object? obj) => obj is AirPoseRequest other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(State, Phase, Family, Stance, Variant);

        public override string ToString() => FullKey();
    }
}
