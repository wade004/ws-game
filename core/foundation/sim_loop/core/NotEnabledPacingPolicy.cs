using System;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="IPacingPolicy"/> 的占位实现：离散时间模型（回合制）本项目暂不启用
    /// （见 ADR-0013、落地方案与分阶段计划.md T1-5 禁止事项）。全部方法一律抛
    /// <see cref="NotSupportedException"/>。
    /// </summary>
    public sealed class NotEnabledPacingPolicy : IPacingPolicy
    {
        private const string NotEnabledMessage = "离散时间模型本项目暂不启用，见 ADR-0013 与落地计划 T1-5";

        public PacingMode Mode() => throw new NotSupportedException(NotEnabledMessage);

        public void OnPlaybackFinished() => throw new NotSupportedException(NotEnabledMessage);
    }
}
