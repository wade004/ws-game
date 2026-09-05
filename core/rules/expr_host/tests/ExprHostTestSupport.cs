using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Expr;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Tests.Rules.ExprHost
{
    /// <summary>
    /// <see cref="Core.Rules.ExprHost.RulesExprHostFactory"/> 测试专用的一组最小可控假实现——
    /// 全部纯内存字典，不接入任何真实 L0/L1/L2 host，只暴露测试需要摆布的最小面（惯例同
    /// <c>core/rules/combat/tests/CombatTestSupport.cs</c> 的 FakeUnitAccess/FakeAuraQuery）。
    /// </summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private sealed class Rec
        {
            public Vec2 Position;
            public Id Faction;
            public int Level = 1;
            public bool Alive = true;
            public List<Id> Tags = new List<Id>();
        }

        private readonly Dictionary<Id, Rec> _units = new Dictionary<Id, Rec>();
        private readonly List<Id> _order = new List<Id>();

        public FakeUnitAccess Add(Id id, Vec2 position, Id faction, int level = 1, bool alive = true, IReadOnlyList<Id>? tags = null)
        {
            if (!_units.ContainsKey(id)) _order.Add(id);
            _units[id] = new Rec { Position = position, Faction = faction, Level = level, Alive = alive, Tags = new List<Id>(tags ?? Array.Empty<Id>()) };
            return this;
        }

        public bool Exists(Id unitId) => _units.ContainsKey(unitId);

        public IReadOnlyList<Id> AllUnits { get { var l = new List<Id>(_order); l.Sort(); return l; } }

        public Vec2 GetPosition(Id unitId) => Require(unitId).Position;

        public void SetPosition(Id unitId, Vec2 position) => Require(unitId).Position = position;

        public Id GetFaction(Id unitId) => Require(unitId).Faction;

        public int GetLevel(Id unitId) => Require(unitId).Level;

        public double GetFacing(Id unitId) => 0;

        public bool IsAlive(Id unitId) => Require(unitId).Alive;

        public void SetAlive(Id unitId, bool alive) => Require(unitId).Alive = alive;

        public Id? GetTemplateId(Id unitId) => null;

        public IReadOnlyList<Id> GetTags(Id unitId) => Require(unitId).Tags;

        private Rec Require(Id unitId)
        {
            if (!_units.TryGetValue(unitId, out var r))
            {
                throw new InvalidOperationException($"FakeUnitAccess: 单位 \"{unitId}\" 未 Add");
            }
            return r;
        }
    }

    internal sealed class FakeStatHost : IStatHost
    {
        private readonly Dictionary<(Id, Id), double> _values = new Dictionary<(Id, Id), double>();

        public FakeStatHost Set(Id unitId, Id stat, double value)
        {
            _values[(unitId, stat)] = value;
            return this;
        }

        public void RegisterUnit(Id unitId) { }
        public void UnregisterUnit(Id unitId) { }
        public bool IsRegistered(Id unitId) => true;
        public void SetBase(Id unitId, Id stat, double value) => _values[(unitId, stat)] = value;
        public double GetBase(Id unitId, Id stat) => GetStat(unitId, stat);
        public double GetStat(Id unitId, Id stat) => _values.TryGetValue((unitId, stat), out var v) ? v : 0;
        public void AddModifier(Id unitId, StatModifier modifier) { }
        public void RemoveModifiersBySource(Id unitId, Id sourceId) { }
        public IReadOnlyList<StatModifier> GetModifiers(Id unitId, Id stat) => Array.Empty<StatModifier>();
    }

    internal sealed class FakePowerHost : IPowerHost
    {
        private readonly Dictionary<(Id, Id), (double cur, double max)> _values = new Dictionary<(Id, Id), (double, double)>();

        public FakePowerHost Set(Id unitId, Id powerType, double current, double max)
        {
            _values[(unitId, powerType)] = (current, max);
            return this;
        }

        public void RegisterUnit(Id unitId, IReadOnlyList<Id> powerTypes) { }
        public void UnregisterUnit(Id unitId) { }
        public bool HasPower(Id unitId, Id powerType) => _values.ContainsKey((unitId, powerType));
        public double GetPower(Id unitId, Id powerType) => _values.TryGetValue((unitId, powerType), out var v) ? v.cur : 0;
        public double GetPowerMax(Id unitId, Id powerType) => _values.TryGetValue((unitId, powerType), out var v) ? v.max : 0;
        public void ModifyPower(Id unitId, Id powerType, double delta, Id sourceId) { }
        public void SetInCombat(Id unitId, bool inCombat) { }
        public void Advance(Id unitId, double timeUnits) { }
        public void AdvanceAll(double timeUnits) { }
        public void RecomputeMax(Id unitId) { }
    }

    internal sealed class FakeAuraQuery : IAuraQuery
    {
        private readonly HashSet<(Id, Id)> _has = new HashSet<(Id, Id)>();
        private readonly Dictionary<(Id, Id), int> _stacks = new Dictionary<(Id, Id), int>();

        public FakeAuraQuery Add(Id unitId, Id auraDefId, int stacks = 1)
        {
            _has.Add((unitId, auraDefId));
            _stacks[(unitId, auraDefId)] = stacks;
            return this;
        }

        public bool HasAura(Id unitId, Id auraDefId) => _has.Contains((unitId, auraDefId));
        public int GetStacks(Id unitId, Id auraDefId) => _stacks.TryGetValue((unitId, auraDefId), out var s) ? s : 0;
        public ControlFlags GetControlFlags(Id unitId) => ControlFlags.None;
        public bool IsImmune(Id unitId, Id school, EffectKind kind) => false;
        public double ConsumeAbsorb(Id unitId, Id school, double amount) => 0;
        public IReadOnlyList<Id> GetActiveAuraDefs(Id unitId) => Array.Empty<Id>();
    }

    internal sealed class FakeThreatTable : IThreatTable
    {
        private readonly Dictionary<Id, Dictionary<Id, double>> _byUnit = new Dictionary<Id, Dictionary<Id, double>>();

        public void AddThreat(Id unitId, Id sourceId, double amount)
        {
            var table = GetTable(unitId);
            table[sourceId] = (table.TryGetValue(sourceId, out var v) ? v : 0) + amount;
        }

        public Id? GetTopThreat(Id unitId)
        {
            if (!_byUnit.TryGetValue(unitId, out var table) || table.Count == 0) return null;
            Id? best = null;
            var bestValue = double.NegativeInfinity;
            foreach (var kv in table)
            {
                if (kv.Value > bestValue || (kv.Value == bestValue && (best == null || kv.Key.CompareTo(best.Value) < 0)))
                {
                    bestValue = kv.Value;
                    best = kv.Key;
                }
            }
            return best;
        }

        public void Clear(Id unitId) => _byUnit.Remove(unitId);
        public double GetThreat(Id unitId, Id sourceId) => GetTable(unitId).TryGetValue(sourceId, out var v) ? v : 0;
        public void SetThreat(Id unitId, Id sourceId, double amount) => GetTable(unitId)[sourceId] = amount;

        public IReadOnlyList<(Id source, double amount)> GetAll(Id unitId)
        {
            var result = new List<(Id, double)>();
            if (_byUnit.TryGetValue(unitId, out var table))
            {
                foreach (var kv in table) result.Add((kv.Key, kv.Value));
            }
            return result;
        }

        private Dictionary<Id, double> GetTable(Id unitId)
        {
            if (!_byUnit.TryGetValue(unitId, out var table))
            {
                table = new Dictionary<Id, double>();
                _byUnit[unitId] = table;
            }
            return table;
        }
    }

    internal sealed class FakeCombatHost : ICombatHost
    {
        private readonly HashSet<Id> _inCombat = new HashSet<Id>();
        public readonly FakeThreatTable Threat = new FakeThreatTable();

        public FakeCombatHost SetInCombat(Id unitId, bool value)
        {
            if (value) _inCombat.Add(unitId); else _inCombat.Remove(unitId);
            return this;
        }

        public ResolveResult ResolveEffect(EffectContext context) =>
            new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: false);

        public IThreatTable GetThreatTable(Id unitId) => Threat;
        public bool IsInCombat(Id unitId) => _inCombat.Contains(unitId);
        public void NotifyCombatEvent(Id unitId, Id? hostileId = null) => _inCombat.Add(unitId);
        public void Update(double timeUnits) { }
    }

    internal sealed class FakeFactionMatrix : IFactionMatrix
    {
        private readonly HashSet<(Id, Id)> _hostile = new HashSet<(Id, Id)>();
        private readonly List<Id> _factions = new List<Id>();

        public FakeFactionMatrix SetHostile(Id a, Id b, bool bothWays = true)
        {
            if (!_factions.Contains(a)) _factions.Add(a);
            if (!_factions.Contains(b)) _factions.Add(b);
            _hostile.Add((a, b));
            if (bothWays) _hostile.Add((b, a));
            return this;
        }

        public Reaction GetReaction(Id from, Id to) =>
            from.Equals(to) ? Reaction.Friendly : (_hostile.Contains((from, to)) ? Reaction.Hostile : Reaction.Neutral);

        public void SetReaction(Id from, Id to, Reaction reaction) { }
        public void ResetOverrides() { }
        public bool IsHostile(Id a, Id b) => !a.Equals(b) && _hostile.Contains((a, b));
        public IReadOnlyList<Id> Factions => _factions;
    }

    internal sealed class FakeSkillHost : ISkillHost
    {
        private readonly HashSet<Id> _casting = new HashSet<Id>();

        public FakeSkillHost SetCasting(Id unitId, bool value)
        {
            if (value) _casting.Add(unitId); else _casting.Remove(unitId);
            return this;
        }

        public Vec2 GetPosition(Id unitId) => default;
        public IReadOnlyList<Id> FindUnits(Shape shape, Vec2 origin, UnitFilter filter) => Array.Empty<Id>();
        public void ApplyStatMod(Id sourceId, Id unitId, Id stat, StatModifierOp op, double value) { }
        public CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets) => CastResult.Fail(CastFailureReason.UnknownSkill);
        public double GetCooldown(Id unitId, Id skillId) => 0;
        public bool IsCasting(Id unitId) => _casting.Contains(unitId);
        public void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration) { }
    }

    /// <summary>可编程的 <see cref="Core.Rules.ExprHost.IExprGroupProvider"/> 假实现，供
    /// <c>world</c>/<c>quest</c>/<c>player</c> 分组的测试摆布返回值。</summary>
    internal sealed class FakeExprGroupProvider : Core.Rules.ExprHost.IExprGroupProvider
    {
        public Func<string, IReadOnlyList<ExprValue>, ExprValue> QueryFunc { get; set; } =
            (key, args) => ExprValue.OfBool(true);

        public ExprValue Query(string key, IReadOnlyList<ExprValue> args) => QueryFunc(key, args);
    }
}
