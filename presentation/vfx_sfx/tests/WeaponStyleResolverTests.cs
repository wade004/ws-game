using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class WeaponStyleResolverTests
    {
        private static readonly Id GreatswordStyle = new Id("display.weapon_style.greatsword");
        private static readonly Id CleaveSkill = new Id("skill.cleave");
        private static readonly Id FireballSkill = new Id("skill.fireball");

        private static WeaponStyleResolver BuildResolver()
        {
            var def = new WeaponStyleDef(
                GreatswordStyle,
                autoAttackAnim: new Id("anim.greatsword.auto_attack"),
                castAnimOverride: new Dictionary<Id, Id> { [CleaveSkill] = new Id("anim.greatsword.cleave") },
                swingVfx: new Id("vfx.greatsword_swing"),
                impactVfxOverride: new Dictionary<Id, Id> { [CleaveSkill] = new Id("vfx.cleave_impact") });

            return new WeaponStyleResolver(new Dictionary<Id, WeaponStyleDef> { [GreatswordStyle] = def });
        }

        [Fact]
        public void ResolveImpactVfxOverride_ReturnsOverride_ForRegisteredSkill()
        {
            var resolver = BuildResolver();

            Assert.Equal(new Id("vfx.cleave_impact"), resolver.ResolveImpactVfxOverride(GreatswordStyle, CleaveSkill));
        }

        [Fact]
        public void ResolveImpactVfxOverride_ReturnsNull_ForUnoverriddenSkill()
        {
            var resolver = BuildResolver();

            Assert.Null(resolver.ResolveImpactVfxOverride(GreatswordStyle, FireballSkill));
        }

        [Fact]
        public void ResolveImpactVfxOverride_ReturnsNull_ForUnknownWeaponStyle()
        {
            var resolver = BuildResolver();

            Assert.Null(resolver.ResolveImpactVfxOverride(new Id("display.weapon_style.unknown"), CleaveSkill));
        }

        [Fact]
        public void ResolveSwingVfx_ReturnsRegisteredValue()
        {
            var resolver = BuildResolver();

            Assert.Equal(new Id("vfx.greatsword_swing"), resolver.ResolveSwingVfx(GreatswordStyle));
        }
    }
}
