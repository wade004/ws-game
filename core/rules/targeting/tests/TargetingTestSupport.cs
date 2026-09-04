using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Numbers.PowerSet;
using Core.Rules.Common;
using Core.Rules.Targeting;

namespace Tests.Rules.Targeting
{
    /// <summary>目标选择模块测试用的最小假实现与搭建帮助方法：不经完整 DataRegistry 加载管线直接
    /// 手搭部分记录时，与 data_registry/power_set 自身测试的最小化搭建方式同一惯例（见
    /// core/numbers/power_set/tests/PowerTestSupport.cs）。</summary>
    internal static class TargetingTestSupport
    {
        public static IEventBus MakeBus(bool includeTargetingResolved = true)
        {
            var definitions = new List<EventDefinition>
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                // 测试用真实 PowerHost（见任务书"真实 PowerHost"）注册单位/修改资源值时会
                // Enqueue 这两个事件，StrictCatalog 默认开启，未登记会直接抛异常。
                new EventDefinition(PowerEventKeys.Changed, "power",
                    new[] { "unitId", "powerType", "oldValue", "newValue" }),
                new EventDefinition(PowerEventKeys.Depleted, "power",
                    new[] { "unitId", "powerType" }),
            };

            if (includeTargetingResolved)
            {
                definitions.Add(new EventDefinition(RulesEventKeys.TargetingResolved, "targeting",
                    new[] { "unitId", "chainId", "targetIds" }));
            }

            return new EventBus(EventCatalog.FromDefinitions(definitions));
        }

        public static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        /// <summary>构造一个只登记 <c>target.chain_def</c> 表的 <see cref="DataRegistry"/>；
        /// <paramref name="withValidationRule"/> 为 true 时同时注册 <see cref="ChainDefValidationRule"/>。</summary>
        public static IDataRegistry BuildChainRegistry(
            IEventBus bus, string chainRowsJson, TargetStrategyRegistry strategies, bool withValidationRule = true)
        {
            var source = new InMemoryDataSource()
                .Add(TargetSchemas.ChainDef.Name, Envelope(TargetSchemas.ChainDef.Name, chainRowsJson));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(TargetSchemas.ChainDef);
            if (withValidationRule)
            {
                registry.RegisterValidationRule(new ChainDefValidationRule(strategies.Names));
            }

            return registry;
        }

