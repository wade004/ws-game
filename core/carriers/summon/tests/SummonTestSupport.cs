using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Tests.Carriers.Summon
{
    /// <summary>
    /// <see cref="ICombatHost"/> 的最小测试假实现：按单位 id 可编程"是否在战斗中"，记录
    /// <see cref="NotifyCombatEvent"/> 调用（供 <c>SummonTickHandler</c> 的进出战斗联动测试使用）。
    /// 惯例同 <c>core/rules/skill/tests/FakeCombatHost.cs</c>：只覆盖本模块测试实际用到的行为。
    /// </summary>
    internal sealed class FakeCombatHost : ICombatHost
    {
        private readonly HashSet<Id> _inCombat = new HashSet<Id>();

        public List<Id> NotifyCalls { get; } = new List<Id>();

        public void SetInCombat(Id unitId, bool inCombat)
        {
            if (inCombat)
            {
                _inCombat.Add(unitId);
            }
            else
            {
                _inCombat.Remove(unitId);
            }
        }

        public bool IsInCombat(Id unitId) => _inCombat.Contains(unitId);

        public void NotifyCombatEvent(Id unitId, Id? hostileId = null)
        {
            NotifyCalls.Add(unitId);
            _inCombat.Add(unitId);
        }

        public ResolveResult ResolveEffect(EffectContext context) =>
            throw new NotSupportedException("FakeCombatHost 不支持 ResolveEffect（本模块测试不需要）");

        public IThreatTable GetThreatTable(Id unitId) =>
            throw new NotSupportedException("FakeCombatHost 不支持 GetThreatTable（本模块测试不需要）");

        public void Update(double timeUnits)
        {
        }
    }
}
