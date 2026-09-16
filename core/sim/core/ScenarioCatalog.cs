using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Sim
{
    /// <summary>T-N6-2a：<c>sim.scenario</c> 的 <c>kind</c> 字段（<see cref="SimSchemas.Scenario"/>
    /// 登记的三值枚举）对应的强类型枚举，供 <see cref="ScenarioCatalog.ByKind"/> 与调用方按类型分派
    /// （ADR-0035 决策 3：arena=战斗仿真、growth=成长仿真、coverage=内容覆盖仿真）。</summary>
    public enum ScenarioKind
    {
        Arena,
        Growth,
        Coverage,
    }

    /// <summary>T-N6-2a：<c>sim.scenario.player</c> 子结构的强类型只读视图。</summary>
    public readonly struct ScenarioPlayerSpec
    {
        public Id ClassId { get; }

        public int Level { get; }

        public Id? QualityId { get; }

        public Id? RaceId { get; }

        internal ScenarioPlayerSpec(Id classId, int level, Id? qualityId, Id? raceId)
        {
            ClassId = classId;
            Level = level;
            QualityId = qualityId;
            RaceId = raceId;
        }
    }

    /// <summary>T-N6-2a：<c>sim.scenario.opponent</c> 子结构的强类型只读视图。<see cref="Level"/> 与
    /// <see cref="LevelOffsets"/> 至多一个非空（<see cref="SimScenarioValidationRule"/> 加载期已保证，
    /// 见该类型判断记录）。</summary>
    public readonly struct ScenarioOpponentSpec
    {
        public Id CreatureId { get; }

        public Id? TierId { get; }

        /// <summary>对手出生等级；为 <c>null</c> 时按惯例等于 <see cref="ScenarioPlayerSpec.Level"/>
        /// （同级战斗），具体取默认值的逻辑由仿真运行器实现，本类型只如实转发数据原值。</summary>
        public int? Level { get; }

        /// <summary>越级矩阵仿真的等级差集合（对手等级－玩家等级），未登记时为空列表。</summary>
        public IReadOnlyList<int> LevelOffsets { get; }

        internal ScenarioOpponentSpec(Id creatureId, Id? tierId, int? level, IReadOnlyList<int> levelOffsets)
        {
            CreatureId = creatureId;
            TierId = tierId;
            Level = level;
            LevelOffsets = levelOffsets;
        }
    }

    /// <summary>T-N6-2a：<c>sim.scenario</c> 一行的强类型只读视图。</summary>
    public sealed class ScenarioDef
    {
        public Id Id { get; }

        public ScenarioKind Kind { get; }

        public ScenarioPlayerSpec Player { get; }

        public ScenarioOpponentSpec Opponent { get; }

        /// <summary>本场景要覆盖的玩家等级集合（<c>kind</c>=<see cref="ScenarioKind.Arena"/>/
        /// <see cref="ScenarioKind.Coverage"/> 时非空，加载期已由 <see cref="SimScenarioValidationRule"/>
        /// 保证，见该类型判断记录）；未登记时为空列表。</summary>
        public IReadOnlyList<int> Levels { get; }

        /// <summary>成长仿真起始/终止等级（<c>kind</c>=<see cref="ScenarioKind.Growth"/> 时均非
        /// <c>null</c> 且 <see cref="LevelFrom"/> &lt;= <see cref="LevelTo"/>，加载期已保证）。</summary>
        public int? LevelFrom { get; }

        public int? LevelTo { get; }

        public int Runs { get; }

        public ulong BaseSeed { get; }

        public int MaxTicks { get; }

        /// <summary>统计量名 → 相对带宽 [0,1] 的映射（数值总纲第 5 节对账等式）。</summary>
        public IReadOnlyDictionary<string, double> Bandwidths { get; }

        /// <summary>预留字段原值（见 <see cref="SimSchemas.Scenario"/> 判断记录），本版本恒为
        /// <c>null</c> 或调用方自行解释，本类型不赋予它任何语义。</summary>
        public string? AnchorRef { get; }

        public string? Note { get; }

        internal ScenarioDef(
            Id id, ScenarioKind kind, ScenarioPlayerSpec player, ScenarioOpponentSpec opponent,
            IReadOnlyList<int> levels, int? levelFrom, int? levelTo, int runs, ulong baseSeed,
            int maxTicks, IReadOnlyDictionary<string, double> bandwidths, string? anchorRef, string? note)
        {
            Id = id;
            Kind = kind;
            Player = player;
            Opponent = opponent;
            Levels = levels;
            LevelFrom = levelFrom;
            LevelTo = levelTo;
            Runs = runs;
            BaseSeed = baseSeed;
            MaxTicks = maxTicks;
            Bandwidths = bandwidths;
            AnchorRef = anchorRef;
            Note = note;
        }

        /// <summary>T-N6-6：返回一份仅 <see cref="Runs"/> 被替换、其余字段原样保留的副本——供
        /// <c>toolchain/simrunner</c> 的 <c>--runs</c> 快速冒烟覆盖使用（任务书原文"<c>--runs</c> 覆盖
        /// 场景 <c>runs</c>（仅用于快速冒烟）"）。ABI：纯新增公开方法，不改动
        /// <see cref="ScenarioDef"/> 既有任何成员。<see cref="ScenarioDef"/> 构造函数是
        /// <c>internal</c>（只应由 <see cref="ScenarioCatalog"/> 从已加载数据解析产出，见类型判断
        /// 记录），调用方（不同程序集，如 <c>Toolchain.SimRunner</c>）因此无法自行拼一个"改了某个
        /// 字段"的 <see cref="ScenarioDef"/>——本方法是唯一开放的、显式声明用途的例外口子，只允许
        /// 覆盖 <see cref="Runs"/> 这一个字段。</summary>
        public ScenarioDef WithRuns(int runs) => new ScenarioDef(
            Id, Kind, Player, Opponent, Levels, LevelFrom, LevelTo, runs, BaseSeed, MaxTicks, Bandwidths, AnchorRef, Note);
    }

    /// <summary>
    /// T-N6-2a：<c>sim.scenario</c> 的类型化只读读取（ADR-0035 决策 4），惯例同 <see cref="AnchorTable"/>——
    /// 构造期一次性把 <see cref="DataRecord"/> 解析为强类型 <see cref="ScenarioDef"/>，后续只读查表。
    /// </summary>
    public sealed class ScenarioCatalog
    {
        private readonly List<ScenarioDef> _all;
        private readonly Dictionary<string, ScenarioDef> _byId;

        /// <summary>从已加载的 <paramref name="registry"/> 读取 <c>sim.scenario</c> 全部记录并解析为
        /// 类型化场景。<paramref name="registry"/> 必须已通过 <see cref="IDataRegistry.LoadAll()"/>/
        /// <see cref="IDataRegistry.LoadAll(IReadOnlyList{IDataSource})"/>。</summary>
        public ScenarioCatalog(IDataRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            _all = new List<ScenarioDef>();
            _byId = new Dictionary<string, ScenarioDef>(StringComparer.Ordinal);

            foreach (var record in registry.GetAll("sim.scenario"))
            {
                var def = ParseScenario(record);
                _all.Add(def);
                _byId[def.Id.Value] = def;
            }
        }

        /// <summary>全部已登记场景，按数据加载顺序（<see cref="IDataRegistryView.GetAll"/> 的返回顺序）。</summary>
        public IReadOnlyList<ScenarioDef> All => _all;

        public bool TryGet(Id id, out ScenarioDef scenario) => _byId.TryGetValue(id.Value, out scenario!);

        /// <summary>按 id 取场景；不存在时抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
        public ScenarioDef Get(Id id)
        {
            if (!TryGet(id, out var scenario))
            {
                throw new ArgumentOutOfRangeException(nameof(id), $"sim.scenario 未登记 id={id}");
            }

            return scenario;
        }

        /// <summary>按 <see cref="ScenarioKind"/> 过滤，保持 <see cref="All"/> 的相对顺序。</summary>
        public IReadOnlyList<ScenarioDef> ByKind(ScenarioKind kind) =>
            _all.Where(s => s.Kind == kind).ToList();

        private static ScenarioDef ParseScenario(DataRecord record)
        {
            var kind = ParseKind(record.GetString("kind"));
            var player = ParsePlayer(record.GetObject("player"));
            var opponent = ParseOpponent(record.GetObject("opponent"));

            var levels = record.TryGetArray("levels", out var levelsArr)
                ? levelsArr.OfType<JsonNumber>().Select(n => (int)n.Value).ToList()
                : (IReadOnlyList<int>)Array.Empty<int>();

            int? levelFrom = record.TryGetInt("level_from", out var lf) ? (int)lf : (int?)null;
            int? levelTo = record.TryGetInt("level_to", out var lt) ? (int)lt : (int?)null;

            var bandwidths = new Dictionary<string, double>(StringComparer.Ordinal);
            if (record.TryGetObject("bandwidths", out var bandwidthsObj))
            {
                foreach (var kv in bandwidthsObj)
                {
                    if (kv.Value is JsonNumber num)
                    {
                        bandwidths[kv.Key] = num.Value;
                    }
                }
            }

            return new ScenarioDef(
                record.Id!.Value,
                kind,
                player,
                opponent,
                levels,
                levelFrom,
                levelTo,
                runs: (int)record.GetInt("runs"),
                baseSeed: (ulong)record.GetInt("base_seed"),
                maxTicks: (int)record.GetInt("max_ticks"),
                bandwidths: bandwidths,
                anchorRef: record.TryGetString("anchor_ref", out var anchorRef) ? anchorRef : null,
                note: record.TryGetString("note", out var note) ? note : null);
        }

        private static ScenarioKind ParseKind(string kind) => kind switch
        {
            "arena" => ScenarioKind.Arena,
            "growth" => ScenarioKind.Growth,
            "coverage" => ScenarioKind.Coverage,
            // SimSchemas.Scenario 把 kind 登记为 FieldKind.Enum（合法取值仅这三个），加载期已阻断
            // 其它取值，走不到这里；防御性保留，避免调用方绕过校验直接注入数据时收到语义不明的行为。
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "sim.scenario.kind 取值必须是 arena/growth/coverage 之一"),
        };

        private static ScenarioPlayerSpec ParsePlayer(JsonObject player)
        {
            var classId = new Id(RequireString(player, "class_id"));
            var level = (int)RequireNumber(player, "level");
            var qualityId = TryGetIdField(player, "quality_id");
            var raceId = TryGetIdField(player, "race_id");
            return new ScenarioPlayerSpec(classId, level, qualityId, raceId);
        }

        private static ScenarioOpponentSpec ParseOpponent(JsonObject opponent)
        {
            var creatureId = new Id(RequireString(opponent, "creature_id"));
            var tierId = TryGetIdField(opponent, "tier_id");
            int? level = opponent.TryGetValue("level", out var levelVal) && levelVal is JsonNumber levelNum
                ? (int)levelNum.Value
                : (int?)null;
            var levelOffsets = opponent.TryGetValue("level_offsets", out var offsetsVal) && offsetsVal is JsonArray offsetsArr
                ? offsetsArr.OfType<JsonNumber>().Select(n => (int)n.Value).ToList()
                : (IReadOnlyList<int>)Array.Empty<int>();
            return new ScenarioOpponentSpec(creatureId, tierId, level, levelOffsets);
        }

        private static Id? TryGetIdField(JsonObject obj, string field) =>
            obj.TryGetValue(field, out var v) && v is JsonString s ? new Id(s.Value) : (Id?)null;

        private static string RequireString(JsonObject obj, string field) =>
            obj.TryGetValue(field, out var v) && v is JsonString s ? s.Value
                : throw new DataFieldException("sim.scenario", "<sub-object>", field, "字段缺失或不是字符串");

        private static double RequireNumber(JsonObject obj, string field) =>
            obj.TryGetValue(field, out var v) && v is JsonNumber n ? n.Value
                : throw new DataFieldException("sim.scenario", "<sub-object>", field, "字段缺失或不是数值");
    }
}
