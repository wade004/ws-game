namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="SimClockHost"/> 的策略配置项（见 01_分层与依赖.md L0 模块表 sim_loop 行
    /// "策略配置项"列："步长、最大补偿步数、慢动作缩放系数……"）。时间模型、先攻策略、
    /// 节奏策略三项配置属于离散模式，本项目暂不启用（见 ADR-0013、落地计划 T1-5），
    /// 不在本类中体现，见本模块 README 与 schema/README.md 的说明。
    /// </summary>
    public sealed class SimLoopOptions
    {
        /// <summary>固定步长（秒），默认对应每秒 60 次模拟。</summary>
        public double StepSeconds { get; set; } = 1.0 / 60.0;

        /// <summary>单帧最大补偿 tick 数，默认 5。</summary>
        public int MaxCatchUpSteps { get; set; } = 5;

        /// <summary>默认时间缩放系数，默认 1（不缩放）。</summary>
        public double DefaultTimeScale { get; set; } = 1.0;
    }
}
