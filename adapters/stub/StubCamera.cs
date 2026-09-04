// StubCamera：ICamera 的最小可用桩实现——只记录调用与保存最近状态，不做任何真实投影渲染。
// 用途：测试断言"镜头配置/跟随/缩放/震屏参数被设置成了什么"，而不启动任何渲染管线。
// 与真实实现的差异：WorldToScreen 用一个固定不变的正交投影近似（screen = planePos，
// 忽略 height 与俯仰角），仅用于让调用链可编译可运行，不代表任何真实镜头数学；
// ScreenToWorld 是其精确逆运算，因此互为可逆，便于测试断言"往返不变性"，但不代表真实
// 固定俯角镜头的投影关系（真实实现需要按 Configure 设定的俯仰/朝向做透视换算）。
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubCamera : ICamera
    {
        public bool Configured { get; private set; }
        public double PitchDegrees { get; private set; }
        public double YawDegrees { get; private set; }
        public ZoomRange ZoomRange { get; private set; }

        public Vec2 FollowTarget { get; private set; }
        public double FollowSmoothing { get; private set; }

        public double Zoom { get; private set; } = 1.0;

        public double LastShakeIntensity { get; private set; }
        public double LastShakeDurationSeconds { get; private set; }

        public void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange)
        {
            PitchDegrees = pitchDegrees;
            YawDegrees = yawDegrees;
            ZoomRange = zoomRange;
            Configured = true;
        }

        public void Follow(Vec2 planePos, double smoothing)
        {
            FollowTarget = planePos;
            FollowSmoothing = smoothing;
        }

        public void SetZoom(double zoom)
        {
            Zoom = zoom;
        }

        public Vec2 WorldToScreen(Vec2 planePos, double height)
        {
            return planePos;
        }

        public Vec2? ScreenToWorld(Vec2 screen)
        {
            return screen;
        }

        public void Shake(double intensity, double durationSeconds)
        {
            LastShakeIntensity = intensity;
            LastShakeDurationSeconds = durationSeconds;
        }
    }
}
