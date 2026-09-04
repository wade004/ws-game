using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Achievement
{
    /// <summary>本模块发出的事件 key 常量（对应 found.event_catalog 登记表 <c>achievement.*</c>
    /// 两行）。惯例同 <c>core/gameplay/world_state</c> 的 <c>WorldStateEventKeys</c>。</summary>
    public static class AchievementEventKeys
    {
        public static readonly Id Progressed = new Id("achievement.progressed");
        public static readonly Id Unlocked = new Id("achievement.unlocked");
    }

    /// <summary>某条 criterion 累计进度变化时触发（见 found.event_catalog
    /// <c>achievement.progressed</c> 行字段表 <c>{achievementId, unitId, current, target}</c>）。
    /// <para>判断记录：该行字段表只给出四个字段，未指明"current/target 是单条 criterion 的进度还是
    /// 整个成就的总进度"——本模块按"每次某条 criterion 计数变化各发一次"实现（08 第 6.1 节"成就
    /// 系统只订阅事件总线，累计计数"最贴近的字面理解是逐条 criterion 累计），<see cref="Current"/>/
    /// <see cref="Target"/> 对应触发本次变化的那一条 criterion，不是跨 criteria 汇总值。</para>
    /// </summary>
    public sealed class AchievementProgressedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => AchievementEventKeys.Progressed;

        public Id AchievementId { get; }

        public Id UnitId { get; }

        public int Current { get; }

        public int Target { get; }

        public AchievementProgressedEvent(Id achievementId, Id unitId, int current, int target)
        {
            AchievementId = achievementId;
            UnitId = unitId;
            Current = current;
            Target = target;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "achievementId": value = ExprValue.OfId(AchievementId); return true;
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "current": value = ExprValue.OfInt(Current); return true;
                case "target": value = ExprValue.OfInt(Target); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>成就全部 criteria 达成、首次解锁时触发（见 found.event_catalog
    /// <c>achievement.unlocked</c> 行字段表 <c>{achievementId, unitId}</c>）；同一
    /// (unitId, achievementId) 只会触发一次（08 第 6.1 节"解锁（一次性）"）。</summary>
    public sealed class AchievementUnlockedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => AchievementEventKeys.Unlocked;

        public Id AchievementId { get; }

        public Id UnitId { get; }

        public AchievementUnlockedEvent(Id achievementId, Id unitId)
        {
            AchievementId = achievementId;
            UnitId = unitId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "achievementId": value = ExprValue.OfId(AchievementId); return true;
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                default: value = default; return false;
            }
        }
    }
}
