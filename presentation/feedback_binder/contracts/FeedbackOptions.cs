using System;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary><see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/> 的策略配置项（见
    /// 09_表现层.md 第 6.3、6.4 节"合并窗口时长...为可配置项""加速与跳过...是表现层本地设置"）。</summary>
    public sealed class FeedbackOptions
    {
        /// <summary>飘字合并窗口时长（模拟时间单位，通常是秒）；小于等于 0 表示不启用合并，
        /// 每条飘字都立即派发。默认 0（不合并）。</summary>
        public double MergeWindow { get; set; } = 0.0;

        public MergeMode MergeMode { get; set; } = MergeMode.Sum;

        /// <summary>数值取整格式化（09 第 6.1 节 <c>text_source: amount</c> "数值取整格式化"）：
        /// 默认按四舍五入取整的十进制文本。</summary>
        public Func<double, string> NumberFormat { get; set; } = value => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

        public QueueMode QueueMode { get; set; } = QueueMode.Immediate;

        /// <summary><see cref="QueueMode.Sequential"/> 下每条动作占用的回放时长（模拟时间单位），
        /// 经 <see cref="Presentation.FeedbackBinder.Core.PlaybackQueue.SpeedMultiplier"/> 缩放。</summary>
        public double SequentialStepSeconds { get; set; } = 0.15;
    }
}
