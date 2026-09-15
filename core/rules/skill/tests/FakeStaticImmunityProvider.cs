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

        /// <summary>T-N3-6 新增：按类别声明的控制免疫（独立于 <see cref="_controlImmunity"/> 的
        /// 按标志位声明，供 <see cref="AuraEffectTests"/> 验证 <see cref="AuraHost"/> 对
        /// <c>control</c> 效果 <c>category</c> 子字段的按类别免疫分支——本假实现显式覆盖接口默认
        /// 实现，不依赖"存在任意标志位免疫即视为全部类别免疫"那条默认转发语义，能表达"只免疫某个
        /// 具体类别"这类更精细的测试场景。</summary>
        private readonly HashSet<(Id unit, string category)> _controlCategoryImmunity = new HashSet<(Id, string)>();

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

        public FakeStaticImmunityProvider SetControlCategoryImmune(Id unitId, string category)
        {
            _controlCategoryImmunity.Add((unitId, category));
            return this;
        }

        public bool IsImmune(Id unitId, Id school, EffectKind kind) => _immune.Contains((unitId, school, kind));

        public ControlFlags GetControlImmunity(Id unitId) =>
            _controlImmunity.TryGetValue(unitId, out var flags) ? flags : ControlFlags.None;

        public bool IsControlCategoryImmune(Id unitId, string category) =>
            _controlCategoryImmunity.Contains((unitId, category));
    }
}
