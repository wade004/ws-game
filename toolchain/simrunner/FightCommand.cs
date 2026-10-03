using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Sim;

namespace Toolchain.SimRunner
{
    /// <summary>
    /// <c>simrunner fight</c> 子命令：直接指定"标准玩家（职业/等级/品质）对一只生物（模板/等级）打一场"，
    /// 用 <see cref="FightRunner.Run(FightRunnerOptions, CancellationToken, IProgress{SimProgress}?)"/> 跑一场，
    /// 可选把逐条战斗日志（<see cref="FightResult.CapturedEvents"/>）写成文件（<c>--fight-log</c>）。
    /// 消费方反馈第 49 条当时的限制是"命令行里没有指定两个模板打一场的入口"，本命令即补上它。
    /// <para>
    /// 判断记录（不并入 <c>run</c> 子命令）：<c>run</c> 的对象是 <c>sim.scenario</c> 场景（带基线比对、带宽、
    /// 退出码 0/1/3 的基线语义），一场自选对手的战斗没有基线可比，硬塞进去会让 <c>run</c> 的参数组合与退出码
    /// 语义混乱；独立子命令的契约更窄：退出码只有 0（跑完，不论胜负）/2（参数或数据装载错误）/4（Ctrl+C 取消）。
    /// 胜负只写在输出里，不映射退出码——"玩家输了"不是命令失败。
    /// </para>
    /// <para>
    /// 判断记录（不猜缺省）：<c>--quality</c> 必填。标准玩家的装备品质没有"天然缺省"，由调用方显式给；
    /// <c>--player-level</c> 缺省 1、<c>--creature-level</c> 缺省等于玩家等级、<c>--seed</c> 缺省 1、
    /// <c>--max-ticks</c> 缺省取 <see cref="FightRunnerOptions.MaxTicks"/> 的缺省值，这四个都有自然缺省。
    /// 同一参数（含种子）两次运行输出逐字节相同（<see cref="FightRunner"/> 的确定性承诺）。
    /// </para>
    /// <para>
    /// 判断记录（输出口径）：标准输出一行摘要 <c>fight outcome=… ticks=… …</c>；<c>--json</c> 时额外再打一份
    /// 聚合结果 JSON（不含逐条日志，日志只进文件，避免终端被刷屏）。<c>--fight-log</c> 的文件是 UTF-8 无 BOM
    /// 的 JSON 对象 <c>{schema_version, truncated, entries:[…]}</c>，条目字段与 <see cref="FightLogEntry"/> 一一对应，
    /// 类别名取枚举名的 snake_case；超过 <c>--max-log-entries</c> 时截断并把 <c>truncated</c> 置真，不报错。
    /// 传了 <c>--fight-log</c> 才开启 <see cref="FightRunnerOptions.CaptureEvents"/>，没传时零开销，聚合结果
    /// 与开启时逐字段一致（<see cref="FightRunner"/> 判断记录已承诺并有测试）。
    /// </para>
    /// </summary>
    internal static class FightCommand
    {
        internal const int LogSchemaVersion = 1;

        internal static string UsageText() =>
            "用法：dotnet run --project toolchain/simrunner -- fight --framework-root <dir> --data-root <dir> " +
            "[--data-root <dir2> ...] --class <arch.class.id> --quality <item.quality.id> --creature <creature.id> " +
            "[--player-level <n>] [--creature-level <n>] [--seed <n>] [--max-ticks <n>] " +
            "[--fight-log <file>] [--max-log-entries <n>] [--json]";

