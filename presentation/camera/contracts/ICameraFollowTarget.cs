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

        /// <summary>
        /// ADR-0121 第 6 条（D6）：跟随目标以"可能不存在"为契约——目标不存在（被跟随实体已销毁/未绑定）时
        /// 返回 <c>false</c>、<paramref name="position"/> 为 <c>default</c>，不抛异常；
        /// <see cref="CameraHost.Update"/> 据此保持最后位置并只记一条诊断。
        /// <para>
        /// 默认接口成员（ABI 只新增，旧实现不改也能编译）：默认实现原样包装 <see cref="GetPosition"/>——
        /// 永远返回 <c>true</c>，<see cref="GetPosition"/> 抛出的异常照旧向上传播（保持旧实现的既有行为）。
        /// 内置的 <see cref="SimSnapshotFollowTarget"/> 与 <see cref="DelegateFollowTarget"/> 覆写为不抛的实现；
        /// 自定义实现要获得"目标丢失保护"须自行覆写本成员。
        /// </para>
        /// </summary>
        bool TryGetPosition(Id entityId, double alpha, out Vec2 position)
        {
            position = GetPosition(entityId, alpha);
            return true;
        }
    }
}
