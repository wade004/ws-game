using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Sim
{
    /// <summary>
    /// T-N6-6（ADR-0035 决策 5）：一条统计量——<see cref="SimReport.Stats"/> 平铺表的一行。
    /// <see cref="Path"/> 是稳定的点路径（如 <c>arena.L10.off+3.win_rate</c>/<c>growth.L5.gold</c>/
    /// <c>coverage.skill.&lt;id&gt;.budget_ratio</c>，见 <see cref="SimReport"/> 类型判断记录"路径命名
    /// 方案"），<see cref="Anchor"/>/<see cref="Deviation"/> 是"该统计量相对数值设计锚点/期望值的偏离"
    /// （仅部分统计量有意义——如对账等式行、成长轨迹行、覆盖仿真离群值行；纯探索性统计量如
    /// <c>win_rate</c> 恒为 <c>null</c>，不编造），与 <see cref="BaselineComparer"/>
    /// "当前值相对既往基线的偏离"是两件不同的事，互不混用（见该类型判断记录）。
    /// </summary>
    public sealed class SimStat
    {
        public string Path { get; }

        public double Value { get; }

        /// <summary>数值设计锚点/期望值；本统计量没有设计锚点时为 <c>null</c>。</summary>
        public double? Anchor { get; }

        /// <summary><see cref="Value"/> 相对 <see cref="Anchor"/> 的偏离（口径由产出方决定，通常是
        /// 相对偏离）；<see cref="Anchor"/> 为 <c>null</c> 时本字段恒为 <c>null</c>。</summary>
        public double? Deviation { get; }

        /// <summary>该统计量所属的等级；不适用（如按 id 索引而非按等级）时为 <c>null</c>。</summary>
        public int? Level { get; }

        public SimStat(string path, double value, double? anchor = null, double? deviation = null, int? level = null)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Value = value;
            Anchor = anchor;
            Deviation = deviation;
            Level = level;
        }

        internal JsonObject ToJson() => new JsonObjectBuilder()
            .Add("path", new JsonString(Path))
            .Add("value", ArenaReport.NumberOrNull(Value))
            .Add("anchor", Anchor.HasValue ? ArenaReport.NumberOrNull(Anchor.Value) : JsonNull.Instance)
            .Add("deviation", Deviation.HasValue ? ArenaReport.NumberOrNull(Deviation.Value) : JsonNull.Instance)
            .Add("level", Level.HasValue ? (JsonValue)new JsonNumber(Level.Value) : JsonNull.Instance)
            .Build();

        internal static SimStat FromJson(JsonObject obj)
        {
            var path = ((JsonString)obj["path"]).Value;
            var value = obj.TryGetValue("value", out var v) && v is JsonNumber vn ? vn.Value : double.NaN;
            double? anchor = obj.TryGetValue("anchor", out var a) && a is JsonNumber an ? an.Value : (double?)null;
            double? deviation = obj.TryGetValue("deviation", out var d) && d is JsonNumber dn ? dn.Value : (double?)null;
            int? level = obj.TryGetValue("level", out var l) && l is JsonNumber ln ? (int)ln.Value : (int?)null;
            return new SimStat(path, value, anchor, deviation, level);
        }
    }

    /// <summary>
    /// T-N6-6（ADR-0035 决策 5"报告为结构化产物"）：三类仿真报告（<see cref="ArenaReport"/>/
    /// <see cref="GrowthReport"/>/<see cref="CoverageReport"/>）统一的对外信封——场景 id + kind +
    /// 数据集指纹 + 框架版本字符串 + 种子 + 统计量平铺表（<see cref="Stats"/>）+ 原始报告
    /// （<see cref="RawReport"/>）。三个 <c>From*Report</c> 工厂方法把各自报告的强类型字段拍平成
    /// <see cref="SimStat"/> 列表，供 <see cref="BaselineComparer"/> 与 <c>toolchain/simrunner</c>
    /// 统一处理，不需要针对三种报告分别写比对逻辑。
    /// <para>
    /// <b>判断记录（路径命名方案）</b>：<c>arena.L{level}.off{+/-N}.{stat}</c>（每个越级矩阵格子一组，
    /// <c>{stat}</c> 直接取 <see cref="ArenaCellResult"/> 字段的 snake_case 名，技能占比另加
    /// <c>skill_share.{skillId}</c> 一段）；<c>arena.reconciliation.L{level}.{dps|hp|ttd}</c>（对账
    /// 等式行，<see cref="SimStat.Anchor"/>=TopDown、<see cref="SimStat.Deviation"/>=既有相对偏离）；
    /// <c>growth.L{level}.{level_duration|item_level|hit_rate|gold}</c>（叶子名与
    /// <c>ScenarioDef.Bandwidths</c> 的既有键名一一对应，见判断记录"叶子名对齐带宽键名"）与
    /// <c>growth.summary.*</c>（经验/金币双路径汇总，无等级、无锚点）；
    /// <c>coverage.{skill|item|creature}.{id}.{budget_ratio|ttk_seconds}</c>（叶子名按类别区分，
    /// 技能/装备统计量物理含义是预算比值，生物是 TTK 秒数，与 <see cref="CoverageOutlierRow"/>
    /// 类型注释"统一的离群值行形状"同一判断记录）、命中时另加 <c>.secondary_measurement</c> 一条。
    /// 全部路径经 <see cref="ToJson"/> 按 <see cref="StringComparer.Ordinal"/> 排序，保证同一份
    /// <see cref="Stats"/> 内容两次序列化逐字节相同。
    /// </para>
    /// <para>
    /// <b>判断记录（叶子名对齐带宽键名）</b>：<c>ScenarioDef.Bandwidths</c>（<c>sim.scenario
    /// .bandwidths</c>）登记的键名本就是"这一类统计量的设计容差"（如 growth 场景登记
    /// <c>level_duration</c>/<c>item_level</c>/<c>hit_rate</c>/<c>gold</c> 四个键，见
    /// <c>core/sim/tests/data/sim/sim.scenario.json</c>）——<see cref="BaselineComparer"/> 需要按
    /// "统计量名"从 <see cref="Bandwidths"/> 里查容差覆盖（任务书原文"容差来源优先级：场景
    /// bandwidths 中同名统计量……"），叶子名直接取这些既有键名（而不是
    /// <see cref="GrowthLevelSample"/> 字段本身的 C# 属性名，如 <c>ActualDurationSeconds</c>）能让
    /// "同名匹配"这一最简单、最直接的实现方式生效，不需要额外维护一张"字段名→带宽键名"映射表。
    /// </para>
    /// <para>
    /// <b>判断记录（<see cref="Anchor"/>/<see cref="Deviation"/> 与 <see cref="BaselineComparer"/>
    /// 的偏离是两件不同的事）</b>：本类型 <see cref="SimStat.Anchor"/>/<see cref="SimStat.Deviation"/>
    /// 回答"这次仿真跑出来的数值离数值设计的期望曲线有多远"（游戏平衡问题，见各报告类型既有的
    /// Reconciliation/Level/Outlier 字段），<see cref="BaselineComparer"/> 回答"这次仿真跑出来的
    /// 数值离上一次记录的基线有多远"（代码回归问题，同回放回归"比对事件流与既往基线"同一原理，见
    /// <c>core/gameplay/tests/Replay/README.md</c>）——两者数值来源完全独立（前者恒定不随代码改动
    /// 变化，后者就是用来发现代码改动引起的偏移），本类型把两者作为同一行的两个独立信息维度保留，
    /// 不做任何换算/合并。
    /// </para>
    /// </summary>
    public sealed class SimReport
    {
        public const int SchemaVersion = 1;

        public Id ScenarioId { get; }

        /// <summary><c>"arena"</c>/<c>"growth"</c>/<c>"coverage"</c>（<see cref="ScenarioKind"/> 的
        /// 小写字符串形式，与 <c>sim.scenario.kind</c> 原始取值一致）。</summary>
        public string Kind { get; }

        /// <summary>参与本次装载的全部数据行的稳定哈希，见 <see cref="ComputeDatasetFingerprint"/>。</summary>
        public string DatasetFingerprint { get; }

        /// <summary>调用方传入的框架版本字符串（本类型不解析、不校验，只如实转存，见任务书"读 VERSION
        /// 由调用方传入，报告里只存字符串"）。</summary>
        public string GeneratedWithVersion { get; }

        public ulong Seed { get; }

        /// <summary>本次运行是否用 <c>--runs</c> 覆盖了场景登记的 <c>runs</c>；未覆盖时为 <c>null</c>。</summary>
        public int? RunsOverride { get; }

        /// <summary>场景登记的 <c>bandwidths</c> 原样转存（<see cref="ScenarioDef.Bandwidths"/>），供
        /// <see cref="BaselineComparer"/> 解析容差覆盖时使用，不需要调用方额外传入场景对象。</summary>
        public IReadOnlyDictionary<string, double> Bandwidths { get; }

        /// <summary>按 <see cref="SimStat.Path"/> 排序前的平铺统计量列表；<see cref="ToJson"/> 内部
        /// 排序，本属性保持产出顺序供调用方按需再加工。</summary>
        public IReadOnlyList<SimStat> Stats { get; }

        /// <summary>原始报告（<see cref="ArenaReport"/>/<see cref="GrowthReport"/>/
        /// <see cref="CoverageReport"/> 各自 <c>ToJson()</c> 文本解析回的 <see cref="JsonValue"/> 树），
        /// 原样嵌入，供需要完整细节（如逐格胜率矩阵）而不满足于平铺统计量表的调用方使用。</summary>
        public JsonValue RawReport { get; }

        internal SimReport(
            Id scenarioId, string kind, string datasetFingerprint, string generatedWithVersion, ulong seed,
            int? runsOverride, IReadOnlyDictionary<string, double> bandwidths, IReadOnlyList<SimStat> stats,
            JsonValue rawReport)
        {
            ScenarioId = scenarioId;
            Kind = kind;
            DatasetFingerprint = datasetFingerprint;
            GeneratedWithVersion = generatedWithVersion;
            Seed = seed;
            RunsOverride = runsOverride;
            Bandwidths = bandwidths;
            Stats = stats;
            RawReport = rawReport;
        }

        public static SimReport FromArenaReport(
            ArenaReport report, ScenarioDef scenario, IDataRegistryView registry, string generatedWithVersion,
            int? runsOverride = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var stats = ExtractArenaStats(report);
            return new SimReport(
                scenario.Id, "arena", ComputeDatasetFingerprint(registry), generatedWithVersion, report.BaseSeed,
                runsOverride, scenario.Bandwidths, stats, JsonReader.Parse(report.ToJson()));
        }

        public static SimReport FromGrowthReport(
            GrowthReport report, ScenarioDef scenario, IDataRegistryView registry, string generatedWithVersion,
            int? runsOverride = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var stats = ExtractGrowthStats(report);
            return new SimReport(
                scenario.Id, "growth", ComputeDatasetFingerprint(registry), generatedWithVersion, report.BaseSeed,
                runsOverride, scenario.Bandwidths, stats, JsonReader.Parse(report.ToJson()));
        }

        public static SimReport FromCoverageReport(
            CoverageReport report, ScenarioDef scenario, IDataRegistryView registry, string generatedWithVersion,
            int? runsOverride = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var stats = ExtractCoverageStats(report);
            return new SimReport(
                scenario.Id, "coverage", ComputeDatasetFingerprint(registry), generatedWithVersion, report.BaseSeed,
                runsOverride, scenario.Bandwidths, stats, JsonReader.Parse(report.ToJson()));
        }

        /// <summary>把本报告写成确定性 JSON 文本，惯例同 <see cref="ArenaReport.ToJson"/>——同一份
        /// <see cref="Stats"/> 内容两次调用逐字节相同（<see cref="Stats"/> 先按
        /// <see cref="SimStat.Path"/> 用 <see cref="StringComparer.Ordinal"/> 排序，<see cref="Bandwidths"/>
        /// 同样按键排序）。</summary>
        public string ToJson()
        {
            var statsArray = Stats.OrderBy(s => s.Path, StringComparer.Ordinal).Select(s => (JsonValue)s.ToJson()).ToList();

            var bandwidthsBuilder = new JsonObjectBuilder();
            foreach (var kv in Bandwidths.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                bandwidthsBuilder.Add(kv.Key, new JsonNumber(kv.Value));
            }

            var root = new JsonObjectBuilder()
                .Add("schema_version", new JsonNumber(SchemaVersion))
                .Add("scenario_id", new JsonString(ScenarioId.Value))
                .Add("kind", new JsonString(Kind))
                .Add("dataset_fingerprint", new JsonString(DatasetFingerprint))
                .Add("generated_with_version", new JsonString(GeneratedWithVersion))
                .Add("seed", new JsonNumber(Seed))
                .Add("runs_override", RunsOverride.HasValue ? (JsonValue)new JsonNumber(RunsOverride.Value) : JsonNull.Instance)
                .Add("bandwidths", bandwidthsBuilder.Build())
                .Add("stats", new JsonArray(statsArray))
                .Add("raw_report", RawReport)
                .Build();

            return JsonWriter.Write(root);
        }

        private static List<SimStat> ExtractArenaStats(ArenaReport report)
        {
            var stats = new List<SimStat>();
            foreach (var cell in report.Cells)
            {
                var prefix = $"arena.L{cell.PlayerLevel.ToString(CultureInfo.InvariantCulture)}.off{FormatOffset(cell.LevelOffset)}";
                stats.Add(new SimStat($"{prefix}.win_rate", cell.WinRate, level: cell.PlayerLevel));
                stats.Add(new SimStat($"{prefix}.ttk_mean_seconds", cell.TtkMeanSeconds, level: cell.PlayerLevel));
                stats.Add(new SimStat($"{prefix}.ttk_median_seconds", cell.TtkMedianSeconds, level: cell.PlayerLevel));
                stats.Add(new SimStat($"{prefix}.ttk_p10_seconds", cell.TtkP10Seconds, level: cell.PlayerLevel));
                stats.Add(new SimStat($"{prefix}.ttk_p90_seconds", cell.TtkP90Seconds, level: cell.PlayerLevel));
                stats.Add(new SimStat($"{prefix}.ttd_estimate_mean", cell.TtdEstimateMean, level: cell.PlayerLevel));
                stats.Add(new SimStat($"{prefix}.player_hit_rate_mean", cell.PlayerHitRateMean, level: cell.PlayerLevel));
                stats.Add(new SimStat($"{prefix}.player_dps_mean", cell.PlayerDpsMean, level: cell.PlayerLevel));
                stats.Add(new SimStat($"{prefix}.player_max_health", cell.PlayerMaxHealth, level: cell.PlayerLevel));

                foreach (var kv in cell.SkillShareMean.OrderBy(kv => kv.Key.Value, StringComparer.Ordinal))
                {
                    stats.Add(new SimStat($"{prefix}.skill_share.{kv.Key.Value}", kv.Value, level: cell.PlayerLevel));
                }
            }

            foreach (var row in report.Reconciliation)
            {
                stats.Add(new SimStat(
                    $"arena.reconciliation.L{row.Level.ToString(CultureInfo.InvariantCulture)}.dps",
                    row.DpsBottomUp, anchor: row.DpsTopDown, deviation: row.DpsDeviation, level: row.Level));
                stats.Add(new SimStat(
                    $"arena.reconciliation.L{row.Level.ToString(CultureInfo.InvariantCulture)}.hp",
                    row.HpBottomUp, anchor: row.HpTopDown, deviation: row.HpDeviation, level: row.Level));
                stats.Add(new SimStat(
                    $"arena.reconciliation.L{row.Level.ToString(CultureInfo.InvariantCulture)}.ttd",
                    row.TtdBottomUp, anchor: row.TtdTopDown, deviation: row.TtdDeviation, level: row.Level));
            }

            return stats;
        }

        private static List<SimStat> ExtractGrowthStats(GrowthReport report)
        {
            var stats = new List<SimStat>();
            foreach (var level in report.Levels)
            {
                var prefix = $"growth.L{level.Level.ToString(CultureInfo.InvariantCulture)}";
                stats.Add(new SimStat($"{prefix}.kills", level.Kills, level: level.Level));
                stats.Add(new SimStat(
                    $"{prefix}.level_duration", level.ActualDurationSeconds,
                    anchor: level.ExpectedDurationSeconds, deviation: level.DurationDeviation, level: level.Level));
                stats.Add(new SimStat(
                    $"{prefix}.item_level", level.AvgItemLevel,
                    anchor: level.ExpectedItemLevel, deviation: level.ItemLevelDeviation, level: level.Level));
                stats.Add(new SimStat(
                    $"{prefix}.hit_rate", level.HitRate,
                    anchor: level.ExpectedHitRate, deviation: level.HitRateDeviation, level: level.Level));
                stats.Add(new SimStat(
                    $"{prefix}.gold", level.CumulativeGold,
                    anchor: level.ExpectedCumulativeGold, deviation: level.GoldDeviation, level: level.Level));
            }

            stats.Add(new SimStat("growth.summary.cumulative_xp_granted_via_api", report.CumulativeXpGrantedViaApi));
            stats.Add(new SimStat("growth.summary.cumulative_xp_granted_via_events", report.CumulativeXpGrantedViaEvents));
            stats.Add(new SimStat("growth.summary.cumulative_gold_via_balance", report.CumulativeGoldViaBalance));
            stats.Add(new SimStat("growth.summary.cumulative_gold_via_events", report.CumulativeGoldViaEvents));
            return stats;
        }

        private static List<SimStat> ExtractCoverageStats(CoverageReport report)
        {
            var stats = new List<SimStat>();
            AddCoverageRows(stats, report.Skills, "skill", "budget_ratio");
            AddCoverageRows(stats, report.Items, "item", "budget_ratio");
            AddCoverageRows(stats, report.Creatures, "creature", "ttk_seconds");
            return stats;
        }

        private static void AddCoverageRows(
            List<SimStat> stats, IReadOnlyList<CoverageOutlierRow> rows, string category, string statName)
        {
            foreach (var row in rows)
            {
                var prefix = $"coverage.{category}.{row.Id.Value}";
                stats.Add(new SimStat($"{prefix}.{statName}", row.Statistic, anchor: row.Baseline, deviation: row.Deviation));
                if (row.SecondaryMeasurement.HasValue)
                {
                    stats.Add(new SimStat($"{prefix}.secondary_measurement", row.SecondaryMeasurement.Value));
                }
            }
        }

        private static string FormatOffset(int offset) =>
            (offset >= 0 ? "+" : "") + offset.ToString(CultureInfo.InvariantCulture);

        // 判断记录：FNV-1a 64 位算法选型同 core/foundation/save_system/contracts/Replay.cs
        // WorldSnapshot.Capture 的确定性摘要同款算法（见该文件"digest（事件流+存活实体状态的 FNV-1a
        // 64 位确定性摘要）"）——只是同款选型，不复用其实现：该常量/方法是 Core.Foundation.SaveSystem
        // 内部私有实现细节，Core.Sim 不依赖该模块内部类型，这里独立写一份同算法的最小版本。逐字段之间
        // 额外过一次 0 字节分隔（同源文件 FnvCombine 惯例），避免"table+key"与"key+table"两种不同
        // 输入序列在字符串直接拼接时产生同一份哈希输入文本的边界歧义。
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>对 <paramref name="registry"/> 已加载的全部表、全部记录做稳定哈希——按表名
        /// （<see cref="StringComparer.Ordinal"/>）、表内按记录 <see cref="DataRecord.Key"/>
        /// （同样按 Ordinal）排序后，逐条把"表名 + 记录 key + 记录原始 JSON 文本"喂入 FNV-1a 64
        /// 累加，与加载顺序、数据来源根的物理路径无关——同一份数据内容（哪怕来自不同的
        /// <c>--data-root</c> 拼法或文件切分方式）产生同一份指纹，见 <see cref="SimReport"/> 类型
        /// 判断记录"路径命名方案"引用的任务书原文"对参与装载的全部数据行做稳定哈希"。</summary>
        public static string ComputeDatasetFingerprint(IDataRegistryView registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var hash = FnvOffsetBasis;
            foreach (var table in registry.Tables.OrderBy(t => t, StringComparer.Ordinal))
            {
                hash = FnvCombine(hash, table);
                foreach (var record in registry.GetAll(table).OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    hash = FnvCombine(hash, record.Key);
                    hash = FnvCombine(hash, JsonWriter.Write(record.Raw));
                }
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static ulong FnvCombine(ulong hash, string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            for (var i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }

            // 字段分隔哨兵字节（同 Replay.cs FnvCombine 惯例），见类型判断记录。
            hash ^= 0;
            hash *= FnvPrime;
            return hash;
        }
    }
}
