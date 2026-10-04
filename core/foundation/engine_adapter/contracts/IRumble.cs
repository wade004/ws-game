namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 手柄震动（触感）输出可选能力（手感设计/07 第 1 节 <c>rumble</c>，ADR-0148）：宿主层或平台适配层实现。
    /// 没有实现本接口、或 <see cref="SupportsRumble"/> 为假（没有连接支持震动的手柄）时，表现层把震动请求静默丢弃，
    /// 不影响其它反馈。
    /// </summary>
    public interface IRumble
    {
        /// <summary>当前是否有可震动的设备（可随运行环境动态变化）。</summary>
        bool SupportsRumble { get; }

        /// <summary>震动：<paramref name="strength"/> 是 0..1 的强度（实现自行映射到低频/高频马达），<paramref name="durationMs"/> 是持续毫秒数（正数）。</summary>
        void Rumble(double strength, double durationMs);
    }
}
