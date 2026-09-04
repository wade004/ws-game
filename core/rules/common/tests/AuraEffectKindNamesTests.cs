using System;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    public class AuraEffectKindNamesTests
    {
        [Theory]
        [InlineData(AuraEffectKind.ModStat, "mod_stat")]
        [InlineData(AuraEffectKind.PeriodicDamage, "periodic_damage")]
        [InlineData(AuraEffectKind.OverrideSkill, "override_skill")]
        [InlineData(AuraEffectKind.Flag, "flag")]
        public void ToText_And_Parse_RoundTrip(AuraEffectKind kind, string text)
        {
            Assert.Equal(text, AuraEffectKindNames.ToText(kind));
            Assert.Equal(kind, AuraEffectKindNames.Parse(text));
        }

        [Fact]
        public void Parse_UnknownText_Throws()
        {
            Assert.Throws<ArgumentException>(() => AuraEffectKindNames.Parse("not_real"));
        }

        [Fact]
        public void TryParse_UnknownText_ReturnsFalseWithoutThrowing()
        {
            var ok = AuraEffectKindNames.TryParse("nope", out var kind);

            Assert.False(ok);
            Assert.Equal(default(AuraEffectKind), kind);
        }
    }
}
