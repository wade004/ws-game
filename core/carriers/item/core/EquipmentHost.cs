using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="IEquipmentHost"/> 默认实现（见 07 第 1.3/1.4 节）。
    /// <para>
    /// 判断记录 1——换装（目标槽已占用）的操作顺序：先把要装备的新实例从背包摘走（腾出一个格子），
    /// 再把旧实例的属性/技能/光环/套装加成撤销并放回背包。07 原文只说"槽已占用 → 先卸下……回背包"，
    /// 没有规定与"从背包移出新实例"的相对先后；本实现选择"先摘新、后还旧"而非按字面"先还旧、后摘
    /// 新"，是因为后者在 <see cref="InventoryOptions.MaxSlots"/> 紧张时会产生一个纯属实现顺序造成的
    /// 伪"背包已满"失败（旧实例还回去的那一刻背包个数暂时 +1，而新实例其实马上就要被摘走），
    /// 与最终状态（两次操作结束后格子数不变）不符；调整顺序后新旧交换在容量层面必然成功，不产生
    /// 该实现细节导致的意外失败分支。<see cref="EquipFailureReason.SlotOccupied"/> 因此不会被本实现
    /// 使用——07 对应的 <c>EquipResult</c> 契约注释本就允许"失败"或"按策略自动置换"两种实现选择
    /// （见 <c>Core.Carriers.Common.EquipFailureReason.SlotOccupied</c> 顶部注释），本模块选择后者。
    /// </para>
    /// <para>
    /// 判断记录 2——<see cref="Unequip"/> 在背包已满时的处理：任务书"背包满按 FullPolicy，拒绝时
    /// Unequip 返回 null 并记诊断"。本实现在改动任何状态（属性/技能/光环/套装加成）之前先探测背包
    /// 是否至少还有一个空位（<see cref="InventoryHost.HasRoomForOne"/>），没有空位则整个操作直接中止
    /// 返回 null（物品继续保持装备状态），不会出现"联动已撤销但物品从世界中消失"的数据丢失分支。
    /// <see cref="InventoryOptions.FullPolicy"/> 的 Reject/Partial 两个取值在这里退化为同一种行为——
    /// 归还的是恰好一个不可拆分的实例（07 校验要求装备类 <c>stack_size == 1</c>），不存在"部分归还"
    /// 的中间状态，Partial 策略在这种场景下没有比 Reject 更宽松的空间可言，见
    /// <see cref="InventoryHost.HasRoomForOne"/> 顶部注释。
    /// </para>
    /// <para>
    /// 判断记录 3——套装加成的来源标记：<see cref="IEffectSink.ApplyAura"/> 的 <c>sourceId</c> 参数
    /// 传套装本身的 id（<c>item.set.&lt;name&gt;</c>），不是任何一件具体物品实例 id——套装加成不属于
    /// 单件物品的联动效果，其生效/失效由"当前凑齐几件"这一集合状态决定，用套装 id 作为来源标记更
    /// 准确地表达"这条光环来自套装门槛，不来自某一件具体装备"，并且天然避免"卸下凑数的其中一件却
    /// 错误撤销了套装光环的 sourceId 归属"这类混淆（套装光环由 <see cref="RecomputeSetBonuses"/> 按
    /// 阈值显式 ApplyAura/RemoveAura 管理，不经 <see cref="IStatHost.RemoveModifiersBySource"/> 一类
    /// "按来源整体撤销"机制，因为光环没有等价的按来源整体撤销接口，见 <see cref="IEffectSink"/>）。
    /// </para>
    /// <para>
    /// 判断记录 4——<c>ISkillHost</c> 契约缺口：06 <c>ISkillHost</c> 没有"学习/遗忘技能"方法（技能书
    /// 相关能力目前只存在于 <c>core/rules/skill</c> 内部实现，未提升到共享契约），本模块改动范围
    /// 不允许修改 <c>core/rules/*</c>；构造参数改用 <see cref="SkillGranter"/> 具名委托绕过，由更
    /// 上层组装代码把真实的 <c>SkillHost.LearnSkill</c>/<c>ForgetSkill</c>（阶段 3 整理补齐）适配
    /// 成这个签名后注入（见该
    /// 委托类型顶部注释、任务书"契约缺口用模块内委托绕过并汇报"）。
    /// </para>
    /// </summary>
    public sealed class EquipmentHost : IEquipmentHost, IWeaponDamageQuery
    {
        private readonly IEventBus _bus;
        private readonly InventoryHost _inventory;
        private readonly IStatHost _statHost;
        private readonly IEffectSink _effectSink;
        private readonly SkillGranter _skillGranter;
        private readonly IUnitAccess _unitAccess;
        private readonly ItemOptions _options;
        private readonly IItemDiagnostics _diagnostics;

        private readonly Dictionary<Id, DataRecord> _slotDefinitions = new Dictionary<Id, DataRecord>();
        private readonly Dictionary<Id, DataRecord> _sets = new Dictionary<Id, DataRecord>();

        private readonly Dictionary<Id, Dictionary<Id, ItemInstance>> _equipped =
            new Dictionary<Id, Dictionary<Id, ItemInstance>>();

        /// <summary>RC-05 收边补齐：value 从 <c>List&lt;AuraInstanceRef&gt;</c> 改为
        /// <c>List&lt;(Id AuraDefId, AuraInstanceRef Ref)&gt;</c>——需要保留每条授予记录对应的
        /// <c>aura_def</c> id，供诊断/调试与 <see cref="RevertGrants"/> 逐条核对。</summary>
        private readonly Dictionary<(Id UnitId, Id InstanceId), List<(Id AuraDefId, AuraInstanceRef Ref)>> _grantedAuras =
            new Dictionary<(Id, Id), List<(Id, AuraInstanceRef)>>();

        /// <summary>
        /// N09 收边补齐（外部审计 68c9bed，P2；取代原 RC-05 按 <c>(unitId, auraDefId)</c> 计数的
        /// <c>_auraGrantRefCount</c>）：改按 <b>(unitId, 实际光环实例句柄 AuraInstanceId)</b> 计数——
        /// 原实现按 <c>aura_def</c> id 聚合计数，隐含假设"同一 <c>aura_def</c> 被多件装备授予时，
        /// 它们拿到的 <see cref="AuraInstanceRef"/> 一定指向同一个共享实例"，这只在
        /// <c>SkillOptions.AllowMultiSourceTiming == false</c>（默认；<c>AuraHost.ApplyAura</c> 对
        /// 同一 <c>(target, aura_def)</c> 的重复施加合并到同一槽位）时成立；原实现注释误写为
        /// "<c>ItemOptions.AllowMultiSourceTiming</c>"——该字段实际不存在于 <c>ItemOptions</c>
        /// （<see cref="EquipmentHost"/> 本身并不持有、也不该持有 <c>core/rules/skill</c> 的
        /// <c>SkillOptions</c>，两层不应该为了这一个标志位耦合，见文档同步）。
        /// <para>
        /// <c>AllowMultiSourceTiming == true</c> 时，<c>AuraHost.ApplyAura</c> 按 sourceId（这里是各自
        /// 装备实例 id）各自开一份独立实例，两件装备同一个 <c>aura_def</c> 会拿到<b>两个不同</b>的
        /// <see cref="AuraInstanceRef"/>——原按 <c>auraDefId</c> 聚合的计数会把这两个本该各自独立的
        /// 实例错记成"同一份、还有 1 个引用"，卸下第一件装备时因为计数未归零而被跳过移除，
        /// 全部装备卸载后这份临时 aura 仍残留在目标身上（见外部审计 N09）。
        /// </para>
        /// <para>
        /// 改按实例句柄计数后不再需要区分两种模式：<c>AllowMultiSourceTiming=false</c> 时多件装备
        /// 的 <see cref="AuraInstanceRef"/> 本就相等（同一份实例），计数天然聚合，只有真正的最后一个
        /// 引用退出才移除，行为与修复前一致；<c>AllowMultiSourceTiming=true</c> 时每件装备的实例句柄
        /// 互不相同，各自计数恒为 1，卸下时立即精确移除自己的那一份，不再误判"还有其它来源"。
        /// </para>
        /// </summary>
        private readonly Dictionary<(Id UnitId, Id AuraInstanceId), int> _auraHandleRefCount =
            new Dictionary<(Id, Id), int>();

        private readonly Dictionary<(Id UnitId, Id SetId), Dictionary<int, List<AuraInstanceRef>>> _appliedSetBonuses =
            new Dictionary<(Id, Id), Dictionary<int, List<AuraInstanceRef>>>();

        public EquipmentHost(
            IDataRegistryView registry,
            IEventBus bus,
            InventoryHost inventory,
            IStatHost statHost,
            IEffectSink effectSink,
            SkillGranter skillGranter,
            IUnitAccess unitAccess,
            ItemOptions? options = null,
            IItemDiagnostics? diagnostics = null,
            IAuraQuery? auraQuery = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _effectSink = effectSink ?? throw new ArgumentNullException(nameof(effectSink));
            _skillGranter = skillGranter ?? throw new ArgumentNullException(nameof(skillGranter));
            _unitAccess = unitAccess ?? throw new ArgumentNullException(nameof(unitAccess));
            _options = options ?? new ItemOptions();
            _diagnostics = diagnostics ?? new InMemoryItemDiagnostics();

            foreach (var record in registry.GetAll("item.slot_definition"))
            {
                _slotDefinitions[record.GetId("id")] = record;
            }

            foreach (var record in registry.GetAll("item.set"))
            {
                _sets[record.GetId("id")] = record;
            }

            // C08 收口（外部审计 7e63d66 第四轮）：可选注入，未提供时（null，惯例同本类型其它可选
            // 依赖）行为与本次改动之前完全一致——只是重新暴露了 C08 描述的那个缺口，不会抛异常或
            // 改变既有测试断言。真实生产装配（见 CarriersAssembly）传入 Rules.Skill.AuraQuery（真实
            // AuraHost），使 StackOverflowPolicy.Replace 换句柄后本类记录的授予句柄能同步随之更新，
            // 见 OnAuraInstanceReplaced 判断记录。
            if (auraQuery != null)
            {
                auraQuery.InstanceReplaced += OnAuraInstanceReplaced;
            }
        }

        /// <summary>
        /// C08 收口：<see cref="IAuraQuery.InstanceReplaced"/> 的订阅回调——<paramref name="oldInstanceId"/>
        /// 已经在光环系统内部失效，<paramref name="newInstanceId"/> 是接替它的新句柄。把
        /// <see cref="_grantedAuras"/> 里全部仍引用 <paramref name="oldInstanceId"/> 的授予记录（可能
        /// 来自任意一件装备，不只是刚发起本次 Replace 调用的那一件）原子迁移到新句柄，并把
        /// <see cref="_auraHandleRefCount"/> 上旧句柄名下的计数原样搬到新句柄名下（与新句柄自己已有的
        /// 计数——即刚触发本次 Replace 的那次施加自身贡献的 1——相加，不是覆盖）。
        /// <para>
        /// 判断记录（为什么必须搬计数，不能只搬 <see cref="_grantedAuras"/> 记录）：<see cref="RevertGrants"/>
        /// 卸装时按 <see cref="_auraHandleRefCount"/> 上"这个句柄还有几个来源"决定是否真的调用
        /// <see cref="IEffectSink.RemoveAura"/>；如果只迁移 <see cref="_grantedAuras"/> 而不迁移计数，
        /// 旧句柄名下的计数会变成孤儿（永远不会再被任何 <see cref="RevertGrants"/> 调用递减，因为
        /// 已经没有任何 <see cref="_grantedAuras"/> 条目还指向它），新句柄的计数又只反映"触发 Replace
        /// 的这一次施加"，少算了此前其它装备已经持有的份额——任一件先卸下都会把计数错误地减到 0
        /// 并提前把光环真的移除掉，另一件仍装备着却没有了应有光环（外部审计 C08 复现场景本身）。
        /// </para>
        /// </summary>
        private void OnAuraInstanceReplaced(Id targetId, Id defId, Id oldInstanceId, Id newInstanceId)
        {
            foreach (var kv in _grantedAuras)
            {
                if (!kv.Key.UnitId.Equals(targetId))
                {
                    continue;
                }

                var list = kv.Value;
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].Ref.AuraInstanceId.Equals(oldInstanceId))
                    {
                        list[i] = (list[i].AuraDefId, new AuraInstanceRef(newInstanceId));
                    }
                }
            }

            // R03 收边补齐：套装门槛加成（RecomputeSetBonuses/_appliedSetBonuses）持有的句柄引用
            // 同样要跟着迁移——理由与上面 _grantedAuras 完全对称：AllowMultiSourceTiming=false 时
            // 装备 grants.auras 与套装门槛加成对同一个 aura_def 施加会合并成同一份实例，任意一侧先
            // 达到 max_stacks 触发 Replace 都可能换掉另一侧已经记着的旧句柄；不迁移的话另一侧后续
            // 释放引用时会拿着一个已经不存在的旧句柄调用 ReleaseAuraHandle，既找不到对应计数，也
            // 无法让真正持有新句柄的那份计数归零——共享的光环实例最终会永久残留（不会被任何一侧
            // 正确移除）。
            foreach (var kv in _appliedSetBonuses)
            {
                if (!kv.Key.UnitId.Equals(targetId))
                {
                    continue;
                }

                foreach (var thresholdEntry in kv.Value)
                {
                    var list = thresholdEntry.Value;
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (list[i].AuraInstanceId.Equals(oldInstanceId))
                        {
                            list[i] = new AuraInstanceRef(newInstanceId);
                        }
                    }
                }
            }

            var oldHandleKey = (targetId, oldInstanceId);
            if (!_auraHandleRefCount.TryGetValue(oldHandleKey, out var migratingCount))
            {
                // 没有任何装备记录持有过这个旧句柄（例如触发 Replace 的这次施加根本不是经
                // EquipmentHost.ApplyGrants 发起——种族被动光环、法术直接施加同一 aura_def 恰好撞上
                // 装备槽位等），没有需要迁移的计数，直接返回。
                return;
            }

            _auraHandleRefCount.Remove(oldHandleKey);
            var newHandleKey = (targetId, newInstanceId);
            _auraHandleRefCount[newHandleKey] =
                _auraHandleRefCount.TryGetValue(newHandleKey, out var existingCount)
                    ? existingCount + migratingCount
                    : migratingCount;
        }

        public EquipResult Equip(Id unitId, Id instanceId, Id slot)
        {
            var maybeInstance = _inventory.FindInstance(unitId, instanceId);
            if (maybeInstance == null)
            {
                return EquipResult.Fail(EquipFailureReason.NotInInventory);
            }

            var template = RequireTemplate(maybeInstance.Value.TemplateId);
            var templateSlot = template.GetId("slot");
            if (!SlotMatches(templateSlot, slot))
            {
                return EquipResult.Fail(EquipFailureReason.SlotMismatch);
            }

            // 阶段 3 整理"事项二"：is_equipment=false 的槽位是分类桶（消耗品/材料），即便 slot 匹配
            // 也不可经 Equip 装备（见 ItemSchemas.SlotDefinition.is_equipment 判断记录）。
            if (!IsEquipmentSlot(slot))
            {
                return EquipResult.Fail(EquipFailureReason.SlotMismatch);
            }

            if (_options.EnforceRequirements && TryGetRequiredLevel(template, out var requiredLevel) &&
                _unitAccess.GetLevel(unitId) < requiredLevel)
            {
                return EquipResult.Fail(EquipFailureReason.RequirementNotMet);
            }

            if (!_inventory.TryTakeWhole(unitId, instanceId, out var taken))
            {
                // 竞态兜底：Equip 内两次访问 InventoryHost 之间理论上不会被并发改动（本层不引入
                // 多线程，见 00 架构总则"单线程确定性模拟"），此分支只做防御。
                return EquipResult.Fail(EquipFailureReason.NotInInventory);
            }

            var unitSlots = GetOrCreateUnitSlots(unitId);
            ItemInstanceRef? replaced = null;
            if (unitSlots.TryGetValue(slot, out var occupying))
            {
                var occupyingTemplate = RequireTemplate(occupying.TemplateId);
                RevertGrants(unitId, occupying, occupyingTemplate);
                unitSlots.Remove(slot);
                if (TryGetSetId(occupyingTemplate, out var occupyingSetId))
                {
                    RecomputeSetBonuses(unitId, occupyingSetId);
                }

                if (!_inventory.TryPutBack(unitId, occupying))
                {
                    _diagnostics.Warn(
                        $"Equip 换装：旧物品 \"{occupying.InstanceId}\" 未能放回单位 \"{unitId}\" 背包" +
                        "（不应发生，见 EquipmentHost 判断记录 1：换装前已让出一个格子）");
                }

                replaced = occupying.ToRef();
            }

            ApplyGrants(unitId, taken, template);
            unitSlots[slot] = taken;
            if (TryGetSetId(template, out var setId))
            {
                RecomputeSetBonuses(unitId, setId);
            }

            _bus.Enqueue(new ItemEquippedEvent(unitId, instanceId, slot));
            return EquipResult.Ok(replaced);
        }

        public ItemInstanceRef? Unequip(Id unitId, Id slot)
        {
            var unitSlots = GetOrCreateUnitSlots(unitId);
            if (!unitSlots.TryGetValue(slot, out var instance))
            {
                return null;
            }

            if (!_inventory.HasRoomForOne(unitId))
            {
                _diagnostics.Warn(
                    $"Unequip 中止：单位 \"{unitId}\" 背包已满，槽位 \"{slot}\" 保持装备状态" +
                    "（见 EquipmentHost 判断记录 2）");
                return null;
            }

            var template = RequireTemplate(instance.TemplateId);
            RevertGrants(unitId, instance, template);
            unitSlots.Remove(slot);
            if (TryGetSetId(template, out var setId))
            {
                RecomputeSetBonuses(unitId, setId);
            }

            var putBack = _inventory.TryPutBack(unitId, instance);
            if (!putBack)
            {
                _diagnostics.Warn(
                    $"Unequip：物品 \"{instance.InstanceId}\" 未能放回单位 \"{unitId}\" 背包" +
                    "（不应发生，见 EquipmentHost 判断记录 2：已由 HasRoomForOne 预先确认有空位）");
            }

            _bus.Enqueue(new ItemUnequippedEvent(unitId, slot, instance.InstanceId));
            return instance.ToRef();
        }

        /// <summary>
        /// FND-10 收边补齐：卸下该单位当前全部已装备物品，撤销全部联动（属性修正/技能授予/光环/
        /// 套装加成），但物品本身不放回背包——供 <see cref="EquipmentPersistable.Load"/> 在应用
        /// 快照前，把单位重置到"无装备"的干净状态使用（见该类型判断记录"按快照替换完整装备状态"）。
        /// <para>
        /// 判断记录：读档是"完整替换"语义，不是"合并"——快照里不存在的已装备物品不应该凭空出现在
        /// 读档后的背包里，它们要么会被 <c>InventoryPersistable.Load</c> 从快照自身的背包数据里
        /// 放回（若确实还在那份历史快照的背包段中），要么本来就不该在这次读档后的世界里存在。
        /// 因此本方法只做联动撤销与 <see cref="_equipped"/> 内部字典清理，不调用
        /// <see cref="InventoryHost"/> 的任何写入方法，与调用方是否已经/将要处理背包段的相对顺序
        /// 无关（不像公开的 <see cref="Unequip"/> 那样把物品放回背包——那是"正常卸装备"的语义，
        /// 这里是"读档前先清空"的语义，两者刻意不同）。
        /// </para>
        /// </summary>
        internal void ClearAllEquippedForLoad(Id unitId)
        {
            if (!_equipped.TryGetValue(unitId, out var slots) || slots.Count == 0)
            {
                return;
            }

            var touchedSetIds = new HashSet<Id>();
            foreach (var kv in new List<KeyValuePair<Id, ItemInstance>>(slots))
            {
                var slot = kv.Key;
                var instance = kv.Value;
                var template = RequireTemplate(instance.TemplateId);
                RevertGrants(unitId, instance, template);
                slots.Remove(slot);
                if (TryGetSetId(template, out var setId))
                {
                    touchedSetIds.Add(setId);
                }

                _bus.Enqueue(new ItemUnequippedEvent(unitId, slot, instance.InstanceId));
            }

            foreach (var setId in touchedSetIds)
            {
                RecomputeSetBonuses(unitId, setId);
            }
        }

        public ItemInstanceRef? GetEquipped(Id unitId, Id slot) =>
            _equipped.TryGetValue(unitId, out var slots) && slots.TryGetValue(slot, out var instance)
                ? instance.ToRef()
                : (ItemInstanceRef?)null;

        public IReadOnlyDictionary<Id, ItemInstanceRef> GetAllEquipped(Id unitId)
        {
            var result = new Dictionary<Id, ItemInstanceRef>();
            if (_equipped.TryGetValue(unitId, out var slots))
            {
                foreach (var kv in slots)
                {
                    result[kv.Key] = kv.Value.ToRef();
                }
            }

            return result;
        }

        /// <summary>补充：该单位当前全部已装备槽位到完整 <see cref="ItemInstance"/>（含堆叠数/扩展
        /// 字段）的映射，供 <see cref="EquipmentPersistable.Save"/> 序列化使用——<see
        /// cref="IEquipmentHost.GetAllEquipped"/> 只返回不透明的 <see cref="ItemInstanceRef"/>，
        /// 存档需要完整数据，见该类型顶部判断记录。</summary>
        public IReadOnlyDictionary<Id, ItemInstance> GetAllEquippedInstances(Id unitId)
        {
            var result = new Dictionary<Id, ItemInstance>();
            if (_equipped.TryGetValue(unitId, out var slots))
            {
                foreach (var kv in slots)
                {
                    result[kv.Key] = kv.Value;
                }
            }

            return result;
        }

        /// <summary>补充：当前装备在 <paramref name="slot"/> 的武器伤害/攻速定义（见 07 第 1.4 节第
        /// 3 点"普通攻击的数值输入……随之更新"、第 6 节"武器决定普通攻击动作……属于表现层职责"——
        /// 本方法只提供数值输入，不决定具体表现）。该槽位无装备，或物品没有 <c>weapon_profile</c>
        /// 字段时返回 null。</summary>
        public WeaponProfile? GetWeaponProfile(Id unitId, Id slot)
        {
            if (!_equipped.TryGetValue(unitId, out var slots) || !slots.TryGetValue(slot, out var instance))
            {
                return null;
            }

            var template = RequireTemplate(instance.TemplateId);
            if (!template.TryGetObject("weapon_profile", out var profile))
            {
                return null;
            }

            return new WeaponProfile(
                GetNumber(profile, "damage_min", 0),
                GetNumber(profile, "damage_max", 0),
                GetNumber(profile, "speed", 0),
                GetIdOpt(profile, "weapon_school"));
        }

        /// <summary>
        /// RC-11 收边补齐：<see cref="IWeaponDamageQuery"/> 实现——<c>weapon_damage_pct</c> 效果原语
        /// （<c>core/rules/skill.EffectDispatcher</c>，经 <c>Core.Rules.Assembly.
        /// DeferredWeaponDamageQuery</c> 依赖倒置注入）取"当前武器基础伤害"的唯一入口，见该接口
        /// 方法注释"判断记录"（<c>damage_min</c>/<c>damage_max</c> 均值；按槽位 id 序数最先命中的
        /// 武器槽为准；未装备任何武器槽返回 0）。不依赖调用方传入具体槽位——本方法自己按
        /// <see cref="_slotDefinitions"/> 的 <c>is_weapon</c> 标记（见 <see cref="IsEquipmentSlot"/>
        /// 同一份数据、ItemSchemas.SlotDefinition.is_weapon 判断记录）从该单位当前已装备的槽位里找
        /// "武器槽"，不需要框架层硬编码任何具体槽位 id（如 "main_hand"）——槽位命名完全由游戏内容
        /// 数据决定。
        /// </summary>
        public double GetWeaponBaseDamage(Id unitId)
        {
            if (!_equipped.TryGetValue(unitId, out var slots) || slots.Count == 0)
            {
                return 0.0;
            }

            var weaponSlotIds = new List<Id>();
            foreach (var slot in slots.Keys)
            {
                if (_slotDefinitions.TryGetValue(slot, out var slotDef) &&
                    slotDef.TryGetBool("is_weapon", out var isWeapon) && isWeapon)
                {
                    weaponSlotIds.Add(slot);
                }
            }

            if (weaponSlotIds.Count == 0)
            {
                return 0.0;
            }

            weaponSlotIds.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            var profile = GetWeaponProfile(unitId, weaponSlotIds[0]);
            return profile.HasValue ? (profile.Value.DamageMin + profile.Value.DamageMax) / 2.0 : 0.0;
        }

        // -----------------------------------------------------------------
        // 装备联动：属性 / 技能 / 光环（07 第 1.4 节步骤 1/2）
        // -----------------------------------------------------------------

        private void ApplyGrants(Id unitId, ItemInstance instance, DataRecord template)
        {
            if (template.TryGetArray("stats", out var stats))
            {
                foreach (var raw in stats)
                {
                    if (!(raw is JsonObject obj))
                    {
                        continue;
                    }

                    var statId = RequireId(obj, "stat");
                    var op = ParseOp(GetString(obj, "op", "flat"));
                    var value = GetNumber(obj, "value", 0);
                    _statHost.AddModifier(unitId, new StatModifier(statId, op, value, instance.InstanceId));
                }
            }

            if (!template.TryGetObject("grants", out var grants))
            {
                return;
            }

            if (grants.TryGetValue("skills", out var skillsRaw) && skillsRaw is JsonArray skillsArr)
            {
                foreach (var s in skillsArr)
                {
                    if (s is JsonString ss && Id.TryParse(ss.Value, out var skillId))
                    {
                        // RC-05 收边补齐：以本装备实例 id 作为来源传给 SkillGranter（见该委托类型
                        // 判断记录）——接收端（SkillHost）按来源做引用计数，卸下这件装备只撤销这一
                        // 个来源，不影响永久学习或其它装备来源仍在授予的同一技能。
                        _skillGranter(unitId, skillId, instance.InstanceId, true);
                    }
                }
            }

            if (grants.TryGetValue("auras", out var aurasRaw) && aurasRaw is JsonArray aurasArr && aurasArr.Count > 0)
            {
                // R03 收边补齐（外部审计 5e779c6，P2）：list 必须在遍历开始前就登记进
                // _grantedAuras（不是遍历结束后一次性赋值）——同一件装备的 grants.auras 里重复
                // 两次同一个 aura_def 时（如测试数据 item.repro.duplicate），第二次 ApplyAura 会因为
                // maxStacks 溢出触发 StackOverflowPolicy.Replace，同步整发 OnAuraInstanceReplaced。
                // 该回调按"_grantedAuras 里已登记的条目"做句柄迁移；如果这里的 list 还只是一个未登记
                // 的局部变量，回调找不到第一次施加留下的那条记录去迁移，list 里就会残留一个已经失效
                // 的旧句柄引用，且 _auraHandleRefCount 会被 ApplyGrants 自身的计数与回调迁移的计数
                // 重复累加——Unequip 时因为多算的计数无法归零，光环卸不干净（见外部审计复现日志
                // ReproEquipmentDuplicate 的 duplicate 分支）。提前登记后，回调能在同一次循环内原地
                // 更新已有条目，list 与 _auraHandleRefCount 全程保持一致。
                var key = (unitId, instance.InstanceId);
                var list = new List<(Id, AuraInstanceRef)>();
                _grantedAuras[key] = list;

                foreach (var a in aurasArr)
                {
                    if (a is JsonString asStr && Id.TryParse(asStr.Value, out var auraDefId))
                    {
                        // N09 收边补齐：先拿到本次施加实际落地的实例句柄，再按句柄（不是 auraDefId）
                        // 计数——见 _auraHandleRefCount 判断记录。
                        var granted = _effectSink.ApplyAura(unitId, auraDefId, instance.InstanceId);
                        RegisterAuraHandle(unitId, granted.AuraInstanceId);

                        list.Add((auraDefId, granted));
                    }
                }
            }
        }

        /// <summary>
        /// R03 收边补齐：登记"多了一个来源持有这个光环实例句柄"，供 <see cref="ReleaseAuraHandle"/>
        /// 配对释放。<see cref="ApplyGrants"/>（装备本身的 <c>grants.auras</c>）与
        /// <see cref="RecomputeSetBonuses"/>（套装门槛加成）此前各自维护互不相通的簿记
        /// （<see cref="_auraHandleRefCount"/> 只被前者使用，后者直接无条件 ApplyAura/RemoveAura）——
        /// <c>AllowMultiSourceTiming=false</c>（默认）时两者对同一个 <c>aura_def</c> 施加会在
        /// <c>AuraHost</c> 内合并成<b>同一个</b>实例句柄（见 <c>AuraHost.ApplyAura</c> 按
        /// <c>(target, aura_def)</c> 合并槽位），只要其中一处不参与共享计数，另一处卸载/降级时就会
        /// 无条件调用 <see cref="IEffectSink.RemoveAura"/> 把仍被别处引用的共享实例整个移除——外部
        /// 审计复现场景正是"卸下普通装备后，仍满足件数门槛的套装光环被一并删除"（见外部审计
        /// ReproEquipmentDuplicate 的 set 分支）。统一改为两处都经这一对方法登记/释放，只有全部来源
        /// 都释放完毕（计数归零）才真正调用 <see cref="IEffectSink.RemoveAura"/>。
        /// </summary>
        private void RegisterAuraHandle(Id unitId, Id auraInstanceId)
        {
            var handleKey = (unitId, auraInstanceId);
            _auraHandleRefCount[handleKey] = _auraHandleRefCount.TryGetValue(handleKey, out var count) ? count + 1 : 1;
        }

        /// <summary>见 <see cref="RegisterAuraHandle"/> 判断记录——释放一个来源持有的引用；仍有其它
        /// 来源持有同一句柄时只递减计数，真正归零时才调用 <see cref="IEffectSink.RemoveAura"/>。</summary>
        private void ReleaseAuraHandle(Id unitId, AuraInstanceRef auraRef)
        {
            var handleKey = (unitId, auraRef.AuraInstanceId);
            var remaining = _auraHandleRefCount.TryGetValue(handleKey, out var count) ? count - 1 : 0;

            if (remaining > 0)
            {
                _auraHandleRefCount[handleKey] = remaining;
                return;
            }

            _auraHandleRefCount.Remove(handleKey);
            _effectSink.RemoveAura(unitId, auraRef);
        }

        private void RevertGrants(Id unitId, ItemInstance instance, DataRecord template)
        {
            _statHost.RemoveModifiersBySource(unitId, instance.InstanceId);

            if (template.TryGetObject("grants", out var grants) &&
                grants.TryGetValue("skills", out var skillsRaw) && skillsRaw is JsonArray skillsArr)
            {
                foreach (var s in skillsArr)
                {
                    if (s is JsonString ss && Id.TryParse(ss.Value, out var skillId))
                    {
                        // 只撤销"这件装备"这一个来源（见 ApplyGrants 判断记录）。
                        _skillGranter(unitId, skillId, instance.InstanceId, false);
                    }
                }
            }

            var key = (unitId, instance.InstanceId);
            if (_grantedAuras.TryGetValue(key, out var list))
            {
                foreach (var (_, r) in list)
                {
                    // N09/R03 收边补齐：按实例句柄（不是 auraDefId）计数，见 _auraHandleRefCount /
                    // RegisterAuraHandle/ReleaseAuraHandle 判断记录——AllowMultiSourceTiming=true 下
                    // 每件装备的句柄互不相同，计数恒为 1，立即精确移除；=false 下多件装备（含套装门槛
                    // 加成，见 RecomputeSetBonuses）可能共享同一句柄，计数聚合，只在最后一个引用退出
                    // 时才真正移除。
                    ReleaseAuraHandle(unitId, r);
                }

                _grantedAuras.Remove(key);
            }
        }

        // -----------------------------------------------------------------
        // 套装件数门槛（07 第 1.5 节 ItemSet）
        // -----------------------------------------------------------------

        private void RecomputeSetBonuses(Id unitId, Id setId)
        {
            if (!_sets.TryGetValue(setId, out var setRecord))
            {
                // 应已被 item.template.set_id 的引用完整性校验拦截。
                return;
            }

            var currentCount = CountEquippedPiecesOfSet(unitId, setId);
            var bonuses = ParseSetBonuses(setRecord);

            var key = (unitId, setId);
            if (!_appliedSetBonuses.TryGetValue(key, out var applied))
            {
                applied = new Dictionary<int, List<AuraInstanceRef>>();
                _appliedSetBonuses[key] = applied;
            }

            foreach (var (threshold, auraRef) in bonuses)
            {
                var isApplied = applied.ContainsKey(threshold);
                if (currentCount >= threshold && !isApplied)
                {
                    var handle = _effectSink.ApplyAura(unitId, auraRef, setId);
                    // R03 收边补齐：套装门槛加成与装备 grants.auras（见 ApplyGrants/RevertGrants）
                    // 统一经 RegisterAuraHandle/ReleaseAuraHandle 记账——AllowMultiSourceTiming=false
                    // 时两者对同一个 aura_def 的施加会在 AuraHost 内合并成同一份实例句柄，这里不再
                    // 各自为政，才能保证任一侧卸载/降级时都不会误删另一侧仍需要的共享光环（见该方法
                    // 判断记录）。
                    RegisterAuraHandle(unitId, handle.AuraInstanceId);
                    applied[threshold] = new List<AuraInstanceRef> { handle };
                }
                else if (currentCount < threshold && isApplied)
                {
                    foreach (var r in applied[threshold])
                    {
                        ReleaseAuraHandle(unitId, r);
                    }

                    applied.Remove(threshold);
                }
            }
        }

        private int CountEquippedPiecesOfSet(Id unitId, Id setId)
        {
            if (!_equipped.TryGetValue(unitId, out var slots))
            {
                return 0;
            }

            var count = 0;
            foreach (var instance in slots.Values)
            {
                var template = RequireTemplate(instance.TemplateId);
                if (TryGetSetId(template, out var sid) && sid.Equals(setId))
                {
                    count++;
                }
            }

            return count;
        }

        private static List<(int Count, Id AuraRef)> ParseSetBonuses(DataRecord setRecord)
        {
            var result = new List<(int, Id)>();
            foreach (var raw in setRecord.GetArray("bonuses"))
            {
                if (!(raw is JsonObject obj))
                {
                    continue;
                }

                var count = (int)GetNumber(obj, "count", 0);
                var auraRef = RequireId(obj, "aura_ref");
                result.Add((count, auraRef));
            }

            return result;
        }

        // -----------------------------------------------------------------
        // 小工具
        // -----------------------------------------------------------------

        private Dictionary<Id, ItemInstance> GetOrCreateUnitSlots(Id unitId)
        {
            if (!_equipped.TryGetValue(unitId, out var slots))
            {
                slots = new Dictionary<Id, ItemInstance>();
                _equipped[unitId] = slots;
            }

            return slots;
        }

        private DataRecord RequireTemplate(Id templateId)
        {
            var template = _inventory.GetTemplate(templateId);
            if (template == null)
            {
                throw new InvalidOperationException(
                    $"物品模板 \"{templateId}\" 不存在（应已通过 item.template 的引用完整性校验）");
            }

            return template;
        }

        /// <summary>该槽位是否为真正的装备位（见 ItemSchemas.SlotDefinition.is_equipment 判断
        /// 记录，缺省 true）。未登记的槽位 id（理论上不会发生——Equip 前已经过 SlotMatches，requested
        /// slot 必然是某个已登记的 item.slot_definition）按 true 处理，与字段缺省语义一致。</summary>
        private bool IsEquipmentSlot(Id slot)
        {
            return !_slotDefinitions.TryGetValue(slot, out var slotDef) ||
                !slotDef.TryGetBool("is_equipment", out var value) || value;
        }

        private bool SlotMatches(Id templateSlot, Id requestedSlot)
        {
            if (templateSlot.Equals(requestedSlot))
            {
                return true;
            }

            if (_slotDefinitions.TryGetValue(requestedSlot, out var slotDef) &&
                slotDef.TryGetIdList("accepts", out var accepts))
            {
                foreach (var a in accepts)
                {
                    if (a.Equals(templateSlot))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetRequiredLevel(DataRecord template, out int level)
        {
            if (template.TryGetObject("requirements", out var req) &&
                req.TryGetValue("level", out var v) && v is JsonNumber n)
            {
                level = (int)n.Value;
                return true;
            }

            level = 0;
            return false;
        }

        private static bool TryGetSetId(DataRecord template, out Id setId)
        {
            if (template.TryGetString("set_id", out var s) && Id.TryParse(s, out setId))
            {
                return true;
            }

            setId = default;
            return false;
        }

        private static Id RequireId(JsonObject o, string key)
        {
            if (o.TryGetValue(key, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }

            throw new ArgumentException($"字段 \"{key}\" 缺失或不是合法 Id");
        }

        private static Id? GetIdOpt(JsonObject o, string key) =>
            o.TryGetValue(key, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id)
                ? id
                : (Id?)null;

        private static string GetString(JsonObject o, string key, string fallback) =>
            o.TryGetValue(key, out var v) && v is JsonString s ? s.Value : fallback;

        private static double GetNumber(JsonObject o, string key, double fallback) =>
            o.TryGetValue(key, out var v) && v is JsonNumber n ? n.Value : fallback;

        private static StatModifierOp ParseOp(string text) => text switch
        {
            "flat" => StatModifierOp.Flat,
            "pct" => StatModifierOp.Pct,
            "mult" => StatModifierOp.Mult,
            _ => throw new ArgumentException($"未知的 stats.op \"{text}\"，应为 flat|pct|mult"),
        };
    }
}
