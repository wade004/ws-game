using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Core.Numbers.Faction;
using Xunit;

namespace Tests.Gameplay.Difficulty
{
    /// <summary>供本模块测试共用的最小装配帮助（惯例同 <c>core/carriers/creature/tests</c> 的
    /// <c>CreatureTestSupport</c>：只覆盖测试实际用到的行为，不代表任何正式实现的完整语义）。</summary>
    internal static class TestSupport
    {
        public static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        public const string TierRows = "[" +
            "{\"id\": \"diff.sample_normal\", \"name_key\": \"l10n.diff.sample_normal.name\", " +
            "\"modifier_aura_refs\": [], \"loot_multiplier\": 1.0, \"sort_weight\": 0}," +
            "{\"id\": \"diff.sample_hard\", \"name_key\": \"l10n.diff.sample_hard.name\", " +
            "\"modifier_aura_refs\": [\"aura.sample_tough\", \"aura.sample_deadly\"], " +
            "\"affix_pool_ref\": \"affix.sample_pool\", \"loot_multiplier\": 1.5, \"sort_weight\": 10}" +
            "]";

        /// <summary>关闭 <see cref="EventBusOptions.StrictCatalog"/> 的事件总线：本模块测试不逐条
        /// 登记 <c>creature.spawned</c>/<c>difficulty.applied</c> 的 <see cref="EventDefinition"/>，
        /// 惯例同 <c>CreatureTestSupport.CreateBus</c>。</summary>
        public static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        public static Core.Foundation.DataRegistry.DataRegistry MakeRegistry(IEventBus bus, string tierRowsJson = TierRows)
        {
            var source = new InMemoryDataSource()
                .Add(Core.Gameplay.Difficulty.DifficultySchemas.Tier.Name,
                    Envelope(Core.Gameplay.Difficulty.DifficultySchemas.Tier.Name, tierRowsJson));

            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(Core.Gameplay.Difficulty.DifficultySchemas.Tier);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);
            return registry;
        }
    }

    /// <summary>最小 <see cref="IUnitAccess"/> 假实现：只覆盖 difficulty 模块用到的
    /// Exists/GetFaction/GetMapId，其余成员未被本模块调用，抛 <see cref="NotImplementedException"/>
    /// 以便一旦被意外调用能立即暴露（惯例同仓库其它模块的 Fake/Stub 单位访问实现）。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private readonly HashSet<string> _existing = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Id> _factions = new Dictionary<string, Id>(StringComparer.Ordinal);
        private readonly Dictionary<string, Id> _mapIds = new Dictionary<string, Id>(StringComparer.Ordinal);

        public void AddUnit(Id unitId, Id faction, Id? mapId = null)
        {
            _existing.Add(unitId.Value);
            _factions[unitId.Value] = faction;
            if (mapId.HasValue)
            {
                _mapIds[unitId.Value] = mapId.Value;
            }
        }

        public bool Exists(Id unitId) => _existing.Contains(unitId.Value);

        public IReadOnlyList<Id> AllUnits => throw new NotImplementedException();

        public Vec2 GetPosition(Id unitId) => throw new NotImplementedException();

        public void SetPosition(Id unitId, Vec2 position) => throw new NotImplementedException();

        public Id GetFaction(Id unitId) => _factions[unitId.Value];

        public int GetLevel(Id unitId) => throw new NotImplementedException();

        public double GetFacing(Id unitId) => throw new NotImplementedException();

        public bool IsAlive(Id unitId) => true;

        public void SetAlive(Id unitId, bool alive) => throw new NotImplementedException();

        public Id? GetTemplateId(Id unitId) => throw new NotImplementedException();

        public IReadOnlyList<Id> GetTags(Id unitId) => throw new NotImplementedException();

        public Id? GetMapId(Id unitId) => _mapIds.TryGetValue(unitId.Value, out var mapId) ? (Id?)mapId : null;
    }

    /// <summary>最小 <see cref="IEffectSink"/> 假实现：只记录 <see cref="ApplyAura"/> 调用，
    /// 其余成员本模块不调用。</summary>
    internal sealed class FakeEffectSink : IEffectSink
    {
        public readonly List<(Id TargetId, Id AuraDefId, Id SourceId)> AppliedAuras = new List<(Id, Id, Id)>();

        public ResolveResult ApplyEffect(EffectContext context) => throw new NotImplementedException();

        public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null)
        {
            AppliedAuras.Add((targetId, auraDefId, sourceId));
            return default;
        }

        public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef) => throw new NotImplementedException();
    }

    /// <summary>最小 <see cref="IFactionMatrix"/> 假实现：按调用方预先登记的
    /// (from, to) → 敌对/非敌对 直接返回，避免为一个"谁跟谁敌对"这么简单的判定去装配真实
    /// <see cref="FactionMatrix"/> 所需的 <c>fac.faction</c>/<c>fac.reaction_matrix</c> 两张表。</summary>
    internal sealed class FakeFactionMatrix : IFactionMatrix
    {
        private readonly HashSet<(string From, string To)> _hostilePairs = new HashSet<(string, string)>();

        public void SetHostile(Id from, Id to) => _hostilePairs.Add((from.Value, to.Value));

        public Reaction GetReaction(Id from, Id to) => IsHostile(from, to) ? Reaction.Hostile : Reaction.Neutral;

        public void SetReaction(Id from, Id to, Reaction reaction) => throw new NotImplementedException();

        public void ResetOverrides() => throw new NotImplementedException();

        public bool IsHostile(Id a, Id b) => _hostilePairs.Contains((a.Value, b.Value));

        public IReadOnlyList<Id> Factions => throw new NotImplementedException();
    }
}
