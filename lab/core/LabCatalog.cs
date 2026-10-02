using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Lab
{
    /// <summary>竞技场里一块轴对齐阻挡矩形。</summary>
    public sealed class LabArenaBlock
    {
        public string Name { get; }

        public string Kind { get; }

        public Vec2 Min { get; }

        public Vec2 Max { get; }

        public LabArenaBlock(string name, string kind, Vec2 min, Vec2 max)
        {
            Name = name;
            Kind = kind;
            Min = min;
            Max = max;
        }
    }

    /// <summary><c>lab.arena</c> 一行的类型化形态。</summary>
    public sealed class LabArena
    {
        public Id Id { get; }

        public Id MapId { get; }

        public IReadOnlyList<LabArenaBlock> Blocks { get; }

        public LabArena(Id id, Id mapId, IReadOnlyList<LabArenaBlock> blocks)
        {
            Id = id;
            MapId = mapId;
            Blocks = blocks;
        }
    }

    /// <summary><c>lab.dummy_set</c> 的一个靶子条目。</summary>
    public sealed class LabDummy
    {
        public string Name { get; }

        public string Kind { get; }

        public Id CreatureId { get; }

        public Vec2 Position { get; }

        public string Group { get; }

        public int Count { get; }

        public double Spacing { get; }

        public bool KeepAi { get; }

        /// <summary>出生脚下高度（世界单位，缺省 0）；只在有竖直轴的格子（side_2d/volume）生效。</summary>
        public double Height { get; }

        public LabDummy(string name, string kind, Id creatureId, Vec2 position, string group, int count, double spacing, bool keepAi, double height = 0.0)
        {
            Name = name;
            Kind = kind;
            CreatureId = creatureId;
            Position = position;
            Group = group;
            Count = count;
            Spacing = spacing;
            KeepAi = keepAi;
            Height = height;
        }
    }

    /// <summary><c>lab.dummy_set</c> 一行的类型化形态。</summary>
    public sealed class LabDummySet
    {
        public Id Id { get; }

        public IReadOnlyList<LabDummy> Entries { get; }

        public LabDummySet(Id id, IReadOnlyList<LabDummy> entries)
        {
            Id = id;
            Entries = entries;
        }
    }

    /// <summary>
    /// <c>lab.scenario</c> 一行（场景矩阵的一个格子）的类型化形态。字段含义见
    /// <c>core/sim/schema/LabSchemas.cs</c> 与 06 第 1.1 节。
    /// </summary>
    public sealed class LabScenario
    {
        public Id Id { get; }

        /// <summary>"2d_targeted" 之类的短名（去掉 <c>lab.scenario.</c> 前缀）。</summary>
        public string Cell { get; }

        public string Space { get; }

        public string Form { get; }

        public string CameraMode { get; }

        public string ControlSpace { get; }

        public string Facing { get; }

        public string HitShape { get; }

        public string DefaultPreset { get; }

        public IReadOnlyList<string> DefaultWeapons { get; }

        public Id DummySetId { get; }

        public IReadOnlyList<string> ScriptSubset { get; }

        public IReadOnlyList<string> RequiredCapabilities { get; }

        public Id ArenaId { get; }

        public string Settlement { get; }

        /// <summary>输入动作 id → 技能 id（按键序，保证遍历稳定）。</summary>
        public IReadOnlyList<KeyValuePair<string, Id>> SkillBindings { get; }

        /// <summary>重力加速度（世界单位/秒²）；数据没写为 <c>null</c>（vertical 空间取 <see cref="Core.Carriers.Unit.VerticalAxisOptions"/> 缺省）。</summary>
        public double? Gravity { get; }

        /// <summary>玩家跳跃顶点高度（世界单位）；数据没写为 <c>null</c>。</summary>
        public double? JumpHeight { get; }

        /// <summary>该格子的空间模型是否带竖直轴（<c>side_2d</c>、<c>volume</c>）；<c>plane</c> 为否。</summary>
        public bool HasVerticalAxis => IsVerticalSpace(Space);

        public static bool IsVerticalSpace(string space) =>
            string.Equals(space, "side_2d", StringComparison.Ordinal) || string.Equals(space, "volume", StringComparison.Ordinal);

        public LabScenario(
            Id id, string cell, string space, string form, string cameraMode, string controlSpace, string facing,
            string hitShape, string defaultPreset, IReadOnlyList<string> defaultWeapons, Id dummySetId,
            IReadOnlyList<string> scriptSubset, IReadOnlyList<string> requiredCapabilities, Id arenaId,
            string settlement, IReadOnlyList<KeyValuePair<string, Id>> skillBindings,
            double? gravity = null, double? jumpHeight = null)
        {
            Id = id;
            Cell = cell;
            Space = space;
            Form = form;
            CameraMode = cameraMode;
            ControlSpace = controlSpace;
            Facing = facing;
            HitShape = hitShape;
            DefaultPreset = defaultPreset;
            DefaultWeapons = defaultWeapons;
            DummySetId = dummySetId;
            ScriptSubset = scriptSubset;
            RequiredCapabilities = requiredCapabilities;
            ArenaId = arenaId;
            Settlement = settlement;
            SkillBindings = skillBindings;
            Gravity = gravity;
            JumpHeight = jumpHeight;
        }

        /// <summary>
        /// 该格子在给定能力集合上的可运行状态（06 第 1.2 节"可运行 / 不可运行（缺能力 X）"显式状态）：
        /// 缺少必需适配能力标"不可运行"并给出原因，不静默跳过。三个空间取值（<c>plane</c>/<c>side_2d</c>/<c>volume</c>）在无头宿主上
        /// 都有真实语义，不再有"预留"（手感设计/06 第 10 节勘误 9）；空间语义靠核心层的竖直轴能力实现，不是宿主能力，所以不进能力集合。
        /// </summary>
        public CellRunnability CheckRunnable(IReadOnlyCollection<string> availableCapabilities)
        {
            var reasons = new List<string>();
            foreach (var cap in RequiredCapabilities)
            {
                var has = false;
                foreach (var a in availableCapabilities)
                {
                    if (string.Equals(a, cap, StringComparison.Ordinal))
                    {
                        has = true;
                        break;
                    }
                }

                if (!has)
                {
                    reasons.Add($"缺能力 {cap}");
                }
            }

            return new CellRunnability(Cell, reasons);
        }
    }

    /// <summary>格子可运行状态：<see cref="Runnable"/> 为假时 <see cref="Reasons"/> 给出每条原因。</summary>
    public sealed class CellRunnability
    {
        public string Cell { get; }

        public IReadOnlyList<string> Reasons { get; }

        public bool Runnable => Reasons.Count == 0;

        public CellRunnability(string cell, IReadOnlyList<string> reasons)
        {
            Cell = cell;
            Reasons = reasons;
        }

        public override string ToString() =>
            Runnable ? $"{Cell}: 可运行" : $"{Cell}: 不可运行（{string.Join("；", Reasons)}）";
    }

    /// <summary>
    /// <c>lab.*</c> 三张表的类型化读取（只读视图，不修改注册表）。无头宿主与测试用它拿格子、地形、靶子集；
    /// 表声明在 <c>core/sim/schema/LabSchemas.cs</c>。
    /// </summary>
    public sealed class LabCatalog
    {
        public const string ScenarioPrefix = "lab.scenario.";

        private readonly IDataRegistryView _registry;

        public LabCatalog(IDataRegistryView registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>按数据行顺序返回全部格子。</summary>
        public IReadOnlyList<LabScenario> Scenarios()
        {
            var result = new List<LabScenario>();
            foreach (var record in _registry.GetAll("lab.scenario"))
            {
                result.Add(ReadScenario(record));
            }

            return result;
        }

        /// <summary>数据集里是否有该格子（短名或完整 id）。</summary>
        public bool HasScenario(string cellOrId)
        {
            var id = cellOrId.StartsWith(ScenarioPrefix, StringComparison.Ordinal) ? cellOrId : ScenarioPrefix + cellOrId;
            return _registry.Get("lab.scenario", id) != null;
        }

        /// <summary>按短名（如 <c>2d_targeted</c>）或完整 id 取格子。</summary>
        public LabScenario GetScenario(string cellOrId)
        {
            var id = cellOrId.StartsWith(ScenarioPrefix, StringComparison.Ordinal) ? cellOrId : ScenarioPrefix + cellOrId;
            var record = _registry.Get("lab.scenario", id)
                ?? throw new LabFormatException($"数据集里没有格子 {id}");
            return ReadScenario(record);
        }

        public LabArena GetArena(Id id)
        {
            var record = _registry.Get("lab.arena", id) ?? throw new LabFormatException($"数据集里没有地形 {id}");
            var blocks = new List<LabArenaBlock>();
            foreach (var item in record.GetArray("blocks"))
            {
                var o = (JsonObject)item;
                blocks.Add(new LabArenaBlock(
                    LabJson.RequireString(o, "name", id.Value),
                    LabJson.RequireString(o, "kind", id.Value),
                    LabJson.ReadVec(o["min"], id.Value + ".min"),
                    LabJson.ReadVec(o["max"], id.Value + ".max")));
            }

            return new LabArena(id, record.GetId("map_ref"), blocks);
        }

        public LabDummySet GetDummySet(Id id)
        {
            var record = _registry.Get("lab.dummy_set", id) ?? throw new LabFormatException($"数据集里没有靶子集 {id}");
            var entries = new List<LabDummy>();
            foreach (var item in record.GetArray("entries"))
            {
                var o = (JsonObject)item;
                var count = o.TryGetValue("count", out var c) && c is JsonNumber cn ? (int)cn.Value : 1;
                var spacing = o.TryGetValue("spacing", out var s) && s is JsonNumber sn ? sn.Value : 1.0;
                var ai = o.TryGetValue("ai", out var a) && a is JsonBool ab && ab.Value;
                var height = o.TryGetValue("height", out var hv) && hv is JsonNumber hn ? hn.Value : 0.0;
                entries.Add(new LabDummy(
                    LabJson.RequireString(o, "name", id.Value),
                    LabJson.RequireString(o, "kind", id.Value),
                    new Id(LabJson.RequireString(o, "creature_ref", id.Value)),
                    LabJson.ReadVec(o["position"], id.Value + ".position"),
                    LabJson.RequireString(o, "group", id.Value),
                    count,
                    spacing,
                    ai,
                    height));
            }

            return new LabDummySet(id, entries);
        }

        private static LabScenario ReadScenario(DataRecord record)
        {
            var id = record.Id ?? throw new LabFormatException($"lab.scenario 行缺少 id：{record.Key}");
            var bindings = new List<KeyValuePair<string, Id>>();
            foreach (var pair in record.GetObject("skill_bindings"))
            {
                bindings.Add(new KeyValuePair<string, Id>(
                    pair.Key,
                    new Id(pair.Value is JsonString s ? s.Value : throw new LabFormatException($"{id.Value}.skill_bindings 的值必须是技能 id"))));
            }

            bindings.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));

            var cell = id.Value.StartsWith(ScenarioPrefix, StringComparison.Ordinal)
                ? id.Value.Substring(ScenarioPrefix.Length)
                : id.Value;
            return new LabScenario(
                id,
                cell,
                record.GetString("space"),
                record.GetString("form"),
                record.GetString("camera_mode"),
                record.GetString("control_space"),
                record.GetString("facing"),
                record.GetString("hit_shape"),
                record.GetId("default_preset").Value,
                IdStrings(record, "default_weapons"),
                record.GetId("dummy_set"),
                IdStrings(record, "script_subset"),
                StringArray(record, "required_capabilities"),
                record.GetId("arena"),
                record.GetString("settlement"),
                bindings,
                record.TryGetNumber("gravity", out var gravity) ? gravity : (double?)null,
                record.TryGetNumber("jump_height", out var jumpHeight) ? jumpHeight : (double?)null);
        }

        private static IReadOnlyList<string> IdStrings(DataRecord record, string field)
        {
            var list = new List<string>();
            if (record.TryGetIdList(field, out var ids))
            {
                foreach (var id in ids)
                {
                    list.Add(id.Value);
                }
            }

            return list;
        }

        private static IReadOnlyList<string> StringArray(DataRecord record, string field)
        {
            var list = new List<string>();
            if (record.TryGetArray(field, out var arr))
            {
                foreach (var item in arr)
                {
                    if (item is JsonString s)
                    {
                        list.Add(s.Value);
                    }
                }
            }

            return list;
        }
    }
}
