using System;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    public class CastResultTests
    {
        [Fact]
        public void Ok_ProducesSuccessWithInstanceIdAndNoneReason()
        {
            var instanceId = new Id("cast.instance_1");

            var result = CastResult.Ok(instanceId);

            Assert.True(result.Success);
            Assert.Equal(CastFailureReason.None, result.Reason);
            Assert.Equal(instanceId, result.CastInstanceId);
        }

        [Fact]
        public void Fail_ProducesFailureWithNullInstanceId()
        {
            var result = CastResult.Fail(CastFailureReason.OnCooldown);

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.OnCooldown, result.Reason);
            Assert.Null(result.CastInstanceId);
        }

        [Fact]
        public void Fail_WithNoneReason_Throws()
        {
            Assert.Throws<ArgumentException>(() => CastResult.Fail(CastFailureReason.None));
        }
    }
}
