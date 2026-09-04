using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// Expr 宿主引用分组的固定清单（见 04 第 6.2 节）：self、target、event、world、quest、
    /// player、combat、enemies、time，九个，全架构统一，不由游戏层扩展。
    /// </summary>
    public static class ExprGroups
    {
        public const string Self = "self";
        public const string Target = "target";
        public const string Event = "event";
        public const string World = "world";
        public const string Quest = "quest";
        public const string Player = "player";
        public const string Combat = "combat";
        public const string Enemies = "enemies";
        public const string Time = "time";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Self, Target, Event, World, Quest, Player, Combat, Enemies, Time,
        };

        public static bool IsKnown(string group)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] == group) return true;
            }
            return false;
        }
    }
}
