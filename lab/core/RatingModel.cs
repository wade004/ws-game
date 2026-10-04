using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Core.Foundation.Common.Json;
using Core.Foundation.Feel;

namespace Lab
{
    /// <summary>评分维度（对应 <c>feel.validation</c> 的 <c>scores</c> 键，手感设计 06 第 6 节四个维度）。</summary>
    public static class RatingDimensions
    {
        public const string Move = "move";
        public const string Turn = "turn";
        public const string HitWeight = "hit_weight";
        public const string Combo = "combo";

        /// <summary>维度键，顺序同 <see cref="FeelMaturity.ScoreDimensions"/>。</summary>
        public static readonly IReadOnlyList<string> All = FeelMaturity.ScoreDimensions;

        public static string Label(string dimension)
        {
            switch (dimension)
            {
                case Move: return "移动";
                case Turn: return "转向";
                case HitWeight: return "命中重量";
                case Combo: return "连招衔接";
                default: return dimension;
            }
        }

        /// <summary>"慢/飘/软/黏/粘滞/拖沓/空挥"七个标签（06 第 4 节）。</summary>
        public static readonly IReadOnlyList<string> Tags = new[] { "慢", "飘", "软", "黏", "粘滞", "拖沓", "空挥" };
    }

    /// <summary>一次评分：四个维度各 1～5 分、标签与一句备注，连同打分时的 tick 与槽位。</summary>
    public sealed class RatingSample
    {
        public int Tick { get; }

        public char Slot { get; }

        public IReadOnlyDictionary<string, int> Scores { get; }

        public IReadOnlyList<string> Tags { get; }

        public string Note { get; }

        internal RatingSample(int tick, char slot, IReadOnlyDictionary<string, int> scores, IReadOnlyList<string> tags, string note)
        {
            Tick = tick;
            Slot = slot;
            Scores = scores;
            Tags = tags;
            Note = note;
        }
    }

    /// <summary>
    /// 评分记录的上下文（手感设计 06 第 4 节"评分"：连同预设版本、指纹哈希、设备条件存本地）。引擎侧宿主填设备条件与姿势集版本，
    /// 其余由 <see cref="RatingModel.BuildContext"/> 从会话读出。
    /// </summary>
    public sealed class RatingContext
    {
        /// <summary>被评分的档案行 id（当前槽位的基础预设）。</summary>
        public string ProfileRef { get; set; } = string.Empty;

        /// <summary>评分时该档案行的 <c>profile_version</c>（行缺省为 1）。</summary>
        public int ProfileVersion { get; set; } = 1;

        /// <summary>格子 id（实验室场景矩阵的格子）。</summary>
        public string Cell { get; set; } = string.Empty;

        /// <summary>数据根版本（本次数据集内容哈希）。</summary>
        public string DataRootVersion { get; set; } = string.Empty;

        /// <summary>姿势集版本（宿主给；缺省占位）。</summary>
        public string PoseSetVersion { get; set; } = "lab_placeholder_pose";

        /// <summary>设备条件自由文本（如 <c>pc_keyboard_mouse</c> 加显卡与帧率）。</summary>
        public string Device { get; set; } = string.Empty;

        /// <summary>评分日期 <c>yyyy-mm-dd</c>。</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>评分时刻会话逻辑指纹的哈希（见 <see cref="RatingModel.FingerprintHash"/>）。</summary>
        public string FingerprintHash { get; set; } = string.Empty;

