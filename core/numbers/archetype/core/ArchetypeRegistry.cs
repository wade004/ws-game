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
    /// <c>arch.talent_tree</c> 建索引；P2-05 关联根治（外部审计 audit-c9ff301-20260909）之前"之后
    /// 只读，不重新查询 registry"——现订阅 <see cref="Core.Foundation.DataRegistry.DataLoadCompletedEvent"/>
    /// 后重新查询并整体替换三张索引，见 <see cref="ReloadFromRegistry"/> 判断记录。
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
        private readonly IDataRegistryView _registry;
        private readonly IEventBus _bus;
        private readonly StatBaseWriter _statBaseWriter;
        private readonly StatModifierWriter _statModifierWriter;
        private readonly PowerRegistrar _powerRegistrar;
        private readonly AuraApplier? _auraApplier;
        private readonly DerivationCoefficientOverrideWriter? _derivationOverrideWriter;

        private readonly Dictionary<string, ClassDefinition> _classes = new Dictionary<string, ClassDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, RaceDefinition> _races = new Dictionary<string, RaceDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, TalentTree> _talentTrees = new Dictionary<string, TalentTree>(StringComparer.Ordinal);
        private readonly List<ClassDefinition> _classOrder = new List<ClassDefinition>();

        public IReadOnlyList<ClassDefinition> Classes => _classOrder;

        /// <param name="auraApplier">
        /// W1 收边补齐：种族 <c>passive_auras</c> 的施加出口（见 <see cref="AuraApplier"/>）。为 null
        /// （默认，向后兼容既有调用方——本模块并行开发期 <c>core/rules/skill</c> 可能尚未就绪）时
        /// <see cref="ApplyTo"/> 跳过被动光环这一步，行为与本次改动之前完全一致；非 null 时才会对
        /// <c>race.passive_auras</c> 逐个调用。
        /// </param>
        public ArchetypeRegistry(
            IDataRegistryView registry,
            IEventBus bus,
            StatBaseWriter statBaseWriter,
            StatModifierWriter statModifierWriter,
            PowerRegistrar powerRegistrar,
            AuraApplier? auraApplier = null)
            : this(registry, bus, statBaseWriter, statModifierWriter, powerRegistrar, derivationOverrideWriter: null, auraApplier)
        {
        }

        /// <summary>
        /// T-N1-4 新增重载（ADR-0030 决策 2"职业模板可覆盖派生系数"）：在既有六参构造之上追加
        /// <paramref name="derivationOverrideWriter"/>。ABI 门禁 G3 禁止给既有公开构造加可选参数，
        /// 只能新增重载——既有六参构造改为委托本构造并传 <c>derivationOverrideWriter: null</c>，
        /// 行为完全不变（<see cref="ApplyTo"/> 在该委托为 null 时跳过派生系数覆盖这一步，见该方法
        /// 判断记录），不产生任何既有调用点的破坏；两个公开构造共用同一份初始化逻辑（只有一次
        /// <see cref="ReloadFromRegistry"/>、一次事件订阅），不是各自独立初始化一遍——避免调用新
        /// 重载时把订阅逻辑跑两遍。
        /// </summary>
        /// <param name="derivationOverrideWriter">
        /// T-N1-4：把 <c>arch.class.derivation_overrides</c> 写入属性宿主的具名委托。为 null（默认，
        /// 向后兼容既有调用方——同 <paramref name="auraApplier"/> 的既有取舍）时 <see cref="ApplyTo"/>
        /// 跳过这一步，行为与本次改动之前完全一致；非 null 时，每次 <see cref="ApplyTo"/> 都会用当前
        /// 职业的完整覆盖列表调用一次（可能是空列表，即"这个职业没有任何覆盖"），由接收方负责全量
        /// 替换语义（见 <see cref="DerivationCoefficientOverrideWriter"/> 判断记录）。
        /// </param>
        public ArchetypeRegistry(
            IDataRegistryView registry,
            IEventBus bus,
            StatBaseWriter statBaseWriter,
            StatModifierWriter statModifierWriter,
            PowerRegistrar powerRegistrar,
            DerivationCoefficientOverrideWriter? derivationOverrideWriter,
            AuraApplier? auraApplier = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _statBaseWriter = statBaseWriter ?? throw new ArgumentNullException(nameof(statBaseWriter));
            _statModifierWriter = statModifierWriter ?? throw new ArgumentNullException(nameof(statModifierWriter));
            _powerRegistrar = powerRegistrar ?? throw new ArgumentNullException(nameof(powerRegistrar));
            _auraApplier = auraApplier;
            _derivationOverrideWriter = derivationOverrideWriter;

            ReloadFromRegistry();

            // P2-05 关联根治（外部审计 audit-c9ff301-20260909，见 Core.Rules.Skill.SkillHost/
            // SkillDefCache.InvalidateAll、Core.Carriers.Item.InventoryHost 同一类判断记录）：
            // _races/_talentTrees/_classes/_classOrder 此前只在构造期从 registry 读取一次、永久
            // 常驻（类型顶部判断记录原文"之后只读，不重新查询 registry"）。本类型本就持有 registry
            // 引用，直接内部订阅、自行重新查询，不影响已经调用过 ApplyTo 的既有单位（属性/资源/光环
            // 是运行期状态，reload 只刷新定义表本身，不倒退已应用的效果）。
            _bus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, _ => ReloadFromRegistry());
        }

        private void ReloadFromRegistry()
        {
            _races.Clear();
            foreach (var record in _registry.GetAll("arch.race"))
            {
                var race = ParseRace(record);
                _races[race.Id.Value] = race;
            }

            _talentTrees.Clear();
            foreach (var record in _registry.GetAll("arch.talent_tree"))
            {
                var tree = ParseTalentTree(record);
                _talentTrees[tree.Id.Value] = tree;
            }

            _classes.Clear();
            _classOrder.Clear();
            foreach (var record in _registry.GetAll("arch.class"))
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

            // T-N1-4（ADR-0030 决策 2）：把本职业的派生系数覆盖整体写入属性宿主——委托为 null
            // （调用方未注入，见构造函数判断记录）时跳过，行为与本次改动之前完全一致。传入的是
            // cls.DerivationOverrides 完整列表（可能为空），由接收方负责全量替换语义。
            _derivationOverrideWriter?.Invoke(unitId, cls.DerivationOverrides);

            if (raceId.HasValue)
            {
                var race = GetRace(raceId.Value) ?? throw new ArgumentException($"未知种族 \"{raceId.Value}\"", nameof(raceId));
                foreach (var kv in race.StatMods)
                {
                    _statModifierWriter(unitId, new Id(kv.Key), "flat", kv.Value, raceId.Value);
                }

                // W1 收边补齐（A3 审计 #8）：race.passive_auras 此前只解析进 RaceDefinition、
                // 从未施加——Models.cs 旧注释"L2 skill 未实现"的前提已过期。sourceId 固定用种族
                // 自身 id，与上面 StatModifierWriter 的来源选取理由一致。
                if (_auraApplier != null)
                {
                    foreach (var auraDefId in race.PassiveAuras)
                    {
                        _auraApplier(unitId, auraDefId, raceId.Value);
                    }
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
            var derivationOverrides = ReadDerivationOverrides(record);

            return new ClassDefinition(id, nameKey, primaryStat, baseStats, powerTypes, skillBookRef, talentTreeRef, levelCurveRef, derivationOverrides);
        }

        /// <summary>T-N1-4：解析 <c>arch.class.derivation_overrides</c>（可选，缺省空列表）——元素
        /// 形状不符（缺字段/类型不对）已由 <c>ArchSchemas.DerivationOverrideEntrySchema</c> 的
        /// <c>required_field</c>/<c>field_type</c> 在加载期报过，这里静默跳过，不重复报（同
        /// <c>StatHost.LoadDefinitions</c> 对 <c>derived_from</c> 元素形状的既有口径）。</summary>
        private static IReadOnlyList<(Id Stat, Id Source, double Coefficient)> ReadDerivationOverrides(DataRecord record)
        {
            if (!record.TryGetArray("derivation_overrides", out var overridesArray) || overridesArray.Count == 0)
            {
                return Array.Empty<(Id, Id, double)>();
            }

            var list = new List<(Id Stat, Id Source, double Coefficient)>(overridesArray.Count);
            for (int i = 0; i < overridesArray.Count; i++)
            {
                if (overridesArray[i] is JsonObject entryObj
                    && entryObj.TryGetValue("stat", out var statRaw) && statRaw is JsonString statStr
                    && Id.TryParse(statStr.Value, out var statId)
                    && entryObj.TryGetValue("source", out var sourceRaw) && sourceRaw is JsonString sourceStr
                    && Id.TryParse(sourceStr.Value, out var sourceId)
                    && entryObj.TryGetValue("coefficient", out var coeffRaw) && coeffRaw is JsonNumber coeffNum)
                {
                    list.Add((statId, sourceId, coeffNum.Value));
                }
            }
            return list;
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
