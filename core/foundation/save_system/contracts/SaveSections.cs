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

        /// <summary>world.dropped_loot 段（10 第 2.3、3 节步骤 7a，世界附属段），见
        /// <c>Core.Gameplay.Loot.DroppedLootPersistable</c>。W2 收边补齐（A4 审计 F2）：此前只是
        /// 字面量自定义段，未登记进 <see cref="KnownOrder"/>，导致实际读写顺序按 key 序数排列，
        /// 与 10 第 3 节"7a 在 7b 之前"的文字顺序不完全一致（<c>sim.turn_state</c> 实际排在
        /// 全部 7a 段之前）——现补登记固定顺序，消除该偏差。</summary>
        public const string WorldDroppedLoot = "world.dropped_loot";

        /// <summary>world.vendor_stock 段（10 第 2.3、3 节步骤 7a），见
        /// <c>Core.Gameplay.Economy.VendorStockPersistable</c>。同 <see cref="WorldDroppedLoot"/>
        /// 判断记录。</summary>
        public const string WorldVendorStock = "world.vendor_stock";

        /// <summary>world.difficulty 段（10 第 2.3、3 节步骤 7a，段 key 不带 <c>world.</c>
        /// 前缀由 10 文档另行说明；本段本身按 <c>Core.Gameplay.Difficulty.DifficultyHost.
        /// SectionKeyValue</c> 字面量取值），见该类型判断记录。同 <see cref="WorldDroppedLoot"/>
        /// 判断记录。</summary>
        public const string WorldDifficulty = "world.difficulty";

        /// <summary>spawn_state 段（10 第 2.3、3 节步骤 7a，段 key 不带 <c>world.</c> 前缀，
        /// 2026-09-05 勘误），见 <c>Core.Gameplay.Spawn.SpawnHost</c>。同
        /// <see cref="WorldDroppedLoot"/> 判断记录。</summary>
        public const string SpawnState = "spawn_state";

        /// <summary>sim.turn_state 段（10 第 2.4、3 节步骤 7b，ADR-0013 离散时间模型；只在离散
        /// 模式装配时有内容，连续模式为空段），见
        /// <c>Core.Foundation.SimLoop.TurnScheduler.SectionKeyConst</c>。同
        /// <see cref="WorldDroppedLoot"/> 判断记录：此前未登记进 <see cref="KnownOrder"/>，
        /// 按 key 序数排序时实际排在全部 7a 段之前，与 10 文档"7a 后 7b"的文字顺序不一致，现
        /// 补登记到 7a 之后，消除该偏差。</summary>
        public const string SimTurnState = "sim.turn_state";

        /// <summary>
        /// 已知段的固定全序（含 <see cref="Meta"/>，供 <see cref="ISaveSystem"/> 内部实现
        /// 参考位置；实际写盘/读档的段迭代会跳过 <see cref="Meta"/>，因为它不经
        /// <see cref="IPersistable"/>）。未列入本表的自定义段在这些段之后，按其 key 的
        /// 序数（ordinal）顺序处理（见本模块 README"自定义段"一节）。
        /// <para>
        /// W2 收边补齐（A4 审计 F2，10 第 3 节"7a 后 7b"）：<see cref="WorldDroppedLoot"/>/
        /// <see cref="WorldVendorStock"/>/<see cref="WorldDifficulty"/>/<see cref="SpawnState"/>
        /// （7a，世界附属段，顺序按 10 文档原文"world.dropped_loot / world.vendor_stock /
        /// world.difficulty / spawn_state"排列）与 <see cref="SimTurnState"/>（7b）此前均未登记
        /// 进本表，落入"自定义段"分支按 key 的 <see cref="System.StringComparer.Ordinal"/> 序数
        /// 排序，导致 <c>sim.turn_state</c> 实际排在全部 7a 段之前，与文档"7a 后 7b"的文字顺序
        /// 不完全一致（<c>TurnScheduler.Load</c> 不在加载期即时校验参与者实体是否存在，未观察到
        /// 运行期异常，纯粹是顺序偏差）。现补登记这 5 个段，固定其相对顺序与文档一致。
        /// </para>
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
            WorldDroppedLoot,
            WorldVendorStock,
            WorldDifficulty,
            SpawnState,
            SimTurnState,
            RngStreamStates,
        };
    }
}
