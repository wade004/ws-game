using System.Collections.Generic;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    public class ResolveResultTests
    {
        [Fact]
        public void Steps_AreDefensivelyCopied_MutatingSourceListDoesNotAffectResult()
        {
            var steps = new List<string> { "base=10" };

            var result = new ResolveResult(HitResult.Hit, 10, 10, 0, false, false, steps);
            steps.Add("crit=false");

            Assert.NotNull(result.Steps);
            Assert.Single(result.Steps!);
            Assert.Equal("base=10", result.Steps![0]);
        }

        [Fact]
        public void Steps_NullWhenNotProvided()
        {
            var result = new ResolveResult(HitResult.Miss, 10, 0, 0, false, false);

            Assert.Null(result.Steps);
        }

        [Fact]
        public void Fields_ExposeConstructorValues()
        {
            var result = new ResolveResult(HitResult.Crit, 100, 150, 20, false, true);

            Assert.Equal(HitResult.Crit, result.Hit);
            Assert.Equal(100, result.RequestedAmount);
            Assert.Equal(150, result.FinalAmount);
            Assert.Equal(20, result.Absorbed);
            Assert.False(result.Immune);
            Assert.True(result.IsHeal);
        }
    }
}