        /// <summary>评分时有效的覆盖（人读摘要，面板里没有覆盖时为空）。</summary>
        public IReadOnlyList<string> Overrides { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 评分面板的视图模型与导出（ADR-0150；<c>feel.validation</c> 的表格式见 ADR-0146）。纯 C#：打分、标签、备注、本地保存、
    /// "导出为 feel.validation 记录"写出游戏仓库可直接提交的表文件。
    /// <para>
    /// 判断记录（汇总口径）：<c>feel.validation</c> 是"评分摘要"，一行对应一个档案行在一个格子的一次验证；面板里可以打多次分
    /// （不同试玩者、不同时刻），导出时各维度取全部评分的算术平均（保留两位小数）作为摘要，明细（逐次分数、标签、备注、覆盖摘要）只留在本地评分文件，
    /// 不入库（ADR-0146 决策 2）。导出的行 id 由调用方给定，同一个表文件里同 id 的行被替换，其余行保持。
    /// 升级门槛（各项不低于 4）由数据加载时的校验规则判定，面板导出不做判断也不改行的 <c>maturity</c>——
    /// 把档案行标成 validated 是游戏仓库里的人工决定，导出只提供证据。
    /// </para>
    /// </summary>
    public sealed class RatingModel
    {
        private readonly List<RatingSample> _samples = new List<RatingSample>();
        private readonly Dictionary<string, int> _current = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _tags = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>已提交的评分。</summary>
        public IReadOnlyList<RatingSample> Samples => _samples;

        public string Note { get; set; } = string.Empty;

        /// <summary>当前（尚未提交）某维度的分数；没打为 0。</summary>
        public int Score(string dimension) => _current.TryGetValue(dimension, out var v) ? v : 0;

        public IReadOnlyCollection<string> CurrentTags => _tags;

        /// <summary>四个维度都打了分才可提交。</summary>
        public bool Complete => RatingDimensions.All.All(d => Score(d) >= 1);

        public void Rate(string dimension, int score)
        {
            if (!RatingDimensions.All.Contains(dimension))
            {
                throw new ArgumentException("未知评分维度：" + dimension, nameof(dimension));
            }

            if (score < 1 || score > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(score), score, "评分必须在 1～5");
            }

            _current[dimension] = score;
        }

        public void ToggleTag(string tag)
        {
            if (!RatingDimensions.Tags.Contains(tag))
            {
                throw new ArgumentException("未知标签：" + tag, nameof(tag));
            }

            if (!_tags.Remove(tag))
            {
                _tags.Add(tag);
            }
        }

        /// <summary>把当前打的分、标签与备注提交成一条评分并清空草稿；没打全抛异常。</summary>
        public RatingSample Submit(int tick, char slot)
        {
            if (!Complete)
            {
                throw new InvalidOperationException("四个维度都要打分才能提交：" +
                    string.Join("、", RatingDimensions.All.Where(d => Score(d) < 1).Select(RatingDimensions.Label)));
            }

            var scores = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in RatingDimensions.All)
            {
                scores[d] = Score(d);
            }

            var tags = RatingDimensions.Tags.Where(_tags.Contains).ToList();
            var sample = new RatingSample(tick, slot, scores, tags, Note);
            _samples.Add(sample);
            _current.Clear();
            _tags.Clear();
            Note = string.Empty;
            return sample;
        }

        /// <summary>各维度全部评分的平均（两位小数）；没有评分返回空字典。</summary>
        public Dictionary<string, double> Summary()
        {
            var summary = new Dictionary<string, double>(StringComparer.Ordinal);
            if (_samples.Count == 0)
            {
                return summary;
            }

            foreach (var d in RatingDimensions.All)
            {
                summary[d] = Math.Round(_samples.Average(s => (double)s.Scores[d]), 2, MidpointRounding.AwayFromZero);
            }

            return summary;
        }

        // ---------------------------------------------------------------- 上下文

