using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Numbers.Faction
{
    /// <summary>
    /// <see cref="IFactionMatrix"/> 的默认实现（见本模块 README）。构造期从
    /// <see cref="IDataRegistryView"/> 一次性读取 <c>fac.faction</c>/<c>fac.reaction_matrix</c>
    /// 建索引；之后只读除 <see cref="SetReaction"/>/<see cref="ResetOverrides"/> 写入/清空的
    /// 运行期覆盖表以外，不重新查询 registry。
    /// </summary>
    public sealed class FactionMatrix : IFactionMatrix
    {
        private readonly IEventBus _bus;

        private readonly Dictionary<string, Reaction> _defaults = new Dictionary<string, Reaction>(StringComparer.Ordinal);
        private readonly List<Id> _factionOrder = new List<Id>();
        private readonly Dictionary<(string From, string To), Reaction> _explicit = new Dictionary<(string, string), Reaction>();
        private readonly Dictionary<(string From, string To), Reaction> _overrides = new Dictionary<(string, string), Reaction>();

        public IReadOnlyList<Id> Factions => _factionOrder;

        public FactionMatrix(IDataRegistryView registry, IEventBus bus)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));

            foreach (var record in registry.GetAll("fac.faction"))
            {
                var id = record.GetId("id");
                var reaction = ParseReaction(record.GetString("default_reaction"));
                _defaults[id.Value] = reaction;
                _factionOrder.Add(id);
            }

            foreach (var record in registry.GetAll("fac.reaction_matrix"))
            {
                var from = record.GetId("from");
                var to = record.GetId("to");
                var reaction = ParseReaction(record.GetString("reaction"));
                _explicit[(from.Value, to.Value)] = reaction;
            }
        }

        public Reaction GetReaction(Id from, Id to)
        {
            EnsureKnown(from);
            EnsureKnown(to);

            if (string.Equals(from.Value, to.Value, StringComparison.Ordinal))
            {
                return Reaction.Friendly;
            }

            var key = (from.Value, to.Value);
            if (_overrides.TryGetValue(key, out var overridden))
            {
                return overridden;
            }

            if (_explicit.TryGetValue(key, out var explicitReaction))
            {
                return explicitReaction;
            }

            return _defaults[from.Value];
        }

        public void SetReaction(Id from, Id to, Reaction reaction)
        {
            EnsureKnown(from);
            EnsureKnown(to);

            var old = GetReaction(from, to);
            _overrides[(from.Value, to.Value)] = reaction;

            if (old != reaction)
            {
                _bus.PublishImmediate(new FactionRelationChangedEvent(from, to, old, reaction));
            }
        }

        public void ResetOverrides() => _overrides.Clear();

        public bool IsHostile(Id a, Id b) => GetReaction(a, b) == Reaction.Hostile;

        private void EnsureKnown(Id factionId)
        {
            if (!_defaults.ContainsKey(factionId.Value))
            {
                throw new ArgumentException($"未知阵营 \"{factionId}\"", nameof(factionId));
            }
        }

        private static Reaction ParseReaction(string value)
        {
            switch (value)
            {
                case "hostile": return Reaction.Hostile;
                case "neutral": return Reaction.Neutral;
                case "friendly": return Reaction.Friendly;
                default: throw new ArgumentException($"未知反应枚举值 \"{value}\"", nameof(value));
            }
        }
    }
}
