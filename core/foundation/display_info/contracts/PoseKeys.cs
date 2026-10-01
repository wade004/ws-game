using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// 姿势键语法（手感设计/04 第 2.1 节）：<c>&lt;state&gt;[.&lt;gait&gt;][.&lt;stance&gt;][.&lt;family&gt;][.&lt;variant&gt;]</c>，
    /// 维度按固定顺序拼接，缺省维度省略。本类型是键语法的单一出处：旧键 <c>combat_&lt;state&gt;</c> 与
    /// <c>&lt;state&gt;.combat</c> 的等价关系、合法字符、维度词汇都只在这里定义。
    /// <para>
    /// 判断记录：①<c>peace</c> 是姿态维度的缺省取值，键里<b>省略</b>（不生成 <c>.peace</c> 段）——这样既有基础键与
    /// 无姿态键语义不变；②步态段只在 <c>move</c> 状态出现（04 第 2 节"仅 move 状态"），其它状态请求里带的步态被忽略；
    /// ③键里的武器族与变体不在这里枚举——它们是自由集合（<c>feel.weapon.family</c> 与游戏层探针声明），
    /// 因此本类型只提供"按结构化请求拼键"，不提供"把任意键拆回维度"（单看一个键无法区分 <c>idle.2h</c> 的
    /// <c>2h</c> 是武器族还是变体）；④合法字符为小写字母、数字、下划线，段间以点分隔——违规只是警告级（既有数据里的
    /// 自由键名不因此失效，见 <see cref="AnimSetPoseRule"/>）。
    /// </para>
    /// </summary>
    public static class PoseKeys
    {
        public const char Separator = '.';

        /// <summary>旧键（ADR-0111）的前缀，等价于 <see cref="CombatClipKeyPrefix"/>（单处定义见 <see cref="AnimSetDef.CombatClipKeyPrefix"/>）。</summary>
        public const string LegacyCombatPrefix = AnimSetDef.CombatClipKeyPrefix;

        /// <summary>只有该状态带步态段。</summary>
        public const string GaitState = "move";

        public const string GaitWalk = "walk";
        public const string GaitRun = "run";
        public const string GaitSprint = "sprint";

        public const string StancePeace = "peace";
        public const string StanceCombat = "combat";

        private static readonly string[] GaitValues = { GaitWalk, GaitRun, GaitSprint };

        /// <summary>步态词汇（<c>walk/run/sprint</c>）。</summary>
        public static IReadOnlyList<string> Gaits => GaitValues;

        /// <summary>该串是否是步态词汇。</summary>
        public static bool IsGait(string token) =>
            token == GaitWalk || token == GaitRun || token == GaitSprint;

        /// <summary>
        /// 规范化：旧键 <c>combat_&lt;state&gt;</c>（<c>&lt;state&gt;</c> 非空且不含点）恒等于 <c>&lt;state&gt;.combat</c>，
        /// 其余键原样返回。不做其它改写（不转小写、不裁剪），保持"数据里写什么就是什么"。
        /// </summary>
        public static string Canonicalize(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length > LegacyCombatPrefix.Length
                && key.StartsWith(LegacyCombatPrefix, StringComparison.Ordinal)
                && key.IndexOf(Separator, LegacyCombatPrefix.Length) < 0)
            {
                return key.Substring(LegacyCombatPrefix.Length) + Separator + StanceCombat;
            }
            return key;
        }

        /// <summary>
        /// 规范键 <c>&lt;state&gt;.combat</c>（恰好两段、第二段是 <c>combat</c>）的旧键写法
        /// <c>combat_&lt;state&gt;</c>；其它键没有旧键别名，返回 false。
        /// </summary>
        public static bool TryGetLegacyAlias(string canonicalKey, out string legacyKey)
        {
            legacyKey = string.Empty;
            if (canonicalKey == null) return false;
            var suffix = Separator + StanceCombat;
            if (canonicalKey.Length > suffix.Length
                && canonicalKey.EndsWith(suffix, StringComparison.Ordinal)
                && canonicalKey.IndexOf(Separator) == canonicalKey.Length - suffix.Length)
            {
                legacyKey = LegacyCombatPrefix + canonicalKey.Substring(0, canonicalKey.Length - suffix.Length);
                return true;
            }
            return false;
        }

        /// <summary>键语法是否合法：非空；点分段，每段非空且仅含小写字母/数字/下划线。</summary>
        public static bool IsWellFormed(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var segmentLength = 0;
            for (var i = 0; i < key.Length; i++)
            {
                var c = key[i];
                if (c == Separator)
                {
                    if (segmentLength == 0) return false;
                    segmentLength = 0;
                    continue;
                }
                var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
                segmentLength++;
            }
            return segmentLength > 0;
        }

        /// <summary>键的状态段（第一个点之前）。</summary>
        public static string StateOf(string key)
        {
            var dot = key.IndexOf(Separator);
            return dot < 0 ? key : key.Substring(0, dot);
        }

        /// <summary>
        /// 去掉最右一段（回落链的"通用去维度"：对任意键逐段去尾）；已是单段返回 false。
        /// </summary>
        public static bool TryDropLastSegment(string key, out string shorter)
        {
            var dot = key.LastIndexOf(Separator);
            if (dot <= 0)
            {
                shorter = string.Empty;
                return false;
            }
            shorter = key.Substring(0, dot);
            return true;
        }

        internal static string Join(IReadOnlyList<string> tokens, int count)
        {
            if (count == 1) return tokens[0];
            var sb = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(Separator);
                sb.Append(tokens[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 一次姿势解析请求（结构化的维度取值，手感设计/04 第 2 节）：状态必填，其余维度可缺省。
    /// <see cref="Chain"/> 给出按回落链排好序的候选键。
    /// </summary>
    public readonly struct PoseRequest : IEquatable<PoseRequest>
    {
        /// <summary>状态段（<c>idle/move/attack/cast/hit/death/jump</c> 等基础键）。</summary>
        public string State { get; }

        /// <summary>步态（<c>walk/run/sprint</c>）；仅 <c>move</c> 状态生效，其它状态忽略；null 或空表示不指定。</summary>
        public string? Gait { get; }

        /// <summary>姿态：<c>combat</c> 生成 <c>.combat</c> 段；null、空或 <c>peace</c> 不生成（缺省姿态）。</summary>
        public string? Stance { get; }

        /// <summary>武器族（<c>feel.weapon.family</c>）；null 或空表示不指定。</summary>
        public string? Family { get; }

        /// <summary>游戏层变体（<c>wounded/carrying/mounted …</c>）；null 或空表示不指定。</summary>
        public string? Variant { get; }

        public PoseRequest(string state, string? gait = null, string? stance = null, string? family = null, string? variant = null)
        {
            if (string.IsNullOrEmpty(state)) throw new ArgumentException("状态不能为空", nameof(state));
            State = state;
            Gait = string.IsNullOrEmpty(gait) ? null : gait;
            Stance = string.IsNullOrEmpty(stance) ? null : stance;
            Family = string.IsNullOrEmpty(family) ? null : family;
            Variant = string.IsNullOrEmpty(variant) ? null : variant;
        }

        /// <summary>仅状态、不带任何维度的请求（解析结果恒为基础键）。</summary>
        public static PoseRequest Base(string state) => new PoseRequest(state);

        /// <summary>请求里实际生效的维度段（按固定顺序）：[state, gait?, stance?, family?, variant?]。</summary>
        public IReadOnlyList<string> Tokens()
        {
            var list = new List<string>(5) { State };
            if (Gait != null && State == PoseKeys.GaitState) list.Add(Gait);
            if (Stance != null && Stance != PoseKeys.StancePeace) list.Add(Stance);
            if (Family != null) list.Add(Family);
            if (Variant != null) list.Add(Variant);
            return list;
        }

        /// <summary>完整请求键（最具体的候选）。</summary>
        public string FullKey()
        {
            var tokens = Tokens();
            return PoseKeys.Join(tokens, tokens.Count);
        }

        /// <summary>
        /// 回落链（04 第 2.2 节）：最具体的键在前，依次去变体、去武器族、去姿态、去步态，最后是基础键。
        /// 请求里没有的维度不产生条目；全部维度齐全时恰好 5 项。
        /// <para>
        /// 判断记录（<c>sprint</c> 先按 <c>run</c> 重走一遍再去步态）：04 第 3 节把 <c>move.sprint</c> 列为可选、"缺项静默回落"，
        /// 而必备键里只有 <c>move.walk/move.run</c>、没有无步态的基础键 <c>move</c>——严格按"去步态 → 基础键"，
        /// 没有冲刺剪辑的姿势集在冲刺时会解析不到任何剪辑（基础键 <c>move</c> 不在必备清单里）。因此请求步态为 <c>sprint</c> 时，
        /// 先走完带 <c>sprint</c> 段的全部候选（去变体/武器族/姿态），再以 <c>run</c> 代替 <c>sprint</c> 把同一串候选走一遍，
        /// 最后才是基础键。顺序依据与 04 第 2.2 节同一条理由：步态保真优先于武器族保真（脚不打滑）。其它步态与旧数据不受影响。
        /// </para>
        /// </summary>
        public IReadOnlyList<string> Chain()
        {
            var tokens = Tokens();
            var chain = new List<string>(tokens.Count + 4);
            var sprintIndex = tokens.Count > 1 && State == PoseKeys.GaitState && tokens[1] == PoseKeys.GaitSprint ? 1 : -1;

            for (var n = tokens.Count; n >= 1; n--)
            {
                if (n == 1 && sprintIndex == 1)
                {
                    // 冲刺请求：在落到基础键之前，用 run 代替 sprint 再走一遍带步态段的候选。
                    var asRun = new List<string>(tokens) { [1] = PoseKeys.GaitRun };
                    for (var m = asRun.Count; m >= 2; m--)
                    {
                        chain.Add(PoseKeys.Join(asRun, m));
                    }
                }
                chain.Add(PoseKeys.Join(tokens, n));
            }
            return chain;
        }

        public bool Equals(PoseRequest other) =>
            State == other.State && Gait == other.Gait && Stance == other.Stance
            && Family == other.Family && Variant == other.Variant;

        public override bool Equals(object? obj) => obj is PoseRequest other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = StringComparer.Ordinal.GetHashCode(State);
                h = (h * 397) ^ (Gait == null ? 0 : StringComparer.Ordinal.GetHashCode(Gait));
                h = (h * 397) ^ (Stance == null ? 0 : StringComparer.Ordinal.GetHashCode(Stance));
                h = (h * 397) ^ (Family == null ? 0 : StringComparer.Ordinal.GetHashCode(Family));
                h = (h * 397) ^ (Variant == null ? 0 : StringComparer.Ordinal.GetHashCode(Variant));
                return h;
            }
        }

        public override string ToString() => FullKey();
    }
}
