using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Encounter;
using Presentation.Camera;
using Xunit;

namespace Tests.PresentationCamera
{
    /// <summary>记录最近一次查询参数的最小假 <see cref="ICameraFollowTarget"/> 实现。</summary>
    internal sealed class FakeFollowTarget : ICameraFollowTarget
    {
        public Vec2 NextPosition { get; set; }

        public Id? LastEntityId { get; private set; }

        public double LastAlpha { get; private set; }

        public Vec2 GetPosition(Id entityId, double alpha)
        {
            LastEntityId = entityId;
            LastAlpha = alpha;
            return NextPosition;
        }
    }

    public class CameraHostTests
    {
        private static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(Array.Empty<EventDefinition>());
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }

        private static CameraProfile MakeProfile(Id id, CameraBounds? bounds = null, IReadOnlyList<ShakePreset>? shakes = null) =>
            new CameraProfile(id, pitchDegrees: 55, yawDegrees: 0, zoomMin: 5, zoomMax: 20, zoomDefault: 10, followLerp: 0.2, bounds: bounds, shakePresets: shakes);

        [Fact]
        public void Configure_CallsICameraConfigureAndAppliesDefaultZoom()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());

            host.Configure(MakeProfile(new Id("camera_profile.default")));

            Assert.True(camera.Configured);
            Assert.Equal(55, camera.PitchDegrees);
            Assert.Equal(0, camera.YawDegrees);
            Assert.Equal(5, camera.ZoomRange.Min);
            Assert.Equal(20, camera.ZoomRange.Max);
            Assert.Equal(10, camera.Zoom);
        }

        [Fact]
        public void Follow_ThenUpdate_CallsICameraFollowWithResolvedPosition()
        {
            var camera = new StubCamera();
            var followTarget = new FakeFollowTarget { NextPosition = new Vec2(3, 4) };
            var host = new CameraHost(camera, followTarget);
            host.Configure(MakeProfile(new Id("camera_profile.default")));

            host.Follow(new Id("unit.player_1"));
            host.Update(0.5);

            Assert.Equal(new Vec2(3, 4), camera.FollowTarget);
            Assert.Equal(0.2, camera.FollowSmoothing);
            Assert.Equal(new Id("unit.player_1"), followTarget.LastEntityId);
            Assert.Equal(0.5, followTarget.LastAlpha);
        }

        [Fact]
        public void Update_WithoutConfigure_DoesNothing()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());
            host.Follow(new Id("unit.player_1"));

            var ex = Record.Exception(() => host.Update(0.5));

            Assert.Null(ex);
            Assert.False(camera.Configured);
        }

        [Fact]
        public void Update_WithoutFollow_DoesNothing()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());
            host.Configure(MakeProfile(new Id("camera_profile.default")));

            host.Update(0.5);

            Assert.Equal(Vec2.Zero, camera.FollowTarget); // 未被设置过，仍是默认值
        }

        [Fact]
        public void Update_WithBounds_ClampsFollowPosition()
        {
            var camera = new StubCamera();
            var followTarget = new FakeFollowTarget { NextPosition = new Vec2(100, -100) };
            var bounds = new CameraBounds(new Vec2(-10, -10), new Vec2(10, 10));
            var host = new CameraHost(camera, followTarget);
            host.Configure(MakeProfile(new Id("camera_profile.default"), bounds: bounds));
            host.Follow(new Id("unit.player_1"));

            host.Update(0.0);

            Assert.Equal(new Vec2(10, -10), camera.FollowTarget);
        }

        [Fact]
        public void SetZoom_ClampsToProfileRange()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());
            host.Configure(MakeProfile(new Id("camera_profile.default")));

            host.SetZoom(999);
            Assert.Equal(20, camera.Zoom);

            host.SetZoom(-999);
            Assert.Equal(5, camera.Zoom);
        }

        [Fact]
        public void SetZoom_WithoutConfigure_Throws()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());

            Assert.Throws<InvalidOperationException>(() => host.SetZoom(10));
        }

        [Fact]
        public void Shake_KnownPreset_CallsICameraShakeWithAmplitudeAndDurationAndFrequency()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());
            var preset = new ShakePreset(new Id("shake.hit"), amplitude: 0.3, duration: 0.2, frequency: 10);
            host.Configure(MakeProfile(new Id("camera_profile.default"), shakes: new[] { preset }));

            host.Shake(new Id("shake.hit"));

            Assert.Equal(0.3, camera.LastShakeIntensity);
            Assert.Equal(0.2, camera.LastShakeDurationSeconds);
            Assert.Equal(10.0, camera.LastShakeFrequency);
        }

        [Fact]
        public void Shake_UnknownPreset_Throws()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());
            host.Configure(MakeProfile(new Id("camera_profile.default")));

            Assert.Throws<ArgumentException>(() => host.Shake(new Id("shake.missing")));
        }

        [Fact]
        public void SwitchProfile_KnownId_ReconfiguresCamera()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());
            host.Configure(MakeProfile(new Id("camera_profile.gameplay")));
            host.RegisterProfile(new CameraProfile(new Id("camera_profile.cutscene"), pitchDegrees: 30, yawDegrees: 90, zoomMin: 1, zoomMax: 2, zoomDefault: 1.5, followLerp: 0.9));

            host.SwitchProfile(new Id("camera_profile.cutscene"));

            Assert.Equal(30, camera.PitchDegrees);
            Assert.Equal(90, camera.YawDegrees);
            Assert.Equal(1.5, camera.Zoom);
        }

        [Fact]
        public void SwitchProfile_UnknownId_Throws()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());

            Assert.Throws<ArgumentException>(() => host.SwitchProfile(new Id("camera_profile.missing")));
        }

        [Fact]
        public void SceneLoadFinished_ResetsFollowTarget()
        {
            var camera = new StubCamera();
            var followTarget = new FakeFollowTarget { NextPosition = new Vec2(1, 1) };
            var bus = CreateBus();
            var host = new CameraHost(camera, followTarget, bus);
            host.Configure(MakeProfile(new Id("camera_profile.default")));
            host.Follow(new Id("unit.player_1"));
            Assert.Equal(new Id("unit.player_1"), host.FollowEntityId);

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("map.next")));

            Assert.Null(host.FollowEntityId);
        }

        [Fact]
        public void EncounterPhaseChanged_SwitchesProfile_WhenMapped()
        {
            var camera = new StubCamera();
            var bus = CreateBus();
            var phaseSwitch = new Dictionary<int, Id> { [1] = new Id("camera_profile.phase1") };
            var host = new CameraHost(camera, new FakeFollowTarget(), bus, new CameraHostOptions(phaseProfileSwitch: phaseSwitch));
            host.Configure(MakeProfile(new Id("camera_profile.default")));
            host.RegisterProfile(new CameraProfile(new Id("camera_profile.phase1"), pitchDegrees: 40, yawDegrees: 10, zoomMin: 1, zoomMax: 3, zoomDefault: 2, followLerp: 0.1));

            bus.PublishImmediate(new EncounterPhaseChangedEvent(new Id("encounter.inst_1"), oldPhase: 0, newPhase: 1));

            Assert.Equal(new Id("camera_profile.phase1"), host.CurrentProfile?.Id);
        }

        [Fact]
        public void EncounterPhaseChanged_NoMapping_DoesNotSwitch()
        {
            var camera = new StubCamera();
            var bus = CreateBus();
            var host = new CameraHost(camera, new FakeFollowTarget(), bus);
            host.Configure(MakeProfile(new Id("camera_profile.default")));

            var ex = Record.Exception(() => bus.PublishImmediate(new EncounterPhaseChangedEvent(new Id("encounter.inst_1"), oldPhase: 0, newPhase: 1)));

            Assert.Null(ex);
            Assert.Equal(new Id("camera_profile.default"), host.CurrentProfile?.Id);
        }

        // -----------------------------------------------------------------
        // 缺口 5（退订）：Dispose 后不再响应 scene.load_finished/encounter.phase_changed。
        // -----------------------------------------------------------------

        [Fact]
        public void Dispose_ThenSceneLoadFinishedEvent_DoesNotResetFollowTarget()
        {
            var camera = new StubCamera();
            var followTarget = new FakeFollowTarget { NextPosition = new Vec2(1, 1) };
            var bus = CreateBus();
            var host = new CameraHost(camera, followTarget, bus);
            host.Configure(MakeProfile(new Id("camera_profile.default")));
            host.Follow(new Id("unit.player_1"));
            host.Dispose();

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("map.next")));

            Assert.Equal(new Id("unit.player_1"), host.FollowEntityId);
        }

        [Fact]
        public void Dispose_ThenEncounterPhaseChangedEvent_DoesNotSwitchProfile()
        {
            var camera = new StubCamera();
            var bus = CreateBus();
            var phaseSwitch = new Dictionary<int, Id> { [1] = new Id("camera_profile.phase1") };
            var host = new CameraHost(camera, new FakeFollowTarget(), bus, new CameraHostOptions(phaseProfileSwitch: phaseSwitch));
            host.Configure(MakeProfile(new Id("camera_profile.default")));
            host.RegisterProfile(new CameraProfile(new Id("camera_profile.phase1"), pitchDegrees: 40, yawDegrees: 10, zoomMin: 1, zoomMax: 3, zoomDefault: 2, followLerp: 0.1));
            host.Dispose();

            bus.PublishImmediate(new EncounterPhaseChangedEvent(new Id("encounter.inst_1"), oldPhase: 0, newPhase: 1));

            Assert.Equal(new Id("camera_profile.default"), host.CurrentProfile?.Id);
        }

        [Fact]
        public void Dispose_IsIdempotent_CalledTwiceDoesNotThrow()
        {
            var camera = new StubCamera();
            var host = new CameraHost(camera, new FakeFollowTarget());

            var ex = Record.Exception(() =>
            {
                host.Dispose();
                host.Dispose();
            });

            Assert.Null(ex);
        }
    }
}
