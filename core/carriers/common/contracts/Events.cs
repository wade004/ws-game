using System;
using System.Collections.Generic;
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

        /// <summary>本次加入实际落地的最后一个实例 id（跨堆叠时只是"最后触碰的那一个"，见
        /// <see cref="Removals"/> 类型注释——旧字段原样保留，不破坏既有消费方）。</summary>
        public Id ItemInstanceId { get; }

        public Id ItemTemplateId { get; }

        /// <summary>本次调用实际加入的总数量（跨堆叠时是全部实例分摊数量之和，与 <see cref="Removals"/>
        /// 逐项求和一致）。</summary>
        public int Count { get; }

        /// <summary>
        /// N12 收边补齐（外部审计 68c9bed，P2）：本次 <c>AddItem</c> 调用实际落地的分摊明细——按
        /// <see cref="Core.Carriers.Item.InventoryHost.AddItem"/> 实际写入顺序列出每个被填充/新建的
        /// 实例 id 与它各自分到的数量（先填已有堆叠、再按 stack_size 新开堆叠，逐项求和等于
        /// <see cref="Count"/>）。
        /// <para>
        /// 判断记录：<see cref="ItemInstanceId"/>/<see cref="Count"/> 这对旧字段在跨堆叠（一次
        /// <c>AddItem</c> 同时填满已有堆叠又新开一个堆叠）时只能描述"最后触碰的那一个实例"+"全部
        /// 数量"，消费方如果拿着这对旧字段去调用 <c>IInventoryHost.RemoveItem(unitId,
        /// ItemInstanceId, Count)</c>（<c>core/gameplay/quest.QuestHost.HandleItemAdded</c> 的
        /// <c>ConsumeOnProgress</c> 分支正是这么做——加入后立即按相同数量整取消费掉，充当"即时上缴"
        /// 语义）会命中 <c>RemoveItem</c> 的"要么整取要么不取"语义：请求数量超过这一个实例实际持有
        /// 的数量时整体失败，返回 false，进度不推进（见外部审计 N12"stack1 一次加 2 时 consume2
        /// 进度为 0"）。本字段按实例逐条列出真实分摊，消费方应改为对每一项分别调用
        /// <c>RemoveItem(unitId, item.InstanceId, item.Count)</c>，不再依赖单一
        /// <see cref="ItemInstanceId"/> 猜测跨堆叠场景下的实际持有量。旧字段保留不删，未跨堆叠
        /// （只命中一个既有实例或只新开一个实例）时 <see cref="Removals"/> 只有一项，与旧字段等价，
        /// 不改变既有消费方在这一常见场景下的行为。
        /// </para>
        /// <para>
        /// 消费方接线：<c>core/gameplay/quest.QuestHost.HandleItemAdded</c> 侧改动不在本次写入范围
        /// （见任务分工，L4 玩法层由另一路径负责），本字段已就绪，供该处改为逐项 <c>RemoveItem</c>。
        /// </para>
        /// </summary>
        public IReadOnlyList<(Id InstanceId, int Count)> Removals { get; }

        public ItemAddedEvent(Id unitId, Id itemInstanceId, Id itemTemplateId, int count)
            : this(unitId, itemInstanceId, itemTemplateId, count, new[] { (itemInstanceId, count) })
        {
        }

        public ItemAddedEvent(
            Id unitId, Id itemInstanceId, Id itemTemplateId, int count,
            IReadOnlyList<(Id InstanceId, int Count)> removals)
        {
            UnitId = unitId;
            ItemInstanceId = itemInstanceId;
            ItemTemplateId = itemTemplateId;
            Count = count;
            Removals = removals ?? throw new ArgumentNullException(nameof(removals));
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "itemInstanceId": value = ExprValue.OfId(ItemInstanceId); return true;
                case "itemTemplateId": value = ExprValue.OfId(ItemTemplateId); return true;
                case "count": value = ExprValue.OfInt(Count); return true;
                // Removals 是列表，Expr 无列表类型（同 SkillCastSuccessEvent.Targets 判断记录），
                // 不在本方法覆盖范围内。
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

        /// <summary>
        /// CR130-05 根治（architecture/落地计划/audit-5c444f1-20260908）：本次交互若命中
        /// <c>kind=teleporter</c> 且 <c>GameObjectHost.DoTeleport</c>（经注入的、可能是调用方自定义的
        /// <c>GobjOptions.TeleportResolver</c>）判定目标在另一张地图——本模块自身无法完成跨地图的场景
        /// 切换（见该方法判断记录"由调用方（L4）驱动真正的场景切换"），把原始 <c>teleport_target_ref</c>
        /// 原样携带在这里，交给唯一权威的下游消费方（<c>GameplayAssembly</c> 的 <c>gobj.interacted</c>
        /// 监听）接手；<b>为 <c>null</c></b> 涵盖"不是 teleporter"、"resolver 判定为同图并已经在
        /// <c>DoTeleport</c> 内部原地完成 <c>SetPosition</c>"、"resolver 解析失败/未注入" 三种情形——
        /// 这三种情形传送这件事本身已经由 <c>DoTeleport</c>（唯一读取 resolver 结果的地方）终局判定
        /// 完毕，不需要、也不允许任何下游再用另一个 resolver（例如默认的
        /// <c>TeleportTargetResolver</c>）重新解析同一个 <c>teleport_target_ref</c> 一遍——重新解析
        /// 会绕开调用方注入的自定义 resolver、用默认结果覆盖已经落地的自定义结果（外部审计
        /// CR130-05：同图自定义传送结果 <c>(99,88)</c> 被内置 <c>(1,2)</c> 覆盖；resolver 显式返回
        /// <c>null</c>——判定"这次不该传送"——时也不应该退化成走内置传送）。<c>template.OnUse</c>
        /// 存在时（分发到 skill/dialog，即使该 gobj 恰好也是 <c>kind=teleporter</c>）本字段固定为
        /// <c>null</c>——<see cref="Core.Carriers.Gobj.GameObjectHost.Interact"/> 复用同一个
        /// <c>InteractResult.DispatchedRef</c> 字段表达"skill/dialog 的分发目标 id"这一无关语义，见该
        /// 类型顶部判断记录，两者不可混同。
        /// </summary>
        public Id? TeleportTargetRef { get; }

        /// <summary>
        /// CR140-03 根治（architecture/落地计划/audit-c86bfa9-20260908）：<see cref="TeleportTargetRef"/>
        /// 只携带原始未解析的 <c>teleport_target_ref</c>——本字段额外携带
        /// <c>GameObjectHost.DoTeleport</c>（经它读到的、可能是调用方自定义的
        /// <c>GobjOptions.TeleportResolver</c>）已经解析出的具体 <c>(MapId, Position)</c>，与 <see
        /// cref="TeleportTargetRef"/> 同一个"非空条件"（都只在真正跨地图时非空，见
        /// <see cref="TeleportTargetRef"/> 判断记录）。问题根因：<see cref="TeleportTargetRef"/> 修复
        /// 前是下游（<c>GameplayAssembly</c> 的 <c>gobj.interacted</c> 监听）唯一能拿到的信息，监听
        /// 只能把这个原始 ref 再喂给自己的 <c>TeleportUnit</c>——而 <c>TeleportUnit</c> 内部固定用
        /// 装配根自己的默认 <c>_teleportTargetResolver</c> 重新解析同一个 ref，等于对同一次交互做了
        /// 两次独立解析：<c>DoTeleport</c> 那次（可能命中自定义 resolver）的结果被完全丢弃，
        /// <c>TeleportUnit</c> 内部默认 resolver 的结果覆盖生效（外部审计复现：跨图自定义解析结果
        /// <c>(99,88)</c> 被内置解析的 <c>(30,40)</c> 覆盖）。现在下游改为直接落地本字段携带的既成
        /// 结果，不再对 <see cref="TeleportTargetRef"/> 做任何二次解析——<c>DoTeleport</c> 才是唯一
        /// 读取 resolver 的地方，真正做到"一次性终局判定"（同 <see cref="TeleportTargetRef"/> 判断
        /// 记录标题）。未注入自定义 <c>GobjOptions.TeleportResolver</c> 时，<c>DoTeleport</c> 读到的
        /// 就是装配根接线的默认 <c>_teleportTargetResolver.Resolve</c>（见
        /// <c>GameplayAssembly</c> 第 16 步 <c>resolvedGobjOptions.TeleportResolver ??= ...</c>），
        /// 效果与"下游内置解析一次"完全等价，不存在"没有自定义 resolver 就传送不了"的退化。
        /// </summary>
        public (Id MapId, Vec2 Position)? ResolvedTeleportTarget { get; }

        public GobjInteractedEvent(
            Id unitId, Id gobjInstanceId, Id? teleportTargetRef = null, (Id MapId, Vec2 Position)? resolvedTeleportTarget = null)
        {
            UnitId = unitId;
            GobjInstanceId = gobjInstanceId;
            TeleportTargetRef = teleportTargetRef;
            ResolvedTeleportTarget = resolvedTeleportTarget;
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
