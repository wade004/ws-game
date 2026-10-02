namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 镜头朝向查询（可选能力，相机相对控制空间用，见输入映射的 <c>camera_relative</c>）：<see cref="ICamera"/> 的实现可以同时实现本接口，
    /// 对外给出当前相机在世界平面上的偏航。不实现本接口的适配层只能配合 <c>world</c> 控制空间使用；声明了 <c>camera_relative</c> 的
    /// 输入动作要求宿主给出一个本接口的实例（缺失在装配期就报错，不静默当成偏航 0）。
    /// <para>
    /// 判断记录：独立成一个新接口而不是给 <see cref="ICamera"/> 加成员——<see cref="ICamera"/> 是必需接口，既有实现不需要为一个可选能力改一行代码
    /// （同 <see cref="ICameraImpulse"/>）；探测写法 <c>camera is ICameraOrientation orientation</c>。
    /// </para>
    /// <para>
    /// 偏航约定：<see cref="YawRadians"/> 是相机绕垂直于世界平面的视线轴逆时针转过的弧度；0 表示相机"上方"（屏幕上方向在世界平面上的指向）就是世界 +Y，
    /// 相机右轴在世界平面上是 (cos yaw, sin yaw)、上轴是 (−sin yaw, cos yaw)。带俯仰的相机同样只报偏航（视线在世界平面上的投影方向），
    /// 俯仰不进入本接口——相机相对移动只依赖"相机朝哪边"，不依赖"相机俯得多低"。
    /// </para>
    /// </summary>
    public interface ICameraOrientation
    {
        /// <summary>相机当前偏航（弧度，逆时针为正）。</summary>
        double YawRadians { get; }
    }
}
