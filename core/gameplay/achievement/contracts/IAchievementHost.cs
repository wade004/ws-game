using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// 成就模块对外契约（见 08 第 9 节契约汇总表 <c>Achievement</c> 行
    /// <c>AchievementHost.evaluate(event)</c>）。由 <c>core/gameplay/achievement</c> 实现。
    /// </summary>
    public interface IAchievementHost
    {
        /// <summary>
        /// 用一个事件驱动全部已登记成就的进度累计（08 第 6.1 节"成就系统只订阅事件总线，累计
        /// 计数，不主动轮询其它系统状态"）。构造期已经按全部 <c>achv.def.criteria[].observe_event</c>
        /// 订阅了 <see cref="IEventBus"/>，本方法是那些订阅的实际处理器，同时也对外公开——供测试
        /// 或组装层在不经事件总线的场景下直接回放一串事件（见 08 第 9 节汇总表"测试方式：脱离引擎
        /// 回放一串历史事件，断言累计计数与解锁时机正确"）。
        /// </summary>
        void Evaluate(IEvent evt);

        /// <summary><paramref name="unitId"/> 在成就 <paramref name="achievementId"/> 下，
        /// 按 <c>achv.def.criteria</c> 声明顺序排列的各条进度快照；该成就未登记任何进度时，
        /// 每条 criterion 的 <see cref="AchievementCriterionProgress.Current"/> 为 0。</summary>
        IReadOnlyList<AchievementCriterionProgress> GetProgress(Id unitId, Id achievementId);

        /// <summary><paramref name="unitId"/> 是否已解锁 <paramref name="achievementId"/>。</summary>
        bool IsUnlocked(Id unitId, Id achievementId);
    }
}