        internal static int Run(string[] args, CancellationToken cancellationToken)
        {
            string? frameworkRoot = null;
            var dataRoots = new List<string>();
            string? classId = null;
            string? qualityId = null;
            string? creatureId = null;
            var playerLevel = 1;
            int? creatureLevel = null;
            ulong seed = 1;
            int? maxTicks = null;
            string? fightLogPath = null;
            int? maxLogEntries = null;
            var jsonOutput = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--framework-root":
                        if (!TryTake(args, ref i, out frameworkRoot)) return ArgError("--framework-root 需要一个目录参数");
                        break;
                    case "--data-root":
                        if (!TryTake(args, ref i, out var dataRoot)) return ArgError("--data-root 需要一个目录参数");
                        dataRoots.Add(dataRoot!);
                        break;
                    case "--class":
                        if (!TryTake(args, ref i, out classId)) return ArgError("--class 需要一个职业 id");
                        break;
                    case "--quality":
                        if (!TryTake(args, ref i, out qualityId)) return ArgError("--quality 需要一个品质 id");
                        break;
                    case "--creature":
                        if (!TryTake(args, ref i, out creatureId)) return ArgError("--creature 需要一个生物模板 id");
                        break;
                    case "--player-level":
                        if (!TryTake(args, ref i, out var plText) || !TryPositiveInt(plText, out playerLevel))
                            return ArgError("--player-level 需要一个正整数");
                        break;
                    case "--creature-level":
                        if (!TryTake(args, ref i, out var clText) || !TryPositiveInt(clText, out var cl))
                            return ArgError("--creature-level 需要一个正整数");
                        creatureLevel = cl;
                        break;
                    case "--seed":
                        if (!TryTake(args, ref i, out var seedText) ||
                            !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                            return ArgError("--seed 需要一个非负整数");
                        break;
                    case "--max-ticks":
                        if (!TryTake(args, ref i, out var mtText) || !TryPositiveInt(mtText, out var mt))
                            return ArgError("--max-ticks 需要一个正整数");
                        maxTicks = mt;
                        break;
                    case "--fight-log":
                        if (!TryTake(args, ref i, out fightLogPath)) return ArgError("--fight-log 需要一个输出文件路径");
                        break;
                    case "--max-log-entries":
                        if (!TryTake(args, ref i, out var mlText) || !TryPositiveInt(mlText, out var ml))
                            return ArgError("--max-log-entries 需要一个正整数");
                        maxLogEntries = ml;
                        break;
                    case "--json":
                        jsonOutput = true;
                        break;
                    default:
                        return ArgError($"未知参数 \"{args[i]}\"");
                }
            }

            if (string.IsNullOrEmpty(frameworkRoot)) return ArgError("缺少必填参数 --framework-root <dir>");
            if (dataRoots.Count == 0) return ArgError("缺少必填参数 --data-root <dir>（可重复传入）");
            if (string.IsNullOrEmpty(classId)) return ArgError("缺少必填参数 --class <arch.class.id>");
            if (string.IsNullOrEmpty(qualityId)) return ArgError("缺少必填参数 --quality <item.quality.id>");
            if (string.IsNullOrEmpty(creatureId)) return ArgError("缺少必填参数 --creature <creature.id>");
            if (maxLogEntries.HasValue && fightLogPath == null) return ArgError("--max-log-entries 需要同时指定 --fight-log <file>");

            try
            {
                var fs = new DiskFileSystem();
                var dataSources = new List<IDataSource> { Program.BuildSource(fs, frameworkRoot!) };
                dataSources.AddRange(dataRoots.Select(root => Program.BuildSource(fs, root)));

                var options = new FightRunnerOptions
                {
                    DataSources = dataSources,
                    ClassId = new Id(classId!),
                    QualityId = new Id(qualityId!),
                    CreatureId = new Id(creatureId!),
                    PlayerLevel = playerLevel,
                    CreatureLevel = creatureLevel ?? playerLevel,
                    Seed = seed,
                    CaptureEvents = fightLogPath != null,
                };
                if (maxTicks.HasValue) options.MaxTicks = maxTicks.Value;
                if (maxLogEntries.HasValue) options.MaxCapturedEvents = maxLogEntries.Value;

                var result = FightRunner.Run(options, cancellationToken);

                if (fightLogPath != null)
                {
                    var full = Path.GetFullPath(fightLogPath);
                    var dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(full, BuildLogJson(result), new UTF8Encoding(false));
                }

                Console.WriteLine(SummaryLine(result));
                if (jsonOutput) Console.WriteLine(BuildResultJson(result));
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("已取消：收到 Ctrl+C 取消信号，这场战斗提前终止；不写战斗日志。");
                return 4;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DirectoryNotFoundException)
            {
                Console.Error.WriteLine("数据装载阻断：" + ex.Message);
                return 2;
            }
        }

