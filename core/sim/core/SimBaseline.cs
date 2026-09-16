using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Sim
{
    /// <summary>
    /// T-N6-6（ADR-0035 决策 5）：一份 <see cref="SimReport"/> 的既往快照——只保留"够用来做回归比对"
    /// 的最小信息（不含 <see cref="SimReport.RawReport"/>/<see cref="SimStat.Anchor"/>/
    /// <see cref="SimStat.Deviation"/>），文件格式为 <c>{schema_version, scenario_id, kind,
    /// dataset_fingerprint, generated_with_version, seed, stats: {path: value}}</c>（任务书原文
    /// 指定形状）。
    /// <para>
    /// <b>判断记录（不做磁盘路径解析）</b>：同 <c>HeadlessWorldOptions.DataSources</c>"数据来源必须
    /// 注入"的既有原则（见 <c>core/sim/README.md</c> 判断记录 2）——本类型只做"文本 ↔ 类型"转换
    /// （<see cref="Parse"/>/<see cref="ToJson"/>），不提供 <c>Load(path)</c>/<c>Save(path)</c> 之类
    /// 触碰磁盘的方法；读写基线文件是调用方（<c>toolchain/simrunner</c>）的职责，装配根/核心类型不
    /// 解析仓库路径。
    /// </para>
    /// </summary>
    public sealed class SimBaseline
    {
        public int SchemaVersion { get; }

        public Id ScenarioId { get; }

        public string Kind { get; }

        public string DatasetFingerprint { get; }

        public string GeneratedWithVersion { get; }

        public ulong Seed { get; }

        /// <summary>统计量路径 → 数值的映射（<see cref="SimStat.Path"/> → <see cref="SimStat.Value"/>）。</summary>
        public IReadOnlyDictionary<string, double> Stats { get; }

        public SimBaseline(
            int schemaVersion, Id scenarioId, string kind, string datasetFingerprint, string generatedWithVersion,
            ulong seed, IReadOnlyDictionary<string, double> stats)
        {
            SchemaVersion = schemaVersion;
            ScenarioId = scenarioId;
            Kind = kind;
            DatasetFingerprint = datasetFingerprint ?? throw new ArgumentNullException(nameof(datasetFingerprint));
            GeneratedWithVersion = generatedWithVersion ?? throw new ArgumentNullException(nameof(generatedWithVersion));
            Seed = seed;
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <summary>从一份 <see cref="SimReport"/> 抽出基线快照——只保留 <see cref="SimStat.Path"/>/
        /// <see cref="SimStat.Value"/>，丢弃 <see cref="SimReport.RawReport"/>/
        /// <see cref="SimStat.Anchor"/>/<see cref="SimStat.Deviation"/>（那些是数值设计层面的信息，
        /// 不是"这次代码跑出来的值"这一回归基线本身该记的东西）。</summary>
        public static SimBaseline FromReport(SimReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            var stats = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var stat in report.Stats)
            {
                stats[stat.Path] = stat.Value;
            }

            return new SimBaseline(
                SimReport.SchemaVersion, report.ScenarioId, report.Kind, report.DatasetFingerprint,
                report.GeneratedWithVersion, report.Seed, stats);
        }

        /// <summary>把本基线写成确定性 JSON 文本，惯例同 <see cref="ArenaReport.ToJson"/>
        /// （<see cref="Stats"/> 按键排序、浮点用不变量 <c>"R"</c> 格式）。</summary>
        public string ToJson()
        {
            var statsBuilder = new JsonObjectBuilder();
            foreach (var kv in Stats.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                statsBuilder.Add(kv.Key, ArenaReport.NumberOrNull(kv.Value));
            }

            var root = new JsonObjectBuilder()
                .Add("schema_version", new JsonNumber(SchemaVersion))
                .Add("scenario_id", new JsonString(ScenarioId.Value))
                .Add("kind", new JsonString(Kind))
                .Add("dataset_fingerprint", new JsonString(DatasetFingerprint))
                .Add("generated_with_version", new JsonString(GeneratedWithVersion))
                .Add("seed", new JsonNumber(Seed))
                .Add("stats", statsBuilder.Build())
                .Build();

            return JsonWriter.Write(root);
        }

        /// <summary>从 <see cref="ToJson"/> 产出的文本还原；<c>stats</c> 里非数字/缺失的字段按
        /// <see cref="double.NaN"/> 处理（与 <see cref="ArenaReport.NumberOrNull"/> 的 JSON
        /// <c>null</c> 往返惯例一致，不当作 0 用）。</summary>
        public static SimBaseline Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            var obj = (JsonObject)JsonReader.Parse(json);
            var schemaVersion = (int)((JsonNumber)obj["schema_version"]).Value;
            var scenarioId = new Id(((JsonString)obj["scenario_id"]).Value);
            var kind = ((JsonString)obj["kind"]).Value;
            var fingerprint = ((JsonString)obj["dataset_fingerprint"]).Value;
            var version = ((JsonString)obj["generated_with_version"]).Value;
            var seed = (ulong)((JsonNumber)obj["seed"]).Value;

            var stats = new Dictionary<string, double>(StringComparer.Ordinal);
            if (obj.TryGetValue("stats", out var statsVal) && statsVal is JsonObject statsObj)
            {
                foreach (var kv in statsObj)
                {
                    stats[kv.Key] = kv.Value is JsonNumber n ? n.Value : double.NaN;
                }
            }

            return new SimBaseline(schemaVersion, scenarioId, kind, fingerprint, version, seed, stats);
        }
    }
}
