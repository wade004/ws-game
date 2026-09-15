using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// T-N2-8（ADR-0032 决策 7/8；08 第 1.1 节修订段"三次独立掷骰"；07 第 1.6 节修订段）：带身份的
    /// 一次掉落产出——在既有 <see cref="ItemStack"/>（仅 <c>TemplateId</c>/<c>Count</c>）之上补上
    /// 品质、词缀引用、物品等级三项，供 <see cref="ILootRoller.RollDetailed"/>、
    /// <c>Core.Gameplay.Loot.ILootHost.RollDetailed</c>、<c>Core.Gameplay.Loot.DroppedLootEntity.Outcomes</c>
    /// 使用。
    /// <para>
    /// 判断记录（本类型登记在 <c>Core.Carriers.Common</c>（L3）而不是 <c>Core.Gameplay.Loot</c>（L4））：
    /// 同 <see cref="ItemStack"/> 判断记录——<see cref="ILootRoller"/> 是 L3 依赖倒置接口，
    /// <c>core/carriers/gobj</c>/<c>core/carriers/creature</c> 一类 L3 实现在组装期需要认识
    /// <see cref="ILootRoller.RollDetailed"/> 的返回类型；本类型若登记在 L4 的
    /// <c>Core.Gameplay.Loot</c>，会让 L3 反向引用 L4，违反 <see cref="ILootRoller"/> 类型注释"本模块
    /// （L3）不得直接引用 L4 的 LootHost"的分层约束——与 <see cref="ItemStack"/> 本就登记在
    /// <c>Core.Carriers.Common</c>（供 <see cref="ILootRoller.Roll"/> 返回）同一个理由。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="QualityId"/>/<see cref="ItemLevel"/> 为什么是可空类型而不是"总是已解析的
    /// 具体值"）：<c>Core.Gameplay.Loot.LootHost</c> 自己产出的结果（真正走过三次独立掷骰）总是把两者
    /// 解析成具体值——品质没配 <c>quality_weights</c> 时取模板自身 <c>quality</c>（不掷骰，见该类型
    /// 判断记录"不消耗随机数"）、物品等级没有来源等级时取模板自身 <c>item_level</c>，两者都不会真的
    /// 留空。但 <see cref="ILootRoller.RollDetailed"/> 的默认接口成员实现（转发旧
    /// <see cref="ILootRoller.Roll"/> 并投影，见该接口判断记录）只能拿到 <see cref="ItemStack"/>
    /// （<c>TemplateId</c>/<c>Count</c>），没有 <c>DataRegistry</c> 可查模板自身的品质/物品等级——
    /// 这种"投影不出具体值，只能表达‘没有额外信息，回退到模板自身’"的场景就是两个字段可空的存在
    /// 意义：<c>null</c> 明确表示"未额外指定，消费方应回退到 <c>item.template.quality</c>/
    /// <c>item_level</c>"，不是"品质/物品等级为空"这种不合法状态（同 <c>ItemInstance.Quality</c>
    /// 判断记录"不是像 Extra 那样的可选扩展点"——那是实例最终落地后的强约束，本类型是实例落地前、
    /// 掉落那一刻的中间结果，允许"未解析"）。
    /// </para>
    /// </summary>
    public readonly struct LootRollOutcome : IEquatable<LootRollOutcome>
    {
        private static readonly IReadOnlyList<Id> EmptyAffixes = Array.Empty<Id>();

        public Id TemplateId { get; }

        public int Count { get; }

        /// <summary>品质骰结果；<c>null</c> 表示未掷骰（<c>loot.table.qualityWeights</c> 未配置该条目），
        /// 消费方应回退到 <c>item.template.quality</c>。</summary>
        public Id? QualityId { get; }

        /// <summary>词缀骰结果（<c>item.affix</c> 引用列表）；永不为 <c>null</c>，可能为空列表，不含重复
        /// 元素（"不重复抽同一词缀"，见 08 第 1.1 节修订段）。</summary>
        public IReadOnlyList<Id> Affixes { get; }

        /// <summary>来源等级（怪物等级/难度层偏移）折算出的物品等级；<c>null</c> 表示未提供来源等级
        /// （<c>RollContext.SourceLevel</c> 为空），消费方应回退到 <c>item.template.item_level</c>。</summary>
        public int? ItemLevel { get; }

        public LootRollOutcome(Id templateId, int count, Id? qualityId, IReadOnlyList<Id>? affixes, int? itemLevel)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "LootRollOutcome.Count 必须为正数");
            }

            TemplateId = templateId;
            Count = count;
            QualityId = qualityId;
            Affixes = affixes ?? EmptyAffixes;
            ItemLevel = itemLevel;
        }

        /// <summary>把一条不含身份信息的 <see cref="ItemStack"/> 投影为"未额外指定"的结果（<see
        /// cref="ILootRoller.RollDetailed"/> 默认接口成员使用，见类型判断记录）。</summary>
        public static LootRollOutcome FromStack(ItemStack stack) =>
            new LootRollOutcome(stack.TemplateId, stack.Count, null, null, null);

        /// <summary>投影为不带身份的 <see cref="ItemStack"/>（供旧 <see cref="ILootRoller.Roll"/>/
        /// <c>Core.Gameplay.Loot.ILootHost.Roll</c> 转发路径使用，见 <c>Core.Gameplay.Loot.LootHost</c>
        /// 判断记录）。</summary>
        public ItemStack ToStack() => new ItemStack(TemplateId, Count);

        public bool Equals(LootRollOutcome other)
        {
            if (!TemplateId.Equals(other.TemplateId) || Count != other.Count ||
                !Nullable.Equals(QualityId, other.QualityId) || !Nullable.Equals(ItemLevel, other.ItemLevel) ||
                Affixes.Count != other.Affixes.Count)
            {
                return false;
            }

            for (var i = 0; i < Affixes.Count; i++)
            {
                if (!Affixes[i].Equals(other.Affixes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is LootRollOutcome other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = TemplateId.GetHashCode();
                hash = (hash * 397) ^ Count;
                hash = (hash * 397) ^ (QualityId?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (ItemLevel ?? 0);
                foreach (var affix in Affixes)
                {
                    hash = (hash * 397) ^ affix.GetHashCode();
                }

                return hash;
            }
        }

        public override string ToString() =>
            $"LootRollOutcome({TemplateId} x{Count}, quality={QualityId?.ToString() ?? "<template>"}, " +
            $"affixes=[{string.Join(",", Affixes)}], itemLevel={ItemLevel?.ToString() ?? "<template>"})";

        public static bool operator ==(LootRollOutcome left, LootRollOutcome right) => left.Equals(right);

        public static bool operator !=(LootRollOutcome left, LootRollOutcome right) => !left.Equals(right);
    }
}
