using Core.Foundation.Common;
using Core.Foundation.SaveSystem;
using Presentation.Assembly;
using Presentation.Camera;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// ADR-0121 收口：D6（相机跟随目标丢失）与 D10（读档场景路由失败）的诊断经真实
    /// <see cref="PresentationAssembly"/> 落进装配层共享诊断（<see cref="PresentationAssembly.FeedbackSinkDiagnostics"/>，
    /// adapters/unity 的 PresentationAssemblyDiagnosticsForwarder 已轮询的同一实例）。期望值由用例内的
    /// 实体 id / 地图 id 常量算出，不写裸数。
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        [Fact]
        public void Camera_And_Shell_Diagnostics_Are_The_Assembly_Shared_Instance_ADR0121()
        {
            var presentation = Build(out _, out _, out _, out _);

            Assert.Same(presentation.FeedbackSinkDiagnostics, presentation.Camera.Diagnostics);
            Assert.Same(presentation.FeedbackSinkDiagnostics, presentation.Shell.Diagnostics);
        }

        [Fact]
        public void Camera_FollowTargetLost_IsRecordedOnAssemblySharedDiagnostics_ADR0121_D6()
        {
            var presentation = Build(out _, out _, out _, out _);
            var shared = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.FeedbackSinkDiagnostics);
            var missingEntity = new Id("unit.d6_never_spawned");
            presentation.Camera.Configure(new CameraProfile(new Id("camera_profile.d6_wiring"), 45, 0, 5, 15, 10, 0.2));
            presentation.Camera.Follow(missingEntity);
            var before = shared.Warnings.Count;

            presentation.Camera.Update(0.5);
            presentation.Camera.Update(0.5);

            Assert.Equal(before + 1, shared.Warnings.Count);
            Assert.Contains(missingEntity.ToString(), shared.Warnings[shared.Warnings.Count - 1]);
        }

        [Fact]
        public void Shell_LoadGameSceneRouteFailure_IsRecordedOnAssemblySharedDiagnostics_ADR0121_D10()
        {
            var unknownMap = new Id("world.d10_not_registered");
            var options = new PresentationAssemblyOptions { LoadedMapIdResolver = () => unknownMap };
            var presentation = Build(out _, out _, out _, out _, options);
            var shared = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.FeedbackSinkDiagnostics);
            var slot = new Id("save.d10_wiring");
            presentation.SaveSystem.Save(new SaveRequest(slot, "2026-10-01T00:00:00Z"));
            var before = shared.Warnings.Count;

            var result = presentation.Shell.LoadGame(slot);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.True(result.SceneRouteFailed);
            Assert.Equal(before + 1, shared.Warnings.Count);
            Assert.Contains(unknownMap.ToString(), shared.Warnings[shared.Warnings.Count - 1]);
        }
    }
}
