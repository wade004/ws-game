using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// 注册表级的手感字段登记扩展（游戏自有字段的 schema 扩展位，手感设计/05 第 4 节，ADR-0146）：<see cref="FeelSchemas.RegisterAll"/>
    /// 传入游戏扩展后的登记时记在这份注册表上，校验规则在校验时才按注册表取字段集（规则按规则 id 去重、先注册者胜出，
    /// 字段集不能在构造时固化，否则框架目录与游戏的注册顺序会影响结果）。
    /// </summary>
    internal static class FeelFieldExtensions
    {
        private static readonly ConditionalWeakTable<object, FeelFieldSet> Sets = new ConditionalWeakTable<object, FeelFieldSet>();

        public static void Set(object registry, FeelFieldSet fields)
        {
            Sets.Remove(registry);
            Sets.Add(registry, fields);
        }

        public static bool Has(object registry) => Sets.TryGetValue(registry, out _);

        public static Func<IDataRegistryView, FeelFieldSet> Provider(FeelFieldSet fallback) =>
            view => Sets.TryGetValue(view, out var set) ? set : fallback;
    }

    /// <summary>
    /// 成熟度（手感设计/05 第 8 节、06 第 6 节，ADR-0146）的取值与升级门槛。框架自带的档案行永远是 <see cref="Experimental"/>
    /// （占位资源上的体验不得标 validated）；<see cref="Validated"/> 只属于游戏，且必须有 <c>feel.validation</c> 里的记录支撑。
    /// </summary>
    public static class FeelMaturity
    {
        public const string Experimental = "experimental";

        public const string Validated = "validated";

        /// <summary>升级门槛：评分摘要的各项都不低于该值（06 第 6 节；验收及格线是另一回事，见实验室验收清单）。</summary>
        public const double ScoreThreshold = 4.0;

        /// <summary>评分摘要的四个维度（06 第 6 节）：移动、转向、命中重量、连招衔接。</summary>
        public static readonly IReadOnlyList<string> ScoreDimensions = new[] { "move", "turn", "hit_weight", "combo" };

        /// <summary>档案行的 <c>profile_version</c>（缺省 1）。</summary>
        public static int ProfileVersionOf(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return record.TryGetInt("profile_version", out var v) && v >= 1 && v <= int.MaxValue ? (int)v : 1;
        }

        /// <summary>档案行是否标了 validated。</summary>
        public static bool IsMarkedValidated(DataRecord record) =>
            record != null && record.TryGetString("maturity", out var m) && string.Equals(m, Validated, StringComparison.Ordinal);
    }

    /// <summary>一条成熟度验证记录（<c>feel.validation</c> 的一行）。</summary>
    public sealed class FeelValidationRecord
    {
        public string Id { get; }

        public string ProfileRef { get; }

        public int ProfileVersion { get; }

        public string Cell { get; }

        public string DataRootVersion { get; }

        public string PoseSetVersion { get; }

        public string Device { get; }

        /// <summary>评分摘要，键为 <see cref="FeelMaturity.ScoreDimensions"/>；缺维度的记录不达标。</summary>
        public IReadOnlyDictionary<string, double> Scores { get; }

        public string Date { get; }

        public FeelValidationRecord(
            string id, string profileRef, int profileVersion, string cell, string dataRootVersion, string poseSetVersion,
            string device, IReadOnlyDictionary<string, double> scores, string date)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ProfileRef = profileRef ?? throw new ArgumentNullException(nameof(profileRef));
            ProfileVersion = profileVersion;
            Cell = cell ?? throw new ArgumentNullException(nameof(cell));
            DataRootVersion = dataRootVersion ?? throw new ArgumentNullException(nameof(dataRootVersion));
            PoseSetVersion = poseSetVersion ?? throw new ArgumentNullException(nameof(poseSetVersion));
            Device = device ?? throw new ArgumentNullException(nameof(device));
            Scores = scores ?? throw new ArgumentNullException(nameof(scores));
            Date = date ?? throw new ArgumentNullException(nameof(date));
        }

        /// <summary>四个维度都有评分且都不低于 <see cref="FeelMaturity.ScoreThreshold"/>。</summary>
        public bool MeetsThreshold
        {
            get
            {
                for (var i = 0; i < FeelMaturity.ScoreDimensions.Count; i++)
                {
                    if (!Scores.TryGetValue(FeelMaturity.ScoreDimensions[i], out var score) || !(score >= FeelMaturity.ScoreThreshold)) return false;
                }

                return true;
            }
        }
    }

    /// <summary>
    /// 成熟度验证账本（<c>feel.validation</c> 的内存形状）：回答"某个档案行在某个格子是否已 validated"。
    /// <para>
    /// 判断记录：覆盖的定义是四条同时成立——记录指向该行、记录的 <c>profile_version</c> 等于该行当前版本（档案改版即过期）、评分各项达标、
    /// 格子非空。档案行的 <c>maturity: validated</c> 是"至少在一个格子验证过"的标记，具体哪些格子已验证以本账本为准
    /// （06 第 1.1 节"一个预设可以在 <c>2_5d_action</c> 已验证而在 <c>3d_action</c> 仍是 experimental"）。
    /// </para>
    /// </summary>
    public sealed class FeelValidationLedger
    {
        public static readonly FeelValidationLedger Empty = new FeelValidationLedger(Array.Empty<FeelValidationRecord>());

        public IReadOnlyList<FeelValidationRecord> Records { get; }

        public FeelValidationLedger(IEnumerable<FeelValidationRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            Records = new List<FeelValidationRecord>(records).ToArray();
        }

        /// <summary>从注册表读取 <c>feel.validation</c>；形状不对的行被跳过（数据登记表的校验已经报告）。</summary>
        public static FeelValidationLedger FromRegistry(IDataRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!view.TryGetAll(FeelTables.Validation, out var rows) || rows.Count == 0) return Empty;

            var records = new List<FeelValidationRecord>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (!r.TryGetString("profile_ref", out var profileRef) || !r.TryGetString("cell", out var cell)) continue;
                var scores = new Dictionary<string, double>(StringComparer.Ordinal);
                if (r.TryGetObject("scores", out var obj))
                {
                    for (var d = 0; d < FeelMaturity.ScoreDimensions.Count; d++)
                    {
                        var name = FeelMaturity.ScoreDimensions[d];
                        if (obj.TryGetValue(name, out var value) && value is Core.Foundation.Common.Json.JsonNumber n) scores[name] = n.Value;
                    }
                }

                records.Add(new FeelValidationRecord(
                    r.Key, profileRef, r.TryGetInt("profile_version", out var pv) && pv >= 1 && pv <= int.MaxValue ? (int)pv : 1, cell,
                    r.TryGetString("data_root_version", out var dv) ? dv : string.Empty,
                    r.TryGetString("pose_set_version", out var ps) ? ps : string.Empty,
                    r.TryGetString("device", out var dev) ? dev : string.Empty,
                    scores,
                    r.TryGetString("date", out var date) ? date : string.Empty));
            }

            return new FeelValidationLedger(records);
        }

        /// <summary>覆盖指定档案行（指定当前版本）的记录，按记录 id 升序。</summary>
        public IReadOnlyList<FeelValidationRecord> Covering(string profileRef, int profileVersion)
        {
            var found = new List<FeelValidationRecord>();
            for (var i = 0; i < Records.Count; i++)
            {
                var r = Records[i];
                if (string.Equals(r.ProfileRef, profileRef, StringComparison.Ordinal) && r.ProfileVersion == profileVersion
                    && r.Cell.Length > 0 && r.MeetsThreshold)
                {
                    found.Add(r);
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return found;
        }

        /// <summary>指定档案行（当前版本）已验证的格子，去重、升序。</summary>
        public IReadOnlyList<string> ValidatedCells(string profileRef, int profileVersion)
        {
            var cells = new SortedSet<string>(StringComparer.Ordinal);
            var covering = Covering(profileRef, profileVersion);
            for (var i = 0; i < covering.Count; i++) cells.Add(covering[i].Cell);
            return new List<string>(cells).ToArray();
        }

        /// <summary>指定档案行（当前版本）在指定格子是否已 validated。</summary>
        public bool IsValidatedIn(string profileRef, int profileVersion, string cell)
        {
            var cells = ValidatedCells(profileRef, profileVersion);
            for (var i = 0; i < cells.Count; i++)
            {
                if (string.Equals(cells[i], cell, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
