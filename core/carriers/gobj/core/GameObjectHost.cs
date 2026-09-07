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
            var kindDispatchRef = ExecuteKindBehavior(unitId, gobj, template);

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
            var teleportTargetRef = !template.OnUse.HasValue ? kindDispatchRef : null;
            _bus.Enqueue(new GobjInteractedEvent(unitId, gobjInstanceId, teleportTargetRef));
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
        /// 时把它填进 <see cref="InteractResult.DispatchedRef"/>。</summary>
        private Id? ExecuteKindBehavior(Id unitId, GameObjectEntity gobj, GameObjectTemplate template)
        {
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
                    return DoTeleport(unitId, gobj, template.TypeData.Teleporter!.Value.TeleportTargetRef);

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

        private void OpenChest(Id unitId, Id gobjInstanceId, Id lootTableRef)
        {
            if (GetStateBool(gobjInstanceId, "open_state"))
            {
                // 已开过：不重复掉落（见 07 第 9 节测试方式"箱子首次开箱掉落入包、再开不重复"）。
                return;
            }

            SetState(gobjInstanceId, "open_state", ExprValue.OfBool(false), ExprValue.OfBool(true));
            RollLootInto(unitId, gobjInstanceId, lootTableRef);
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
        private Id? DoTeleport(Id unitId, GameObjectEntity gobj, Id teleportTargetRef)
        {
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
