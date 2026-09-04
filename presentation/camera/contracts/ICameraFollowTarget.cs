using Core.Foundation.Common;

namespace Presentation.Camera
{
    /// <summary>
    /// 跟随目标位置来源（见 09 第 3.5 节"每帧 Update(alpha) 用
    /// <c>ViewBinder.GetInterpolatedPosition</c> 或 <c>ISimSnapshot</c>"）。
    /// <para>
    /// 判断记录：任务书给出两个可选来源——本模块不直接依赖
    /// <c>Presentation.ViewBinding.ViewBinder</c>（避免 <c>presentation/camera</c> 反过来依赖
    /// <c>presentation/view_binding</c>，两个 L5 同层模块只应经契约/事件交互，见 01 第 3 节"同一层内
    /// 的模块之间只经契约接口与事件总线交互"），改用本接口统一两种来源：
    /// <see cref="SimSnapshotFollowTarget"/> 包一层 <c>ISimSnapshot</c>（不插值，直接读当前位置，
    /// 因为 <c>ICamera.Follow</c> 自带 <c>smoothing</c> 平滑系数）；组装代码也可以用
    /// <see cref="DelegateFollowTarget"/> 包一层 <c>ViewBinder.GetInterpolatedPosition</c>
    /// 方法组，两种来源对 <c>CameraHost</c> 完全透明。
    /// </para>
    /// </summary>
    public interface ICameraFollowTarget
    {
        Vec2 GetPosition(Id entityId, double alpha);
    }
}
