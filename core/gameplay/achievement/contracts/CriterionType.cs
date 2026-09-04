using System.Collections.Generic;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// 成就达成条件的六种类型（见 08_玩法层_掉落任务对话关卡.md 第 6.1 节 <c>Criterion.type</c>
    /// 枚举）。
    /// </summary>
    public enum CriterionType
    {
        KillCount,
        CollectCount,
        QuestComplete,
        ReachArea,
        CastCount,
        CustomEvent,
    }

    /// <summary><see cref="CriterionType"/> 与 <c>achv.def.criteria[].type</c> 字段 snake_case
    /// 取值之间的映射（数据表用字符串枚举，见 <see cref="AchievementSchemas.Def"/> 判断记录）。</summary>
    public static class CriterionTypeIds
    {
        public const string KillCountValue = "kill_count";
        public const string CollectCountValue = "collect_count";
        public const string QuestCompleteValue = "quest_complete";
        public const string ReachAreaValue = "reach_area";
        public const string CastCountValue = "cast_count";
        public const string CustomEventValue = "custom_event";

        public static readonly IReadOnlyList<string> AllValues = new[]
        {
            KillCountValue, CollectCountValue, QuestCompleteValue, ReachAreaValue, CastCountValue, CustomEventValue,
        };

        public static bool TryParse(string text, out CriterionType type)
        {
            switch (text)
            {
                case KillCountValue: type = CriterionType.KillCount; return true;
                case CollectCountValue: type = CriterionType.CollectCount; return true;
                case QuestCompleteValue: type = CriterionType.QuestComplete; return true;
                case ReachAreaValue: type = CriterionType.ReachArea; return true;
                case CastCountValue: type = CriterionType.CastCount; return true;
                case CustomEventValue: type = CriterionType.CustomEvent; return true;
                default: type = default; return false;
            }
        }
    }
}
