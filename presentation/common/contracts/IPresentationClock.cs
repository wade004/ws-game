namespace Presentation.Common
{
    /// <summary>
    /// 插值系数来源（见 03_运行时骨架.md 第 3.1 节"accumulator / stepSeconds 得到插值系数
    /// alpha（范围 0~1），交给表现层的视图绑定用于……插值渲染"、第 9 节
    /// <c>SimClockHost.advance</c> 返回值）。表现层（View 绑定、镜头跟随等）经本接口只读取当前
    /// alpha，不反向驱动主循环推进——满足铁律 P1"只读逻辑状态"。
    /// </summary>
    public interface IPresentationClock
    {
        /// <summary>当前插值系数，范围 [0,1)（连续模式）；离散模式下由具体实现决定固定返回值
        /// （见 03 第 9 节 <c>SimClockHost.advance</c> 注释"离散模式：改由 TurnScheduler.nextStep
        /// 产生步，本方法只驱动表现插值，不产生模拟步"）。</summary>
        double Alpha { get; }
    }
}
