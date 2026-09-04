using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Tests.Rules.Skill
{
    /// <summary>阶段 3 整理"事项三"：<see cref="IStaticImmunityProvider"/> 的最小可控测试假实现，
    /// 供 <see cref="AuraEffectTests"/> 验证 <see cref="AuraHost"/> 在光环免疫/控制之外正确叠加
    /// 内容驱动的静态免疫（如 control_immune 一类"tier 全部控制免疫"）。</summary>
    internal sealed class FakeStaticImmunityProvider : IStaticImmunityProvider
    {
        private readonly HashSet<(Id unit, Id school, EffectKind kind)> _immune = new HashSet<(Id, Id, EffectKind)>();
        private readonly Dictionary<Id, ControlFlags> _controlImmunity = new Dictionary<Id, ControlFlags>();

        public FakeStaticImmunityProvider SetImmune(Id unitId, Id school, EffectKind kind)
        {
            _immune.Add((unitId, school, kind));
            return this;
        }

        public FakeStaticImmunityProvider SetControlImmune(Id unitId, ControlFlags flags)
        {
            _controlImmunity[unitId] = flags;
            return this;
        }

        public bool IsImmune(Id unitId, Id school, EffectKind kind) => _immune.Contains((unitId, school, kind));

        public ControlFlags GetControlImmunity(Id unitId) =>
            _controlImmunity.TryGetValue(unitId, out var flags) ? flags : ControlFlags.None;
    }
}
