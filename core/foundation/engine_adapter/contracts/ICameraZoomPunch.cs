namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 镜头缩放脉冲可选能力（手感设计/07 第 1 节 <c>zoom_punch</c>，ADR-0148）：命中瞬间镜头沿缩放轴"拍"一下再回落。
    /// 与 <see cref="ICameraImpulse"/> 同一做法——<see cref="ICamera"/> 的实现可以同时实现本接口；不实现、或
    /// <see cref="SupportsCameraZoomPunch"/> 为假的适配层，<c>Presentation.Camera.CameraHost.ZoomPunch</c> 记一条诊断后忽略
    /// （缩放脉冲是纯增味，没有对等的降级通道，不退化为别的通道）。
    /// </summary>
    public interface ICameraZoomPunch
    {
        /// <summary>适配层是否真正支持缩放脉冲。</summary>
        bool SupportsCameraZoomPunch { get; }

        /// <summary>
        /// 触发一次缩放脉冲：<paramref name="magnitude"/> 是峰值缩放变化比例（相对当前缩放，非负；0.05 表示可视范围瞬时收窄 5%，
        /// 即画面放大），<paramref name="decayMs"/> 是从峰值线性回落到 0 的时长（毫秒，正数）。多次脉冲取向量相加的等价和
        /// （各自独立线性衰减后相加）。
        /// </summary>
        void ZoomPunch(double magnitude, double decayMs);
    }
}
