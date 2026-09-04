using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Tests.Rules.Skill
{
    /// <summary><see cref="ICombatHost"/> 的测试假实现：记录每次 <see cref="ResolveEffect"/> 调用，
    /// 按可编程规则（<see cref="ResolveFunc"/>）返回结果；默认规则原样把 <c>BaseValue</c> 当作
    /// <c>FinalAmount</c>、不做免疫/减免。</summary>
    internal sealed class FakeCombatHost : ICombatHost
    {
        public readonly List<EffectContext> ResolveCalls = new List<EffectContext>();

        public Func<EffectContext, ResolveResult> ResolveFunc { get; set; } =
            context => new ResolveResult(
                HitResult.Hit, context.BaseValue, context.BaseValue, 0, immune: false, isHeal: context.Kind == EffectKind.Heal);

        private readonly HashSet<Id> _inCombat = new HashSet<Id>();

        public ResolveResult ResolveEffect(EffectContext context)
        {
            ResolveCalls.Add(context);
            return ResolveFunc(context);
        }

        public IThreatTable GetThreatTable(Id unitId) => throw new NotSupportedException("本测试假实现不需要仇恨表");

        public bool IsInCombat(Id unitId) => _inCombat.Contains(unitId);

        public void NotifyCombatEvent(Id unitId) => _inCombat.Add(unitId);

        public void Update(double timeUnits)
        {
        }
    }
}
