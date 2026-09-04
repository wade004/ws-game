using System.Collections.Generic;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 已知存档段 key 常量与固定加载顺序（见 10_存档与持久化.md 第 3 节"SaveSystem 汇总顺序
    /// （拍板，固定，避免加载时出现依赖尚未加载的数据）"）。10 文档按 8 个阶段分组给出顺序
    /// （如"3. player.progression / archetype"同属一个阶段），本表在同阶段内按任务书拍板的
    /// 固定顺序展开为一条全序列表 <see cref="KnownOrder"/>，供 <see cref="ISaveSystem"/> 内部
    /// 实现直接按数组下标迭代，不必再自行决定同阶段内的先后。
    /// </summary>
    public static class SaveSections
    {
        /// <summary>meta 段：仅摘要，不参与模拟，由 <see cref="ISaveSystem"/> 自身读写，
        /// 不经 <see cref="IPersistable"/>（见 10 第 2.1 节、第 3 节步骤 1）。</summary>
        public const string Meta = "meta";

        /// <summary>world_state_flags 段（10 第 2.3、3 节步骤 2）。</summary>
        public const string WorldStateFlags = "world_state_flags";

        /// <summary>player.progression 段（10 第 2.2、3 节步骤 3）。</summary>
        public const string PlayerProgression = "player.progression";

        /// <summary>player.archetype 段（10 第 2.2、3 节步骤 3，对应字段 <c>archetype_id</c>）。</summary>
        public const string PlayerArchetype = "player.archetype";

        /// <summary>player.inventory 段（10 第 2.2、3 节步骤 4）。</summary>
        public const string PlayerInventory = "player.inventory";

        /// <summary>player.equipment 段（10 第 2.2、3 节步骤 4）。</summary>
        public const string PlayerEquipment = "player.equipment";

        /// <summary>player.known_skills 段（10 第 2.2、3 节步骤 5）。</summary>
        public const string PlayerKnownSkills = "player.known_skills";

        /// <summary>player.skill_bindings 段（10 第 2.2、3 节步骤 5）。</summary>
        public const string PlayerSkillBindings = "player.skill_bindings";

        /// <summary>player.quest_state 段（10 第 2.2、3 节步骤 6）。</summary>
        public const string PlayerQuestState = "player.quest_state";

        /// <summary>player.achievement_state 段（10 第 2.2、3 节步骤 6）。</summary>
        public const string PlayerAchievementState = "player.achievement_state";

        /// <summary>player.currencies 段（10 第 2.2、3 节步骤 6）。</summary>
        public const string PlayerCurrencies = "player.currencies";

        /// <summary>world.current_map_id 段（10 第 2.3、3 节步骤 7）。</summary>
        public const string WorldCurrentMapId = "world.current_map_id";

        /// <summary>world.current_position 段（10 第 2.3、3 节步骤 7）。</summary>
        public const string WorldCurrentPosition = "world.current_position";

        /// <summary>rng.stream_states 段（10 第 2.4、3 节步骤 8），见
        /// <see cref="Core.Foundation.SaveSystem.RngStreamsPersistable"/>。</summary>
        public const string RngStreamStates = "rng.stream_states";

        /// <summary>
        /// 已知段的固定全序（含 <see cref="Meta"/>，供 <see cref="ISaveSystem"/> 内部实现
        /// 参考位置；实际写盘/读档的段迭代会跳过 <see cref="Meta"/>，因为它不经
        /// <see cref="IPersistable"/>）。未列入本表的自定义段在这些段之后，按其 key 的
        /// 序数（ordinal）顺序处理（见本模块 README"自定义段"一节）。
        /// </summary>
        public static readonly IReadOnlyList<string> KnownOrder = new[]
        {
            Meta,
            WorldStateFlags,
            PlayerProgression,
            PlayerArchetype,
            PlayerInventory,
            PlayerEquipment,
            PlayerKnownSkills,
            PlayerSkillBindings,
            PlayerQuestState,
            PlayerAchievementState,
            PlayerCurrencies,
            WorldCurrentMapId,
            WorldCurrentPosition,
            RngStreamStates,
        };
    }
}
