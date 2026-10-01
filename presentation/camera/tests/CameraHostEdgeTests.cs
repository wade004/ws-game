using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Encounter;
using Presentation.Camera;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.PresentationCamera
{
    /// <summary>
    /// 测试覆盖剩余项 T-M29：<see cref="CameraHost"/> 单测层缺口——<c>PhaseProfileSwitch</c> 指向未登记 profile、
    /// <c>SetZoom</c> 随 <c>SwitchProfile</c> 的范围变化、<c>Follow</c> 早于 <c>Configure</c>、<c>Update</c> 在 Dispose 后、
    /// 重置回调（resolver）返回 null / 顺序不变量 / 抛异常、<c>ResetFollowOnSceneLoadFinished=false</c> 开关。
    /// 期望值由 profile 的缩放范围与跟随目标位置规则算出。
    /// </summary>
    public class CameraHostEdgeTests
    {
        private static readonly Id DefaultProfileId = new Id("camera_profile.default");
        private static readonly Id WideProfileId = new Id("camera_profile.wide");
        private static readonly Id NarrowProfileId = new Id("camera_profile.narrow");
        private static readonly Id Player = new Id("unit.player_1");
        private static readonly Id Other = new Id("unit.other_1");

        /// <summary>记录每次 Configure/Follow/SetZoom 调用的 ICamera 桩（StubCamera 只保存最近一次，数不出调用次数）。</summary>
        private sealed class RecordingCamera : ICamera
        {
            public readonly List<string> Order = new List<string>();
            public int ConfigureCalls;
            public int FollowCalls;
            public readonly List<double> Zooms = new List<double>();
            public Vec2 LastFollow;
            public ZoomRange LastRange;

            public void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange)
            {
                ConfigureCalls++;
                Order.Add("configure");
                LastRange = zoomRange;
            }

            public void Follow(Vec2 planePos, double smoothing)
            {
                FollowCalls++;
                LastFollow = planePos;
            }

            public void SetZoom(double zoom)
            {
                Zooms.Add(zoom);
                Order.Add("zoom");
            }

            public Vec2 WorldToScreen(Vec2 planePos, double height) => planePos;

            public Vec2? ScreenToWorld(Vec2 screen) => screen;

            public void Shake(double intensity, double durationSeconds, double frequency)
            {
            }
        }

        /// <summary>只认名单内实体的跟随目标：名单外 <c>TryGetPosition</c> 返回 false。</summary>
        private sealed class DictionaryFollowTarget : ICameraFollowTarget
        {
            public readonly Dictionary<Id, Vec2> Positions = new Dictionary<Id, Vec2>();

            public Vec2 GetPosition(Id entityId, double alpha) => Positions[entityId];

            public bool TryGetPosition(Id entityId, double alpha, out Vec2 position) =>
                Positions.TryGetValue(entityId, out position);
        }

        private static EventBus CreateBus(out InMemoryEventDiagnostics busDiagnostics)
        {
            busDiagnostics = new InMemoryEventDiagnostics();
            var catalog = EventCatalog.FromDefinitions(Array.Empty<EventDefinition>());
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false }, busDiagnostics);
        }

        private static CameraProfile Profile(Id id, double zoomMin, double zoomMax, double zoomDefault, CameraBounds? bounds = null) =>
            new CameraProfile(id, pitchDegrees: 50, yawDegrees: 0, zoomMin, zoomMax, zoomDefault, followLerp: 0.3, bounds: bounds);

        private static CameraProfile DefaultProfile() => Profile(DefaultProfileId, zoomMin: 5, zoomMax: 20, zoomDefault: 10);

        // ------------------------------------------------------------------ PhaseProfileSwitch 指向未登记 profile

        [Fact]
        public void EncounterPhaseChanged_MappedToUnregisteredProfile_IsContainedByBus_CameraUntouched()
        {
            var bus = CreateBus(out var busDiagnostics);
            var camera = new RecordingCamera();
            var missing = new Id("camera_profile.never_registered");
            var options = new CameraHostOptions(phaseProfileSwitch: new Dictionary<int, Id> { [1] = missing });
            var host = new CameraHost(camera, new DictionaryFollowTarget(), bus, options);
            host.Configure(DefaultProfile());
            var configuresBefore = camera.ConfigureCalls;
            var zoomsBefore = camera.Zooms.Count;

            var thrown = Record.Exception(() =>
                bus.PublishImmediate(new EncounterPhaseChangedEvent(new Id("encounter.inst_1"), oldPhase: 0, newPhase: 1)));

            Assert.Null(thrown); // 订阅者异常由总线隔离，不向发布方冒泡。
            Assert.Equal(DefaultProfileId, host.CurrentProfile?.Id); // 档位保持不变。
            Assert.Equal(configuresBefore, camera.ConfigureCalls); // 相机没有被重新配置。
            Assert.Equal(zoomsBefore, camera.Zooms.Count);
            var error = Assert.Single(busDiagnostics.Errors);
            Assert.IsType<ArgumentException>(error.Exception);
            Assert.Contains(missing.Value, error.Exception!.Message);
        }

        [Fact]
        public void EncounterPhaseChanged_UnregisteredProfile_DoesNotPreventLaterValidSwitch()
        {
            var bus = CreateBus(out _);
            var camera = new RecordingCamera();
            var options = new CameraHostOptions(phaseProfileSwitch: new Dictionary<int, Id>
            {
                [1] = new Id("camera_profile.never_registered"),
                [2] = WideProfileId,
            });
            var host = new CameraHost(camera, new DictionaryFollowTarget(), bus, options);
            host.Configure(DefaultProfile());
            host.RegisterProfile(Profile(WideProfileId, zoomMin: 1, zoomMax: 3, zoomDefault: 2));

            bus.PublishImmediate(new EncounterPhaseChangedEvent(new Id("encounter.inst_1"), oldPhase: 0, newPhase: 1));
            bus.PublishImmediate(new EncounterPhaseChangedEvent(new Id("encounter.inst_1"), oldPhase: 1, newPhase: 2));

            Assert.Equal(WideProfileId, host.CurrentProfile?.Id);
        }

        // ------------------------------------------------------------------ SetZoom 随 SwitchProfile 范围变化

        [Fact]
        public void SetZoom_AfterSwitchProfile_ClampsToTheNewProfileRange()
        {
            var camera = new RecordingCamera();
            var host = new CameraHost(camera, new DictionaryFollowTarget());
            var wide = Profile(WideProfileId, zoomMin: 2, zoomMax: 40, zoomDefault: 10);
            var narrow = Profile(NarrowProfileId, zoomMin: 1, zoomMax: 3, zoomDefault: 2);
            host.Configure(wide);
            host.RegisterProfile(narrow);

            host.SetZoom(25);
            Assert.Equal(25, camera.Zooms.Last()); // 宽档范围内原样通过。

            host.SwitchProfile(NarrowProfileId);
            Assert.Equal(narrow.ZoomDefault, camera.Zooms.Last()); // 切档立即应用新档默认缩放。

            host.SetZoom(25);
            Assert.Equal(narrow.ZoomMax, camera.Zooms.Last()); // 同一请求值被新档上限夹住。
            host.SetZoom(-5);
            Assert.Equal(narrow.ZoomMin, camera.Zooms.Last());

            host.SwitchProfile(WideProfileId);
            host.SetZoom(25);
            Assert.Equal(25, camera.Zooms.Last()); // 切回宽档，范围随之恢复。
        }

        [Fact]
        public void Configure_ReplacesProfileAndRangeSeenByICamera_EachTime()
        {
            var camera = new RecordingCamera();
            var host = new CameraHost(camera, new DictionaryFollowTarget());
            var first = Profile(WideProfileId, zoomMin: 2, zoomMax: 40, zoomDefault: 10);
            var second = Profile(NarrowProfileId, zoomMin: 1, zoomMax: 3, zoomDefault: 2);

            host.Configure(first);
            Assert.Equal(first.ZoomMin, camera.LastRange.Min);
            Assert.Equal(first.ZoomMax, camera.LastRange.Max);

            host.Configure(second);
            Assert.Equal(second.ZoomMin, camera.LastRange.Min);
            Assert.Equal(second.ZoomMax, camera.LastRange.Max);
            Assert.Equal(2, camera.ConfigureCalls);
        }

        [Fact]
        public void Configure_AppliesRangeBeforeDefaultZoom_SoTheDefaultIsNeverClampedByAStaleRange()
        {
            var camera = new RecordingCamera();
            var host = new CameraHost(camera, new DictionaryFollowTarget());
            var profile = DefaultProfile();

            host.Configure(profile);

            Assert.Equal(new[] { "configure", "zoom" }, camera.Order);
            Assert.Equal(profile.ZoomDefault, camera.Zooms.Single());
        }

        [Fact]
        public void RegisterProfile_NullProfile_ThrowsArgumentNull()
        {
            var host = new CameraHost(new RecordingCamera(), new DictionaryFollowTarget());

            Assert.Throws<ArgumentNullException>(() => host.RegisterProfile(null!));
        }

        // ------------------------------------------------------------------ Follow 早于 Configure

        [Fact]
        public void Follow_BeforeConfigure_RecordsTargetAndUpdateStartsFollowingOnceConfigured()
        {
            var camera = new RecordingCamera();
            var target = new DictionaryFollowTarget();
            target.Positions[Player] = new Vec2(6, -3);
            var host = new CameraHost(camera, target);

            host.Follow(Player);
            host.Update(0.5);

            Assert.Equal(Player, host.FollowEntityId);
            Assert.Equal(0, camera.FollowCalls); // 尚无 profile：不跟随（也不抛）。

            host.Configure(DefaultProfile());
            host.Update(0.5);

            Assert.Equal(1, camera.FollowCalls);
            Assert.Equal(target.Positions[Player], camera.LastFollow);
        }

        // ------------------------------------------------------------------ Update 在 Dispose 后

        [Fact]
        public void Update_AfterDispose_StillFollows_DisposeOnlyDetachesEventSubscriptions()
        {
            var bus = CreateBus(out _);
            var camera = new RecordingCamera();
            var target = new DictionaryFollowTarget();
            target.Positions[Player] = new Vec2(1, 2);
            var host = new CameraHost(camera, target, bus);
            host.Configure(DefaultProfile());
            host.Follow(Player);
            host.Dispose();

            var thrown = Record.Exception(() => host.Update(0.5));

            Assert.Null(thrown);
            Assert.Equal(1, camera.FollowCalls);
            Assert.Equal(target.Positions[Player], camera.LastFollow);
        }

        // ------------------------------------------------------------------ resolver（FollowTargetResolverOnReset）

        [Fact]
        public void SceneLoadFinished_ResolverReturnsNull_LeavesNoFollowTarget_AndPreviousTargetIsCleared()
        {
            var bus = CreateBus(out _);
            var calls = 0;
            var options = new CameraHostOptions(true, null, () => { calls++; return null; });
            var host = new CameraHost(new RecordingCamera(), new DictionaryFollowTarget(), bus, options);
            host.Configure(DefaultProfile());
            host.Follow(Player);

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("map.next")));

            Assert.Equal(1, calls);
            Assert.Null(host.FollowEntityId);
        }

        [Fact]
        public void SceneLoadFinished_ResolverRunsAfterReset_AndItsResultReplacesTheOldTarget()
        {
            var bus = CreateBus(out _);
            CameraHost? host = null;
            Id? followIdSeenByResolver = Player; // 哨兵：若 resolver 没被调用，会保持非 null。
            var options = new CameraHostOptions(true, null, () =>
            {
                followIdSeenByResolver = host!.FollowEntityId;
                return Other;
            });
            host = new CameraHost(new RecordingCamera(), new DictionaryFollowTarget(), bus, options);
            host.Configure(DefaultProfile());
            host.Follow(Player);

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("map.next")));

            Assert.Null(followIdSeenByResolver); // 顺序不变量：resolver 被调用时旧目标已先清空。
            Assert.Equal(Other, host.FollowEntityId); // 随后才落入 resolver 的结果。
        }

        [Fact]
        public void SceneLoadFinished_ResetClearsLastPosition_SoNewTargetThatIsMissingDoesNotMoveCameraToTheOldOne()
        {
            var bus = CreateBus(out _);
            var camera = new RecordingCamera();
            var target = new DictionaryFollowTarget();
            target.Positions[Player] = new Vec2(9, 9);
            // Other 不在名单内：TryGetPosition 恒为 false。
            var options = new CameraHostOptions(true, null, () => Other);
            var host = new CameraHost(camera, target, bus, options);
            host.Configure(DefaultProfile());
            host.Follow(Player);
            host.Update(0.5);
            Assert.Equal(1, camera.FollowCalls);

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("map.next")));
            host.Update(0.5);

            Assert.Equal(Other, host.FollowEntityId);
            Assert.Equal(1, camera.FollowCalls); // 旧目标的最后位置已随重置丢弃，不会被拿去喂给新目标。
        }

        [Fact]
        public void SceneLoadFinished_ResetRearmsLossDiagnostic()
        {
            var bus = CreateBus(out _);
            var target = new DictionaryFollowTarget(); // 空名单：Player 一开始就丢失。
            var host = new CameraHost(new RecordingCamera(), target, bus, new CameraHostOptions(true, null, () => Player));
            host.Configure(DefaultProfile());
            host.Follow(Player);
            var recorder = (PresentationDiagnosticsRecorder)host.Diagnostics;

            host.Update(0.5);
            host.Update(0.5);
            Assert.Single(recorder.Warnings);

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("map.next"))); // 重置并重新指向同一 id。
            host.Update(0.5);

            Assert.Equal(2, recorder.Warnings.Count); // 去重标志被复位：新场景里的丢失是新的一次。
        }

        [Fact]
        public void SceneLoadFinished_ResolverThrows_FollowStaysCleared_ErrorIsolatedByBus()
        {
            var bus = CreateBus(out var busDiagnostics);
            var options = new CameraHostOptions(true, null, () => throw new InvalidOperationException("resolver boom"));
            var host = new CameraHost(new RecordingCamera(), new DictionaryFollowTarget(), bus, options);
            host.Configure(DefaultProfile());
            host.Follow(Player);

            var thrown = Record.Exception(() => bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("map.next"))));

            Assert.Null(thrown);
            Assert.Null(host.FollowEntityId); // 重置先于 resolver 执行，resolver 抛出后仍保持“已重置”。
            var error = Assert.Single(busDiagnostics.Errors);
            Assert.IsType<InvalidOperationException>(error.Exception);
        }

        // ------------------------------------------------------------------ 关闭重置开关

        [Fact]
        public void SceneLoadFinished_ResetDisabled_KeepsFollowTargetAndNeverCallsResolver()
        {
            var bus = CreateBus(out _);
            var calls = 0;
            var options = new CameraHostOptions(false, null, () => { calls++; return Other; });
            var camera = new RecordingCamera();
            var target = new DictionaryFollowTarget();
            target.Positions[Player] = new Vec2(4, 4);
            var host = new CameraHost(camera, target, bus, options);
            host.Configure(DefaultProfile());
            host.Follow(Player);
            host.Update(0.5);

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("map.next")));
            host.Update(0.5);

            Assert.Equal(Player, host.FollowEntityId);
            Assert.Equal(0, calls);
            Assert.Equal(2, camera.FollowCalls); // 加载完成前后都仍在跟随原目标。
        }

        [Fact]
        public void SceneLoadFinished_ResetDisabled_StillAllowsPhaseSwitchSubscription()
        {
            var bus = CreateBus(out _);
            var options = new CameraHostOptions(false, new Dictionary<int, Id> { [1] = WideProfileId });
            var host = new CameraHost(new RecordingCamera(), new DictionaryFollowTarget(), bus, options);
            host.Configure(DefaultProfile());
            host.RegisterProfile(Profile(WideProfileId, zoomMin: 1, zoomMax: 3, zoomDefault: 2));

            bus.PublishImmediate(new EncounterPhaseChangedEvent(new Id("encounter.inst_1"), oldPhase: 0, newPhase: 1));

            Assert.Equal(WideProfileId, host.CurrentProfile?.Id); // 开关只管跟随重置，不影响阶段切档。
        }

        [Fact]
        public void Follow_Twice_ReplacesTargetAndLastKnownPositionStartsOver()
        {
            var camera = new RecordingCamera();
            var target = new DictionaryFollowTarget();
            target.Positions[Player] = new Vec2(3, 3);
            var host = new CameraHost(camera, target);
            host.Configure(DefaultProfile());
            host.Follow(Player);
            host.Update(0.5);
            Assert.Equal(1, camera.FollowCalls);

            host.Follow(Other); // Other 不在名单内：既无当前位置，也不应沿用 Player 的最后位置。
            host.Update(0.5);

            Assert.Equal(Other, host.FollowEntityId);
            Assert.Equal(1, camera.FollowCalls);
        }
    }
}
