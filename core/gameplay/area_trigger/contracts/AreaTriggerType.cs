using System;
using System.Collections.Generic;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>数据表 <c>area.trigger_def.trigger_type</c> 的四种取值（见 05_对象模型与世界.md 第 7
    /// 节表格行序）。新增类型需过审批（同 07 <c>NpcFlag</c> 一类"新增原语受控开口"的惯例）。</summary>
    public enum AreaTriggerType
    {
        MapTransition,
        QuestExplore,
        EncounterStart,
        Script,
    }

    /// <summary><see cref="AreaTriggerType"/> 与数据表 snake_case 文本互转（惯例同
    /// <c>Core.Carriers.Gobj.GobjKindNames</c>）。</summary>
    public static class AreaTriggerTypeNames
    {
        private static readonly IReadOnlyDictionary<AreaTriggerType, string> ToName = new Dictionary<AreaTriggerType, string>
        {
            [AreaTriggerType.MapTransition] = "map_transition",
            [AreaTriggerType.QuestExplore] = "quest_explore",
            [AreaTriggerType.EncounterStart] = "encounter_start",
            [AreaTriggerType.Script] = "script",
        };

        private static readonly IReadOnlyDictionary<string, AreaTriggerType> FromName = BuildReverse(ToName);

        private static Dictionary<string, AreaTriggerType> BuildReverse(IReadOnlyDictionary<AreaTriggerType, string> map)
        {
            var reverse = new Dictionary<string, AreaTriggerType>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                reverse[pair.Value] = pair.Key;
            }

            return reverse;
        }

        public static string ToText(AreaTriggerType type) => ToName[type];

        public static bool TryParse(string text, out AreaTriggerType type) => FromName.TryGetValue(text, out type);
    }

    /// <summary>
    /// <see cref="AreaTriggerHost"/> 运行期登记表用的调度种类：在数据驱动的四种
    /// <see cref="AreaTriggerType"/> 之外，追加一个只能经 <see cref="IAreaTriggerHost.RegisterTrap"/>
    /// 动态登记、不出现在 <c>area.trigger_def</c> 数据表里的 <see cref="Trap"/>（见 07 第 3.1 节
    /// <c>trap</c> 类型 <c>GameObjectHost.TriggerTrap</c> 的触发来源、05 第 7 节判断记录）。
    /// </summary>
    internal enum AreaTriggerKind
    {
        MapTransition,
        QuestExplore,
        EncounterStart,
        Script,
        Trap,
    }

    internal static class AreaTriggerKindConvert
    {
        public static AreaTriggerKind FromType(AreaTriggerType type)
        {
            switch (type)
            {
                case AreaTriggerType.MapTransition: return AreaTriggerKind.MapTransition;
                case AreaTriggerType.QuestExplore: return AreaTriggerKind.QuestExplore;
                case AreaTriggerType.EncounterStart: return AreaTriggerKind.EncounterStart;
                case AreaTriggerType.Script: return AreaTriggerKind.Script;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "未知 AreaTriggerType");
            }
        }
    }
}
