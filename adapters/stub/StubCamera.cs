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
    /// <summary><see cref="ICamera"/> 的最小桩实现：只记录调用与保存最近一次配置，不做真实投影，供测试断言镜头参数被设置成了什么。</summary>
    public sealed class StubCamera : ICamera
    {
        /// <summary>是否已调用过 <c>Configure</c>。</summary>
        public bool Configured { get; private set; }
        /// <summary>最近一次 <c>Configure</c> 设置的俯仰角（度）。</summary>
        public double PitchDegrees { get; private set; }
        /// <summary>最近一次 <c>Configure</c> 设置的朝向角（度）。</summary>
        public double YawDegrees { get; private set; }
        /// <summary>最近一次 <c>Configure</c> 设置的缩放区间。</summary>
        public ZoomRange ZoomRange { get; private set; }

        /// <summary>最近一次 <c>Follow</c> 的平面跟随目标位置。</summary>
        public Vec2 FollowTarget { get; private set; }
        /// <summary>最近一次 <c>Follow</c> 的平滑系数。</summary>
        public double FollowSmoothing { get; private set; }

        /// <summary>当前缩放值（初始 1.0；桩不做区间夹紧，原样保存传入值）。</summary>
        public double Zoom { get; private set; } = 1.0;

        /// <summary>最近一次 <c>Shake</c> 的强度。</summary>
        public double LastShakeIntensity { get; private set; }
        /// <summary>最近一次 <c>Shake</c> 的持续时间（秒）。</summary>
        public double LastShakeDurationSeconds { get; private set; }
        /// <summary>最近一次 <c>Shake</c> 的频率。</summary>
        public double LastShakeFrequency { get; private set; }

        /// <summary>记录镜头俯仰角、朝向角与缩放区间，并把 <see cref="Configured"/> 置为 true。</summary>
        public void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange)
        {
            PitchDegrees = pitchDegrees;
            YawDegrees = yawDegrees;
            ZoomRange = zoomRange;
            Configured = true;
        }

        /// <summary>记录跟随目标与平滑系数。</summary>
        public void Follow(Vec2 planePos, double smoothing)
        {
            FollowTarget = planePos;
            FollowSmoothing = smoothing;
        }

        /// <summary>记录缩放值。</summary>
        public void SetZoom(double zoom)
        {
            Zoom = zoom;
        }

        /// <summary>固定正交近似：直接返回平面坐标（忽略高度与俯仰角），与 <c>ScreenToWorld</c> 互为逆运算。</summary>
        public Vec2 WorldToScreen(Vec2 planePos, double height)
        {
            return planePos;
        }

        /// <summary><c>WorldToScreen</c> 的精确逆运算：直接返回屏幕坐标，永不返回 null。</summary>
        public Vec2? ScreenToWorld(Vec2 screen)
        {
            return screen;
        }

        /// <summary>记录震屏的强度、持续时间与频率。</summary>
        public void Shake(double intensity, double durationSeconds, double frequency)
        {
            LastShakeIntensity = intensity;
            LastShakeDurationSeconds = durationSeconds;
            LastShakeFrequency = frequency;
        }
    }
}
