using System;
using Presentation.Render;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary><see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/> 的策略配置项（见
    /// 09_表现层.md 第 6.3、6.4 节"合并窗口时长...为可配置项""加速与跳过...是表现层本地设置"）。</summary>
    public sealed class FeedbackOptions
    {
        /// <summary>ADR-0017 决策 d：命中帧同步策略，应与驱动 <c>ICharacterRig</c> 的
        /// <see cref="Presentation.Render.RenderOptions.HitFrameSync"/> 取同一个值（两者是同一个口味
        /// 配置项在渲染侧/反馈绑定侧的两个落点，装配层负责保持一致，见
        /// <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/> 判断记录）。默认
        /// <see cref="HitFrameSyncStrategy.LogicDriven"/>——该策略下 <c>FeedbackRule.Sync</c> 字段被
        /// 忽略，全部动作按既有行为立即派发。</summary>
        public HitFrameSyncStrategy HitFrameSync { get; set; } = HitFrameSyncStrategy.LogicDriven;

        /// <summary><c>Presentation.FeedbackBinder.Core.HitFrameSyncPolicy</c> 的超时兜底时长（秒），
        /// 见该类型判断记录。默认 0.5（同该类型 <c>DefaultTimeoutSeconds</c>；本类型——Contracts——
        /// 不反向依赖 Core 实现类型，字面量各自独立声明，二者含义上是同一个默认值）。</summary>
        public double HitFrameSyncTimeoutSeconds { get; set; } = 0.5;

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
