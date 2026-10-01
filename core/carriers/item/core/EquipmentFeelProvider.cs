using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="IFeelEquipmentProvider"/> 的默认实现（手感设计/05 第 2 节第 4 层、08 第 1 节换装链）：
    /// 从装备宿主读取单位当前装备，取武器槽里物品模板的 <c>feel_weapon_ref</c> 作为主手/副手武器手感引用。
    /// <para>
    /// 判断记录（主手/副手怎么定）：同 <c>EquipmentHost.GetWeaponBaseDamage</c> 既有惯例——只看<b>已装备</b>的武器槽
    /// （<c>item.slot_definition.is_weapon</c> 为真的槽），按槽位 id 序数序，第一个是主手，第二个是副手；
    /// 空槽不占位（主手槽空而副手槽有物品时，副手槽的物品按序数序成为"主手"，与基础伤害口径一致，不另起一套）。
    /// 武器槽里的物品模板没有 <c>feel_weapon_ref</c> 时该手视为"无手感武器"（返回 null，解析器按该层为空处理）。
    /// </para>
    /// <para>
    /// 判断记录（无缓存）：本类每次调用都读装备宿主，不自带缓存——解析器按单位缓存解析结果，换装后由
    /// <see cref="EquipmentFeelChain"/> 失效；这里再缓存一层只会多一个需要对账的失效点。
    /// </para>
    /// </summary>
    public sealed class EquipmentFeelProvider : IFeelEquipmentProvider
    {
        private readonly IEquipmentHost _equipment;
        private readonly IDataRegistryView _registry;

        public EquipmentFeelProvider(IEquipmentHost equipment, IDataRegistryView registry)
        {
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public string? GetMainWeaponRef(Id unitId) => WeaponRefAt(unitId, 0);

        public string? GetOffhandWeaponRef(Id unitId) => WeaponRefAt(unitId, 1);

        private string? WeaponRefAt(Id unitId, int ordinal)
        {
            var identities = _equipment.GetAllEquippedIdentities(unitId);
            if (identities.Count == 0)
            {
                return null;
            }

            var weaponSlots = new List<Id>();
            foreach (var pair in identities)
            {
                var slotDef = _registry.Get("item.slot_definition", pair.Key);
                if (slotDef != null && slotDef.TryGetBool("is_weapon", out var isWeapon) && isWeapon)
                {
                    weaponSlots.Add(pair.Key);
                }
            }

            if (ordinal >= weaponSlots.Count)
            {
                return null;
            }

            weaponSlots.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            var template = _registry.Get("item.template", identities[weaponSlots[ordinal]].TemplateId);
            return template != null && template.TryGetString("feel_weapon_ref", out var weaponRef) ? weaponRef : null;
        }
    }
}
