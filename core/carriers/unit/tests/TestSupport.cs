using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// <see cref="IStatHost"/> 的最小测试假实现（惯例同 <c>core/rules/*/tests</c> 各模块自己的
    /// <c>FakeUnitAccess</c>/<c>FakeAuraQuery</c>：只覆盖本模块测试实际用到的行为——按单位/属性 id
    /// 存取一个基础值，<see cref="GetStat"/> 直接返回基础值（忽略修正器聚合，移动速度场景不需要
    /// 三段式聚合），不代表 <c>Core.Numbers.StatBlock.StatHost</c> 正式实现的完整语义。</summary>
    internal sealed class FakeStatHost : IStatHost
    {
        private readonly HashSet<Id> _registered = new HashSet<Id>();
        private readonly Dictionary<(Id unit, Id stat), double> _bases = new Dictionary<(Id, Id), double>();
        private readonly Dictionary<(Id unit, Id stat), List<StatModifier>> _modifiers =
            new Dictionary<(Id, Id), List<StatModifier>>();

        public void RegisterUnit(Id unitId) => _registered.Add(unitId);

        public void UnregisterUnit(Id unitId) => _registered.Remove(unitId);

        public bool IsRegistered(Id unitId) => _registered.Contains(unitId);

        public void SetBase(Id unitId, Id stat, double value) => _bases[(unitId, stat)] = value;

        public double GetBase(Id unitId, Id stat) => _bases.TryGetValue((unitId, stat), out var v) ? v : 0;

        public double GetStat(Id unitId, Id stat) => GetBase(unitId, stat);

        public void AddModifier(Id unitId, StatModifier modifier)
        {
            var key = (unitId, modifier.Stat);
            if (!_modifiers.TryGetValue(key, out var list))
            {
                list = new List<StatModifier>();
                _modifiers[key] = list;
            }

            list.Add(modifier);
        }

        public void RemoveModifiersBySource(Id unitId, Id sourceId)
        {
            foreach (var key in new List<(Id, Id)>(_modifiers.Keys))
            {
                if (key.Item1.Equals(unitId))
                {
                    _modifiers[key].RemoveAll(m => m.SourceId.Equals(sourceId));
                }
            }
        }

        public IReadOnlyList<StatModifier> GetModifiers(Id unitId, Id stat) =>
            _modifiers.TryGetValue((unitId, stat), out var list)
                ? list
                : (IReadOnlyList<StatModifier>)Array.Empty<StatModifier>();
    }

    /// <summary>
    /// <see cref="IAuraQuery"/> 的最小测试假实现：只覆盖 <see cref="MovementTickHandler"/> 实际用到
    /// 的 <see cref="GetControlFlags"/>（按单位 id 存取一组标志位），其余成员返回固定的"无光环"
    /// 默认值，不代表 <c>core/rules/skill</c> 正式实现的完整语义（惯例同 <see cref="FakeStatHost"/>）。
    /// </summary>
    internal sealed class FakeAuraQuery : IAuraQuery
    {
        private readonly Dictionary<Id, ControlFlags> _controlFlags = new Dictionary<Id, ControlFlags>();

        public void SetControlFlags(Id unitId, ControlFlags flags) => _controlFlags[unitId] = flags;

        public bool HasAura(Id unitId, Id auraDefId) => false;

        public int GetStacks(Id unitId, Id auraDefId) => 0;

        public ControlFlags GetControlFlags(Id unitId) =>
            _controlFlags.TryGetValue(unitId, out var flags) ? flags : ControlFlags.None;

        public bool IsImmune(Id unitId, Id school, EffectKind kind) => false;

        public double ConsumeAbsorb(Id unitId, Id school, double amount) => 0;

        public IReadOnlyList<Id> GetActiveAuraDefs(Id unitId) => Array.Empty<Id>();
    }

}
