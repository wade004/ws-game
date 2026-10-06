using System;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>
    /// 控制空间取值与相机相对输入的"独立期望"（06 第 4 节第 3 点）：<c>world</c>（设备轴直接当世界方向，所有既有格子）与
    /// <c>camera_relative</c>（设备轴相对相机朝向，第三人称）。
    /// <para>
    /// 判断记录（换算是框架原生能力，本类型只做测试基准）：相机相对输入由框架的输入映射原生完成——移动动作声明
    /// <c>camera_relative</c> 控制空间（<c>InputControlSpace.CameraRelative</c>），输入映射在每次更新时按相机朝向查询
    /// （<c>ICameraOrientation</c>）给出的偏航把摇杆旋转成世界方向。实验室宿主扩展只提供朝向查询
    /// （<see cref="LabHostExtension.CameraOrientation"/>），不再自己换算；本类型里的 <see cref="CameraRelativeToWorld"/>、
    /// <see cref="ExpectedFromAxes"/>、<see cref="AngleDegrees"/> 是独立于框架实现的期望公式，用来在测试里核对原生换算
    /// （引擎宿主还用真实相机的右/上轴再核对一路，见引擎宿主的相机相对输入用例）。
    /// </para>
    /// <para>
    /// 判断记录（约定）：世界平面是 (X, Y)；偏航 <c>yaw</c> 是相机绕垂直于世界平面的视线轴逆时针转过的角度（弧度），0 表示相机"上方"
    /// 就是世界 +Y。摇杆 (x, y) 里 x 向右、y 向上（屏幕语义）。相机的右轴在世界平面上是 (cos yaw, sin yaw)，上轴是 (−sin yaw, cos yaw)，
    /// 世界方向 = x·右 + y·上，模长与摇杆一致（旋转不改长度，保留小幅轴值的语义）。带俯仰的相机同样只用偏航。
    /// </para>
    /// </summary>
    public static class ControlSpace
    {
        public const string World = "world";

        public const string CameraRelative = "camera_relative";

        /// <summary>
        /// 脚本里的偏航标记名（<see cref="ScriptEventKind.Marker"/>，<c>Value.X</c> = 偏航度，逆时针为正）：脚本声明 <c>meta.controlSpace = camera_relative</c>（ADR-0161）时，
        /// 它是相机偏航流——标记所在固定步的意图采集之前生效；没声明的脚本里它仍是纯呈现标记，逻辑不读。
        /// </summary>
        public const string YawMarker = "camera_yaw";

        /// <summary>摇杆轴值经相机偏航转成世界平面方向（保持模长）；测试用的独立期望公式。</summary>
        public static Vec2 CameraRelativeToWorld(Vec2 stick, double yawRadians)
        {
            var c = Math.Cos(yawRadians);
            var s = Math.Sin(yawRadians);
            return new Vec2(stick.X * c - stick.Y * s, stick.X * s + stick.Y * c);
        }

        /// <summary>相机的右轴/上轴在世界平面上的投影给出的期望方向：<c>x·right + y·up</c>。</summary>
        public static Vec2 ExpectedFromAxes(Vec2 stick, Vec2 cameraRight, Vec2 cameraUp) =>
            new Vec2(stick.X * cameraRight.X + stick.Y * cameraUp.X, stick.X * cameraRight.Y + stick.Y * cameraUp.Y);

        /// <summary>
        /// 脚本驱动的相机朝向查询（ADR-0161）：偏航只来自脚本里的偏航标记流（<see cref="YawMarker"/>），开局 0。宿主在固定步边界（脚本事件应用处）调用 <see cref="Commit"/>，
        /// 输入映射每次更新取样的偏航因此只由脚本决定，与是否有引擎、有没有真实相机无关——录下来的相机相对会话在任何宿主里逐位复现。
        /// </summary>
        public sealed class ScriptYawOrientation : Core.Foundation.EngineAdapter.ICameraOrientation
        {
            /// <summary>当前偏航（度，逆时针为正）。</summary>
            public double YawDegrees { get; private set; }

            /// <summary><see cref="Core.Foundation.EngineAdapter.ICameraOrientation.YawRadians"/>：当前偏航（弧度）。</summary>
            public double YawRadians => YawDegrees * Math.PI / 180.0;

            /// <summary>提交一个偏航标记的取值（度）。</summary>
            public void Commit(double yawDegrees) => YawDegrees = yawDegrees;
        }

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
