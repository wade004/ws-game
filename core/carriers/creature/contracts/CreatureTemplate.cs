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

        /// <summary>ADR-0051：生物原生交互路径——指向 <c>dialog.gossip_menu</c> 的对话菜单引用，
        /// 供 <c>Core.Carriers.Creature.CreatureInteractionHost.Interact</c> 在 <c>interact</c> 意图
        /// 直接命中一个生物实例（不必先包装成 <c>gobj.template</c>）时分发使用；<c>null</c>（缺省，
        /// 既有数据行零改动仍合法）表示该生物当前未配置任何原生可交互内容，交互会记一条诊断，见
        /// <see cref="Core.Carriers.Creature.CreatureInteractionHost"/> 判断记录。语义与命名对齐
        /// <c>Core.Carriers.Gobj.GameObjectTemplate</c> 的 <c>on_use: dialog</c> 分支（同一份
        /// <c>dialog.gossip_menu</c> 表，L3 不引用 L4，登记为软引用，见
        /// <see cref="Core.Carriers.Creature.CreatureSchemas"/> 判断记录），但不复用 <c>on_use</c>
        /// 判别联合结构——生物当前只有"对话"一种原生交互结果（不像 gobj 还有 <c>on_use: skill</c>
        /// 分支），沿用判别联合会引入一个恒定单分支的多余抽象层。</summary>
        public Id? GossipMenuRef { get; }

        /// <summary>ADR-0059（消费方反馈第三批第 5 条"普通攻击缺少框架原生执行机制"）新增可选字段：
        /// 该生物没有装备武器时，普通攻击挥击间隔（秒）的回退数据源——见
        /// <c>Core.Rules.Common.IAttackIntervalFallbackProvider</c>/<c>Core.Rules.Combat
        /// .AutoAttackHost</c> 判断记录"优先取武器 speed，武器缺失才回退本字段"。纯新增可选字段
        /// （07 第 2.1 节原字段表未列出，本次任务补录），不升 <see cref="CreatureSchemas.Template"/>
        /// 的 <c>currentSchemaVersion</c>、不需要迁移函数——旧数据行零改动仍合法，未声明时为
        /// <c>null</c>，等价于"该生物没有回退攻击间隔"（<c>AutoAttackHost</c> 据此按"运行时不静默
        /// 降级"处理，记诊断、当次不攻击，不是"当前完全没影响"的静默行为——但这是调用方
        /// <c>AutoAttackHost</c> 的诊断口径，不是本类型/本字段自身的行为变化：未声明本字段的既有
        /// 生物模板在"没有启用普通攻击"这一前提下，行为逐位不变）。</summary>
        public double? AttackInterval { get; }

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
            Id? onDeathReactionRef,
            Id? gossipMenuRef,
            double? attackInterval)
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
            GossipMenuRef = gossipMenuRef;
            AttackInterval = attackInterval;
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
            var gossipMenuRef = record.TryGetId("gossip_menu_ref", out var gmr) ? (Id?)gmr : null;
            var attackInterval = record.TryGetNumber("attack_interval", out var ai) ? (double?)ai : null;

            return new CreatureTemplate(
                id, nameKey, level, tierId, baseStats, statGrowthRef, factionId, npcFlags,
                aiRotationRef, aiBehaviorRef, lootTableRef, displayRef, immunities,
                onHitReactionRef, onDeathReactionRef, gossipMenuRef, attackInterval);
        }
    }
}
