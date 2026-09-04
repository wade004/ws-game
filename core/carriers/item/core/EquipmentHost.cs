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
    /// 上层组装代码把真实的 <c>SkillHost.LearnSkill</c>/<c>Forget</c> 适配成这个签名后注入（见该
    /// 委托类型顶部注释、任务书"契约缺口用模块内委托绕过并汇报"）。
    /// </para>
    /// </summary>
    public sealed class EquipmentHost : IEquipmentHost
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

        private readonly Dictionary<(Id UnitId, Id InstanceId), List<AuraInstanceRef>> _grantedAuras =
            new Dictionary<(Id, Id), List<AuraInstanceRef>>();

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
            IItemDiagnostics? diagnostics = null)
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
                        _skillGranter(unitId, skillId, true);
                    }
                }
            }

            if (grants.TryGetValue("auras", out var aurasRaw) && aurasRaw is JsonArray aurasArr && aurasArr.Count > 0)
            {
                var list = new List<AuraInstanceRef>();
                foreach (var a in aurasArr)
                {
                    if (a is JsonString asStr && Id.TryParse(asStr.Value, out var auraDefId))
                    {
                        list.Add(_effectSink.ApplyAura(unitId, auraDefId, instance.InstanceId));
                    }
                }

                _grantedAuras[(unitId, instance.InstanceId)] = list;
            }
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
                        _skillGranter(unitId, skillId, false);
                    }
                }
            }

            var key = (unitId, instance.InstanceId);
            if (_grantedAuras.TryGetValue(key, out var list))
            {
                foreach (var r in list)
                {
                    _effectSink.RemoveAura(unitId, r);
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
                    applied[threshold] = new List<AuraInstanceRef> { handle };
                }
                else if (currentCount < threshold && isApplied)
                {
                    foreach (var r in applied[threshold])
                    {
                        _effectSink.RemoveAura(unitId, r);
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
