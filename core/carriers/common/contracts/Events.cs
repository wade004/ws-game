using System;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// L3 载体层四个模块（item/creature/gobj/summon）与 unit 移动系统共用的事件 key 常量（对应
    /// <c>found.event_catalog</c> 登记表，见 data/_sample/found/found.event_catalog.json 对应行）。
    /// 惯例同 <c>core/rules/common</c> 的 <c>RulesEventKeys</c>：模块自持一份常量，不依赖生成物。
    /// </summary>
    public static class CarriersEventKeys
    {
        public static readonly Id ItemAdded = new Id("item.added");
        public static readonly Id ItemRemoved = new Id("item.removed");
        public static readonly Id ItemEquipped = new Id("item.equipped");
        public static readonly Id ItemUnequipped = new Id("item.unequipped");

        public static readonly Id CreatureSpawned = new Id("creature.spawned");
        public static readonly Id CreatureDespawned = new Id("creature.despawned");

        public static readonly Id GobjInteracted = new Id("gobj.interacted");
        public static readonly Id GobjStateChanged = new Id("gobj.state_changed");

        public static readonly Id SummonCreated = new Id("summon.created");
        public static readonly Id SummonExpired = new Id("summon.expired");

        public static readonly Id UnitMoved = new Id("unit.moved");
        public static readonly Id UnitStateChanged = new Id("unit.state_changed");
        public static readonly Id UnitSkillBindingChanged = new Id("unit.skill_binding_changed");
    }

    /// <summary>物品加入背包时触发（见 found.event_catalog <c>item.added</c> 行、07 第 1.3 节
    /// <c>InventoryHost.addItem</c>）。</summary>
    public sealed class ItemAddedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.ItemAdded;

        public Id UnitId { get; }

        public Id ItemInstanceId { get; }

        public Id ItemTemplateId { get; }

        public int Count { get; }

        public ItemAddedEvent(Id unitId, Id itemInstanceId, Id itemTemplateId, int count)
        {
            UnitId = unitId;
            ItemInstanceId = itemInstanceId;
            ItemTemplateId = itemTemplateId;
            Count = count;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "itemInstanceId": value = ExprValue.OfId(ItemInstanceId); return true;
                case "itemTemplateId": value = ExprValue.OfId(ItemTemplateId); return true;
                case "count": value = ExprValue.OfInt(Count); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>物品从背包移除时触发，与 <see cref="ItemAddedEvent"/> 对应（见 found.event_catalog
    /// <c>item.removed</c> 行）。<see cref="Reason"/> 是自由文本分类（该行字段表未给出固定枚举，惯例
    /// 同 <c>core/rules/common</c> 的 <c>AuraRemovedEvent.Reason</c>）。</summary>
    public sealed class ItemRemovedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.ItemRemoved;

        public Id UnitId { get; }

        public Id ItemInstanceId { get; }

        public int Count { get; }

        public string Reason { get; }

        public ItemRemovedEvent(Id unitId, Id itemInstanceId, int count, string reason)
        {
            UnitId = unitId;
            ItemInstanceId = itemInstanceId;
            Count = count;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "itemInstanceId": value = ExprValue.OfId(ItemInstanceId); return true;
                case "count": value = ExprValue.OfInt(Count); return true;
                case "reason": value = ExprValue.OfString(Reason); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>装备穿戴生效后触发，供纸娃娃层更新对应槽位（见 found.event_catalog
    /// <c>item.equipped</c> 行、07 第 1.4 节）。</summary>
    public sealed class ItemEquippedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.ItemEquipped;

        public Id UnitId { get; }

        public Id ItemInstanceId { get; }

        public Id Slot { get; }

        public ItemEquippedEvent(Id unitId, Id itemInstanceId, Id slot)
        {
            UnitId = unitId;
            ItemInstanceId = itemInstanceId;
            Slot = slot;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "itemInstanceId": value = ExprValue.OfId(ItemInstanceId); return true;
                case "slot": value = ExprValue.OfId(Slot); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>装备卸下后触发，供纸娃娃层移除对应槽位（见 found.event_catalog
    /// <c>item.unequipped</c> 行、07 第 1.4 节）。</summary>
    public sealed class ItemUnequippedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.ItemUnequipped;

        public Id UnitId { get; }

        public Id Slot { get; }

        public Id ItemInstanceId { get; }

        public ItemUnequippedEvent(Id unitId, Id slot, Id itemInstanceId)
        {
            UnitId = unitId;
            Slot = slot;
            ItemInstanceId = itemInstanceId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "slot": value = ExprValue.OfId(Slot); return true;
                case "itemInstanceId": value = ExprValue.OfId(ItemInstanceId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>生物模板实例化生成时触发（见 found.event_catalog <c>creature.spawned</c> 行）。</summary>
    public sealed class CreatureSpawnedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.CreatureSpawned;

        public Id EntityId { get; }

        public Id TemplateId { get; }

        public CreatureSpawnedEvent(Id entityId, Id templateId)
        {
            EntityId = entityId;
            TemplateId = templateId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "entityId": value = ExprValue.OfId(EntityId); return true;
                case "templateId": value = ExprValue.OfId(TemplateId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>生物实例消失时触发（见 found.event_catalog <c>creature.despawned</c> 行，字段参照 05
    /// 第 5.3 节 <c>SpawnHost.notifyDespawn(entityId, reason: died|despawned)</c> 契约拟定）。
    /// <see cref="Reason"/> 判断记录：见 <see cref="ICreatureFactory.Despawn"/> 顶部注释——保留自由
    /// 字符串而非强类型两值枚举，因为调用方不限于刷新表。</summary>
    public sealed class CreatureDespawnedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.CreatureDespawned;

        public Id EntityId { get; }

        public string Reason { get; }

        public CreatureDespawnedEvent(Id entityId, string reason)
        {
            EntityId = entityId;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "entityId": value = ExprValue.OfId(EntityId); return true;
                case "reason": value = ExprValue.OfString(Reason); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary><c>GameObjectHost.interact</c> 交互触发时发出（见 found.event_catalog
    /// <c>gobj.interacted</c> 行、07 第 3.6 节）。</summary>
    public sealed class GobjInteractedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.GobjInteracted;

        public Id UnitId { get; }

        public Id GobjInstanceId { get; }

        public GobjInteractedEvent(Id unitId, Id gobjInstanceId)
        {
            UnitId = unitId;
            GobjInstanceId = gobjInstanceId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "gobjInstanceId": value = ExprValue.OfId(GobjInstanceId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary><c>GameObject.state</c> 可变字段变化时触发，对应一次 <c>WorldState.set</c>（见
    /// found.event_catalog <c>gobj.state_changed</c> 行、07 第 3.4 节）。<see cref="OldValue"/>/
    /// <see cref="NewValue"/> 按 <see cref="IWorldFlags"/> 顶部判断记录统一用 <see cref="ExprValue"/>
    /// 承载。</summary>
    public sealed class GobjStateChangedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.GobjStateChanged;

        public Id GobjInstanceId { get; }

        public string StateKey { get; }

        public ExprValue OldValue { get; }

        public ExprValue NewValue { get; }

        public GobjStateChangedEvent(Id gobjInstanceId, string stateKey, ExprValue oldValue, ExprValue newValue)
        {
            GobjInstanceId = gobjInstanceId;
            StateKey = stateKey ?? throw new ArgumentNullException(nameof(stateKey));
            OldValue = oldValue;
            NewValue = newValue;
        }

        /// <summary><see cref="OldValue"/>/<see cref="NewValue"/> 已经是 <see cref="ExprValue"/>，
        /// 直接透传。</summary>
        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "gobjInstanceId": value = ExprValue.OfId(GobjInstanceId); return true;
                case "stateKey": value = ExprValue.OfString(StateKey); return true;
                case "oldValue": value = OldValue; return true;
                case "newValue": value = NewValue; return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>召唤物由 <c>summon</c> 效果创建时触发（见 found.event_catalog <c>summon.created</c>
    /// 行、07 第 4 节）。</summary>
    public sealed class SummonCreatedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.SummonCreated;

        public Id EntityId { get; }

        public Id OwnerId { get; }

        public SummonCreatedEvent(Id entityId, Id ownerId)
        {
            EntityId = entityId;
            OwnerId = ownerId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "entityId": value = ExprValue.OfId(EntityId); return true;
                case "ownerId": value = ExprValue.OfId(OwnerId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>召唤物到期或被主动取消/死亡销毁时触发（见 found.event_catalog <c>summon.expired</c>
    /// 行、07 第 4 节）。</summary>
    public sealed class SummonExpiredEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.SummonExpired;

        public Id EntityId { get; }

        public Id OwnerId { get; }

        public SummonExpiredEvent(Id entityId, Id ownerId)
        {
            EntityId = entityId;
            OwnerId = ownerId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "entityId": value = ExprValue.OfId(EntityId); return true;
                case "ownerId": value = ExprValue.OfId(OwnerId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>单位逻辑位置变化时触发，供表现层 View 同步（见 found.event_catalog
    /// <c>unit.moved</c> 行）。<see cref="Position"/> 是 <c>Vec2</c>，不在 <see cref="IExprReadableEvent"/>
    /// 覆盖范围内（同 <c>core/rules/common</c> <c>SkillCastSuccessEvent.Targets</c> 的处理，Expr 无
    /// 复合类型），查询 "position" 返回 false。</summary>
    public sealed class UnitMovedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.UnitMoved;

        public Id UnitId { get; }

        public Vec2 Position { get; }

        public UnitMovedEvent(Id unitId, Vec2 position)
        {
            UnitId = unitId;
            Position = position;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>单位可视状态（如移动/待机/施法等外显状态）变化时触发，供表现层 View 同步（见
    /// found.event_catalog <c>unit.state_changed</c> 行，字段命名参照同表 <c>ai.state_changed</c>
    /// 行）。判断记录：<see cref="OldState"/>/<see cref="NewState"/> 用自由字符串而非强类型枚举——
    /// 与 <c>core/rules/common</c> 的 <c>AiStateChangedEvent</c>（承载 <c>BehaviorState</c>）是同一个
    /// 事件 key 在不同调用方下的不同用法之一，本模块（<c>core/carriers/unit</c> 的
    /// <c>MovementTickHandler</c>）用它承载 <c>MoveMode</c> 名称，其它调用方可能承载别的状态词汇表，
    /// 本类型只提供承载 <c>found.event_catalog</c> 该行字段的通用外壳，不绑定具体状态集合。</summary>
    public sealed class UnitStateChangedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.UnitStateChanged;

        public Id UnitId { get; }

        public string OldState { get; }

        public string NewState { get; }

        public UnitStateChangedEvent(Id unitId, string oldState, string newState)
        {
            UnitId = unitId;
            OldState = oldState ?? throw new ArgumentNullException(nameof(oldState));
            NewState = newState ?? throw new ArgumentNullException(nameof(newState));
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "oldState": value = ExprValue.OfString(OldState); return true;
                case "newState": value = ExprValue.OfString(NewState); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>技能槽位绑定关系变化时触发（缺口 4：<c>Core.Carriers.Unit.ISkillBindingHost.Bind</c>/
    /// <c>Unbind</c>，见 found.event_catalog <c>unit.skill_binding_changed</c> 行、
    /// 10_存档与持久化.md 第 2.2 节 <c>player.skill_bindings</c>）。<see cref="SkillId"/> 在
    /// <c>Unbind</c> 后为空（该槽位当前未绑定任何技能）。</summary>
    public sealed class UnitSkillBindingChangedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => CarriersEventKeys.UnitSkillBindingChanged;

        public Id UnitId { get; }

        public string Slot { get; }

        public Id? SkillId { get; }

        public UnitSkillBindingChangedEvent(Id unitId, string slot, Id? skillId)
        {
            UnitId = unitId;
            Slot = slot ?? throw new ArgumentNullException(nameof(slot));
            SkillId = skillId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "slot": value = ExprValue.OfString(Slot); return true;
                case "skillId":
                    if (SkillId.HasValue)
                    {
                        value = ExprValue.OfId(SkillId.Value);
                        return true;
                    }
                    value = default;
                    return false;
                default: value = default; return false;
            }
        }
    }
}
