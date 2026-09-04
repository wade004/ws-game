using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Numbers.Archetype
{
    /// <summary>
    /// <see cref="IArchetypeRegistry"/> 的默认实现（见本模块 README）。构造期从
    /// <see cref="IDataRegistryView"/> 一次性读取 <c>arch.class</c>/<c>arch.race</c>/
    /// <c>arch.talent_tree</c> 建索引；之后只读，不重新查询 registry。
    /// <para>
    /// 判断记录（种族修正的 <c>sourceId</c>）：任务书只说"StatModifierWriter 写种族修正"，未
    /// 指定来源 id 取值；本模块选用种族自身的 id（即 <paramref name="raceId"/> 参数本身）作为
    /// <c>sourceId</c>，理由：属性宿主按 <c>(unitId, sourceId)</c> 分组管理修正来源，用种族 id
    /// 本身当来源，语义上"这份修正来自哪个种族"一目了然，换种族（先移除旧来源、写入新来源）
    /// 时调用方也天然知道该传哪个 sourceId 给移除委托，不需要额外发明一个"archetype.race"
    /// 之类的常量来源再让调用方去追踪"当前是哪个种族"。
    /// </para>
    /// <para>
    /// 判断记录（无具体职业/种族名称）：本类与其测试全程只使用 <c>arch.class.sample_a</c> 一类
    /// 中性 id（落地方案 T2-3"禁止 archetype 模块内写死具体游戏的职业名称"），职业/种族的取名、
    /// 数量、具体数值完全由外部注入的数据行决定。
    /// </para>
    /// </summary>
    public sealed class ArchetypeRegistry : IArchetypeRegistry
    {
        private readonly IEventBus _bus;
        private readonly StatBaseWriter _statBaseWriter;
        private readonly StatModifierWriter _statModifierWriter;
        private readonly PowerRegistrar _powerRegistrar;

        private readonly Dictionary<string, ClassDefinition> _classes = new Dictionary<string, ClassDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, RaceDefinition> _races = new Dictionary<string, RaceDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, TalentTree> _talentTrees = new Dictionary<string, TalentTree>(StringComparer.Ordinal);
        private readonly List<ClassDefinition> _classOrder = new List<ClassDefinition>();

        public IReadOnlyList<ClassDefinition> Classes => _classOrder;

        public ArchetypeRegistry(
            IDataRegistryView registry,
            IEventBus bus,
            StatBaseWriter statBaseWriter,
            StatModifierWriter statModifierWriter,
            PowerRegistrar powerRegistrar)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _statBaseWriter = statBaseWriter ?? throw new ArgumentNullException(nameof(statBaseWriter));
            _statModifierWriter = statModifierWriter ?? throw new ArgumentNullException(nameof(statModifierWriter));
            _powerRegistrar = powerRegistrar ?? throw new ArgumentNullException(nameof(powerRegistrar));

            foreach (var record in registry.GetAll("arch.race"))
            {
                var race = ParseRace(record);
                _races[race.Id.Value] = race;
            }

            foreach (var record in registry.GetAll("arch.talent_tree"))
            {
                var tree = ParseTalentTree(record);
                _talentTrees[tree.Id.Value] = tree;
            }

            foreach (var record in registry.GetAll("arch.class"))
            {
                var cls = ParseClass(record);
                _classes[cls.Id.Value] = cls;
                _classOrder.Add(cls);
            }
        }

        public ClassDefinition? GetClass(Id id) => _classes.TryGetValue(id.Value, out var c) ? c : null;

        public RaceDefinition? GetRace(Id id) => _races.TryGetValue(id.Value, out var r) ? r : null;

        public TalentTree? GetTalentTree(Id id) => _talentTrees.TryGetValue(id.Value, out var t) ? t : null;

        public AppliedArchetype ApplyTo(Id unitId, Id classId, Id? raceId)
        {
            var cls = GetClass(classId) ?? throw new ArgumentException($"未知职业 \"{classId}\"", nameof(classId));

            foreach (var kv in cls.BaseStats)
            {
                _statBaseWriter(unitId, new Id(kv.Key), kv.Value);
            }

            if (raceId.HasValue)
            {
                var race = GetRace(raceId.Value) ?? throw new ArgumentException($"未知种族 \"{raceId.Value}\"", nameof(raceId));
                foreach (var kv in race.StatMods)
                {
                    _statModifierWriter(unitId, new Id(kv.Key), "flat", kv.Value, raceId.Value);
                }
            }

            _powerRegistrar(unitId, cls.PowerTypes);

            _bus.PublishImmediate(new ArchetypeAppliedEvent(unitId, classId, raceId));

            return new AppliedArchetype(classId, raceId, cls.LevelCurveRef);
        }

        // -----------------------------------------------------------------
        // 解析
        // -----------------------------------------------------------------

        private static ClassDefinition ParseClass(DataRecord record)
        {
            var id = record.GetId("id");
            var nameKey = record.GetString("name_key");
            var primaryStat = record.GetId("primary_stat");
            var baseStats = ReadNumberObject(record.GetObject("base_stats"));
            var powerTypes = record.GetIdList("power_types");
            var skillBookRef = record.TryGetId("skill_book_ref", out var sb) ? sb : (Id?)null;
            var talentTreeRef = record.TryGetId("talent_tree_ref", out var tt) ? tt : (Id?)null;
            var levelCurveRef = record.TryGetId("level_curve_ref", out var lc) ? lc : (Id?)null;

            return new ClassDefinition(id, nameKey, primaryStat, baseStats, powerTypes, skillBookRef, talentTreeRef, levelCurveRef);
        }

        private static RaceDefinition ParseRace(DataRecord record)
        {
            var id = record.GetId("id");
            var nameKey = record.GetString("name_key");
            var statMods = ReadNumberObject(record.GetObject("stat_mods"));
            var passiveAuras = record.TryGetIdList("passive_auras", out var auras) ? auras : Array.Empty<Id>();

            return new RaceDefinition(id, nameKey, statMods, passiveAuras);
        }

        private static TalentTree ParseTalentTree(DataRecord record)
        {
            var id = record.GetId("id");
            var nodesJson = record.GetArray("nodes");
            var nodes = new List<TalentNode>(nodesJson.Count);

            foreach (var nodeValue in nodesJson)
            {
                if (!(nodeValue is JsonObject nodeObj))
                {
                    throw new ArgumentException($"天赋树 \"{id}\" 存在非对象节点");
                }

                if (!(nodeObj.TryGetValue("id", out var idV) && idV is JsonString idS))
                {
                    throw new ArgumentException($"天赋树 \"{id}\" 存在缺少 id 的节点");
                }

                var prerequisites = new List<string>();
                if (nodeObj.TryGetValue("prerequisites", out var preV) && preV is JsonArray preArr)
                {
                    foreach (var p in preArr)
                    {
                        if (p is JsonString ps)
                        {
                            prerequisites.Add(ps.Value);
                        }
                    }
                }

                var cost = 0;
                if (nodeObj.TryGetValue("cost", out var costV) && costV is JsonNumber costN && costN.TryGetInt64(out var costL))
                {
                    cost = (int)costL;
                }

                var grants = nodeObj.TryGetValue("grants", out var grantsV) && grantsV is JsonObject grantsObj
                    ? grantsObj
                    : new JsonObjectBuilder().Build();

                nodes.Add(new TalentNode(idS.Value, prerequisites, cost, grants));
            }

            return new TalentTree(id, nodes);
        }

        private static IReadOnlyList<KeyValuePair<string, double>> ReadNumberObject(JsonObject obj)
        {
            var list = new List<KeyValuePair<string, double>>(obj.Count);
            foreach (var kv in obj)
            {
                if (kv.Value is JsonNumber n)
                {
                    list.Add(new KeyValuePair<string, double>(kv.Key, n.Value));
                }
            }
            return list;
        }
    }
}
