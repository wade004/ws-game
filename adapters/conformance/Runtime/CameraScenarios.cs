#nullable enable
// CameraScenarios：ICamera 契约一致性场景（见 02_引擎适配层.md 第 1.13 节 / ADR-0016 决策 4）。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class CameraScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<ICamera>> All = new[]
        {
            new ConformanceScenario<ICamera>("Configure_SetZoom_夹紧到区间内", Configure_ThenSetZoom_ClampsToRange),
            new ConformanceScenario<ICamera>("WorldToScreen_ScreenToWorld_往返一致", WorldToScreen_ThenScreenToWorld_RoundTrips),
            new ConformanceScenario<ICamera>("Shake_接受frequency参数不抛异常", Shake_DoesNotThrow),
        };

        private static IEnumerator Configure_ThenSetZoom_ClampsToRange(ICamera camera, IConformanceAssert assert, ConformanceContext ctx)
        {
            camera.Configure(pitchDegrees: 45, yawDegrees: 0, zoomRange: new ZoomRange(2, 8));

            assert.DoesNotThrow(() => camera.SetZoom(5), "SetZoom（区间内）不应抛异常");
            assert.DoesNotThrow(() => camera.SetZoom(100), "SetZoom（超出上限，应被夹紧而不是抛异常）不应抛异常");
            assert.DoesNotThrow(() => camera.SetZoom(-100), "SetZoom（超出下限，应被夹紧而不是抛异常）不应抛异常");
            yield break;
        }

        private static IEnumerator WorldToScreen_ThenScreenToWorld_RoundTrips(ICamera camera, IConformanceAssert assert, ConformanceContext ctx)
        {
            camera.Configure(pitchDegrees: 45, yawDegrees: 0, zoomRange: new ZoomRange(1, 20));
            camera.SetZoom(5);

            var worldPoint = new Vec2(3, 4);
            var screen = camera.WorldToScreen(worldPoint, height: 0);
            var backToWorld = camera.ScreenToWorld(screen);

            assert.NotNull(backToWorld, "WorldToScreen 的结果反投影（ScreenToWorld）不应返回 null（地面平面上的点必然有交点）");
            if (backToWorld != null)
            {
                var dx = System.Math.Abs(backToWorld.Value.X - worldPoint.X);
                var dy = System.Math.Abs(backToWorld.Value.Y - worldPoint.Y);
                assert.True(dx < 0.1 && dy < 0.1, $"WorldToScreen 再 ScreenToWorld 往返应接近原点：原点={worldPoint}，往返结果={backToWorld.Value}");
            }
            yield break;
        }

        private static IEnumerator Shake_DoesNotThrow(ICamera camera, IConformanceAssert assert, ConformanceContext ctx)
        {
            camera.Configure(pitchDegrees: 45, yawDegrees: 0, zoomRange: new ZoomRange(1, 20));
            assert.DoesNotThrow(
                () => camera.Shake(intensity: 0.5, durationSeconds: 0.3, frequency: 20.0),
                "Shake(intensity, durationSeconds, frequency) 不应抛异常（frequency 是 ADR-0016 决策 4 新增参数）");
            yield break;
        }
    }
}
