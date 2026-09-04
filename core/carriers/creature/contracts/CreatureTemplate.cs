using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// 一条 <c>creature.template</c> 记录的强类型视图（见 07_载体层_物品生物物件.md 第 2.1 节字段
    /// 表）。从 <see cref="DataRecord"/> 构造，构造期完成全部字段解析，非法数据在构造期即抛
    /// <see cref="DataFieldException"/>（惯例同 <c>Core.Numbers.PowerSet.PowerTypeDefinition</c>：
    /// "字段值不合法"在构造期拦截，不允许进入运行时）。
    /// </summary>
    public sealed class CreatureTemplate
    {
        private static readonly IReadOnlyList<NpcFlag> EmptyFlags = Array.Empty<NpcFlag>();
        private static readonly IReadOnlyList<Id> EmptyIds = Array.Empty<Id>();

        public Id Id { get; }

        /// <summary>显示名文本键（07 第 2.1 节未列出，任务书拍板补录，见
        /// <c>CreatureSchemas.Template</c> 判断记录）。</summary>
        public Id NameKey { get; }

        public int Level { get; }

        /// <summary>强度分档引用（<c>creature.tier_definition</c>）。</summary>
        public Id TierId { get; }

        /// <summary>基础属性：StatKey → 数值（07 第 2.1 节 <c>base_stats: Map&lt;StatKey, Number&gt;</c>）。</summary>
        public IReadOnlyDictionary<Id, double> BaseStats { get; }

        /// <summary>成长曲线引用（<c>prog.level_curve</c>，见 07 第 2.1 节 <c>stat_growth_ref</c>
        /// 判断记录"与 prog.level_curve 同类结构"，拍板直接复用该表）。</summary>
        public Id? StatGrowthRef { get; }

        public Id FactionId { get; }

        public IReadOnlyList<NpcFlag> NpcFlags { get; }

        public Id? AiRotationRef { get; }

        public Id? AiBehaviorRef { get; }

        public Id? LootTableRef { get; }

        public Id DisplayRef { get; }

        /// <summary>免疫的学派/效果类型/控制类别（07 第 2.1 节 <c>immunities</c>）。</summary>
        public IReadOnlyList<Id> Immunities { get; }

        public Id? OnHitReactionRef { get; }

        public Id? OnDeathReactionRef { get; }

        private CreatureTemplate(
            Id id,
            Id nameKey,
            int level,
            Id tierId,
            IReadOnlyDictionary<Id, double> baseStats,
            Id? statGrowthRef,
            Id factionId,
            IReadOnlyList<NpcFlag> npcFlags,
            Id? aiRotationRef,
            Id? aiBehaviorRef,
            Id? lootTableRef,
            Id displayRef,
            IReadOnlyList<Id> immunities,
            Id? onHitReactionRef,
            Id? onDeathReactionRef)
        {
            Id = id;
            NameKey = nameKey;
            Level = level;
            TierId = tierId;
            BaseStats = baseStats;
            StatGrowthRef = statGrowthRef;
            FactionId = factionId;
            NpcFlags = npcFlags;
            AiRotationRef = aiRotationRef;
            AiBehaviorRef = aiBehaviorRef;
            LootTableRef = lootTableRef;
            DisplayRef = displayRef;
            Immunities = immunities;
            OnHitReactionRef = onHitReactionRef;
            OnDeathReactionRef = onDeathReactionRef;
        }

        public static CreatureTemplate FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var id = record.GetId("id");
            var nameKey = record.GetId("name_key");
            var level = (int)record.GetInt("level");
            var tierId = record.GetId("tier");

            var baseStatsObj = record.GetObject("base_stats");
            var baseStats = new Dictionary<Id, double>();
            foreach (var kv in baseStatsObj)
            {
                if (kv.Value is JsonNumber num && Id.TryParse(kv.Key, out var statId))
                {
                    baseStats[statId] = num.Value;
                }
                else
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "base_stats",
                        $"元素 \"{kv.Key}\" 不是合法的 Id → Number 映射");
                }
            }

            var statGrowthRef = record.TryGetId("stat_growth_ref", out var sgr) ? (Id?)sgr : null;
            var factionId = record.GetId("faction_id");

            var npcFlags = EmptyFlags;
            if (record.TryGetIdList("npc_flags", out var flagIds))
            {
                var flags = new List<NpcFlag>(flagIds.Count);
                foreach (var flagId in flagIds)
                {
                    if (NpcFlagIds.TryParse(flagId, out var flag))
                    {
                        flags.Add(flag);
                    }
                    else
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "npc_flags",
                            $"未登记的职能标志 \"{flagId}\"（合法集合见 07 第 2.2 节六值）");
                    }
                }
                npcFlags = flags;
            }

            var aiRotationRef = record.TryGetId("ai_rotation_ref", out var arr) ? (Id?)arr : null;
            var aiBehaviorRef = record.TryGetId("ai_behavior_ref", out var abr) ? (Id?)abr : null;
            var lootTableRef = record.TryGetId("loot_table_ref", out var ltr) ? (Id?)ltr : null;
            var displayRef = record.GetId("display_ref");

            var immunities = record.TryGetIdList("immunities", out var immList) ? immList : EmptyIds;

            var onHitReactionRef = record.TryGetId("on_hit_reaction_ref", out var ohr) ? (Id?)ohr : null;
            var onDeathReactionRef = record.TryGetId("on_death_reaction_ref", out var odr) ? (Id?)odr : null;

            return new CreatureTemplate(
                id, nameKey, level, tierId, baseStats, statGrowthRef, factionId, npcFlags,
                aiRotationRef, aiBehaviorRef, lootTableRef, displayRef, immunities,
                onHitReactionRef, onDeathReactionRef);
        }
    }
}