        /// <summary>直接从 JSON 手搭一条 <see cref="PowerTypeDefinition"/>（同 PowerTestSupport.Definition），
        /// 不经完整数据表加载——本模块只需要一个 <c>arch.power.health</c> 类型即可测排序/过滤。</summary>
        public static PowerTypeDefinition HealthPowerType(double max = 100)
        {
            var json = "{"
                + "\"id\": \"" + WellKnownPowers.Health + "\","
                + "\"name_key\": \"l10n.power.health.name\","
                + "\"max_source\": {\"kind\": \"fixed\", \"value\": " + max.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},"
                + "\"start_full\": true"
                + "}";
            var obj = (JsonObject)JsonReader.Parse(json);
            var idString = ((JsonString)obj["id"]).Value;
            var record = new DataRecord(PowerSchemas.PowerType, idString, new Id(idString), obj);
            return new PowerTypeDefinition(record);
        }

        public static IReadOnlyList<Id> Ids(params string[] values)
        {
            var list = new List<Id>(values.Length);
            foreach (var v in values)
            {
                list.Add(new Id(v));
            }

            return list;
        }
    }

    /// <summary><see cref="IUnitAccess"/> 的内存假实现：手工登记单位的位置/阵营/等级/朝向/存活/
    /// 标签，供测试驱动 <c>TargetHost</c> 而不依赖任何真实载体层实现。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private sealed class Entry
        {
            public Vec2 Position;
            public Id Faction;
            public int Level = 1;
            public double Facing;
            public bool Alive = true;
            // 显式初始化为 null（而非仅声明）以避免 CS0649："字段从未被赋值"——本模块测试用不
            // 到内容模板 id，GetTemplateId 恒返回 null，但显式赋值能让意图更清楚。
            public Id? TemplateId = null;
            public List<Id> Tags = new List<Id>();
        }

        private readonly SortedDictionary<string, Entry> _entries = new SortedDictionary<string, Entry>(StringComparer.Ordinal);

        public FakeUnitAccess Add(
            Id id, Id faction, Vec2 position, bool alive = true, int level = 1, double facing = 0,
            IReadOnlyList<Id>? tags = null)
        {
            var entry = new Entry
            {
                Position = position,
                Faction = faction,
                Level = level,
                Facing = facing,
                Alive = alive,
            };
            if (tags != null)
            {
                entry.Tags.AddRange(tags);
            }

            _entries[id.Value] = entry;
            return this;
        }

        public void SetAlive(Id unitId, bool alive)
        {
            _entries[unitId.Value].Alive = alive;
        }

        public void SetPosition(Id unitId, Vec2 position)
        {
            _entries[unitId.Value].Position = position;
        }

        public bool Exists(Id unitId) => _entries.ContainsKey(unitId.Value);

        public IReadOnlyList<Id> AllUnits
        {
            get
            {
                var list = new List<Id>(_entries.Count);
                foreach (var key in _entries.Keys)
                {
                    list.Add(new Id(key));
                }

                return list;
            }
        }

        public Vec2 GetPosition(Id unitId) => _entries[unitId.Value].Position;

        public Id GetFaction(Id unitId) => _entries[unitId.Value].Faction;

        public int GetLevel(Id unitId) => _entries[unitId.Value].Level;

        public double GetFacing(Id unitId) => _entries[unitId.Value].Facing;

        public bool IsAlive(Id unitId) => _entries[unitId.Value].Alive;

        public Id? GetTemplateId(Id unitId) => _entries[unitId.Value].TemplateId;

        public IReadOnlyList<Id> GetTags(Id unitId) => _entries[unitId.Value].Tags;
    }

    /// <summary><see cref="IThreatTable"/> 的内存假实现：每个 unitId 各自一张来源 → 仇恨值的表。</summary>
    internal sealed class FakeThreatTable : IThreatTable
    {
        private readonly Dictionary<string, Dictionary<string, double>> _tables = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);

        private Dictionary<string, double> TableFor(Id unitId)
        {
            if (!_tables.TryGetValue(unitId.Value, out var table))
            {
                table = new Dictionary<string, double>(StringComparer.Ordinal);
                _tables[unitId.Value] = table;
            }

            return table;
        }

        public void AddThreat(Id unitId, Id sourceId, double amount)
        {
            var table = TableFor(unitId);
            table.TryGetValue(sourceId.Value, out var current);
            table[sourceId.Value] = current + amount;
        }

        public Id? GetTopThreat(Id unitId)
        {
            if (!_tables.TryGetValue(unitId.Value, out var table) || table.Count == 0)
            {
                return null;
            }

            string? bestSource = null;
            var bestValue = double.NegativeInfinity;
            foreach (var kv in table)
            {
                if (kv.Value > bestValue || (kv.Value == bestValue && string.CompareOrdinal(kv.Key, bestSource) < 0))
                {
                    bestValue = kv.Value;
                    bestSource = kv.Key;
                }
            }

            return bestSource == null ? (Id?)null : new Id(bestSource);
        }

        public void Clear(Id unitId) => _tables.Remove(unitId.Value);

        public double GetThreat(Id unitId, Id sourceId)
        {
            return _tables.TryGetValue(unitId.Value, out var table) && table.TryGetValue(sourceId.Value, out var v) ? v : 0.0;
        }

        public void SetThreat(Id unitId, Id sourceId, double amount)
        {
            TableFor(unitId)[sourceId.Value] = amount;
        }

        public IReadOnlyList<(Id source, double amount)> GetAll(Id unitId)
        {
            var result = new List<(Id, double)>();
            if (_tables.TryGetValue(unitId.Value, out var table))
            {
                foreach (var kv in table)
                {
                    result.Add((new Id(kv.Key), kv.Value));
                }
            }

            return result;
        }
    }

    /// <summary>
    /// <see cref="IExprHostFactory"/> 的测试实现：把 <c>self</c>/<c>target</c> 两个分组接到真实的
    /// <see cref="IUnitAccess"/>/<see cref="IPowerHost"/>，回答 <c>hp_pct</c>/<c>faction</c>/
    /// <c>level</c> 三个 key（对应任务书"可编程 target.hp_pct、target.faction 等"——这里不是靠
    /// 委托编程，而是直接桥接到测试搭建的真实状态，行为等价且更贴近真实集成）。
    /// </summary>
    internal sealed class TestExprHostFactory : IExprHostFactory
    {
        private readonly IUnitAccess _units;
        private readonly IPowerHost _powers;

        public TestExprHostFactory(IUnitAccess units, IPowerHost powers)
        {
            _units = units;
            _powers = powers;
        }

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) =>
            new Host(_units, _powers, selfId, targetId);

        private sealed class Host : IExprHost
        {
            private readonly IUnitAccess _units;
            private readonly IPowerHost _powers;
            private readonly Id _self;
            private readonly Id? _target;

            public Host(IUnitAccess units, IPowerHost powers, Id self, Id? target)
            {
                _units = units;
                _powers = powers;
                _self = self;
                _target = target;
            }

            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
            {
                Id subject;
                if (group == ExprGroups.Self)
                {
                    subject = _self;
                }
                else if (group == ExprGroups.Target && _target.HasValue)
                {
                    subject = _target.Value;
                }
                else
                {
                    throw new InvalidOperationException($"测试宿主不支持的分组：\"{group}\"");
                }

                switch (key)
                {
                    case "hp_pct":
                        if (!_powers.HasPower(subject, WellKnownPowers.Health))
                        {
                            return ExprValue.OfNumber(0);
                        }

                        var max = _powers.GetPowerMax(subject, WellKnownPowers.Health);
                        return ExprValue.OfNumber(max > 0 ? _powers.GetPower(subject, WellKnownPowers.Health) / max : 0);

                    case "faction":
                        return ExprValue.OfId(_units.GetFaction(subject));

                    case "level":
                        return ExprValue.OfInt(_units.GetLevel(subject));

                    default:
                        throw new InvalidOperationException($"测试宿主不支持的 key：\"{group}.{key}\"");
                }
            }
        }
    }
}
