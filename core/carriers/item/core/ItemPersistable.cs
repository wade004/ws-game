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
    /// <para>
    /// FND-10 收边勘误——<see cref="Load"/> 必须是"完整替换"，不是"合并"：原实现只对快照里出现的
    /// 槽位调用 Inject/Equip，从不清理调用前已经装备着、快照里没有提到的物品——空快照因此完全是
    /// 空操作（旧装备原样保留），缺槽快照也只会让对应槽位"多"出一件旧物品而不是变空，两种情况都
    /// 与"读档=回到快照那一刻的状态"矛盾（见外部审计 <c>architecture/落地计划/
    /// audit-b3b91ee-20260907/code-review.md</c> FND-10、<c>validation-repros.txt</c> R2/R2b）。
    /// 现在 <see cref="Load"/> 开头先调用 <see cref="EquipmentHost.ClearAllEquippedForLoad"/> 把该
    /// 单位重置到"无装备"（撤销全部联动，物品不放回背包，见该方法判断记录），再按快照从零重新
    /// Inject/Equip：空快照清空装备、缺槽快照对应槽位归空、重复 Load 两次幂等、"快照之外新装备的
    /// 物品"不会在换装时被错误地放回背包污染背包状态（不再依赖 <see cref="EquipmentHost.Equip"/>
    /// 内部"换装"分支的副作用来处理"旧物品该去哪"这个问题——那个分支是为正常运行时换装设计的，
    /// 语义是"卸下的旧物品还留在这个世界里，放回背包"，与读档"旧物品本就不属于这份快照代表的历史
    /// 状态"完全不同）。
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
            // FND-10 修复：先把该单位重置到"无装备"（撤销全部联动、不放回背包，见
            // EquipmentHost.ClearAllEquippedForLoad 判断记录），再按快照原子恢复——保证空快照/
            // 缺槽快照/JsonNull（本段在这份存档里不存在）都能正确让对应槽位归空，且与调用前的
            // 装备状态、调用顺序（先/后于 InventoryPersistable.Load）无关。
            _equipment.ClearAllEquippedForLoad(_unitId);

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
