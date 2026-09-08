using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// <see cref="IGameObjectHost"/> 的实现（见 07 第 3.6 节契约、9 节契约汇总表 GameObject 行）。
    /// 构造注入的六个宿主/查询接口（<see cref="IDataRegistryView"/>/<see cref="IWorldSim"/>/
    /// <see cref="IEventBus"/>/<see cref="IWorldFlags"/>/<see cref="IUnitAccess"/>/
    /// <see cref="IInventoryHost"/>/<see cref="IStatHost"/>/<see cref="ISkillHost"/>）均由游戏组装根
    /// 提供（其中 <see cref="IWorldFlags"/>/<see cref="ILootRoller"/> 是依赖倒置接口，见
    /// <c>core/carriers/common</c> README"L3 不依赖 L4，L4 通过……反向注入"）；
    /// <see cref="ILootRoller"/> 可选——未注入时 <c>chest</c>/<c>gather_node</c> 的掉落步骤记一条
    /// 诊断并跳过（不阻断交互本身，见 <see cref="GobjOptions"/> 顶部判断记录同款处理方式）。
    /// </summary>
    public sealed class GameObjectHost : IGameObjectHost
    {
        private readonly IDataRegistryView _registry;
        private readonly IWorldSim _world;
        private readonly IEventBus _bus;
        private readonly IWorldFlags _flags;
        private readonly IUnitAccess _units;
        private readonly IInventoryHost _inventory;
        private readonly IStatHost _stats;
        private readonly ISkillHost _skills;
        private readonly ILootRoller? _loot;
        private readonly GobjOptions _options;
        private readonly IGobjDiagnostics _diagnostics;

        /// <summary>CR140-01 根治：<c>chest</c> 在 <see cref="GobjLootDeliveryPolicy.Partial"/> 策略下
        /// 未能全部交付的剩余物品堆叠，按 gobj 实例 id 索引，供下次交互补发（见 <see
        /// cref="OpenChestPartial"/>/<see cref="DeliverPendingChestLoot"/>）。判断记录（第七方审核
        /// 收口修订）：本字段自身不直接依赖 <c>core/gameplay/loot</c>（L4）的
        /// <c>DroppedLootEntity</c>/存档持久化概念，进程内存态本身随进程重启即丢失；但存读档场景
        /// （满包 Partial 开箱 -> Save -> 新宿主 Load -> 腾出空间 -> 再交互）必须恰好补发一次剩余部分
        /// ，不能因为这份记账只活在内存里而丢失——已补齐可选存档段 <see
        /// cref="PendingChestLootSnapshot"/>/<see cref="RestorePendingChestLoot"/>（供
        /// <c>GobjPendingLootPersistable</c> 读写），是否注册该段由装配层决定（同 L4
        /// <c>DroppedLootPersistable</c> 的接线方式，本模块不强制依赖它）。</summary>
        private readonly Dictionary<Id, List<ItemStack>> _pendingChestLoot = new Dictionary<Id, List<ItemStack>>();

        public GameObjectHost(
            IDataRegistryView registry,
            IWorldSim world,
            IEventBus bus,
            IWorldFlags flags,
            IUnitAccess units,
            IInventoryHost inventory,
            IStatHost stats,
            ISkillHost skills,
            ILootRoller? loot = null,
            GobjOptions? options = null,
            IGobjDiagnostics? diagnostics = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
            _loot = loot;
            _options = options ?? new GobjOptions();
            _diagnostics = diagnostics ?? new InMemoryGobjDiagnostics();
        }

        // -----------------------------------------------------------------
        // 存档段支持（见 <see cref="_pendingChestLoot"/> 判断记录的收口修订：原判断记录"不落地为
        // 存档段"仅覆盖审计探针实测到的"当次运行内重试"这一不变式，未覆盖"存读档"路径——满包
        // Partial 开箱后存档、读档到新宿主，_pendingChestLoot 是纯进程内字段会丢失，腾出空间后再
        // 交互不会补发。以下两个方法供 <c>Core.Carriers.Gobj.GobjPendingLootPersistable</c>（可选
        // 存档段，装配层决定是否注册，同 <c>Core.Gameplay.Loot.DroppedLootPersistable</c> 的接线
        // 方式）读写这份台账，不改变 <see cref="OpenChestPartial"/>/<see
        // cref="DeliverPendingChestLoot"/> 的既有语义。
        // -----------------------------------------------------------------

        /// <summary>当前 <see cref="_pendingChestLoot"/> 的只读快照，供存档段 <c>Save()</c> 使用。</summary>
        public IReadOnlyDictionary<Id, IReadOnlyList<ItemStack>> PendingChestLootSnapshot()
        {
            var result = new Dictionary<Id, IReadOnlyList<ItemStack>>(_pendingChestLoot.Count);
            foreach (var kv in _pendingChestLoot)
            {
                result[kv.Key] = new List<ItemStack>(kv.Value);
            }

            return result;
        }

        /// <summary>用存档段 <c>Load()</c> 解析出的内容整体替换 <see cref="_pendingChestLoot"/>
        /// （读档 = 归零重建，惯例同 <c>DroppedLootPersistable.Load</c> 判断记录）。旧存档没有这段
        /// （<c>data is JsonNull</c>）时调用方传入空字典即可，视为"无待补发余量"，不视为错误。</summary>
        public void RestorePendingChestLoot(IReadOnlyDictionary<Id, IReadOnlyList<ItemStack>> snapshot)
        {
            _pendingChestLoot.Clear();
            if (snapshot == null)
            {
                return;
            }

            foreach (var kv in snapshot)
            {
                if (kv.Value == null || kv.Value.Count == 0)
                {
                    continue;
                }

                _pendingChestLoot[kv.Key] = new List<ItemStack>(kv.Value);
            }
        }

        // -----------------------------------------------------------------
        // IGameObjectHost
        // -----------------------------------------------------------------

        public InteractResult Interact(Id unitId, Id gobjInstanceId)
        {
            if (!(_world.GetEntity(gobjInstanceId) is GameObjectEntity gobj) || !_units.Exists(unitId))
            {
                return new InteractResult(false, InteractOutcome.Unknown);
            }

            var distance = Vec2.Distance(_units.GetPosition(unitId), gobj.Position);
            if (distance > _options.InteractRange)
            {
                return new InteractResult(false, InteractOutcome.Unknown);
            }

            if (gobj.LockId.HasValue && !GetStateBool(gobjInstanceId, "unlocked") && !TryUnlock(unitId, gobjInstanceId))
            {
                return new InteractResult(false, InteractOutcome.Locked);
            }

            var template = RequireTemplate(gobj.TemplateId!.Value);
            var kindDispatchRef = ExecuteKindBehavior(unitId, gobj, template, out var resolvedTeleportTarget);

            InteractResult result;
            if (template.OnUse.HasValue)
            {
                result = DispatchOnUse(unitId, gobjInstanceId, template.OnUse.Value);
            }
            else if (kindDispatchRef.HasValue)
            {
                // 跨地图传送等"内置行为无法在本模块内完成，需要交给 L4"的情形：既无 on_use 可分发，
                // 也没有 InteractOutcome 专属取值可用（见 <c>GameObjectHost</c> 顶部对
                // InteractOutcome 未新增取值的判断记录），复用 DispatchedRef 承载留给上层的引用，
                // Outcome 仍为 NoAction。
                result = new InteractResult(true, InteractOutcome.NoAction, kindDispatchRef);
            }
            else
            {
                result = new InteractResult(true, InteractOutcome.NoAction);
            }

            // CR130-05 根治：kindDispatchRef 只在"没有 on_use 可分发"（上面 else if 分支）时才是
            // GobjInteractedEvent.TeleportTargetRef 判断记录所说的"teleporter 跨地图目标"语义——
            // 有 on_use 时 result.DispatchedRef 是完全不同的 skill/dialog 分发目标，绝不能当传送目标
            // 转发给下游监听（那会把一次技能/对话交互误当传送处理）。这里独立算一遍同样的条件，不
            // 直接复用 result.DispatchedRef，避免两种语义在事件层面被混同。
            //
            // CR140-03 根治（architecture/落地计划/audit-c86bfa9-20260908）：同一条件下把
            // resolvedTeleportTarget（DoTeleport 已经用可能是自定义的 TeleportResolver 解析出的
            // (MapId, Position)）一并携带进事件——见 GobjInteractedEvent.ResolvedTeleportTarget
            // 判断记录，下游不再需要（也不允许）自己重新解析同一个 teleportTargetRef。
            var teleportTargetRef = !template.OnUse.HasValue ? kindDispatchRef : null;
            var resolvedForEvent = !template.OnUse.HasValue ? resolvedTeleportTarget : null;
            _bus.Enqueue(new GobjInteractedEvent(unitId, gobjInstanceId, teleportTargetRef, resolvedForEvent));
            return result;
        }

        public bool TryUnlock(Id unitId, Id gobjInstanceId)
        {
            if (!(_world.GetEntity(gobjInstanceId) is GameObjectEntity gobj))
            {
                return false;
            }

            if (!gobj.LockId.HasValue)
            {
                return true;
            }

            if (GetStateBool(gobjInstanceId, "unlocked"))
            {
                return true;
            }

            var lockRecord = _registry.Get(GobjSchemas.Lock.Name, gobj.LockId.Value);
            if (lockRecord == null)
            {
                _diagnostics.Warn($"gobj \"{gobjInstanceId}\" 的 lock_id \"{gobj.LockId.Value}\" 在 gobj.lock 中不存在");
                return false;
            }

            var lockDef = LockDef.FromRecord(lockRecord);
            var ok = CheckRequirement(unitId, lockDef);

            if (ok)
            {
                SetState(gobjInstanceId, "unlocked", ExprValue.OfBool(false), ExprValue.OfBool(true));
            }

            return ok;
        }

        // -----------------------------------------------------------------
        // 补充能力（07 第 3.1 节 trap/spell_focus 两行要求的额外查询/触发入口，不在
        // IGameObjectHost 契约之内——07 第 9 节契约汇总表只登记 interact/tryUnlock 两个方法）
        // -----------------------------------------------------------------

        /// <summary><c>trap</c> 不经 <see cref="Interact"/>（见 07 第 3.1 节该行"踩踏/触碰触发效果"，
        /// 触发来源是 L4 区域触发，不是玩家主动交互）：由 L4 区域触发在检测到 <paramref name="unitId"/>
        /// 进入陷阱形状范围时调用本方法，按 <c>type_data.skill_id</c> 释放技能。施法者取
        /// <see cref="GobjOptions.TrapCasterId"/>，缺省时用 <paramref name="unitId"/> 自身（见
        /// <see cref="GobjOptions.TrapCasterId"/> 判断记录）。</summary>
        public CastResult TriggerTrap(Id gobjInstanceId, Id unitId)
        {
            if (!(_world.GetEntity(gobjInstanceId) is GameObjectEntity gobj))
            {
                return CastResult.Fail(CastFailureReason.NoValidTarget);
            }

            var template = RequireTemplate(gobj.TemplateId!.Value);
            if (template.Kind != GobjKind.Trap || !template.TypeData.Trap.HasValue)
            {
                _diagnostics.Warn($"gobj \"{gobjInstanceId}\" 不是 trap 类型，TriggerTrap 被忽略");
                return CastResult.Fail(CastFailureReason.NoValidTarget);
            }

            var casterId = _options.TrapCasterId ?? unitId;
            return _skills.CastSkill(casterId, template.TypeData.Trap.Value.SkillId, new[] { unitId });
        }

        /// <summary><c>spell_focus</c> 类型的物件不通过 <see cref="Interact"/> 产生副作用（见 07 第
        /// 3.1 节该行"施放特定类别技能的焦点"），而是被技能施法条件查询——<paramref name="tag"/>
        /// 匹配 <c>type_data.required_skill_tag</c>、且与 <paramref name="position"/> 的距离不超过
        /// <paramref name="radius"/> 的 <c>spell_focus</c> 物件是否存在。</summary>
        public bool HasSpellFocus(Vec2 position, Id tag, double radius)
        {
            var candidates = _world.QueryEntities(new EntityFilter(kind: EntityKinds.Gobj));
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!(candidates[i] is GameObjectEntity gobj) || !gobj.TemplateId.HasValue)
                {
                    continue;
                }

                var template = TryGetTemplate(gobj.TemplateId.Value);
                if (template == null || template.Kind != GobjKind.SpellFocus || !template.TypeData.SpellFocus.HasValue)
                {
                    continue;
                }

                if (!template.TypeData.SpellFocus.Value.RequiredSkillTag.Equals(tag))
                {
                    continue;
                }

                if (Vec2.Distance(position, gobj.Position) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>读取某个游戏对象实例的某个状态字段当前值（见 07 第 3.4 节，key 见
        /// <see cref="GobjStateKeys.For"/>）；未设置过返回 null。</summary>
        public ExprValue? GetState(Id gobjInstanceId, string field) => _flags.Get(GobjStateKeys.For(gobjInstanceId, field));

        // -----------------------------------------------------------------
        // 按 kind 的内置交互行为（见 07 第 3.1 节表格 + 任务拍板的具体落地方式）
        // -----------------------------------------------------------------

        /// <summary>返回值非空时表示"本次内置行为需要留一个引用给上层处理"（目前只有 <c>teleporter</c>
        /// 异图传送这一种情形，见该分支注释），供 <see cref="Interact"/> 在没有 <c>on_use</c> 可分发
        /// 时把它填进 <see cref="InteractResult.DispatchedRef"/>。<paramref
        /// name="resolvedTeleportTarget"/>（CR140-03 根治）在同一种情形下额外带出 <see
        /// cref="DoTeleport"/> 已经解析好的 <c>(MapId, Position)</c>，其余全部分支恒为 <c>null</c>，
        /// 供 <see cref="Interact"/> 原样转交 <see cref="GobjInteractedEvent.ResolvedTeleportTarget"/>，
        /// 不需要下游再重新解析一遍同一个 ref。</summary>
        private Id? ExecuteKindBehavior(
            Id unitId, GameObjectEntity gobj, GameObjectTemplate template, out (Id MapId, Vec2 Position)? resolvedTeleportTarget)
        {
            resolvedTeleportTarget = null;

            switch (template.Kind)
            {
                case GobjKind.Door:
                    ToggleOpenState(gobj.EntityId);
                    return null;

                case GobjKind.Chest:
                    OpenChest(unitId, gobj.EntityId, template.TypeData.Chest!.Value.LootTableRef);
                    return null;

                case GobjKind.GatherNode:
                    GatherNode(unitId, gobj.EntityId, template.TypeData.GatherNode!.Value);
                    return null;

                case GobjKind.Teleporter:
                    return DoTeleport(
                        unitId, gobj, template.TypeData.Teleporter!.Value.TeleportTargetRef, out resolvedTeleportTarget);

                case GobjKind.SavePoint:
                    if (_options.SaveRequester != null)
                    {
                        _options.SaveRequester(unitId);
                    }
                    else
                    {
                        _diagnostics.Warn($"gobj \"{gobj.EntityId}\" 是 save_point 但未注入 GobjOptions.SaveRequester");
                    }

                    return null;

                case GobjKind.Lever:
                    ToggleLever(template.TypeData.Lever!.Value.LinkedObjectIds);
                    return null;

                case GobjKind.QuestObject:
                    var questRef = template.TypeData.QuestObject!.Value.QuestActionRef;
                    if (_options.QuestActionDispatcher != null)
                    {
                        _options.QuestActionDispatcher(unitId, questRef);
                    }
                    else
                    {
                        _diagnostics.Warn($"gobj \"{gobj.EntityId}\" 是 quest_object 但未注入 GobjOptions.QuestActionDispatcher");
                    }

                    return null;

                case GobjKind.Sign:
                    // 纯文本告示牌，无交互副作用（见 07 第 3.1 节该行）。
                    return null;

                case GobjKind.SpellFocus:
                    // 见 HasSpellFocus：spell_focus 不通过 Interact 产生副作用，只被动查询。
                    return null;

                case GobjKind.Trap:
                    _diagnostics.Warn($"gobj \"{gobj.EntityId}\" 是 trap，应经 TriggerTrap 而非 Interact 触发");
                    return null;

                default:
                    throw new ArgumentOutOfRangeException(nameof(template), template.Kind, "未知 GobjKind");
            }
        }

        private void ToggleOpenState(Id gobjInstanceId)
        {
            var current = GetStateBool(gobjInstanceId, "open_state");
            SetState(gobjInstanceId, "open_state", ExprValue.OfBool(current), ExprValue.OfBool(!current));
        }

        /// <summary>
        /// CR140-01 根治（architecture/落地计划/audit-c86bfa9-20260908，P1）：修复前先无条件把
        /// <c>open_state</c> 置为已开，再对每个抽出的物品堆叠直接调用 <see
        /// cref="IInventoryHost.AddItem"/>，不检查是否真的放得下、不回滚——满包时奖励可能部分/全部
        /// 丢失，且因为 <c>open_state</c> 已经永久标记为已开，<see cref="Interact"/> 再交互一次会直接
        /// 因"已开过"短路返回，玩家再也拿不到丢失的那部分（见 <c>OpenChest</c> 修复前实现）。
        /// <para>
        /// 根治采用与 <see cref="Core.Gameplay.Loot.LootHost.PickUp"/> 相同的 batch/Partial 协议（经
        /// <see cref="IBatchableInventoryHost"/>/<see cref="IInventoryTransaction"/>，见 <see
        /// cref="GobjLootDeliveryPolicy"/> 类型注释）：只有整批交付成功才提交 <c>open_state=true</c>；
        /// <see cref="GobjLootDeliveryPolicy.Reject"/> 下但凡有一堆放不下就整体回滚、不标记，允许下次
        /// 交互重新完整 roll 一遍重试；<see cref="GobjLootDeliveryPolicy.Partial"/> 下保留已交付部分，
        /// 未交付部分记入 <see cref="_pendingChestLoot"/> 供下次交互只补发剩余（不重新 roll，避免在
        /// 已交付部分之上又叠加一份全新掉落），同时仍然标记 <c>open_state=true</c>（防止两种策略混淆
        /// 出"未标记但已经拿到过一部分"的状态）。
        /// </para>
        /// </summary>
        private void OpenChest(Id unitId, Id gobjInstanceId, Id lootTableRef)
        {
            if (GetStateBool(gobjInstanceId, "open_state"))
            {
                // 已开过：Partial 策略下可能仍有未交付完的剩余奖励，尝试补发（不重新 roll）；Reject
                // 策略或已经补发完的 Partial 走到这里是 no-op（见 07 第 9 节测试方式"箱子首次开箱
                // 掉落入包、再开不重复"，本方法把该不变式扩展为"……或已补发完剩余部分"）。
                DeliverPendingChestLoot(unitId, gobjInstanceId);
                return;
            }

            if (_loot == null)
            {
                _diagnostics.Warn($"gobj \"{gobjInstanceId}\" 需要掉落但未注入 ILootRoller");
                // 没有掉落表可抽，没有奖励会丢失：仍按原有行为标记已开，避免每次交互都重复报同一条
                // 诊断（判断记录同 GatherNode 分支——这不属于 CR140-01"发奖前被永久标记"的缺口，
                // 因为压根没有发生过任何发奖尝试）。
                SetState(gobjInstanceId, "open_state", ExprValue.OfBool(false), ExprValue.OfBool(true));
                return;
            }

            var stacks = _loot.Roll(lootTableRef, gobjInstanceId, unitId);
            if (_options.ChestLootPolicy == GobjLootDeliveryPolicy.Reject)
            {
                OpenChestReject(unitId, gobjInstanceId, stacks);
            }
            else
            {
                OpenChestPartial(unitId, gobjInstanceId, stacks);
            }
        }

        /// <summary>见 <see cref="GobjLootDeliveryPolicy.Reject"/>：整批交付成功才提交
        /// <c>open_state=true</c>，否则整体回滚（同 <see
        /// cref="Core.Gameplay.Loot.LootHost.PickUpReject"/> 判断记录）。</summary>
        private void OpenChestReject(Id unitId, Id gobjInstanceId, IReadOnlyList<ItemStack> stacks)
        {
            if (stacks.Count == 0)
            {
                SetState(gobjInstanceId, "open_state", ExprValue.OfBool(false), ExprValue.OfBool(true));
                return;
            }

            var addedPerStack = new List<int>(stacks.Count);
            var fullySucceeded = true;

            var transaction = _inventory is IBatchableInventoryHost batchable ? batchable.BeginBatch() : null;
            using (transaction)
            {
                foreach (var stack in stacks)
                {
                    _inventory.TryAddItem(unitId, stack.TemplateId, stack.Count, out var actualCount);
                    addedPerStack.Add(actualCount);
                    if (actualCount < stack.Count)
                    {
                        fullySucceeded = false;
                    }
                }

                if (!fullySucceeded)
                {
                    if (transaction == null)
                    {
                        // 宿主不支持事务：按实际落地量（不是请求量）逐项回滚，惯例同
                        // LootHost.PickUpReject/RewardDispatcher.GrantItems。
                        for (var i = 0; i < stacks.Count; i++)
                        {
                            if (addedPerStack[i] > 0)
                            {
                                RollbackAdd(unitId, stacks[i].TemplateId, addedPerStack[i]);
                            }
                        }
                    }
                    // 宿主支持事务时，using 块结束触发 Dispose（未 Commit）即整体撤销状态与缓存事件，
                    // 不需要在这里手动回滚。

                    // 不标记 open_state：这次尝试没有任何奖励真正留在玩家背包里，下次交互允许重新
                    // 完整 roll 一遍并重试（CR140-01 根治"Reject 时不标记、可重试"）。
                    return;
                }

                transaction?.Commit();
            }

            SetState(gobjInstanceId, "open_state", ExprValue.OfBool(false), ExprValue.OfBool(true));
        }

        /// <summary>见 <see cref="GobjLootDeliveryPolicy.Partial"/>：能拿多少拿多少，未交付部分记入
        /// <see cref="_pendingChestLoot"/>（同 <see
        /// cref="Core.Gameplay.Loot.LootHost.PickUpPartial"/> 判断记录，不需要事务——每次
        /// <see cref="IInventoryHost.TryAddItem"/> 调用本身已经是"这一堆放多少算多少"的原子操作，
        /// 从不需要撤销已经成功落地的部分）。</summary>
        private void OpenChestPartial(Id unitId, Id gobjInstanceId, IReadOnlyList<ItemStack> stacks)
        {
            var remaining = new List<ItemStack>(stacks.Count);
            var anyDelivered = stacks.Count == 0;

            foreach (var stack in stacks)
            {
                _inventory.TryAddItem(unitId, stack.TemplateId, stack.Count, out var actualCount);
                if (actualCount > 0)
                {
                    anyDelivered = true;
                }

                var leftover = stack.Count - actualCount;
                if (leftover > 0)
                {
                    remaining.Add(new ItemStack(stack.TemplateId, leftover));
                }
            }

            if (!anyDelivered)
            {
                // 一件都没能交付（如背包已经完全没有任何空间）：没有产生任何背包变化，等价于 Reject
                // 场景的"完全没拿到"，不标记，允许下次交互重新完整 roll 一遍。
                return;
            }

            // 至少交付了一部分：必须标记 open_state，否则下次交互会重新 roll 一遍掉落表，在已经拿到
            // 的部分之上又叠加一份全新的（CR140-01 根治"整批交付成功才提交 open_state=true"是对
            // "完全没交付"的另一面——这里"部分/全部交付"都必须标记，用同一个 open_state 字段区分
            // "还没开过"与"开过（可能仍有 pending 待补发）"）。
            SetState(gobjInstanceId, "open_state", ExprValue.OfBool(false), ExprValue.OfBool(true));

            if (remaining.Count > 0)
            {
                _pendingChestLoot[gobjInstanceId] = remaining;
            }
        }

        /// <summary>已开过的箱子再次交互时，尝试补发 <see cref="_pendingChestLoot"/> 里记录的剩余部分
        /// （不重新 <see cref="ILootRoller.Roll"/>——见 <see cref="OpenChestPartial"/> 判断记录）。</summary>
        private void DeliverPendingChestLoot(Id unitId, Id gobjInstanceId)
        {
            if (!_pendingChestLoot.TryGetValue(gobjInstanceId, out var pending) || pending.Count == 0)
            {
                return;
            }

            var remaining = new List<ItemStack>(pending.Count);
            foreach (var stack in pending)
            {
                _inventory.TryAddItem(unitId, stack.TemplateId, stack.Count, out var actualCount);
                var leftover = stack.Count - actualCount;
                if (leftover > 0)
                {
                    remaining.Add(new ItemStack(stack.TemplateId, leftover));
                }
            }

            if (remaining.Count == 0)
            {
                _pendingChestLoot.Remove(gobjInstanceId);
            }
            else
            {
                _pendingChestLoot[gobjInstanceId] = remaining;
            }
        }

        /// <summary>把之前已经成功 <see cref="IInventoryHost.AddItem"/> 的 <paramref name="amount"/>
        /// 个 <paramref name="templateId"/> 物品移除（宿主不支持 <see cref="IBatchableInventoryHost"/>
        /// 时 <see cref="OpenChestReject"/> 的手动回滚路径使用），惯例同 <see
        /// cref="Core.Gameplay.Loot.LootHost.RollbackAdd"/>。</summary>
        private void RollbackAdd(Id unitId, Id templateId, int amount)
        {
            var remaining = amount;
            foreach (var instance in _inventory.ListItems(unitId))
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (!instance.TemplateId.Equals(templateId))
                {
                    continue;
                }

                var take = Math.Min(remaining, instance.Count);
                _inventory.RemoveItem(unitId, instance.InstanceId, take);
                remaining -= take;
            }
        }

        private void GatherNode(Id unitId, Id gobjInstanceId, GatherNodeTypeData data)
        {
            var key = GobjStateKeys.For(gobjInstanceId, "used_at");
            var now = _options.SimTime();
            var previous = _flags.Get(key);

            if (previous.HasValue && previous.Value.IsNumeric)
            {
                var usedAt = previous.Value.ToDouble();
                if (now - usedAt < data.RespawnAfterUse)
                {
                    // 尚未到刷新时间，不可再采（见 GobjOptions.SimTime 判断记录"读取时判断"）。
                    return;
                }
            }

            SetState(gobjInstanceId, "used_at", previous ?? ExprValue.OfNumber(0), ExprValue.OfNumber(now));
            RollLootInto(unitId, gobjInstanceId, data.LootTableRef);
        }

        private void RollLootInto(Id unitId, Id gobjInstanceId, Id lootTableRef)
        {
            if (_loot == null)
            {
                _diagnostics.Warn($"gobj \"{gobjInstanceId}\" 需要掉落但未注入 ILootRoller");
                return;
            }

            var stacks = _loot.Roll(lootTableRef, gobjInstanceId, unitId);
            for (var i = 0; i < stacks.Count; i++)
            {
                _inventory.AddItem(unitId, stacks[i].TemplateId, stacks[i].Count);
            }
        }

        /// <summary>同图直接调用 <see cref="IUnitAccess.SetPosition"/>；异图（
        /// <see cref="TeleportResolverDelegate"/> 解析出的 <c>MapId</c> 与 <paramref name="gobj"/>
        /// 所在地图不同）不在本模块内完成实际切图——<see cref="IUnitAccess"/> 没有"改变单位所属地图"
        /// 的方法（场景/地图切换是 03 第 6 节场景路由的职责，属 L4/更上层），本模块只把解析出的
        /// <paramref name="teleportTargetRef"/> 原样返回，交给 <see cref="Interact"/> 填进
        /// <see cref="InteractResult.DispatchedRef"/>，由调用方（L4）驱动真正的场景切换。</summary>
        private Id? DoTeleport(
            Id unitId, GameObjectEntity gobj, Id teleportTargetRef, out (Id MapId, Vec2 Position)? resolvedTeleportTarget)
        {
            resolvedTeleportTarget = null;

            if (_options.TeleportResolver == null)
            {
                _diagnostics.Warn($"gobj \"{gobj.EntityId}\" 是 teleporter 但未注入 GobjOptions.TeleportResolver");
                return null;
            }

            var resolved = _options.TeleportResolver(teleportTargetRef);
            if (!resolved.HasValue)
            {
                _diagnostics.Warn($"gobj \"{gobj.EntityId}\" 的 teleport_target_ref \"{teleportTargetRef}\" 无法解析");
                return null;
            }

            if (resolved.Value.MapId.Equals(gobj.MapId))
            {
                _units.SetPosition(unitId, resolved.Value.Position);
                return null;
            }

            // CR140-03 根治：本方法（唯一读取 GobjOptions.TeleportResolver 的地方）已经得到终局的
            // (MapId, Position)，随 teleportTargetRef 一并带出，供 Interact 原样转交
            // GobjInteractedEvent.ResolvedTeleportTarget——下游不再需要（也不允许）用另一个 resolver
            // 重新解析同一个 ref（见该事件字段判断记录）。
            resolvedTeleportTarget = resolved;
            return teleportTargetRef;
        }

        private void ToggleLever(IReadOnlyList<Id> linkedObjectIds)
        {
            var sorted = new List<Id>(linkedObjectIds);
            sorted.Sort();
            for (var i = 0; i < sorted.Count; i++)
            {
                ToggleOpenState(sorted[i]);
            }
        }

        private InteractResult DispatchOnUse(Id unitId, Id gobjInstanceId, OnUseRef onUse)
        {
            if (onUse.Kind == OnUseKind.Skill)
            {
                _skills.CastSkill(unitId, onUse.Ref, new[] { gobjInstanceId });
                return new InteractResult(true, InteractOutcome.Skill, onUse.Ref);
            }

            if (_options.DialogOpener == null)
            {
                _diagnostics.Warn($"gobj \"{gobjInstanceId}\" 的 on_use 指向对话但未注入 GobjOptions.DialogOpener");
                return new InteractResult(false, InteractOutcome.NoAction);
            }

            _options.DialogOpener(unitId, onUse.Ref);
            return new InteractResult(true, InteractOutcome.Dialog, onUse.Ref);
        }

        // -----------------------------------------------------------------
        // 锁判定（见 07 第 3.2 节三种 requirement）
        // -----------------------------------------------------------------

        private bool CheckRequirement(Id unitId, LockDef lockDef)
        {
            switch (lockDef.Requirement.Kind)
            {
                case LockRequirementKind.ItemKey:
                    var itemId = lockDef.Requirement.ItemId!.Value;
                    var hasKey = _inventory.CountOf(unitId, itemId) > 0;
                    if (hasKey && lockDef.ConsumeKey)
                    {
                        ConsumeKeyItem(unitId, itemId);
                    }

                    return hasKey;

                case LockRequirementKind.WorldFlag:
                    var current = _flags.Get(lockDef.Requirement.FlagKey!.Value);
                    return current.HasValue && current.Value.Equals(lockDef.Requirement.Expected!.Value);

                case LockRequirementKind.SkillCheck:
                    // 拍板：skill_tag 形如 "stat.lockpicking" 直接作为 IStatHost 的属性 id 使用，
                    // 单机简化为读一个数值属性比较（见 07 第 3.2 节原文"技能相关数值达到门槛……单机
                    // 简化为读一个数值属性比较"），不另外拼接/改写这个 id。
                    var value = _stats.GetStat(unitId, lockDef.Requirement.SkillTag!.Value);
                    return value >= lockDef.Requirement.MinValue;

                default:
                    return false;
            }
        }

        private void ConsumeKeyItem(Id unitId, Id itemTemplateId)
        {
            var items = _inventory.ListItems(unitId);
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].TemplateId.Equals(itemTemplateId))
                {
                    _inventory.RemoveItem(unitId, items[i].InstanceId, 1);
                    return;
                }
            }

            _diagnostics.Warn($"单位 \"{unitId}\" 的 CountOf(\"{itemTemplateId}\") > 0 但 ListItems 中找不到对应实例，consume_key 未生效");
        }

        // -----------------------------------------------------------------
        // WorldState 读写辅助（见 07 第 3.4 节）
        // -----------------------------------------------------------------

        private bool GetStateBool(Id gobjInstanceId, string field)
        {
            var value = _flags.Get(GobjStateKeys.For(gobjInstanceId, field));
            return value.HasValue && value.Value.Kind == ExprValueKind.Bool && value.Value.AsBool;
        }

        /// <summary>写入一个状态字段并发出 <c>gobj.state_changed</c>（见 07 第 3.4 节"落地为一条
        /// WorldState 标志"、9 节契约汇总表"事件：gobj.state_changed（对应一次 WorldState.set）"，
        /// 二者一一对应：本模块内一切状态写入都必须经本方法，不直接调用 <see cref="IWorldFlags.Set"/>，
        /// 保证"一次 WorldState.set 恰好一次 gobj.state_changed"这条不变式。</summary>
        private void SetState(Id gobjInstanceId, string field, ExprValue oldValue, ExprValue newValue)
        {
            var key = GobjStateKeys.For(gobjInstanceId, field);
            _flags.Set(key, newValue, _options.WriterId);
            _bus.Enqueue(new GobjStateChangedEvent(gobjInstanceId, field, oldValue, newValue));
        }

        // -----------------------------------------------------------------
        // 模板查询（见 GameObjectFactory 顶部判断记录：不引入缓存层）
        // -----------------------------------------------------------------

        private GameObjectTemplate RequireTemplate(Id templateId)
        {
            var template = TryGetTemplate(templateId);
            if (template == null)
            {
                throw new InvalidOperationException($"gobj.template \"{templateId}\" 不存在");
            }

            return template;
        }

        private GameObjectTemplate? TryGetTemplate(Id templateId)
        {
            var record = _registry.Get(GobjSchemas.Template.Name, templateId);
            return record == null ? null : GameObjectTemplate.FromRecord(record);
        }
    }
}
