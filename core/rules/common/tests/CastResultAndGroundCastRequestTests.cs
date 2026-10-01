using System;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    /// <summary>
    /// T-L10（测试覆盖剩余项 2026-10-01）：<see cref="CastResult"/> 的值相等性与手写哈希、
    /// <see cref="GroundCastRequest"/> 的构造缺省值。
    /// </summary>
    public class CastResultAndGroundCastRequestTests
    {
        private static readonly Id InstA = new Id("cast.instance_a");
        private static readonly Id InstB = new Id("cast.instance_b");

        [Fact]
        public void CastResult_EqualOkResults_AreEqual_WithSameHash()
        {
            var a = CastResult.Ok(InstA);
            var b = CastResult.Ok(new Id(InstA.Value));

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.True(a.Equals((object)b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void CastResult_OkWithDifferentInstanceId_IsNotEqual()
        {
            Assert.NotEqual(CastResult.Ok(InstA), CastResult.Ok(InstB));
            Assert.True(CastResult.Ok(InstA) != CastResult.Ok(InstB));
        }

        [Fact]
        public void CastResult_FailWithSameReason_AreEqual_WithSameHash()
        {
            var a = CastResult.Fail(CastFailureReason.OnCooldown);
            var b = CastResult.Fail(CastFailureReason.OnCooldown);

            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void CastResult_FailWithDifferentReason_IsNotEqual()
        {
            Assert.NotEqual(
                CastResult.Fail(CastFailureReason.OnCooldown),
                CastResult.Fail(CastFailureReason.OutOfRange));
        }

        [Fact]
        public void CastResult_OkAndFail_AreNeverEqual()
        {
            Assert.NotEqual(CastResult.Ok(InstA), CastResult.Fail(CastFailureReason.OnCooldown));
            Assert.False(CastResult.Ok(InstA).Equals("not a result"));
            Assert.False(CastResult.Ok(InstA).Equals(null));
        }

        [Fact]
        public void CastResult_DefaultValue_IsFailureWithNoReasonAndNoInstance()
        {
            var result = default(CastResult);

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.None, result.Reason);
            Assert.Null(result.CastInstanceId);
        }

        [Fact]
        public void GroundCastRequest_Defaults_AreExplicitSourceAtRequestSnapshotNoSampler()
        {
            var point = new Vec2(3, 4);

            var request = new GroundCastRequest(point);

            Assert.Equal(point, request.Point);
            Assert.Equal(GroundCastSource.Explicit, request.Source);
            Assert.Equal(0.0, request.RequestedAtTick);
            Assert.Equal(GroundCastSnapshotPolicy.AtRequest, request.SnapshotPolicy);
            Assert.Null(request.Sampler);
        }

        [Fact]
        public void GroundCastRequest_StoresAllSuppliedFields_AndSamplerIsTheSameDelegate()
        {
            Func<Vec2> sampler = () => new Vec2(9, 9);

            var request = new GroundCastRequest(
                new Vec2(1, 2), GroundCastSource.PointerAtRequest, requestedAtTick: 42.5,
                snapshotPolicy: GroundCastSnapshotPolicy.AtRelease, sampler: sampler);

            Assert.Equal(GroundCastSource.PointerAtRequest, request.Source);
            Assert.Equal(42.5, request.RequestedAtTick);
            Assert.Equal(GroundCastSnapshotPolicy.AtRelease, request.SnapshotPolicy);
            Assert.Same(sampler, request.Sampler);
            Assert.Equal(new Vec2(9, 9), request.Sampler!());
        }
    }
}