        /// <summary>
        /// 会话逻辑指纹的哈希：把会话脚本截到 <paramref name="tick"/>（前缀脚本，逻辑是因果的）经无头宿主重放，取逻辑组规范文本的 SHA-256 前 16 位十六进制。
        /// 同一段输入在同一数据与覆盖下哈希相同，可以拿它对账"评分对应的是哪一局"。
        /// </summary>
        public static string FingerprintHash(LabRunner runner, InputScript sessionScript, int tick, string cell)
        {
            var prefix = LabLive.PrefixScript(sessionScript, Math.Max(1, tick));
            var recording = runner.Record(prefix, cell);
            var text = runner.FingerprintOf(prefix, cell, recording).Project(runner.Registry, MetricClass.Logic);
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder();
                for (var i = 0; i < 8; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>从会话与调参面板读出上下文（预设与版本、覆盖摘要、数据根版本、格子、指纹哈希）；设备条件与日期由调用方补。</summary>
        public static RatingContext BuildContext(LabRunner runner, LabSession session, TuningPanel panel, string cell, string device, string date)
        {
            var presetId = panel.ActivePreset;
            var version = 1;
            var record = session.Context!.World.Registry.Get(FeelTables.Preset, new Core.Foundation.Common.Id(presetId));
            if (record != null && record.TryGetInt("profile_version", out var pv) && pv >= 1 && pv <= int.MaxValue)
            {
                version = (int)pv;
            }

            return new RatingContext
            {
                ProfileRef = presetId,
                ProfileVersion = version,
                Cell = cell,
                DataRootVersion = runner.DatasetHashFor(session.Script, cell),
                Device = device,
                Date = date,
                FingerprintHash = FingerprintHash(runner, session.Script, session.Tick, cell),
                Overrides = panel.ActiveWrites.Select(w => w.ToString()).ToList(),
            };
        }

        // ---------------------------------------------------------------- 本地保存与导出

        public const int LocalFormatVersion = 1;

        /// <summary>评分明细存本地文件（<paramref name="path"/> 整个重写；含上下文与逐次评分，不入库）。</summary>
        public void SaveLocal(string path, RatingContext context)
        {
            var samples = new List<JsonValue>();
            foreach (var s in _samples)
            {
                var scores = new JsonObjectBuilder();
                foreach (var d in RatingDimensions.All)
                {
                    scores.Add(d, LabJson.Num(s.Scores[d]));
                }

                samples.Add(new JsonObjectBuilder()
                    .Add("tick", LabJson.Num(s.Tick))
                    .Add("slot", LabJson.Str(s.Slot.ToString()))
                    .Add("scores", scores.Build())
                    .Add("tags", new JsonArray(s.Tags.Select(t => (JsonValue)LabJson.Str(t))))
                    .Add("note", LabJson.Str(s.Note))
                    .Build());
            }

            var summary = new JsonObjectBuilder();
            foreach (var pair in Summary())
            {
                summary.Add(pair.Key, LabJson.Num(pair.Value));
            }

            var root = new JsonObjectBuilder()
                .Add("formatVersion", LabJson.Num(LocalFormatVersion))
                .Add("profile_ref", LabJson.Str(context.ProfileRef))
                .Add("profile_version", LabJson.Num(context.ProfileVersion))
                .Add("cell", LabJson.Str(context.Cell))
                .Add("data_root_version", LabJson.Str(context.DataRootVersion))
                .Add("pose_set_version", LabJson.Str(context.PoseSetVersion))
                .Add("device", LabJson.Str(context.Device))
                .Add("date", LabJson.Str(context.Date))
                .Add("fingerprint_hash", LabJson.Str(context.FingerprintHash))
                .Add("overrides", new JsonArray(context.Overrides.Select(o => (JsonValue)LabJson.Str(o))))
                .Add("summary", summary.Build())
                .Add("samples", new JsonArray(samples))
                .Build();
            WriteText(path, LabJson.Write(root));
        }

        /// <summary>
        /// 导出为 <c>feel.validation</c> 记录（ADR-0146 的表格式）：写 <paramref name="directory"/>/feel/feel.validation.json（本地文件，供游戏仓库复制提交）；
        /// 同 id 的行替换，其余保持。返回写出的路径。没有评分抛异常。
        /// </summary>
        public string ExportValidation(string directory, string rowId, RatingContext context)
        {
            if (_samples.Count == 0)
            {
                throw new InvalidOperationException("还没有提交任何评分，无从导出");
            }

            if (!rowId.StartsWith(FeelTables.Validation + ".", StringComparison.Ordinal))
            {
                throw new ArgumentException("验证记录行 id 必须以 " + FeelTables.Validation + ". 开头：" + rowId, nameof(rowId));
            }

            var scores = new JsonObjectBuilder();
            foreach (var pair in Summary())
            {
                scores.Add(pair.Key, LabJson.Num(pair.Value));
            }

            var tagCount = _samples.SelectMany(s => s.Tags).GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key + "×" + g.Count()).ToList();
            var note = _samples.Count + " 次评分" + (tagCount.Count > 0 ? "；标签 " + string.Join("、", tagCount) : string.Empty)
                + (context.Overrides.Count > 0 ? "；评分时带 " + context.Overrides.Count + " 个调试覆盖" : string.Empty)
                + "；指纹 " + context.FingerprintHash + "（实验室调参面板导出）";
            var row = new JsonObjectBuilder()
                .Add("id", LabJson.Str(rowId))
                .Add("profile_ref", LabJson.Str(context.ProfileRef))
                .Add("profile_version", LabJson.Num(context.ProfileVersion))
                .Add("cell", LabJson.Str(context.Cell))
                .Add("data_root_version", LabJson.Str(context.DataRootVersion))
                .Add("pose_set_version", LabJson.Str(context.PoseSetVersion))
                .Add("device", LabJson.Str(context.Device))
                .Add("scores", scores.Build())
                .Add("date", LabJson.Str(context.Date))
                .Add("note", LabJson.Str(note))
                .Build();

            var path = Path.Combine(directory, "feel", FeelTables.Validation + ".json");
            var rows = new List<JsonValue>();
            if (File.Exists(path))
            {
                var existing = LabJson.ParseObject(File.ReadAllText(path, Encoding.UTF8), path);
                foreach (var item in LabJson.RequireArray(existing, "rows", path))
                {
                    if (!(item is JsonObject o && o.TryGetValue("id", out var idv) && idv is JsonString ids && ids.Value == rowId))
                    {
                        rows.Add(item);
                    }
                }
            }

            rows.Add(row);
            var table = new JsonObjectBuilder()
                .Add("table", LabJson.Str(FeelTables.Validation))
                .Add("schema_version", LabJson.Num(1))
                .Add("rows", new JsonArray(rows))
                .Build();
            WriteText(path, LabJson.Write(table));
            return path;
        }

        private static void WriteText(string path, string text)
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text, new UTF8Encoding(false));
        }
    }
}
