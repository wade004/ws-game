using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 消费方反馈第 52 条（<c>architecture/落地计划/消费方反馈-2026-09-17-编辑器-第52-53条.md</c>）：
    /// 把一个已装备 <see cref="ItemInstance"/> 的身份字段（模板/品质/词缀）原样重排为"武器摘要三元组"
    /// ——纯搬运、不计算数值（同 <see cref="ItemInstance"/> 顶部判断记录"物品实例只存身份……不存任何
    /// 算出的属性数值"的口径），供下游消费方（如编辑器结算预览 <c>SettlementPreviewRequest.CasterWeapon
    /// (TemplateId, QualityId, AffixIds)</c>）一次调用取得与自建适配层（如反馈原文提到的
    /// <c>StandardPlayerWeaponAdapter</c>）手工搬运完全一致的三元组，不必各个消费方各自重复实现同一份
    /// 薄转换层。
    /// <para>
    /// 判断记录（不限定"只用于武器槽"）：类型名沿用反馈原文与建议方法名
    /// <c>StandardPlayer.GetEquippedWeaponSummary(weaponSlotId)</c> 里的"武器"字样，但本类型与
    /// <see cref="EquipmentHost.GetEquippedWeaponSummary"/> 实际不关心槽位是不是
    /// <c>item.slot_definition.is_weapon</c>——<see cref="ItemInstance"/> 本身不携带"是不是武器"这个
    /// 判断，槽位是否为武器槽由调用方自行对照 <c>item.slot_definition</c> 判断；本类型只做"给定一个
    /// 槽位，取它当前挂的实例身份三元组"这一件事，武器/非武器槽都能用同一套 API。
    /// </para>
    /// <para>
    /// 判断记录（用 <c>readonly struct</c> 而不是 <c>class</c>）：与 <see cref="ItemInstance"/>/
    /// <see cref="ItemInstanceRef"/> 同类型定位一致——三个字段都是不可变身份数据，值语义更贴近调用方
    /// 的使用方式（逐字段比较、不需要引用相等）。
    /// </para>
    /// </summary>
    public readonly struct EquippedWeaponSummary : IEquatable<EquippedWeaponSummary>
    {
        private static readonly IReadOnlyList<Id> EmptyAffixes = Array.Empty<Id>();

        public Id TemplateId { get; }

        public Id QualityId { get; }

        /// <summary>不可变、默认为空列表（不是 null）——同 <see cref="ItemInstance.Affixes"/> 的口径。</summary>
        public IReadOnlyList<Id> AffixIds { get; }

        public EquippedWeaponSummary(Id templateId, Id qualityId, IReadOnlyList<Id>? affixIds)
        {
            TemplateId = templateId;
            QualityId = qualityId;
            AffixIds = affixIds ?? EmptyAffixes;
        }

        /// <summary>从一个已装备的 <see cref="ItemInstance"/> 原样映射三个身份字段——纯搬运，不做任何
        /// 数值计算（见类型顶部判断记录）。</summary>
        public static EquippedWeaponSummary FromInstance(ItemInstance instance) =>
            new EquippedWeaponSummary(instance.TemplateId, instance.Quality, instance.Affixes);

        public bool Equals(EquippedWeaponSummary other)
        {
            if (!TemplateId.Equals(other.TemplateId) || !QualityId.Equals(other.QualityId)) return false;
            if (AffixIds.Count != other.AffixIds.Count) return false;
            for (var i = 0; i < AffixIds.Count; i++)
            {
                if (!AffixIds[i].Equals(other.AffixIds[i])) return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is EquippedWeaponSummary other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = TemplateId.GetHashCode() * 397 ^ QualityId.GetHashCode();
                foreach (var affixId in AffixIds)
                {
                    hash = hash * 397 ^ affixId.GetHashCode();
                }

                return hash;
            }
        }

        public static bool operator ==(EquippedWeaponSummary left, EquippedWeaponSummary right) => left.Equals(right);

        public static bool operator !=(EquippedWeaponSummary left, EquippedWeaponSummary right) => !left.Equals(right);
    }
}
