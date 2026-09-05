using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>缩放范围（Configure 的 zoomRange: {min, max}）。</summary>
    public readonly struct ZoomRange
    {
        public double Min { get; }
        public double Max { get; }

        public ZoomRange(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// 固定俯角镜头配置、跟随、缩放、世界平面与屏幕坐标互投影、震屏
    /// （见 02_引擎适配层.md 第 1.13 节）。必需接口。Configure 设定固定俯角/水平朝向/缩放范围，
    /// 镜头姿态在此之后只能通过再次 Configure 整体调整，不支持自由旋转。
    /// 取代了 v1 版 IRenderer2D 的 setCameraTransform/shakeScreen。
    /// </summary>
    public interface ICamera
    {
        void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange);

        /// <summary>让镜头以给定平滑系数持续跟随一个世界平面坐标（通常是玩家单位的位置）。</summary>
        void Follow(Vec2 planePos, double smoothing);

        void SetZoom(double zoom);

        /// <summary>把世界平面坐标加高度投影为屏幕坐标，供血条、飘字、UI 挂点等定位使用。</summary>
        Vec2 WorldToScreen(Vec2 planePos, double height);

        /// <summary>
        /// 把屏幕坐标与地面平面求交反投影为世界平面坐标，找不到交点（如指向天空方向）时返回 null。
        /// </summary>
        Vec2? ScreenToWorld(Vec2 screen);

        /// <summary>
        /// frequency 与 09_表现层.md 第 6.1 节震屏反馈动作引用的震屏预设的频率字段一一对应，
        /// 强度、时长、频率三项经这一个方法完整传递（见 ADR-0016 决策 4）。
        /// </summary>
        void Shake(double intensity, double durationSeconds, double frequency);
    }
}
