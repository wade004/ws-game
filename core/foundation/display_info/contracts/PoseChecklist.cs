using System;
using System.Collections.Generic;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>标准姿势清单的层级（手感设计/04 第 3 节）。</summary>
    public enum PoseTier
    {
        /// <summary>缺任一项为错误，姿势集不能发布。</summary>
        Required,

        /// <summary>缺项为警告并进完整性报告；运行期按回落链兜底。</summary>
        Recommended,

        /// <summary>缺项静默回落（完整性报告里仍列出）。</summary>
        Optional,
    }

    /// <summary>清单里的一项。<see cref="Key"/> 是展示与求回落目标用的键；某些项是"任意一个满足即可"（见 <see cref="AnyOf"/>）。</summary>
    public sealed class PoseChecklistEntry
    {
        public string Key { get; }

        public PoseTier Tier { get; }

        /// <summary>可空：给出时，只要<b>任一</b>已声明键（规范拼写）满足它，本项即视为齐全（如"某个武器族的攻击二段"）。</summary>
        public Func<string, bool>? AnyOf { get; }

        /// <summary>该项缺失且没有可回落的键时，报告里写的运行期行为说明。</summary>
        public string WhenMissing { get; }

        /// <summary>可空：缺失时先从哪个键起求回落目标（缺省从 <see cref="Key"/> 逐段去尾；<c>move.sprint</c> 先回落到 <c>move.run</c>，
        /// 见 <see cref="PoseRequest.Chain"/> 判断记录）。</summary>
        public string? FallbackFrom { get; }

        public PoseChecklistEntry(string key, PoseTier tier, string whenMissing, Func<string, bool>? anyOf = null, string? fallbackFrom = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Tier = tier;
            WhenMissing = whenMissing ?? throw new ArgumentNullException(nameof(whenMissing));
            AnyOf = anyOf;
            FallbackFrom = fallbackFrom;
        }
    }

    /// <summary>完整性报告里的一条缺项。</summary>
    public sealed class PoseChecklistFinding
    {
        public PoseChecklistEntry Entry { get; }

        /// <summary>运行期回落到的已有键（规范拼写）；没有可回落的键为 null。</summary>
        public string? FallbackKey { get; }

        public PoseChecklistFinding(PoseChecklistEntry entry, string? fallbackKey)
        {
            Entry = entry;
            FallbackKey = fallbackKey;
        }

        /// <summary>一句话描述，形如"缺推荐键 hit.heavy，运行期回落到 hit"。</summary>
        public string Describe()
        {
            var tier = Entry.Tier == PoseTier.Required ? "必备" : Entry.Tier == PoseTier.Recommended ? "推荐" : "可选";
            var tail = FallbackKey != null ? $"运行期回落到 {FallbackKey}" : Entry.WhenMissing;
            return $"缺{tier}键 {Entry.Key}，{tail}";
        }
    }

    /// <summary>完整性报告（校验输出，不入库）：按层级列出缺项。</summary>
    public sealed class PoseChecklistReport
    {
        public IReadOnlyList<PoseChecklistFinding> MissingRequired { get; }

        public IReadOnlyList<PoseChecklistFinding> MissingRecommended { get; }

        public IReadOnlyList<PoseChecklistFinding> MissingOptional { get; }

        public PoseChecklistReport(
            IReadOnlyList<PoseChecklistFinding> required,
            IReadOnlyList<PoseChecklistFinding> recommended,
            IReadOnlyList<PoseChecklistFinding> optional)
        {
            MissingRequired = required;
            MissingRecommended = recommended;
            MissingOptional = optional;
        }

        /// <summary>必备项齐全（可发布）。</summary>
        public bool IsPublishable => MissingRequired.Count == 0;
    }

    /// <summary>
    /// 标准姿势清单（手感设计/04 第 3 节）——必备/推荐/可选三层的单一出处。校验规则（<see cref="AnimSetPoseRule"/>）、
    /// 导入工具（<c>toolchain/asset_import</c> 的同名检查，由一致性测试对照本清单）与测试都从这里取。
    /// <para>
    /// 判断记录：①"<c>attack</c>（每个武器族一段）"按基础键 <c>attack</c> 判定——基础键对任意武器族都可作回落，
    /// 逐族检查需要先知道"有哪些族"，而族是自由集合（单看一个键分不出武器族与变体），本版不逐族检查；
    /// ②"<c>attack</c> 二三段"按"任一键形如 <c>attack[.&lt;族&gt;].02/.03</c>"判定（武器族各有各的连招段数，重武器两段即止，
    /// 不能要求每族都有三段）；③"<c>wounded</c> 变体"按"任一键以 <c>.wounded</c> 结尾"判定；④启停过渡
    /// （<c>move.start/stop/pivot</c>）缺失的回落是"混合"（<c>start_blend_ms/stop_blend_ms</c>），不是回落到另一个键。
    /// </para>
    /// </summary>
    public static class PoseChecklist
    {
        private static bool IsAttackSegment(string key, string segment)
        {
            if (!key.StartsWith("attack.", StringComparison.Ordinal)) return false;
            return key.EndsWith("." + segment, StringComparison.Ordinal);
        }

        private const string NoFallback = "无可回落的键（该状态不播放，维持当前显示）";
        private const string BlendFallback = "回退为 start_blend_ms/stop_blend_ms 混合";

        private static readonly PoseChecklistEntry[] EntriesArray =
        {
            // 必备
            new PoseChecklistEntry("idle", PoseTier.Required, NoFallback),
            new PoseChecklistEntry("move.walk", PoseTier.Required, NoFallback),
            new PoseChecklistEntry("move.run", PoseTier.Required, NoFallback),
            new PoseChecklistEntry("attack", PoseTier.Required, NoFallback),
            new PoseChecklistEntry("hit", PoseTier.Required, NoFallback),
            new PoseChecklistEntry("death", PoseTier.Required, NoFallback),

            // 推荐
            new PoseChecklistEntry("idle.combat", PoseTier.Recommended, NoFallback),
            new PoseChecklistEntry("move.run.combat", PoseTier.Recommended, NoFallback),
            new PoseChecklistEntry("attack.02", PoseTier.Recommended, NoFallback, k => IsAttackSegment(k, "02")),
            new PoseChecklistEntry("attack.03", PoseTier.Recommended, NoFallback, k => IsAttackSegment(k, "03")),
            new PoseChecklistEntry("hit.heavy", PoseTier.Recommended, NoFallback),
            new PoseChecklistEntry("hit.knockback", PoseTier.Recommended, NoFallback),
            new PoseChecklistEntry("hit.knockdown", PoseTier.Recommended, NoFallback),
            new PoseChecklistEntry("hit.getup", PoseTier.Recommended, NoFallback),
            new PoseChecklistEntry("cast", PoseTier.Recommended, NoFallback),
            new PoseChecklistEntry("dodge", PoseTier.Recommended, NoFallback),
            new PoseChecklistEntry("jump", PoseTier.Recommended, NoFallback),

            // 可选
            new PoseChecklistEntry("move.sprint", PoseTier.Optional, NoFallback, fallbackFrom: "move.run"),
            new PoseChecklistEntry("move.walk.combat", PoseTier.Optional, NoFallback),
            new PoseChecklistEntry("move.start", PoseTier.Optional, BlendFallback),
            new PoseChecklistEntry("move.stop", PoseTier.Optional, BlendFallback),
            new PoseChecklistEntry("move.pivot", PoseTier.Optional, BlendFallback),
            new PoseChecklistEntry("hit.launch", PoseTier.Optional, NoFallback),
            new PoseChecklistEntry("stunned", PoseTier.Optional, NoFallback),
            new PoseChecklistEntry("block", PoseTier.Optional, NoFallback),
            new PoseChecklistEntry("wounded", PoseTier.Optional, NoFallback,
                k => k.EndsWith(".wounded", StringComparison.Ordinal)),
        };

        /// <summary>全部清单项（必备、推荐、可选的声明顺序）。</summary>
        public static IReadOnlyList<PoseChecklistEntry> Entries => EntriesArray;

        /// <summary>框架级姿势集 id 前缀（04 第 6.3 节）：以它开头的姿势集自动按清单校验。</summary>
        public const string FrameworkSetIdPrefix = "display.anim_set.std_";

        /// <summary>
        /// 对一组已声明键（可含旧键 <c>combat_&lt;state&gt;</c>；继承链合并后的键）求完整性报告。
        /// </summary>
        public static PoseChecklistReport Evaluate(IEnumerable<string> declaredKeys)
        {
            if (declaredKeys == null) throw new ArgumentNullException(nameof(declaredKeys));
            var raw = new HashSet<string>(declaredKeys, StringComparer.Ordinal);
            var canonical = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in raw)
            {
                canonical.Add(PoseKeys.Canonicalize(k));
            }

            bool Has(string key) => canonical.Contains(key);

            var required = new List<PoseChecklistFinding>();
            var recommended = new List<PoseChecklistFinding>();
            var optional = new List<PoseChecklistFinding>();

            foreach (var entry in EntriesArray)
            {
                var satisfied = Has(entry.Key);
                if (!satisfied && entry.AnyOf != null)
                {
                    foreach (var k in canonical)
                    {
                        if (entry.AnyOf(k))
                        {
                            satisfied = true;
                            break;
                        }
                    }
                }

                if (satisfied) continue;

                // 回落目标：从缺失键本身逐段去尾，第一个已声明的键（键本身缺失，所以从它的上一级开始）。
                string? fallback = null;
                if (entry.FallbackFrom != null)
                {
                    fallback = PoseResolver.FallbackTargetOf(entry.FallbackFrom, Has);
                }
                else if (PoseKeys.TryDropLastSegment(entry.Key, out var parent))
                {
                    fallback = PoseResolver.FallbackTargetOf(parent, Has);
                }

                var finding = new PoseChecklistFinding(entry, fallback);
                switch (entry.Tier)
                {
                    case PoseTier.Required: required.Add(finding); break;
                    case PoseTier.Recommended: recommended.Add(finding); break;
                    default: optional.Add(finding); break;
                }
            }

            return new PoseChecklistReport(required, recommended, optional);
        }

        /// <summary>该记录是否按标准清单校验：显式 <c>pose_standard: true</c>，或 id 以 <see cref="FrameworkSetIdPrefix"/> 开头。</summary>
        public static bool AppliesTo(string recordId, bool poseStandardFlag) =>
            poseStandardFlag || recordId.StartsWith(FrameworkSetIdPrefix, StringComparison.Ordinal);
    }
}
