using System;
using System.Collections.Generic;
using Core.Carriers.Assembly;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Rules.Skill;
using Core.Sim;
using Presentation.Ui;

namespace Lab
{
    /// <summary>
    /// 衣橱舞台（手感设计/06 第 3.6 节、08 第 6 节、ADR-0149）：一个活的、可逐步穿脱的装备世界，供引擎宿主的换装场景（装备面板 + 预览区）
    /// 在屏幕上逐件穿戴。世界是真实的无头世界（真实背包与装备载体，同 <see cref="EquipRig"/> 的穿脱路径），装备面板视图模型
    /// 是生产用的 <see cref="EquipmentViewModel"/>——引擎侧的面板绑的就是它，数量由数据与真实装备状态算出，不是演出。
    /// <para>
    /// 判断记录（与 <see cref="EquipWardrobe"/> 分工）：<see cref="EquipWardrobe"/> 生成脚本并把<b>无头/引擎宿主跑完的记录</b>核对成报告（回归口径、可复现）；
    /// 本类给引擎宿主一个<b>可视化逐步穿戴</b>的活世界（所见即所得、截图拼图用）。两者读同一份数据清单（<see cref="EquipWardrobe.ListEquippable"/>），
    /// 同一条穿脱路径，所以屏幕上看到的步骤与报告里的步骤一一对应。舞台不跑动作与手感系统（不需要），也不进指纹。
    /// </para>
    /// </summary>
    public sealed class WardrobeStage : IDisposable
    {
        private readonly HeadlessWorld _world;
        private readonly Id _player;
        private readonly DisplayInfoRegistry _displayInfo;

        public IReadOnlyList<WardrobeEntry> Entries { get; }

        /// <summary>数据里装备槽位总数。</summary>
        public int SlotCount { get; }

        public IDataRegistryView Registry => _world.Registry;

        public IDisplayInfoRegistry DisplayInfo => _displayInfo;

        /// <summary>界面数据源（装备面板视图模型读它；引擎侧面板读它查询玩家装备）。</summary>
        public IUiDataSource Data { get; }

        /// <summary>装备面板视图模型（生产类型）。</summary>
        public EquipmentViewModel Panel { get; }

        private WardrobeStage(HeadlessWorld world, IReadOnlyList<WardrobeEntry> entries, int slotCount)
        {
            _world = world;
            _player = world.Player.EntityId;
            Entries = entries;
            SlotCount = slotCount;
            _displayInfo = new DisplayInfoRegistry(world.Registry, world.Bus);

            var carriers = world.Gameplay.Carriers;
            var skillBook = new SkillHostSkillBookQuery(carriers.Rules.Skill);
            var providers = new IUiPathProvider[]
            {
                new PlayerPathProvider(
                    _player, carriers.Rules.Stats, carriers.Rules.Powers, carriers.Rules.Progression, carriers.Inventory, carriers.Equipment,
                    world.Gameplay.Quest, world.Gameplay.Economy, skillBook, carriers.Rules.Skill.AuraQuery,
                    carriers.Units, carriers.Rules.AutoAttack, world.Gameplay.AreaTrigger, world.Registry),
            };
            Data = new UiDataSource(world.Bus, providers);
            Panel = new EquipmentViewModel(Data, world.Registry, _displayInfo);
        }

        /// <summary>在模板脚本声明的数据集（基础数据 + 额外数据根）上建舞台，物品清单由数据算出。</summary>
        public static WardrobeStage Create(LabRunner runner, InputScript template)
        {
            var dataset = runner.DatasetFor(template);
            var world = LabHost.BuildProbe(dataset.HostOptions);
            var entries = EquipWardrobe.ListEquippable(world.Registry, out var slots);
            return new WardrobeStage(world, entries, slots);
        }

        /// <summary>穿上一件物品（背包加一件再穿到模板声明的槽位，同换装脚本的 equip 事件）。成功返回 true。</summary>
        public bool Equip(string itemTemplateId)
        {
            var template = _world.Registry.Get("item.template", itemTemplateId)
                ?? throw new LabFormatException($"物品 {itemTemplateId} 在数据里不存在");
            var slot = template.GetId("slot");
            var inventory = _world.Gameplay.Carriers.Inventory;
            if (!inventory.AddItem(_player, new Id(itemTemplateId), 1))
            {
                return false;
            }

            Id? instance = null;
            foreach (var item in inventory.ListItems(_player))
            {
                if (string.Equals(item.TemplateId.Value, itemTemplateId, StringComparison.Ordinal))
                {
                    instance = item.InstanceId;
                }
            }

            if (!instance.HasValue)
            {
                return false;
            }

            var result = _world.Gameplay.Carriers.Equipment.Equip(_player, instance.Value, slot);
            _world.Bus.DispatchPending();
            Panel.Refresh();
            return result.Success;
        }

        /// <summary>卸下一个槽位。</summary>
        public bool Unequip(string slotId)
        {
            var removed = _world.Gameplay.Carriers.Equipment.Unequip(_player, new Id(slotId)) != null;
            _world.Bus.DispatchPending();
            Panel.Refresh();
            return removed;
        }

        /// <summary>卸空全部已装备槽位。</summary>
        public void UnequipAll()
        {
            // 先取快照：卸下会让面板刷新并重建槽位清单。
            var occupied = new List<string>();
            foreach (var slot in Panel.Slots)
            {
                if (slot.Occupied)
                {
                    occupied.Add(slot.SlotId.Value);
                }
            }

            foreach (var slotId in occupied)
            {
                Unequip(slotId);
            }
        }

        public void Dispose()
        {
            Panel.Dispose();
        }
    }
}
