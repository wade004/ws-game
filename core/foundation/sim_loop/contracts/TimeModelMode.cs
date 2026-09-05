namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 当前生效的时间模型（见 03_运行时骨架.md 第 3、9 节、ADR-0013 决策 1、2）：
    /// <see cref="Continuous"/> 固定步长（时间单位为秒），<see cref="Discrete"/> 回合
    /// （时间单位为回合）。由主循环宿主持有（见 03 第 9 节 <c>SimClockHost.advance</c>
    /// 注释"模式状态 TimeModelMode 由主循环宿主持有"）——<see cref="ISimClockHost"/> 只是
    /// 该状态的一处消费方（<see cref="ISimClockHost.Mode"/>），真正的模式切换判断与
    /// <see cref="ITurnScheduler"/> 的启停由 <c>core/gameplay/assembly.TimeModelSwitch</c>
    /// 驱动（见 03 第 3.3 节模式切换流程）。
    /// </summary>
    public enum TimeModelMode
    {
        Continuous,
        Discrete
    }
}
