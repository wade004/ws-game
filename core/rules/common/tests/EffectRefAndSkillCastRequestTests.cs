using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    public class EffectRefAndSkillCastRequestTests
    {
        [Fact]
        public void EffectRef_DefaultParams_IsEmptyNotNull()
        {
            var effectRef = new EffectRef(EffectKind.Heal);

            Assert.NotNull(effectRef.Params);
            Assert.Empty(effectRef.Params);
        }

        [Fact]
        public void SkillCastRequest_DefaultTargets_IsEmptyList()
        {
            var request = new SkillCastRequest(new Id("unit.hero"), new Id("skill.fireball"));

            Assert.Empty(request.Targets);
            Assert.Null(request.TargetPoint);
        }

        [Fact]
        public void SkillCastRequest_WithTargetPoint_RoundTrips()
        {
            var point = new Vec2(1, 2);

            var request = new SkillCastRequest(new Id("unit.hero"), new Id("skill.teleport"), targetPoint: point);

            Assert.Equal(point, request.TargetPoint);
        }
    }
}
