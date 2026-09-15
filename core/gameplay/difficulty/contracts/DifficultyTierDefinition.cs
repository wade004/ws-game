using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Difficulty
{
    /// <summary>
    /// 一条 <c>diff.tier</c> 记录的强类型视图（见 08 第 5.1 节字段表 + 任务书拍板补录
    /// <c>name_key</c>、<c>sort_weight</c>，惯例同 <c>Core.Carriers.Creature.CreatureTemplate</c>：
    /// 从 <see cref="DataRecord"/> 构造，非法数据在构造期即抛 <see cref="DataFieldException"/>）。
    /// </summary>
    public sealed class DifficultyTierDefinition
    {
        private static readonly IReadOnlyList<Id> EmptyIds = Array.Empty<Id>();

        public Id Id { get; }

        /// <summary>显示名文本键（08 第 5.1 节未列出，任务书拍板补录，惯例同
        /// <c>creature.tier_definition.name_key</c>）。</summary>
        public Id NameKey { get; }

        /// <summary>施加给该难度下敌对单位（或全体单位，见 <see cref="DifficultyOptions.ApplyToAll"/>）
        /// 的修正光环列表（08 第 5.1 节）。</summary>
        public IReadOnlyList<Id> ModifierAuraRefs { get; }

        /// <summary>词缀池引用（08 第 5.1 节"具体词缀内容为后续扩展，本版只登记挂载点"）。</summary>
        public Id? AffixPoolRef { get; }

        /// <summary>掉落数量/概率的整体倍率（08 第 5.1 节）。</summary>
        public double LootMultiplier { get; }

        /// <summary>T-N2-8 新增（ADR-0032 决策 5；08 第 5.1 节修订段）：该难度下掉落与商人上架的物品
        /// 等级偏移——满级后由它接手，装备等级替代角色等级成为成长轴（见
        /// <c>Core.Gameplay.Loot.RollContext.ItemLevelOffset</c> 判断记录）。缺省 0（同
        /// <see cref="AffixPoolRef"/>"本版只登记挂载点"一贯口径，未配置时无偏移）。</summary>
        public int ItemLevelOffset { get; }

        /// <summary>仅供内容管线/编辑器排序展示，运行期不读取（惯例同
        /// <c>creature.tier_definition.sort_weight</c>）。</summary>
        public double SortWeight { get; }

        /// <summary>T-N4-4 新增（ADR-0033 决策 4；08 第 5.1 节修订段）：该难度档的经验倍率，缺省
        /// 1（同 <see cref="LootMultiplier"/>/<see cref="ItemLevelOffset"/> 一贯口径——未配置时
        /// 中性默认值）。</summary>
        public double XpMultiplier { get; }

        private DifficultyTierDefinition(
            Id id, Id nameKey, IReadOnlyList<Id> modifierAuraRefs, Id? affixPoolRef,
            double lootMultiplier, int itemLevelOffset, double sortWeight, double xpMultiplier)
        {
            Id = id;
            NameKey = nameKey;
            ModifierAuraRefs = modifierAuraRefs;
            AffixPoolRef = affixPoolRef;
            LootMultiplier = lootMultiplier;
            ItemLevelOffset = itemLevelOffset;
            SortWeight = sortWeight;
            XpMultiplier = xpMultiplier;
        }

        public static DifficultyTierDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var id = record.GetId("id");
            var nameKey = record.GetId("name_key");
            var modifierAuraRefs = record.TryGetIdList("modifier_aura_refs", out var refs) ? refs : EmptyIds;
            var affixPoolRef = record.TryGetId("affix_pool_ref", out var apr) ? (Id?)apr : null;
            var lootMultiplier = record.GetNumber("loot_multiplier");
            var itemLevelOffset = record.TryGetInt("item_level_offset", out var ilo) ? (int)ilo : 0;
            var sortWeight = record.TryGetNumber("sort_weight", out var sw) ? sw : 0.0;
            var xpMultiplier = record.TryGetNumber("xp_multiplier", out var xm) ? xm : 1.0;

            return new DifficultyTierDefinition(id, nameKey, modifierAuraRefs, affixPoolRef, lootMultiplier, itemLevelOffset, sortWeight, xpMultiplier);
        }
    }
}
