namespace Core.Foundation.EngineAdapter
{
    /// <summary>每帧回调，携带自上一帧以来经过的真实秒数。</summary>
    public delegate void FrameCallback(double deltaSeconds);

    /// <summary>固定步长回调，携带固定的步长秒数（与注册时的 stepSeconds 相同）。</summary>
    public delegate void FixedStepCallback(double stepSeconds);

    /// <summary>
    /// 帧回调、固定步长驱动、真实时间（见 02_引擎适配层.md 第 1.2 节）。必需接口。
    /// Now() 返回单调递增的真实时间（秒），不受模拟暂停/慢动作影响，供 UI 动画等场景使用；
    /// RequestFixedStep 是主循环驱动固定步长模拟的唯一入口，引擎适配层负责按 stepSeconds
    /// 的粒度触发回调并处理帧率抖动的补偿（固定步长回调的触发误差应控制在步长的 5% 以内）。
    /// </summary>
    public interface IClock
    {
        double Now();

        double GetDeltaSeconds();

        void OnFrame(FrameCallback callback);

        void RequestFixedStep(double stepSeconds, FixedStepCallback callback);
    }
}
