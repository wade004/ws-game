using Core.Foundation.Common;

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
    /// OnFrame/RequestFixedStep 均返回 SubscriptionHandle，调用方可据此退订该回调；主循环在
    /// 场景/世界重建（见 03_运行时骨架.md 第 6 节场景路由卸载旧场景）前必须退订旧世界注册的
    /// 全部固定步回调，避免旧回调残留导致重复推进（见 ADR-0016 决策 1）。
    /// </summary>
    public interface IClock
    {
        double Now();

        double GetDeltaSeconds();

        SubscriptionHandle OnFrame(FrameCallback callback);

        SubscriptionHandle RequestFixedStep(double stepSeconds, FixedStepCallback callback);
    }
}
