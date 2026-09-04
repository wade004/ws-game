using System;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    public class EffectKindNamesTests
    {
        [Theory]
        [InlineData(EffectKind.SchoolDamage, "school_damage")]
        [InlineData(EffectKind.WeaponDamagePct, "weapon_damage_pct")]
        [InlineData(EffectKind.Script, "script")]
        [InlineData(EffectKind.SetWorldFlag, "set_world_flag")]
        public void ToText_And_Parse_RoundTrip(EffectKind kind, string text)
        {
            Assert.Equal(text, EffectKindNames.ToText(kind));
            Assert.Equal(kind, EffectKindNames.Parse(text));
        }

        [Fact]
        public void TryParse_UnknownText_ReturnsFalse()
        {
            var ok = EffectKindNames.TryParse("not_a_real_effect", out var kind);

            Assert.False(ok);
            Assert.Equal(default(EffectKind), kind);
        }

        [Fact]
        public void Parse_UnknownText_Throws()
        {
            Assert.Throws<ArgumentException>(() => EffectKindNames.Parse("bogus"));
        }

        [Fact]
        public void TryParse_Null_ReturnsFalse()
        {
            Assert.False(EffectKindNames.TryParse(null, out _));
        }
    }
}
