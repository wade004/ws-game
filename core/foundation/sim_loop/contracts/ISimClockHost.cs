namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 连续模式主循环契约（见 03_运行时骨架.md 第 3.1 节累积器五步、第 9 节签名）。
    /// 只驱动连续模式：不读取任何系统时间源，<see cref="Advance"/> 的唯一输入是调用方
    /// （引擎适配层 <c>IClock.onFrame</c>）传入的 <c>realDeltaSeconds</c>，保证核心库的
    /// 确定性（见落地方案与分阶段计划.md 第 4.1 节）。离散模式改由
    /// <see cref="ITurnScheduler.NextStep"/> 产生步，不经过本接口。
    /// </summary>
    public interface ISimClockHost
    {
        /// <summary>配置步长（秒，必须为正）与单帧最大补偿 tick 数（必须为正）。</summary>
        void ConfigureStep(double stepSeconds, int maxCatchUpSteps);

        /// <summary>
        /// 推进一次：按 03 第 3.1 节五步处理 <paramref name="realDeltaSeconds"/>
        /// （不能为负），产生 0 到 <see cref="MaxCatchUpSteps"/> 个 <c>Continuous</c> tick，
        /// 返回本次推进后的插值系数 alpha（范围 [0,1)）。
        /// </summary>
        double Advance(double realDeltaSeconds);

        /// <summary>暂停/取消暂停：暂停时 <see cref="Advance"/> 不推进、累积器不增长。</summary>
        void SetPaused(bool paused);

        /// <summary>设置时间缩放系数（慢动作），只影响累积速度，不能为负。</summary>
        void SetTimeScale(double scale);

        /// <summary>当前配置的步长（秒）。</summary>
        double StepSeconds { get; }

        /// <summary>当前配置的单帧最大补偿 tick 数。</summary>
        int MaxCatchUpSteps { get; }

        /// <summary>是否处于暂停状态。</summary>
        bool IsPaused { get; }

        /// <summary>当前时间缩放系数。</summary>
        double TimeScale { get; }

        /// <summary>累计已产生的 tick 数。</summary>
        long TickIndex { get; }

        /// <summary>累计模拟秒数，等于 <see cref="TickIndex"/> × <see cref="StepSeconds"/>。</summary>
        double SimTimeSeconds { get; }
    }
}