        internal static string SummaryLine(FightResult r)
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Format(
                inv,
                "fight outcome={0} ticks={1} duration_s={2:0.###} player_damage={3:0.###} creature_damage={4:0.###} " +
                "player_hit_rate={5:0.####} creature_hit_rate={6:0.####} log_entries={7} log_truncated={8}",
                r.Outcome, r.TicksUsed, r.DurationSeconds, r.PlayerTotalDamage, r.CreatureTotalDamage,
                r.PlayerHitRate, r.CreatureHitRate, r.CapturedEvents.Count, r.CapturedEventsTruncated ? "true" : "false");
        }

        /// <summary>聚合结果 JSON（不含逐条日志）。无穷大（生物没造成伤害时的 TTD）写 null——JSON 没有无穷大。</summary>
        internal static string BuildResultJson(FightResult r)
        {
            var share = new JsonObjectBuilder();
            foreach (var kv in r.PlayerSkillDamageShare.OrderBy(kv => kv.Key.Value, StringComparer.Ordinal))
            {
                share.Add(kv.Key.Value, Num(kv.Value));
            }

            var root = new JsonObjectBuilder()
                .Add("outcome", new JsonString(r.Outcome.ToString()))
                .Add("duration_seconds", Num(r.DurationSeconds))
                .Add("ticks_used", new JsonNumber(r.TicksUsed))
                .Add("player_total_damage", Num(r.PlayerTotalDamage))
                .Add("creature_total_damage", Num(r.CreatureTotalDamage))
                .Add("player_dps", Num(r.PlayerDps))
                .Add("creature_dps", Num(r.CreatureDps))
                .Add("player_hit_rate", Num(r.PlayerHitRate))
                .Add("creature_hit_rate", Num(r.CreatureHitRate))
                .Add("player_max_health", Num(r.PlayerMaxHealth))
                .Add("ttd_estimate", Num(r.TtdEstimate))
                .Add("player_skill_damage_share", share.Build())
                .Add("log_entries", new JsonNumber(r.CapturedEvents.Count))
                .Add("log_truncated", r.CapturedEventsTruncated ? JsonBool.True : JsonBool.False)
                .Build();
            return JsonWriter.Write(root);
        }

        /// <summary>逐条战斗日志文件内容：<c>{schema_version, truncated, entries:[…]}</c>。</summary>
        internal static string BuildLogJson(FightResult r)
        {
            var entries = new List<JsonValue>(r.CapturedEvents.Count);
            foreach (var e in r.CapturedEvents)
            {
                entries.Add(new JsonObjectBuilder()
                    .Add("tick", new JsonNumber(e.Tick))
                    .Add("category", new JsonString(SnakeCase(e.Category.ToString())))
                    .Add("source_id", IdOrNull(e.SourceId))
                    .Add("target_id", IdOrNull(e.TargetId))
                    .Add("skill_or_effect_id", IdOrNull(e.SkillOrEffectId))
                    .Add("amount", e.Amount.HasValue ? Num(e.Amount.Value) : JsonNull.Instance)
                    .Add("result_tag", e.ResultTag != null ? new JsonString(e.ResultTag) : JsonNull.Instance)
                    .Build());
            }

            var root = new JsonObjectBuilder()
                .Add("schema_version", new JsonNumber(LogSchemaVersion))
                .Add("truncated", r.CapturedEventsTruncated ? JsonBool.True : JsonBool.False)
                .Add("entries", new JsonArray(entries))
                .Build();
            return JsonWriter.Write(root);
        }

        private static JsonValue Num(double value) =>
            double.IsFinite(value) ? new JsonNumber(value) : JsonNull.Instance;

        private static JsonValue IdOrNull(Id? id) => id.HasValue ? new JsonString(id.Value.Value) : JsonNull.Instance;

        private static string SnakeCase(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (var i = 0; i < pascal.Length; i++)
            {
                var c = pascal[i];
                if (char.IsUpper(c) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        private static bool TryTake(string[] args, ref int i, out string? value)
        {
            if (i + 1 >= args.Length)
            {
                value = null;
                return false;
            }

            value = args[++i];
            return true;
        }

        private static bool TryPositiveInt(string? text, out int value)
        {
            value = 0;
            return text != null &&
                   int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        private static int ArgError(string message)
        {
            Console.Error.WriteLine("参数错误：" + message);
            Console.Error.WriteLine(UsageText());
            return 2;
        }
    }
}
