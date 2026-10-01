using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.StubAdapters
{
    public class StubCameraTests
    {
        [Fact]
        public void InitialState_NotConfigured_ZoomIsIdentity()
        {
            var camera = new StubCamera();
            Assert.False(camera.Configured);
            Assert.Equal(1.0, camera.Zoom);
            Assert.Equal(Vec2.Zero, camera.FollowTarget);
        }

        [Fact]
        public void Configure_RecordsAllThreeArguments_AndMarksConfigured()
        {
            var camera = new StubCamera();
            camera.Configure(35.0, 45.0, new ZoomRange(0.5, 3.0));

            Assert.True(camera.Configured);
            Assert.Equal(35.0, camera.PitchDegrees);
            Assert.Equal(45.0, camera.YawDegrees);
            Assert.Equal(0.5, camera.ZoomRange.Min);
            Assert.Equal(3.0, camera.ZoomRange.Max);
        }

        [Fact]
        public void Configure_Twice_LatestWins()
        {
            var camera = new StubCamera();
            camera.Configure(10, 20, new ZoomRange(1, 2));
            camera.Configure(30, 40, new ZoomRange(3, 4));

            Assert.Equal(30, camera.PitchDegrees);
            Assert.Equal(40, camera.YawDegrees);
            Assert.Equal(3, camera.ZoomRange.Min);
            Assert.Equal(4, camera.ZoomRange.Max);
        }

        [Fact]
        public void Follow_RecordsTargetAndSmoothing()
        {
            var camera = new StubCamera();
            camera.Follow(new Vec2(7, -2), 0.35);
            Assert.Equal(new Vec2(7, -2), camera.FollowTarget);
            Assert.Equal(0.35, camera.FollowSmoothing);
        }

        [Fact]
        public void SetZoom_StoresValueAsIs_NoClampAgainstConfiguredRange()
        {
            var camera = new StubCamera();
            camera.Configure(0, 0, new ZoomRange(0.5, 2.0));
            camera.SetZoom(10.0);
            Assert.Equal(10.0, camera.Zoom);
            camera.SetZoom(0.01);
            Assert.Equal(0.01, camera.Zoom);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(1.5, -2.5, 0)]
        [InlineData(-100, 42, 3.0)]
        public void WorldToScreen_IsPlanePositionIdentity_IgnoringHeightAndPitch(double x, double y, double height)
        {
            var camera = new StubCamera();
            camera.Configure(60.0, 90.0, new ZoomRange(1, 1));
            var world = new Vec2(x, y);

            Assert.Equal(world, camera.WorldToScreen(world, height));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(320, 240)]
        [InlineData(-5.5, 9.25)]
        public void ScreenToWorld_NeverNull_AndRoundTripsWithWorldToScreen(double x, double y)
        {
            var camera = new StubCamera();
            var point = new Vec2(x, y);

            var world = camera.ScreenToWorld(point);
            Assert.NotNull(world);
            Assert.Equal(point, camera.WorldToScreen(world!.Value, 0));
        }

        [Fact]
        public void Shake_RecordsLatestIntensityDurationAndFrequency()
        {
            var camera = new StubCamera();
            Assert.Equal(0.0, camera.LastShakeIntensity);

            camera.Shake(0.4, 0.2, 30);
            camera.Shake(0.8, 0.5, 15);

            Assert.Equal(0.8, camera.LastShakeIntensity);
            Assert.Equal(0.5, camera.LastShakeDurationSeconds);
            Assert.Equal(15, camera.LastShakeFrequency);
        }
    }
}
