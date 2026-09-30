using System;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// 分层音量入参的统一校验与夹取规则（ADR-0125 D21），<see cref="AudioLayerVolumeHost"/> 与
    /// <see cref="SfxPlayer.SetLayerVolume"/> 共用同一出口，避免两处各说各话。
    /// <para>
    /// 规则：NaN/Infinity 抛 <see cref="ArgumentOutOfRangeException"/>（不是可夹取的"很大的数"，是调用方缺陷，
    /// 不静默吞掉）；其余有限值夹取到 [0,1]（滑条越界、设置文件被手改都属于正常输入，夹取后应用并持久化）。
    /// </para>
    /// </summary>
    internal static class LayerVolume
    {
        public const double Min = 0.0;
        public const double Max = 1.0;

        /// <summary>校验并夹取；<paramref name="paramName"/> 用于异常的 ParamName。</summary>
        public static double Normalize(double volume, string paramName)
        {
            if (double.IsNaN(volume) || double.IsInfinity(volume))
            {
                throw new ArgumentOutOfRangeException(paramName, volume, "音量必须是有限数值（NaN/Infinity 不合法）");
            }
            return Clamp(volume);
        }

        /// <summary>仅夹取（调用方已确认有限）。</summary>
        public static double Clamp(double volume) => volume < Min ? Min : volume > Max ? Max : volume;
    }
}
