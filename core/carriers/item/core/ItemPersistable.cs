using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <c>player.inventory</c> 段（见 10 第 2.2 节"inventory: List&lt;ItemInstance&gt;"、第 3 节汇总
    /// 顺序步骤 4）。序列化为一个 <see cref="ItemInstance"/> 的 JSON 数组（见 <see
    /// cref="ItemInstanceJson"/>）。
    /// </summary>
    public sealed class InventoryPersistable : IPersistable
    {
        private readonly Id _unitId;
        private readonly InventoryHost _inventory;

        public InventoryPersistable(Id unitId, InventoryHost inventory)
        {
            _unitId = unitId;
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public string SectionKey => SaveSections.PlayerInventory;

        public JsonValue Save()
        {
            var items = new List<JsonValue>();
            foreach (var instance in _inventory.ListItems(_unitId))
            {
                items.Add(ItemInstanceJson.ToJson(instance));
            }

            return new JsonArray(items);
        }

        public void Load(JsonValue data)
        {
            if (data is JsonNull)
            {
                return;
            }

            if (!(data is JsonArray array))
            {
                throw new FormatException(
                    $"{SectionKey} 段的数据不是 JSON 数组（实际种类：{data.Kind}）");
            }

            var items = new List<ItemInstance>(array.Count);
            foreach (var raw in array)
            {
                items.Add(ItemInstanceJson.FromJson(raw));
            }

            _inventory.ReplaceBag(_unitId, items);
        }
    }

    /// <summary>
    /// <c>player.equipment</c> 段（见 10 第 2.2 节"equipment: Map&lt;String, ItemInstance&gt;"、第 3
    /// 节汇总顺序步骤 4）。
    /// <para>
    /// 判断记录——"属性/技能/光环不存快照，读档后重新执行装备联动"（见 10 第 2.5 节"属性快照……
    /// 默认不存……存基础来源（装备……）后可在读档时重新聚合得到""光环实例……默认不存"）：<see
    /// cref="Load"/> 不直接把 <see cref="ItemInstance"/> 塞进 <see cref="EquipmentHost"/> 内部字典，
    /// 而是先经 <see cref="InventoryHost.InjectInstance"/> 放回背包，再调用一次真正的 <see
    /// cref="EquipmentHost.Equip"/>——这样属性修正/技能授予/光环施加/套装加成会按穿戴当时的同一套
    /// 逻辑重新跑一遍，不存在"存档里的是旧版本平衡性数值，读档后又没重新算"的双份真相问题。
    /// </para>
    /// </summary>
    public sealed class EquipmentPersistable : IPersistable
    {
        private readonly Id _unitId;
        private readonly InventoryHost _inventory;
        private readonly EquipmentHost _equipment;
        private readonly IItemDiagnostics _diagnostics;

        public EquipmentPersistable(
            Id unitId,
            InventoryHost inventory,
            EquipmentHost equipment,
            IItemDiagnostics? diagnostics = null)
        {
            _unitId = unitId;
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _diagnostics = diagnostics ?? new InMemoryItemDiagnostics();
        }

        public string SectionKey => SaveSections.PlayerEquipment;

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder();
            foreach (var kv in _equipment.GetAllEquippedInstances(_unitId))
            {
                builder.Add(kv.Key.Value, ItemInstanceJson.ToJson(kv.Value));
            }

            return builder.Build();
        }

        public void Load(JsonValue data)
        {
            if (data is JsonNull)
            {
                return;
            }

            if (!(data is JsonObject obj))
            {
                throw new FormatException(
                    $"{SectionKey} 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            foreach (var kv in obj)
            {
                if (!Id.TryParse(kv.Key, out var slot))
                {
                    throw new FormatException($"{SectionKey} 段的槽位键 \"{kv.Key}\" 不是合法 Id");
                }

                var instance = ItemInstanceJson.FromJson(kv.Value);
                _inventory.InjectInstance(_unitId, instance);

                var result = _equipment.Equip(_unitId, instance.InstanceId, slot);
                if (!result.Success)
                {
                    _diagnostics.Warn(
                        $"{SectionKey} 加载：槽位 \"{slot}\" 的物品 \"{instance.InstanceId}\" 重新装备" +
                        $"失败（{result.Reason}），物品已留在背包中");
                }
            }
        }
    }
}
