namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 轴动作的控制空间取值（<see cref="ActionDefinition.ControlSpace"/>、<c>found.input_action.control_space</c>）。
    /// <para>
    /// <see cref="World"/>：轴值原样当世界平面方向（缺省，既有全部动作）。<see cref="CameraRelative"/>：轴值按"当前相机偏航"换算成世界方向——
    /// 摇杆 (x, y)（x 向右、y 向上，屏幕语义）→ 世界方向 = x·相机右轴 + y·相机上轴，相机偏航 yaw 是相机绕垂直于世界平面的视线轴逆时针转过的
    /// 弧度，0 表示相机"上方"就是世界 +Y，右轴在世界平面上是 (cos yaw, sin yaw)、上轴是 (−sin yaw, cos yaw)；旋转不改长度，小幅轴值的语义保留。
    /// 偏航由输入映射宿主的可选相机朝向查询（<see cref="InputMapOptions.CameraOrientation"/>，即
    /// <see cref="Core.Foundation.EngineAdapter.ICameraOrientation"/>）在每次 <see cref="IInputMapHost.Update"/> 时采样。
    /// 带俯仰的镜头同样只用偏航：俯仰只把世界平面在屏幕上压扁，不改变"摇杆上 = 相机前方在地面上的投影"这一约定。
    /// </para>
    /// </summary>
    public static class InputControlSpace
    {
        public const string World = "world";

        public const string CameraRelative = "camera_relative";

        /// <summary>取值是否合法。</summary>
        public static bool IsValid(string? value) =>
            string.Equals(value, World, System.StringComparison.Ordinal)
            || string.Equals(value, CameraRelative, System.StringComparison.Ordinal);
    }
}
