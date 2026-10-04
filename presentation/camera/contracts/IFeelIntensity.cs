using System;

namespace Presentation.Camera
{
    /// <summary>玩家可调的手感表现强度种类（手感设计/07 第 2 节"玩家强度"，ADR-0148）：每种一个 0..1 系数，0 = 关闭，缺省 1。</summary>
    public enum FeelIntensityKind
    {
        /// <summary>震屏（<c>ICamera.Shake</c> 通道，含规则驱动的 <c>shake_camera</c>）。</summary>
        Shake = 0,

        /// <summary>镜头冲击与缩放脉冲（<c>ICameraImpulse</c>/<c>ICameraZoomPunch</c> 通道）。</summary>
        Impulse = 1,

        /// <summary>闪白。光敏类无障碍的主要开关。</summary>
        Flash = 2,

        /// <summary>手柄震动。</summary>
        Rumble = 3,
    }

    /// <summary>读玩家强度系数的窄口；返回值已夹在 [0, 1]。</summary>
    public interface IFeelIntensity
    {
        double Get(FeelIntensityKind kind);
    }

    /// <summary>设置文件里四个强度键的命名（<c>feel.intensity.&lt;kind&gt;</c>）。</summary>
    public static class FeelIntensityKeys
    {
        public const string Prefix = "feel.intensity.";

        public static string Of(FeelIntensityKind kind)
        {
            switch (kind)
            {
                case FeelIntensityKind.Shake: return Prefix + "shake";
                case FeelIntensityKind.Impulse: return Prefix + "impulse";
                case FeelIntensityKind.Flash: return Prefix + "flash";
                case FeelIntensityKind.Rumble: return Prefix + "rumble";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>是否是本框架保留的强度键（出口处已统一缩放，反馈包流水线读到这些名字时不再重复乘）。</summary>
        public static bool IsReserved(string? name) =>
            name != null && name.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
