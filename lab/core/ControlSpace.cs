using System;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>
    /// 控制空间的主机侧转换（06 第 4 节第 3 点）：<c>world</c>（设备轴直接当世界方向，所有既有格子）与
    /// <c>camera_relative</c>（设备轴相对相机朝向，第三人称）。
    /// <para>
    /// 判断记录（转换是宿主层行为，不是框架能力）：框架的输入映射只产出设备轴值，"摇杆上 = 相机前方"的转换发生在设备轴 →
    /// 逻辑移动请求的边界上，历来是各游戏宿主自己的事；实验室宿主在这个边界上做转换（<see cref="LabHostExtension.ConvertMoveAxis"/>），
    /// 用来证明"三种平面组合下相机相对输入都得到正确的世界方向"。框架若以后提供生产级的相机相对输入，应以框架实现为准、本转换退为测试基准。
    /// </para>
    /// <para>
    /// 判断记录（约定）：世界平面是 (X, Y)；偏航 <c>yaw</c> 是相机绕垂直于世界平面的视线轴逆时针转过的角度（弧度），0 表示相机"上方"
    /// 就是世界 +Y。摇杆 (x, y) 里 x 向右、y 向上（屏幕语义）。相机的右轴在世界平面上是 (cos yaw, sin yaw)，上轴是 (−sin yaw, cos yaw)，
    /// 世界方向 = x·右 + y·上，模长与摇杆一致（旋转不改长度，保留小幅轴值的语义）。
    /// </para>
    /// </summary>
    public static class ControlSpace
    {
        public const string World = "world";

        public const string CameraRelative = "camera_relative";

        /// <summary>摇杆轴值经相机偏航转成世界平面方向（保持模长）。</summary>
        public static Vec2 CameraRelativeToWorld(Vec2 stick, double yawRadians)
        {
            var c = Math.Cos(yawRadians);
            var s = Math.Sin(yawRadians);
            return new Vec2(stick.X * c - stick.Y * s, stick.X * s + stick.Y * c);
        }

        /// <summary>相机的右轴/上轴在世界平面上的投影给出的期望方向：<c>x·right + y·up</c>。</summary>
        public static Vec2 ExpectedFromAxes(Vec2 stick, Vec2 cameraRight, Vec2 cameraUp) =>
            new Vec2(stick.X * cameraRight.X + stick.Y * cameraUp.X, stick.X * cameraRight.Y + stick.Y * cameraUp.Y);

        /// <summary>两个方向的夹角（度，0～180）；任一为零向量时返回 0（没有方向可比）。</summary>
        public static double AngleDegrees(Vec2 a, Vec2 b)
        {
            var la = Math.Sqrt(a.X * a.X + a.Y * a.Y);
            var lb = Math.Sqrt(b.X * b.X + b.Y * b.Y);
            if (la < 1e-12 || lb < 1e-12)
            {
                return 0.0;
            }

            var cos = (a.X * b.X + a.Y * b.Y) / (la * lb);
            cos = Math.Max(-1.0, Math.Min(1.0, cos));
            return Math.Acos(cos) * 180.0 / Math.PI;
        }
    }
}
